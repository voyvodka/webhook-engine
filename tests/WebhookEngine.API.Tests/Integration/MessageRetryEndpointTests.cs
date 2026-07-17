using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.API.Tests.Integration;

// A6 controller wiring for POST /api/v1/messages/{id}/retry, read-time gate only: 404
// and early-422 run before RetryAsync. The 200/409 branches sit behind RetryAsync's
// ExecuteUpdateAsync — unsupported by InMemory — and are covered by RetryCasGuardTests.
public class MessageRetryEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MessageRetryEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Retry_When_Message_Is_Delivered_Returns_422_Unprocessable()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var messageId = await SeedMessageAsync(app.AppId, MessageStatus.Delivered, attemptCount: 1);

        var response = await client.PostAsync($"/api/v1/messages/{messageId}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await ParseJsonAsync(response);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("UNPROCESSABLE");
    }

    // A Sending row is rejected at the read-time 422 gate, before RetryAsync is reached.
    [Fact]
    public async Task Retry_When_Message_Is_Sending_Returns_422_Unprocessable()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var messageId = await SeedMessageAsync(app.AppId, MessageStatus.Sending, attemptCount: 2);

        var response = await client.PostAsync($"/api/v1/messages/{messageId}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await ParseJsonAsync(response);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("UNPROCESSABLE");
    }

    [Fact]
    public async Task Retry_When_Message_Not_Found_Returns_404()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var response = await client.PostAsync($"/api/v1/messages/{Guid.NewGuid()}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await ParseJsonAsync(response);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    // ── Helpers ─────────────────────────────────────────

    private async Task<Guid> SeedMessageAsync(Guid appId, MessageStatus status, int attemptCount)
    {
        var (endpointId, eventTypeId) = await GetEndpointAndEventTypeIdsAsync(appId);
        var messageId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await ExecuteDbAsync(async db =>
        {
            db.Messages.Add(new Message
            {
                Id = messageId,
                AppId = appId,
                EndpointId = endpointId,
                EventTypeId = eventTypeId,
                Payload = "{}",
                Status = status,
                AttemptCount = attemptCount,
                MaxRetries = 7,
                LockedBy = status == MessageStatus.Sending ? "worker1" : null,
                LockedAt = status == MessageStatus.Sending ? now.AddMinutes(-1) : null,
                DeliveredAt = status == MessageStatus.Delivered ? now.AddMinutes(-1) : null,
                CreatedAt = now.AddMinutes(-10),
                ScheduledAt = now.AddMinutes(-10)
            });
            await db.SaveChangesAsync();
        });

        return messageId;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<(Guid AppId, string ApiKey)> CreateApplicationWithEndpointAndEventTypeAsync(string eventTypeName)
    {
        var appId = Guid.NewGuid();
        var appShort = appId.ToString("N")[..8];
        var apiKey = $"whe_{appShort}_{Guid.NewGuid():N}";
        var apiKeyPrefix = $"whe_{appShort}_";
        var apiKeyHash = ComputeSha256(apiKey);

        var endpointId = Guid.NewGuid();
        var eventTypeId = Guid.NewGuid();

        await ExecuteDbAsync(async db =>
        {
            db.Applications.Add(new ApplicationEntity
            {
                Id = appId,
                Name = $"App-{appShort}",
                ApiKeyPrefix = apiKeyPrefix,
                ApiKeyHash = apiKeyHash,
                SigningSecret = "secret_test_123",
                RetryPolicyJson = "{\"maxRetries\":7,\"backoffSchedule\":[5,30,120,900,3600,21600,86400]}",
                IsActive = true
            });

            db.Endpoints.Add(new Endpoint
            {
                Id = endpointId,
                AppId = appId,
                Url = "https://example.com/webhook",
                Status = EndpointStatus.Active
            });

            db.EventTypes.Add(new EventType
            {
                Id = eventTypeId,
                AppId = appId,
                Name = eventTypeName
            });

            await db.SaveChangesAsync();
        });

        return (appId, apiKey);
    }

    private async Task<(Guid EndpointId, Guid EventTypeId)> GetEndpointAndEventTypeIdsAsync(Guid appId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var endpointId = await db.Endpoints.Where(e => e.AppId == appId).Select(e => e.Id).FirstAsync();
        var eventTypeId = await db.EventTypes.Where(e => e.AppId == appId).Select(e => e.Id).FirstAsync();
        return (endpointId, eventTypeId);
    }

    private async Task ExecuteDbAsync(Func<WebhookDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await action(db);
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
