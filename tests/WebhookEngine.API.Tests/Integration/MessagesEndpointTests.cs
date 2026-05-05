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
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;
using ApplicationEntity = WebhookEngine.Core.Entities.Application;

namespace WebhookEngine.API.Tests.Integration;

public class MessagesEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MessagesEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Batch send ───────────────────────────────────────

    [Fact]
    public async Task Batch_Send_Accepts_All_Valid_Items_And_Reports_Per_Item_Status()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var response = await client.PostAsJsonAsync("/api/v1/messages/batch", new
        {
            messages = new[]
            {
                new { eventType = "order.created", payload = new { id = "1" } },
                new { eventType = "order.created", payload = new { id = "2" } },
                new { eventType = "order.created", payload = new { id = "3" } }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var json = await ParseJsonAsync(response);
        var data = json.GetProperty("data");
        data.GetProperty("totalEvents").GetInt32().Should().Be(3);
        data.GetProperty("acceptedEvents").GetInt32().Should().Be(3);
        data.GetProperty("rejectedEvents").GetInt32().Should().Be(0);
        data.GetProperty("totalEnqueuedMessages").GetInt32().Should().Be(3);
        data.GetProperty("results").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Batch_Send_Reports_Mixed_Success_When_One_Item_Has_Unknown_Event_Type()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var response = await client.PostAsJsonAsync("/api/v1/messages/batch", new
        {
            messages = new object[]
            {
                new { eventType = "order.created", payload = new { id = "1" } },
                new { eventType = "this.event.does.not.exist", payload = new { id = "2" } },
                new { eventType = "order.created", payload = new { id = "3" } }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var json = await ParseJsonAsync(response);
        var data = json.GetProperty("data");
        data.GetProperty("totalEvents").GetInt32().Should().Be(3);
        data.GetProperty("acceptedEvents").GetInt32().Should().Be(2);
        data.GetProperty("rejectedEvents").GetInt32().Should().Be(1);

        var results = data.GetProperty("results").EnumerateArray().ToList();
        results.Should().HaveCount(3);
        results[0].GetProperty("success").GetBoolean().Should().BeTrue();
        results[1].GetProperty("success").GetBoolean().Should().BeFalse();
        results[1].GetProperty("error").GetProperty("code").GetString().Should().NotBeNullOrEmpty();
        results[2].GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Batch_Send_Rejects_Empty_Messages_With_422()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var response = await client.PostAsJsonAsync("/api/v1/messages/batch", new
        {
            messages = Array.Empty<object>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Replay ──────────────────────────────────────────

    [Fact]
    public async Task Replay_Enqueues_New_Messages_For_Source_Candidates_In_Range()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var (endpointId, eventTypeId) = await GetEndpointAndEventTypeIdsAsync(app.AppId);

        var now = DateTime.UtcNow;
        await ExecuteDbAsync(async db =>
        {
            db.Messages.AddRange(
                CreateMessage(app.AppId, endpointId, eventTypeId, MessageStatus.Failed, now.AddMinutes(-30)),
                CreateMessage(app.AppId, endpointId, eventTypeId, MessageStatus.Failed, now.AddMinutes(-20)),
                // Out-of-range message — must not be replayed.
                CreateMessage(app.AppId, endpointId, eventTypeId, MessageStatus.Failed, now.AddDays(-3))
            );
            await db.SaveChangesAsync();
        });

        var response = await client.PostAsJsonAsync("/api/v1/messages/replay", new
        {
            eventType = "order.created",
            from = now.AddHours(-1),
            to = now,
            statuses = new[] { "failed" },
            maxMessages = 10
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var json = await ParseJsonAsync(response);
        var data = json.GetProperty("data");
        data.GetProperty("sourceCount").GetInt32().Should().Be(2);
        data.GetProperty("replayedCount").GetInt32().Should().Be(2);
        data.GetProperty("messageIds").GetArrayLength().Should().Be(2);

        // Each replay creates a *new* row with status=pending.
        await ExecuteDbAsync(async db =>
        {
            var pendingCount = await db.Messages.CountAsync(m => m.Status == MessageStatus.Pending);
            pendingCount.Should().Be(2);
        });
    }

    [Fact]
    public async Task Replay_Returns_Zero_When_No_Candidates_Match_The_Range()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var now = DateTime.UtcNow;
        var response = await client.PostAsJsonAsync("/api/v1/messages/replay", new
        {
            eventType = "order.created",
            from = now.AddHours(-1),
            to = now,
            statuses = new[] { "failed" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var json = await ParseJsonAsync(response);
        var data = json.GetProperty("data");
        data.GetProperty("sourceCount").GetInt32().Should().Be(0);
        data.GetProperty("replayedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Replay_Returns_422_When_Both_EventType_And_EventTypeId_Are_Missing()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await CreateApplicationWithEndpointAndEventTypeAsync("order.created");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var now = DateTime.UtcNow;
        var response = await client.PostAsJsonAsync("/api/v1/messages/replay", new
        {
            from = now.AddHours(-1),
            to = now
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
    }

    // ── Helpers ─────────────────────────────────────────

    private static Message CreateMessage(Guid appId, Guid endpointId, Guid eventTypeId, MessageStatus status, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        AppId = appId,
        EndpointId = endpointId,
        EventTypeId = eventTypeId,
        Payload = "{}",
        Status = status,
        AttemptCount = 1,
        MaxRetries = 7,
        CreatedAt = createdAt,
        ScheduledAt = createdAt
    };

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
