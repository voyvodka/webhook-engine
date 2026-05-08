using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.API.Auth;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.API.Tests.Integration;

public class EndpointDnsCheckTests : IClassFixture<TestWebApplicationFactory>
{
    // RFC 6761 reserves `.invalid` so DNS lookups against it always fail —
    // the perfect deterministic stand-in for "this hostname does not exist".
    private const string UnresolvableUrl = "https://does-not-exist.invalid/webhook";

    private const string DashboardEmail = "admin@test.local";
    private const string DashboardPassword = "P@ssw0rd-123";

    private readonly TestWebApplicationFactory _factory;

    public EndpointDnsCheckTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Public_Create_Endpoint_Returns_422_When_Hostname_Cannot_Be_Resolved()
    {
        await ResetDatabaseAsync();
        using var client = CreatePublicClient();
        var (_, apiKey) = await SeedApplicationAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.PostAsJsonAsync("/api/v1/endpoints", new
        {
            url = UnresolvableUrl
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Public_Update_Endpoint_Returns_422_When_Hostname_Cannot_Be_Resolved()
    {
        await ResetDatabaseAsync();
        using var client = CreatePublicClient();
        var (appId, apiKey) = await SeedApplicationAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Seed a real endpoint directly so the update has a row to mutate,
        // bypassing the create-time validator we're not exercising here.
        var endpointId = await SeedEndpointAsync(appId);

        var response = await client.PutAsJsonAsync($"/api/v1/endpoints/{endpointId}", new
        {
            url = UnresolvableUrl
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Dashboard_Create_Endpoint_Returns_422_When_Hostname_Cannot_Be_Resolved()
    {
        await ResetDatabaseAsync();
        using var client = CreateDashboardClient();
        await AuthenticateDashboardAsync(client);
        var (appId, _) = await SeedApplicationAsync();

        var response = await client.PostAsJsonAsync("/api/v1/dashboard/endpoints", new
        {
            appId,
            url = UnresolvableUrl
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Plumbing ───────────────────────────────────────

    private HttpClient CreatePublicClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    private HttpClient CreateDashboardClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task AuthenticateDashboardAsync(HttpClient client)
    {
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

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = DashboardEmail,
            password = DashboardPassword
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(Guid AppId, string ApiKey)> SeedApplicationAsync()
    {
        var appId = Guid.NewGuid();
        var appShort = appId.ToString("N")[..8];
        var apiKey = $"whe_{appShort}_{Guid.NewGuid():N}";
        var apiKeyPrefix = $"whe_{appShort}_";
        var apiKeyHash = ComputeSha256(apiKey);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        db.Applications.Add(new ApplicationEntity
        {
            Id = appId,
            Name = $"App-{appShort}",
            ApiKeyPrefix = apiKeyPrefix,
            ApiKeyHash = apiKeyHash,
            SigningSecret = "secret_test_dns",
            RetryPolicyJson = "{\"maxRetries\":7,\"backoffSchedule\":[5,30,120,900,3600,21600,86400]}",
            IsActive = true
        });

        await db.SaveChangesAsync();
        return (appId, apiKey);
    }

    private async Task<Guid> SeedEndpointAsync(Guid appId)
    {
        var endpointId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        db.Endpoints.Add(new Endpoint
        {
            Id = endpointId,
            AppId = appId,
            Url = "https://example.com/webhook",
            Status = EndpointStatus.Active
        });

        await db.SaveChangesAsync();
        return endpointId;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
