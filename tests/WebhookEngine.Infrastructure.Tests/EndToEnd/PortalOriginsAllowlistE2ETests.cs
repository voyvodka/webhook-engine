using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Repositories;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.Infrastructure.Tests.EndToEnd;

/// <summary>
/// Real-PostgreSQL coverage for <see cref="ApplicationRepository.AnyAllowsPortalOriginAsync"/>.
/// The method itself does the JSON containment in C# (so the InMemory and
/// Npgsql providers should agree), but the column is JSONB and the candidate
/// filter <c>WHERE PortalSigningKey IS NOT NULL AND AllowedPortalOriginsJson IS NOT NULL</c>
/// is provider-translated — Testcontainers exercises that the round-trip
/// through real PostgreSQL doesn't drop a portal-enabled row or surface a
/// disabled one.
/// </summary>
public class PortalOriginsAllowlistE2ETests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public PortalOriginsAllowlistE2ETests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AnyAllowsPortalOriginAsync_Returns_True_For_Exact_Match()
    {
        await SeedAsync(portalEnabled: true, originsJson: "[\"https://app.example.com\"]");
        var (db, repo) = NewScope();

        var allowed = await repo.AnyAllowsPortalOriginAsync("https://app.example.com");

        allowed.Should().BeTrue();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AnyAllowsPortalOriginAsync_Is_Case_Insensitive_On_Host()
    {
        // RFC 6454 §4: scheme + host case-insensitive. Allowlist stored with
        // uppercase host; lookup hits with lowercase.
        await SeedAsync(portalEnabled: true, originsJson: "[\"https://APP.EXAMPLE.COM\"]");
        var (db, repo) = NewScope();

        var allowed = await repo.AnyAllowsPortalOriginAsync("https://app.example.com");

        allowed.Should().BeTrue();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AnyAllowsPortalOriginAsync_Skips_Portal_Disabled_Apps_Even_If_Allowlist_Matches()
    {
        // The candidate filter requires PortalSigningKey != null. A row with
        // an origin that would otherwise match must not slip through if the
        // app has the portal disabled.
        await SeedAsync(portalEnabled: false, originsJson: "[\"https://app.example.com\"]");
        var (db, repo) = NewScope();

        var allowed = await repo.AnyAllowsPortalOriginAsync("https://app.example.com");

        allowed.Should().BeFalse();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AnyAllowsPortalOriginAsync_Returns_False_When_Origin_Not_In_Allowlist()
    {
        await SeedAsync(portalEnabled: true, originsJson: "[\"https://app.example.com\"]");
        var (db, repo) = NewScope();

        var allowed = await repo.AnyAllowsPortalOriginAsync("https://attacker.example");

        allowed.Should().BeFalse();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AnyAllowsPortalOriginAsync_Returns_False_When_Allowlist_Is_Empty_Array()
    {
        await SeedAsync(portalEnabled: true, originsJson: "[]");
        var (db, repo) = NewScope();

        var allowed = await repo.AnyAllowsPortalOriginAsync("https://app.example.com");

        allowed.Should().BeFalse();
        await db.DisposeAsync();
    }

    // Note: a "malformed JSON" case isn't tested here because PostgreSQL
    // rejects invalid JSON at INSERT into a JSONB column — the corresponding
    // catch (JsonException) inside the repository is defensive code that
    // covers a hypothetical "different source schema" path and isn't
    // reachable through the EF Core write surface.

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnyAllowsPortalOriginAsync_Returns_False_For_Blank_Origin(string origin)
    {
        await SeedAsync(portalEnabled: true, originsJson: "[\"https://app.example.com\"]");
        var (db, repo) = NewScope();

        var allowed = await repo.AnyAllowsPortalOriginAsync(origin);

        allowed.Should().BeFalse();
        await db.DisposeAsync();
    }

    private async Task SeedAsync(bool portalEnabled, string? originsJson)
    {
        var (db, _) = NewScope();
        await using (db)
        {
            var appId = Guid.NewGuid();
            db.Applications.Add(new ApplicationEntity
            {
                Id = appId,
                Name = $"portal-origins-{appId:N}",
                ApiKeyPrefix = $"whe_{appId:N}".Substring(0, 12) + "_",
                ApiKeyHash = "deadbeef",
                SigningSecret = "whsec_test",
                PortalSigningKey = portalEnabled ? "k" : null,
                AllowedPortalOriginsJson = originsJson,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
    }

    private (WebhookDbContext db, ApplicationRepository repo) NewScope()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        var db = new WebhookDbContext(options);
        return (db, new ApplicationRepository(db));
    }
}
