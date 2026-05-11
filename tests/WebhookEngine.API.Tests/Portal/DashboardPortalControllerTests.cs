using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.API.Auth;
using WebhookEngine.API.Tests.Integration;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;
using WebhookEngine.Infrastructure.Services;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.API.Tests.Portal;

/// <summary>
/// Dashboard portal-access management controller. Cookie auth + audit-log
/// secret-leak guarantees + cache-invalidation guarantees + origin-validation
/// rules all live here. These tests own the load-bearing security contracts
/// (signing key never echoed on read, never written to audit, cache flushes
/// immediately on disable).
/// </summary>
public class DashboardPortalControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private const string DashboardEmail = "admin@portal-tests.local";
    private const string DashboardPassword = "P@ssw0rd-portal-1";

    private readonly TestWebApplicationFactory _factory;

    public DashboardPortalControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Enable_Generates_SigningKey_And_Returns_It_Once()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var enable = await client.PostAsync(PortalRoute(appId, "enable"), content: null);

        enable.StatusCode.Should().Be(HttpStatusCode.OK);
        var enableJson = await ParseJsonAsync(enable);
        var signingKey = enableJson.GetProperty("data").GetProperty("signingKey").GetString();
        signingKey.Should().NotBeNullOrEmpty();
        signingKey!.Should().StartWith("whsec_");
        signingKey.Length.Should().Be(50, "32 random bytes base64-encode to 44 chars + 6-char prefix");

        // Subsequent reads MUST NOT echo the signing key.
        var read = await client.GetAsync(PortalRoute(appId));
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var readJson = await ParseJsonAsync(read);
        var data = readJson.GetProperty("data");
        data.GetProperty("portalEnabled").GetBoolean().Should().BeTrue();
        data.GetProperty("signingKey").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Enable_Twice_Rotates_The_Existing_Key()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var first = await client.PostAsync(PortalRoute(appId, "enable"), content: null);
        var second = await client.PostAsync(PortalRoute(appId, "enable"), content: null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstKey = (await ParseJsonAsync(first)).GetProperty("data").GetProperty("signingKey").GetString();
        var secondKey = (await ParseJsonAsync(second)).GetProperty("data").GetProperty("signingKey").GetString();

        firstKey.Should().NotBe(secondKey, "the second enable rotates rather than no-ops");
    }

    [Fact]
    public async Task Rotate_Returns_409_When_Portal_Not_Enabled()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var rotate = await client.PostAsync(PortalRoute(appId, "rotate"), content: null);

        rotate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorCodeAsync(rotate)).Should().Be("PORTAL_NOT_ENABLED");
    }

    [Fact]
    public async Task Rotate_Returns_New_Signing_Key_And_Updates_RotatedAt()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var enable = await client.PostAsync(PortalRoute(appId, "enable"), content: null);
        enable.StatusCode.Should().Be(HttpStatusCode.OK);
        var enabledKey = (await ParseJsonAsync(enable)).GetProperty("data").GetProperty("signingKey").GetString();
        DateTime? firstRotatedAt = await ReadRotatedAtAsync(appId);
        firstRotatedAt.Should().NotBeNull();

        // Move forward enough that the second timestamp is strictly newer even on
        // coarse-resolution wall clocks.
        await Task.Delay(20);

        var rotate = await client.PostAsync(PortalRoute(appId, "rotate"), content: null);
        rotate.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotatedKey = (await ParseJsonAsync(rotate)).GetProperty("data").GetProperty("signingKey").GetString();

        rotatedKey.Should().NotBe(enabledKey);
        var secondRotatedAt = await ReadRotatedAtAsync(appId);
        secondRotatedAt.Should().NotBeNull();
        secondRotatedAt!.Value.Should().BeAfter(firstRotatedAt!.Value);
    }

    [Fact]
    public async Task Disable_Clears_SigningKey_But_Preserves_Origins()
    {
        // Operator behaviour: disable revokes auth (signing key + rotated-at)
        // but keeps the CORS allowlist so re-enable doesn't force the
        // operator to re-curate origins. Explicit clear is via /portal/origins.
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(
            portalEnabled: true,
            allowedOriginsJson: "[\"https://app.acme.com\"]");
        using var client = await CreateAuthenticatedClientAsync();

        var disable = await client.PostAsync(PortalRoute(appId, "disable"), content: null);
        disable.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await ExecuteDbAsync(async db =>
        {
            var app = await db.Applications.AsNoTracking().FirstAsync(a => a.Id == appId);
            app.PortalSigningKey.Should().BeNull();
            app.PortalRotatedAt.Should().BeNull();
            app.AllowedPortalOriginsJson.Should().Be("[\"https://app.acme.com\"]");
        });
    }

    [Fact]
    public async Task Update_Origins_Persists_Lowercased_Values()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: true);
        using var client = await CreateAuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync(PortalRoute(appId, "origins"), new
        {
            origins = new[] { "HTTPS://APP.ACME.COM", "https://staging.acme.com" }
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        await ExecuteDbAsync(async db =>
        {
            var app = await db.Applications.AsNoTracking().FirstAsync(a => a.Id == appId);
            app.AllowedPortalOriginsJson.Should().NotBeNullOrEmpty();
            var stored = JsonSerializer.Deserialize<string[]>(app.AllowedPortalOriginsJson!);
            stored.Should().BeEquivalentTo(new[] { "https://app.acme.com", "https://staging.acme.com" });
        });
    }

    [Fact]
    public async Task Update_Origins_Returns_422_On_Wildcard()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: true);
        using var client = await CreateAuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync(PortalRoute(appId, "origins"), new
        {
            origins = new[] { "https://*.acme.com" }
        });

        put.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Update_Origins_Returns_409_When_Portal_Not_Enabled()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync(PortalRoute(appId, "origins"), new
        {
            origins = new[] { "https://app.acme.com" }
        });

        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorCodeAsync(put)).Should().Be("PORTAL_NOT_ENABLED");
    }

    [Fact]
    public async Task Audit_Log_Never_Contains_SigningKey()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var enable = await client.PostAsync(PortalRoute(appId, "enable"), content: null);
        enable.StatusCode.Should().Be(HttpStatusCode.OK);

        await ExecuteDbAsync(async db =>
        {
            var rows = await db.AuditLogs
                .AsNoTracking()
                .Where(l => l.ResourceId == appId && l.Action == "application.portal.enabled")
                .ToListAsync();

            rows.Should().HaveCount(1);
            var row = rows[0];
            (row.BeforeJson ?? string.Empty).Should().NotContain("whsec_",
                "the audit snapshot must never include the portal signing key");
            (row.AfterJson ?? string.Empty).Should().NotContain("whsec_",
                "the audit snapshot must never include the portal signing key");
        });
    }

    [Fact]
    public async Task Get_Portal_Returns_Enabled_State_Without_Key()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var enable = await client.PostAsync(PortalRoute(appId, "enable"), content: null);
        enable.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await client.GetAsync(PortalRoute(appId));
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ParseJsonAsync(read)).GetProperty("data");

        data.GetProperty("portalEnabled").GetBoolean().Should().BeTrue();
        data.GetProperty("signingKey").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("rotatedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_Portal_Returns_Disabled_State_For_Fresh_App()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var read = await client.GetAsync(PortalRoute(appId));
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ParseJsonAsync(read)).GetProperty("data");

        data.GetProperty("portalEnabled").GetBoolean().Should().BeFalse();
        data.GetProperty("signingKey").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("rotatedAt").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("allowedOrigins").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Cache_Invalidation_Reflects_Disabled_State_Immediately()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        // Enable portal — this is the path the production code uses to populate
        // the cache miss → DB hit on the first portal request.
        var enable = await client.PostAsync(PortalRoute(appId, "enable"), content: null);
        enable.StatusCode.Should().Be(HttpStatusCode.OK);

        // Warm the cache by directly resolving the lookup. After this, the cache
        // holds the enabled state for `LookupCacheTtlSeconds` (60s default).
        await WarmCacheAsync(appId);

        // Disable through the controller — this MUST call PortalLookupCache.InvalidateApplication.
        var disable = await client.PostAsync(PortalRoute(appId, "disable"), content: null);
        disable.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Re-resolve — the cache must already see the disabled state, NOT the
        // stale enabled lookup that's still within the TTL window.
        var lookup = await ResolveCacheAsync(appId);
        lookup.Should().BeNull("the controller must invalidate the cache on disable, not wait for TTL");
    }

    [Fact]
    public async Task Enable_Returns_404_When_Application_Missing()
    {
        // Frozen contract: a non-existent appId yields the project's standard
        // dashboard `NOT_FOUND` envelope (matches DashboardEndpointController).
        await ResetDatabaseAsync();
        using var client = await CreateAuthenticatedClientAsync();
        var unknownAppId = Guid.NewGuid();

        var response = await client.PostAsync(PortalRoute(unknownAppId, "enable"), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Disable_On_Already_Disabled_Application_Is_Idempotent()
    {
        // Frozen contract: disable on an already-disabled app is a no-op success
        // (204), not a 409. Rationale: the operator's intent ("portal off") is
        // already satisfied; surfacing 409 would force them to script around a
        // benign condition. Cache invalidation still fires (defensive) but the
        // row write is a no-op on already-null columns.
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync(portalEnabled: false);
        using var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(PortalRoute(appId, "disable"), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Plumbing ──────────────────────────────────────

    private static string PortalRoute(Guid appId, string? action = null)
    {
        return action is null
            ? $"/api/v1/dashboard/applications/{appId}/portal"
            : $"/api/v1/dashboard/applications/{appId}/portal/{action}";
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
            if (!await db.DashboardUsers.AnyAsync(u => u.Email == DashboardEmail))
            {
                db.DashboardUsers.Add(new DashboardUser
                {
                    Email = DashboardEmail,
                    PasswordHash = PasswordHasher.HashPassword(DashboardPassword),
                    Role = "admin"
                });
                await db.SaveChangesAsync();
            }
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = DashboardEmail,
            password = DashboardPassword
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<(Guid AppId, ApplicationEntity App)> SeedAppAsync(
        bool portalEnabled = false,
        string? allowedOriginsJson = null)
    {
        var appId = Guid.NewGuid();
        var app = new ApplicationEntity
        {
            Id = appId,
            Name = $"app-{appId:N}",
            ApiKeyPrefix = $"whe_{appId:N}".Substring(0, 12) + "_",
            ApiKeyHash = "deadbeef",
            SigningSecret = "whsec_test",
            PortalSigningKey = portalEnabled ? "whsec_" + Convert.ToBase64String(new byte[32]) : null,
            AllowedPortalOriginsJson = allowedOriginsJson,
            PortalRotatedAt = portalEnabled ? DateTime.UtcNow : null,
            IsActive = true
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        db.Applications.Add(app);
        await db.SaveChangesAsync();
        return (appId, app);
    }

    private async Task<DateTime?> ReadRotatedAtAsync(Guid appId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var app = await db.Applications.AsNoTracking().FirstAsync(a => a.Id == appId);
        return app.PortalRotatedAt;
    }

    private async Task ExecuteDbAsync(Func<WebhookDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await action(db);
    }

    private async Task WarmCacheAsync(Guid appId)
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<PortalLookupCache>();
        await cache.GetAsync(appId, CancellationToken.None);
    }

    private async Task<PortalAppLookup?> ResolveCacheAsync(Guid appId)
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<PortalLookupCache>();
        return await cache.GetAsync(appId, CancellationToken.None);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString();
    }
}
