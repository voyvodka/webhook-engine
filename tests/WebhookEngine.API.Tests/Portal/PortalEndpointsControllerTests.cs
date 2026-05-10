using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using WebhookEngine.API.Tests.Integration;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Data;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;
using EndpointEntity = WebhookEngine.Core.Entities.Endpoint;

namespace WebhookEngine.API.Tests.Portal;

/// <summary>
/// End-to-end exercise of <c>PortalEndpointsController</c> through the production
/// pipeline: portal JWT middleware → MVC → controller → repository. Uses the
/// shared <see cref="TestWebApplicationFactory"/> so the request travels through
/// the real middleware ordering rather than an isolated test harness.
/// </summary>
public class PortalEndpointsControllerTests : IClassFixture<PortalEndpointsControllerTests.PortalTestFactory>
{
    private const string PortalRoot = "/api/v1/portal";

    private readonly PortalTestFactory _factory;

    public PortalEndpointsControllerTests(PortalTestFactory factory)
    {
        _factory = factory;
    }

    // ── Read paths ─────────────────────────────────────

    [Fact]
    public async Task Portal_List_Endpoints_Returns_App_Scoped_Rows_With_Secrets_Stripped()
    {
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync(secretOverride: "whsec_top_secret");
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        var response = await client.GetAsync($"{PortalRoot}/endpoints");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ParseJsonAsync(response)).GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        var row = data[0];
        row.GetProperty("id").GetGuid().Should().Be(endpointId);
        // The portal list shape never carries the override value or full custom-header values.
        row.TryGetProperty("secretOverride", out _).Should().BeFalse();
        row.GetProperty("hasSecretOverride").GetBoolean().Should().BeTrue();
        row.GetProperty("customHeaderNames").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Portal_Get_Endpoint_Strips_Transform_And_AllowedIps_Fields()
    {
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync(
            transformExpression: "@",
            transformEnabled: true,
            allowedIpsJson: "[\"203.0.113.0/24\"]");
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        var response = await client.GetAsync($"{PortalRoot}/endpoints/{endpointId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ParseJsonAsync(response)).GetProperty("data");
        data.TryGetProperty("transformExpression", out _).Should().BeFalse();
        data.TryGetProperty("transformEnabled", out _).Should().BeFalse();
        data.TryGetProperty("transformValidatedAt", out _).Should().BeFalse();
        data.TryGetProperty("allowedIpsJson", out _).Should().BeFalse();
        data.TryGetProperty("allowedIps", out _).Should().BeFalse();
    }

    // ── Write paths ────────────────────────────────────

    [Fact]
    public async Task Portal_Create_Endpoint_Persists_Without_Transform_Or_AllowedIps()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync();
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        // Transform / allowed-ips are smuggled into the body; model binding must drop them.
        var response = await client.PostAsJsonAsync($"{PortalRoot}/endpoints", new
        {
            url = "https://example.com/portal-create",
            description = "from portal",
            transformExpression = "@",
            transformEnabled = true,
            allowedIpsJson = "[\"203.0.113.0/24\"]"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdId = (await ParseJsonAsync(response))
            .GetProperty("data").GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var persisted = await db.Endpoints.AsNoTracking().FirstAsync(e => e.Id == createdId);

        persisted.AppId.Should().Be(appId);
        persisted.TransformExpression.Should().BeNull();
        persisted.TransformEnabled.Should().BeFalse();
        persisted.AllowedIpsJson.Should().BeNull();
    }

    [Fact]
    public async Task Portal_Update_Endpoint_Returns_200_And_Updates_Fields()
    {
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync();
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        var response = await client.PutAsJsonAsync($"{PortalRoot}/endpoints/{endpointId}", new
        {
            description = "updated by portal"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var persisted = await db.Endpoints.AsNoTracking().FirstAsync(e => e.Id == endpointId);
        persisted.Description.Should().Be("updated by portal");
    }

    [Fact]
    public async Task Portal_Enable_And_Disable_Endpoint_Toggle_Status()
    {
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync();
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        var disable = await client.PostAsync($"{PortalRoot}/endpoints/{endpointId}/disable", content: null);
        disable.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetEndpointStatusAsync(endpointId)).Should().Be(EndpointStatus.Disabled);

        var enable = await client.PostAsync($"{PortalRoot}/endpoints/{endpointId}/enable", content: null);
        enable.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetEndpointStatusAsync(endpointId)).Should().Be(EndpointStatus.Active);
    }

    [Fact]
    public async Task Portal_Delete_Endpoint_Returns_204()
    {
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync();
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        var response = await client.DeleteAsync($"{PortalRoot}/endpoints/{endpointId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var exists = await db.Endpoints.AsNoTracking().AnyAsync(e => e.Id == endpointId);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Portal_Test_Endpoint_Posts_Through_IEndpointTester()
    {
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync();
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        // Reset prior calls — the substitute is shared across tests in the fixture.
        _factory.EndpointTester.ClearReceivedCalls();

        var response = await client.PostAsJsonAsync($"{PortalRoot}/endpoints/{endpointId}/test", new
        {
            eventType = "order.created",
            payload = new { id = "evt_1" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.EndpointTester.Received(1).ExecuteAsync(
            Arg.Is<EndpointTestContext>(c =>
                c.Endpoint.Id == endpointId
                && c.Endpoint.AppId == appId
                && c.Application.Id == appId
                && c.Request.EventTypeName == "order.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Portal_Get_Attempts_Returns_Most_Recent_First_With_Pagination()
    {
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync();
        await SeedAttemptsAsync(appId, endpointId, count: 3);
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        var response = await client.GetAsync($"{PortalRoot}/endpoints/{endpointId}/attempts?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);
        var rows = json.GetProperty("data");
        rows.GetArrayLength().Should().Be(3);

        // Most-recent first → AttemptNumber sequence is reversed.
        rows[0].GetProperty("attemptNumber").GetInt32().Should().Be(3);
        rows[1].GetProperty("attemptNumber").GetInt32().Should().Be(2);
        rows[2].GetProperty("attemptNumber").GetInt32().Should().Be(1);

        var pagination = json.GetProperty("meta").GetProperty("pagination");
        pagination.GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    // ── Capability gating ──────────────────────────────

    [Fact]
    public async Task Portal_Read_Only_Token_Cannot_Create_An_Endpoint()
    {
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync();
        var token = MintToken(appId, "endpoints:read");
        using var client = CreateClient(token);

        var response = await client.PostAsJsonAsync($"{PortalRoot}/endpoints", new
        {
            url = "https://example.com/forbidden"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorCodeAsync(response)).Should().Be("PORTAL_INSUFFICIENT_CAPABILITY");
    }

    [Fact]
    public async Task Portal_Token_Without_AttemptsRead_Cannot_Read_Attempts()
    {
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync();
        var token = MintToken(appId, "endpoints:read");
        using var client = CreateClient(token);

        var response = await client.GetAsync($"{PortalRoot}/endpoints/{endpointId}/attempts");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorCodeAsync(response)).Should().Be("PORTAL_INSUFFICIENT_CAPABILITY");
    }

    // ── Cross-tenant guard ─────────────────────────────

    [Fact]
    public async Task Portal_Cannot_See_Other_Apps_Endpoint()
    {
        await ResetDatabaseAsync();
        var (appA, _) = await SeedAppAsync(name: "tenant-A");
        var (appB, otherEndpointId) = await SeedAppAndEndpointAsync(name: "tenant-B");

        // Token scoped to tenant A asks for tenant B's endpoint id.
        var tokenA = MintFullToken(appA);
        using var client = CreateClient(tokenA);

        var response = await client.GetAsync($"{PortalRoot}/endpoints/{otherEndpointId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorCodeAsync(response)).Should().Be("PORTAL_NOT_FOUND");
    }

    [Fact]
    public async Task Portal_Token_Without_EndpointsTest_Cannot_Send_Test()
    {
        // Highest-risk capability boundary: the test route makes outbound HTTP.
        // A token that holds endpoints:read+write but NOT endpoints:test must
        // be rejected so a leaked read/write token cannot be used to fan out
        // arbitrary HTTP requests through the engine.
        await ResetDatabaseAsync();
        var (appId, endpointId) = await SeedAppAndEndpointAsync();
        var token = MintToken(appId, "endpoints:read", "endpoints:write");
        using var client = CreateClient(token);

        var body = new { eventType = "test.event", payload = new { } };
        var response = await client.PostAsJsonAsync($"{PortalRoot}/endpoints/{endpointId}/test", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorCodeAsync(response)).Should().Be("PORTAL_INSUFFICIENT_CAPABILITY");
    }

    [Fact]
    public async Task Portal_Cannot_Update_Other_Apps_Endpoint()
    {
        // Cross-tenant write smoke: even with full write capability, a token
        // scoped to tenant A must not silently mutate or 204 a tenant B
        // endpoint. The 2-arg app-scoped GetByIdAsync inside Update is the
        // only thing standing between this and a quiet cross-tenant write.
        await ResetDatabaseAsync();
        var (appA, _) = await SeedAppAsync(name: "tenant-A");
        var (appB, otherEndpointId) = await SeedAppAndEndpointAsync(name: "tenant-B");

        var tokenA = MintFullToken(appA);
        using var client = CreateClient(tokenA);

        var body = new { url = "https://example.com/new" };
        var response = await client.PutAsJsonAsync($"{PortalRoot}/endpoints/{otherEndpointId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorCodeAsync(response)).Should().Be("PORTAL_NOT_FOUND");
    }

    [Fact]
    public async Task Portal_Create_With_Weak_Secret_Override_Returns_422()
    {
        // SecretOverride entropy floor: a customer typing "password123" as
        // their HMAC secret silently undermines every signed delivery's
        // authenticity guarantee. The validator requires the whsec_ prefix
        // and a 32-char minimum so a hand-typed weak secret is rejected
        // before it touches the database.
        await ResetDatabaseAsync();
        var (appId, _) = await SeedAppAsync();
        var token = MintFullToken(appId);
        using var client = CreateClient(token);

        var body = new
        {
            url = "https://example.com/hook",
            secretOverride = "password123" // missing prefix + too short
        };
        var response = await client.PostAsJsonAsync($"{PortalRoot}/endpoints", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Plumbing ──────────────────────────────────────

    private HttpClient CreateClient(string bearerToken)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    private static string MintFullToken(Guid appId) => MintToken(
        appId,
        "endpoints:read",
        "endpoints:write",
        "endpoints:test",
        "attempts:read");

    private static string MintToken(Guid appId, params string[] capabilities)
        => PortalJwtFactory.Mint(appId, capabilities);

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<(Guid AppId, Guid EndpointId)> SeedAppAndEndpointAsync(
        string? name = null,
        string? secretOverride = null,
        string? transformExpression = null,
        bool transformEnabled = false,
        string? allowedIpsJson = null)
    {
        var (appId, _) = await SeedAppAsync(name);
        var endpointId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        db.Endpoints.Add(new EndpointEntity
        {
            Id = endpointId,
            AppId = appId,
            Url = "https://example.invalid/seeded",
            Status = EndpointStatus.Active,
            SecretOverride = secretOverride,
            TransformExpression = transformExpression,
            TransformEnabled = transformEnabled,
            AllowedIpsJson = allowedIpsJson
        });
        await db.SaveChangesAsync();
        return (appId, endpointId);
    }

    private async Task<(Guid AppId, ApplicationEntity App)> SeedAppAsync(string? name = null)
    {
        var appId = Guid.NewGuid();
        var app = new ApplicationEntity
        {
            Id = appId,
            Name = name ?? $"app-{appId:N}",
            ApiKeyPrefix = $"whe_{appId:N}".Substring(0, 12) + "_",
            ApiKeyHash = "deadbeef",
            SigningSecret = "whsec_test",
            PortalSigningKey = PortalJwtFactory.ValidSigningKey,
            IsActive = true
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        db.Applications.Add(app);
        await db.SaveChangesAsync();
        return (appId, app);
    }

    private async Task SeedAttemptsAsync(Guid appId, Guid endpointId, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var eventType = new EventType { Id = Guid.NewGuid(), AppId = appId, Name = "evt" };
        var message = new Message
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventType.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        db.EventTypes.Add(eventType);
        db.Messages.Add(message);
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        for (int i = 1; i <= count; i++)
        {
            db.MessageAttempts.Add(new MessageAttempt
            {
                Id = Guid.NewGuid(),
                MessageId = message.Id,
                EndpointId = endpointId,
                AttemptNumber = i,
                Status = AttemptStatus.Success,
                StatusCode = 200,
                LatencyMs = 42,
                // Strict ordering so the "most-recent first" assertion is unambiguous.
                CreatedAt = baseTime.AddSeconds(i)
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<EndpointStatus> GetEndpointStatusAsync(Guid endpointId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var endpoint = await db.Endpoints.AsNoTracking().FirstAsync(e => e.Id == endpointId);
        return endpoint.Status;
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

    /// <summary>
    /// Production <see cref="TestWebApplicationFactory"/> with the live
    /// <see cref="IEndpointTester"/> swapped for an NSubstitute fake. Lets the
    /// "send test" route exercise the full controller path without a real HTTP
    /// dial, and lets the test assert call arguments.
    /// </summary>
    public sealed class PortalTestFactory : TestWebApplicationFactory
    {
        public IEndpointTester EndpointTester { get; } = Substitute.For<IEndpointTester>();

        public PortalTestFactory()
        {
            EndpointTester
                .ExecuteAsync(Arg.Any<EndpointTestContext>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new EndpointTestResult
                {
                    Success = true,
                    StatusCode = 200,
                    LatencyMs = 7,
                    ResponseBody = "{}"
                }));
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                // Replace the live tester so the portal /test route doesn't dial out.
                services.RemoveAll<IEndpointTester>();
                services.AddSingleton(EndpointTester);
            });
        }
    }
}
