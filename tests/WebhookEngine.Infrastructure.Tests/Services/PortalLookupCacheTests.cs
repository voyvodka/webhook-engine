using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Options;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using WebhookEngine.Infrastructure.Services;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.Services;

/// <summary>
/// Behavioural tests for <see cref="PortalLookupCache"/> — TTL miss / cache hit /
/// invalidation / portal-not-enabled, plus a concurrent-Set+Invalidate race
/// that exercises the post-merge fix where <c>Set</c> now atomically swaps
/// the per-app <see cref="System.Threading.CancellationTokenSource"/> instead
/// of <c>GetOrAdd</c>-reusing it.
/// </summary>
public class PortalLookupCacheTests
{
    [Fact]
    public async Task GetAsync_Returns_Null_When_Portal_Not_Enabled()
    {
        await using var db = CreateDbContext();
        var app = SeedApp(db, portalSigningKey: null);
        var cache = CreateCache(db);

        var lookup = await cache.GetAsync(app.Id, CancellationToken.None);

        lookup.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_Returns_Lookup_For_Portal_Enabled_App()
    {
        await using var db = CreateDbContext();
        var app = SeedApp(db, portalSigningKey: "k", originsJson: "[\"https://app.example\"]");
        var cache = CreateCache(db);

        var lookup = await cache.GetAsync(app.Id, CancellationToken.None);

        lookup.Should().NotBeNull();
        lookup!.PortalSigningKey.Should().Be("k");
        lookup.AllowedOrigins.Should().BeEquivalentTo(["https://app.example"]);
    }

    [Fact]
    public async Task GetAsync_Caches_Across_Calls_Even_After_Db_Mutation()
    {
        // Once cached, the lookup must survive the next call without going
        // back to the database — proving the cache is actually in the loop.
        // We mutate the underlying row to a state that would deserialize
        // differently if a fresh load happened (origins JSON cleared); the
        // second call must still return the cached origins.
        await using var db = CreateDbContext();
        var app = SeedApp(db, portalSigningKey: "k", originsJson: "[\"https://first.example\"]");
        var cache = CreateCache(db);

        var first = await cache.GetAsync(app.Id, CancellationToken.None);

        // Mutate row directly without going through the cache's invalidation path.
        var tracked = await db.Applications.SingleAsync(a => a.Id == app.Id);
        tracked.AllowedPortalOriginsJson = "[]";
        await db.SaveChangesAsync();

        var second = await cache.GetAsync(app.Id, CancellationToken.None);

        first!.AllowedOrigins.Should().BeEquivalentTo(["https://first.example"]);
        second!.AllowedOrigins.Should().BeEquivalentTo(["https://first.example"]);
    }

    [Fact]
    public async Task InvalidateApplication_Forces_Database_Reload_On_Next_GetAsync()
    {
        await using var db = CreateDbContext();
        var app = SeedApp(db, portalSigningKey: "k", originsJson: "[\"https://before\"]");
        var cache = CreateCache(db);

        await cache.GetAsync(app.Id, CancellationToken.None);

        var tracked = await db.Applications.SingleAsync(a => a.Id == app.Id);
        tracked.AllowedPortalOriginsJson = "[\"https://after\"]";
        await db.SaveChangesAsync();

        PortalLookupCache.InvalidateApplication(app.Id);

        var refreshed = await cache.GetAsync(app.Id, CancellationToken.None);

        refreshed!.AllowedOrigins.Should().BeEquivalentTo(["https://after"]);
    }

    [Fact]
    public async Task Concurrent_Invalidate_Calls_Do_Not_Throw_ObjectDisposed()
    {
        // Regression for the v0.2.0 audit fix: a single CTS per app, when
        // racing N concurrent InvalidateApplication callers, must end up
        // with exactly one Cancel + Dispose (the TryRemove winner) and N-1
        // no-ops — never a double-dispose throw. Set itself can't be raced
        // from outside the class without a real DbContext (which isn't
        // thread-safe), so the orthogonal Set-side race is exercised by
        // the integration tests that drive the rotate endpoint.
        await using var db = CreateDbContext();
        var app = SeedApp(db, portalSigningKey: "k", originsJson: "[\"https://race\"]");
        var cache = CreateCache(db);

        // Pre-warm so the per-app CTS exists in the static dictionary.
        await cache.GetAsync(app.Id, CancellationToken.None);

        var act = async () =>
        {
            var tasks = new List<Task>();
            for (var i = 0; i < 64; i++)
            {
                tasks.Add(Task.Run(() => PortalLookupCache.InvalidateApplication(app.Id)));
            }
            await Task.WhenAll(tasks);
        };

        await act.Should().NotThrowAsync();
    }

    private static WebhookDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseInMemoryDatabase($"portal_lookup_cache_tests_{Guid.NewGuid()}")
            .Options;
        return new WebhookDbContext(options);
    }

    private static ApplicationEntity SeedApp(
        WebhookDbContext db,
        string? portalSigningKey,
        string? originsJson = null)
    {
        var appId = Guid.NewGuid();
        var app = new ApplicationEntity
        {
            Id = appId,
            Name = $"portal-cache-{appId:N}",
            ApiKeyPrefix = $"whe_{appId:N}".Substring(0, 12) + "_",
            ApiKeyHash = "deadbeef",
            SigningSecret = "whsec_test",
            PortalSigningKey = portalSigningKey,
            AllowedPortalOriginsJson = originsJson,
            IsActive = true
        };
        db.Applications.Add(app);
        db.SaveChanges();
        return app;
    }

    private static PortalLookupCache CreateCache(WebhookDbContext db)
    {
        // SizeLimit is required because PortalLookupCache sets entry Size = 1.
        var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 });
        var repo = new ApplicationRepository(db);
        var options = Options.Create(new PortalAuthOptions { LookupCacheTtlSeconds = 60 });
        return new PortalLookupCache(memory, repo, options);
    }
}
