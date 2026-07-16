using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public class EndpointTestEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private const string DashboardEmail = "admin@test.local";
    private const string DashboardPassword = "P@ssw0rd-123";

    // RFC 6761 reserves `.invalid` so DNS resolution is guaranteed to fail. The
    // probe therefore exits the HTTP path at the resolver with a deterministic
    // failure result — no real network traffic, no test flakes.
    private const string ProbeEndpointUrl = "https://example.invalid/webhook";

    private readonly TestWebApplicationFactory _factory;

    public EndpointTestEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Public API ───────────────────────────────────────

    [Fact]
    public async Task Public_Test_Endpoint_Returns_Result_With_Signed_Request_Preview()
    {
        await ResetDatabaseAsync();
        using var client = CreatePublicClient();
        var (appId, apiKey, endpointId) = await SeedAppAndEndpointAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.PostAsJsonAsync($"/api/v1/endpoints/{endpointId}/test", new
        {
            eventType = "order.created",
            payload = new { id = "order_42", total = 99.95 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ParseJsonAsync(response)).GetProperty("data");

        // Probe is wired through the real HMAC + custom-header pipeline; the
        // example.invalid hostname guarantees a deterministic delivery failure
        // so we assert the failure path *and* that the preview survived round-trip.
        data.GetProperty("success").GetBoolean().Should().BeFalse();
        data.GetProperty("statusCode").GetInt32().Should().Be(0);
        data.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();

        var preview = data.GetProperty("request");
        preview.GetProperty("url").GetString().Should().Be(ProbeEndpointUrl);
        preview.GetProperty("body").GetString().Should().Contain("order_42");

        var headers = preview.GetProperty("headers");
        headers.TryGetProperty("webhook-id", out var webhookId).Should().BeTrue();
        webhookId.GetString().Should().StartWith("test_");
        headers.GetProperty("webhook-signature").GetString().Should().StartWith("v1,");
    }

    [Fact]
    public async Task Public_Test_Endpoint_Returns_Custom_Header_Values_Verbatim()
    {
        // Owner-facing counterpart to the portal redaction: the operator's own
        // credential path returns custom-header VALUES verbatim (masking is portal-only).
        await ResetDatabaseAsync();
        using var client = CreatePublicClient();
        const string headerValue = "operator-internal-token";
        var (_, apiKey, endpointId) = await SeedAppAndEndpointAsync(
            customHeadersJson: JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["X-Internal-Auth"] = headerValue
            }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.PostAsJsonAsync($"/api/v1/endpoints/{endpointId}/test", new
        {
            eventType = "order.created",
            payload = new { id = "order_42" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var headers = (await ParseJsonAsync(response))
            .GetProperty("data").GetProperty("request").GetProperty("headers");
        headers.GetProperty("X-Internal-Auth").GetString().Should().Be(headerValue);
    }

    [Fact]
    public async Task Public_Test_Endpoint_Falls_Back_To_Default_Payload_When_Body_Omitted()
    {
        await ResetDatabaseAsync();
        using var client = CreatePublicClient();
        var (appId, apiKey, endpointId) = await SeedAppAndEndpointAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Empty JSON body — both EventType and Payload are optional on the wire,
        // so the tester should fall back to its self-describing default payload.
        var response = await client.PostAsJsonAsync($"/api/v1/endpoints/{endpointId}/test", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ParseJsonAsync(response)).GetProperty("data");

        var body = data.GetProperty("request").GetProperty("body").GetString()!;
        body.Should().Contain("\"test\":true");
    }

    [Fact]
    public async Task Public_Test_Endpoint_Returns_404_For_Unknown_Endpoint()
    {
        await ResetDatabaseAsync();
        using var client = CreatePublicClient();
        var (_, apiKey, _) = await SeedAppAndEndpointAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.PostAsJsonAsync($"/api/v1/endpoints/{Guid.NewGuid()}/test", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Public_Test_Endpoint_Rejects_Oversized_Payload_With_422()
    {
        await ResetDatabaseAsync();
        using var client = CreatePublicClient();
        var (_, apiKey, endpointId) = await SeedAppAndEndpointAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // 257 KB string — one byte over the 256 KB probe cap once embedded in JSON.
        var oversized = new string('x', 257 * 1024);
        var response = await client.PostAsJsonAsync($"/api/v1/endpoints/{endpointId}/test", new
        {
            payload = new { blob = oversized }
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Dashboard API ───────────────────────────────────

    [Fact]
    public async Task Dashboard_Test_Endpoint_Returns_Result_With_Signed_Request_Preview()
    {
        await ResetDatabaseAsync();
        using var client = CreateDashboardClient();
        await AuthenticateDashboardAsync(client);
        var (_, _, endpointId) = await SeedAppAndEndpointAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/dashboard/endpoints/{endpointId}/test", new
        {
            eventType = "order.created",
            payload = new { hello = "world" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ParseJsonAsync(response)).GetProperty("data");

        data.GetProperty("success").GetBoolean().Should().BeFalse();
        data.GetProperty("request").GetProperty("body").GetString().Should().Contain("\"hello\":\"world\"");
    }

    [Fact]
    public async Task Dashboard_Test_Endpoint_Requires_Authentication()
    {
        await ResetDatabaseAsync();
        using var client = CreateDashboardClient();
        var (_, _, endpointId) = await SeedAppAndEndpointAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/dashboard/endpoints/{endpointId}/test", new { });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect, HttpStatusCode.Forbidden);
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

    private async Task<(Guid AppId, string ApiKey, Guid EndpointId)> SeedAppAndEndpointAsync(
        string? customHeadersJson = null)
    {
        var appId = Guid.NewGuid();
        var appShort = appId.ToString("N")[..8];
        var apiKey = $"whe_{appShort}_{Guid.NewGuid():N}";
        var apiKeyPrefix = $"whe_{appShort}_";
        var apiKeyHash = ComputeSha256(apiKey);
        var endpointId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();

        db.Applications.Add(new ApplicationEntity
        {
            Id = appId,
            Name = $"App-{appShort}",
            ApiKeyPrefix = apiKeyPrefix,
            ApiKeyHash = apiKeyHash,
            SigningSecret = "whsec_test_signing_secret_for_endpoint_probe",
            RetryPolicyJson = "{\"maxRetries\":7,\"backoffSchedule\":[5,30,120,900,3600,21600,86400]}",
            IsActive = true
        });

        db.Endpoints.Add(new Endpoint
        {
            Id = endpointId,
            AppId = appId,
            Url = ProbeEndpointUrl,
            Status = EndpointStatus.Active,
            CustomHeadersJson = customHeadersJson ?? "{}"
        });

        await db.SaveChangesAsync();
        return (appId, apiKey, endpointId);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
