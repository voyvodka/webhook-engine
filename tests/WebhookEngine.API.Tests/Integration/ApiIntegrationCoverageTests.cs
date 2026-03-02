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

public class ApiIntegrationCoverageTests : IClassFixture<TestWebApplicationFactory>
{
    private const string DashboardEmail = "admin@test.local";
    private const string DashboardPassword = "P@ssw0rd-123";

    private readonly TestWebApplicationFactory _factory;

    public ApiIntegrationCoverageTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_EventType_Crud_Flow_Returns_Envelope_And_Archive_State()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        var appId = await CreateApplicationAsync("Dashboard App", isActive: true);

        var createResponse = await client.PostAsJsonAsync("/api/v1/dashboard/event-types", new
        {
            appId,
            name = "order.created",
            description = "Order creation event"
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createJson = await ParseJsonAsync(createResponse);
        var eventTypeId = createJson.GetProperty("data").GetProperty("id").GetGuid();
        createJson.GetProperty("data").GetProperty("name").GetString().Should().Be("order.created");
        createJson.GetProperty("meta").GetProperty("requestId").GetString().Should().StartWith("req_");

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/dashboard/event-types/{eventTypeId}", new
        {
            name = "order.created.v2",
            description = "Updated description"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateJson = await ParseJsonAsync(updateResponse);
        updateJson.GetProperty("data").GetProperty("name").GetString().Should().Be("order.created.v2");

        var listBeforeArchive = await client.GetAsync($"/api/v1/dashboard/event-types?appId={appId}&includeArchived=false");
        listBeforeArchive.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBeforeArchiveJson = await ParseJsonAsync(listBeforeArchive);
        var activeItems = listBeforeArchiveJson.GetProperty("data").EnumerateArray().ToList();
        activeItems.Should().ContainSingle(item => item.GetProperty("id").GetGuid() == eventTypeId);
        activeItems.Single(item => item.GetProperty("id").GetGuid() == eventTypeId)
            .GetProperty("isArchived").GetBoolean().Should().BeFalse();

        var archiveResponse = await client.DeleteAsync($"/api/v1/dashboard/event-types/{eventTypeId}");
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfterArchive = await client.GetAsync($"/api/v1/dashboard/event-types?appId={appId}&includeArchived=true");
        listAfterArchive.StatusCode.Should().Be(HttpStatusCode.OK);
        var listAfterArchiveJson = await ParseJsonAsync(listAfterArchive);
        var archivedItem = listAfterArchiveJson.GetProperty("data")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == eventTypeId);

        archivedItem.GetProperty("isArchived").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Dashboard_Messages_List_Applies_App_Endpoint_And_Date_Filters()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        var app1Id = await CreateApplicationAsync("App 1", isActive: true);
        var app2Id = await CreateApplicationAsync("App 2", isActive: true);

        var endpoint1 = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app1Id,
            Url = "https://example.com/ep-1",
            Status = EndpointStatus.Active
        };

        var endpoint2 = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app1Id,
            Url = "https://example.com/ep-2",
            Status = EndpointStatus.Active
        };

        var endpoint3 = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app2Id,
            Url = "https://example.com/ep-3",
            Status = EndpointStatus.Active
        };

        var eventType1 = new EventType
        {
            Id = Guid.NewGuid(),
            AppId = app1Id,
            Name = "order.created"
        };

        var eventType2 = new EventType
        {
            Id = Guid.NewGuid(),
            AppId = app2Id,
            Name = "invoice.created"
        };

        var now = DateTime.UtcNow;
        var insideWindowCreatedAt = now.AddMinutes(-10);
        var outsideWindowCreatedAt = now.AddDays(-2);

        var messageInScope = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app1Id,
            EndpointId = endpoint1.Id,
            EventTypeId = eventType1.Id,
            Payload = "{}",
            Status = MessageStatus.Failed,
            CreatedAt = insideWindowCreatedAt,
            ScheduledAt = insideWindowCreatedAt
        };

        var messageDifferentEndpoint = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app1Id,
            EndpointId = endpoint2.Id,
            EventTypeId = eventType1.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = insideWindowCreatedAt,
            ScheduledAt = insideWindowCreatedAt
        };

        var messageOutsideDateWindow = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app1Id,
            EndpointId = endpoint1.Id,
            EventTypeId = eventType1.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = outsideWindowCreatedAt,
            ScheduledAt = outsideWindowCreatedAt
        };

        var messageFromDifferentApp = new Message
        {
            Id = Guid.NewGuid(),
            AppId = app2Id,
            EndpointId = endpoint3.Id,
            EventTypeId = eventType2.Id,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            CreatedAt = insideWindowCreatedAt,
            ScheduledAt = insideWindowCreatedAt
        };

        await ExecuteDbAsync(async db =>
        {
            db.Endpoints.AddRange(endpoint1, endpoint2, endpoint3);
            db.EventTypes.AddRange(eventType1, eventType2);
            db.Messages.AddRange(messageInScope, messageDifferentEndpoint, messageOutsideDateWindow, messageFromDifferentApp);
            await db.SaveChangesAsync();
        });

        var response = await client.GetAsync(
            $"/api/v1/dashboard/messages?appId={app1Id}&endpointId={endpoint1.Id}&after={now.AddHours(-1):O}&before={now:O}&page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);

        var items = json.GetProperty("data").EnumerateArray().ToList();
        items.Should().ContainSingle();
        items[0].GetProperty("id").GetGuid().Should().Be(messageInScope.Id);

        var pagination = json.GetProperty("meta").GetProperty("pagination");
        pagination.GetProperty("totalCount").GetInt32().Should().Be(1);
        pagination.GetProperty("totalPages").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ApiKey_Middleware_Rejects_Inactive_Application_With_Expected_Error()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();

        var inactiveApp = await CreateApplicationWithApiKeyAsync("Inactive App", isActive: false);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", inactiveApp.ApiKey);
        var response = await client.GetAsync("/api/v1/messages");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = await ParseJsonAsync(response);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
        json.GetProperty("error").GetProperty("message").GetString().Should().Be("Application is inactive.");
    }

    [Fact]
    public async Task ApiKey_Middleware_Allows_Active_Application_And_Returns_Envelope()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();

        var activeApp = await CreateApplicationWithApiKeyAsync("Active App", isActive: true);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", activeApp.ApiKey);
        var response = await client.GetAsync("/api/v1/messages?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);

        json.TryGetProperty("data", out var data).Should().BeTrue();
        data.ValueKind.Should().Be(JsonValueKind.Array);

        var meta = json.GetProperty("meta");
        meta.GetProperty("requestId").GetString().Should().StartWith("req_");
        meta.TryGetProperty("pagination", out _).Should().BeTrue();
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private async Task ResetDatabaseAsync()
    {
        await ExecuteDbAsync(async db =>
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        });
    }

    private async Task AuthenticateDashboardAsync(HttpClient client)
    {
        await ExecuteDbAsync(async db =>
        {
            if (await db.DashboardUsers.AnyAsync(u => u.Email == DashboardEmail))
                return;

            db.DashboardUsers.Add(new DashboardUser
            {
                Email = DashboardEmail,
                PasswordHash = PasswordHasher.HashPassword(DashboardPassword),
                Role = "admin"
            });

            await db.SaveChangesAsync();
        });

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = DashboardEmail,
            password = DashboardPassword
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateApplicationAsync(string name, bool isActive)
    {
        var result = await CreateApplicationWithApiKeyAsync(name, isActive);
        return result.AppId;
    }

    private async Task<(Guid AppId, string ApiKey)> CreateApplicationWithApiKeyAsync(string name, bool isActive)
    {
        var appId = Guid.NewGuid();
        var appShort = appId.ToString("N")[..8];
        var apiKey = $"whe_{appShort}_{Guid.NewGuid():N}";
        var apiKeyPrefix = $"whe_{appShort}_";
        var apiKeyHash = ComputeSha256(apiKey);

        await ExecuteDbAsync(async db =>
        {
            db.Applications.Add(new ApplicationEntity
            {
                Id = appId,
                Name = name,
                ApiKeyPrefix = apiKeyPrefix,
                ApiKeyHash = apiKeyHash,
                SigningSecret = "secret_test_123",
                RetryPolicyJson = "{\"maxRetries\":7,\"backoffSchedule\":[5,30,120,900,3600,21600,86400]}",
                IsActive = isActive
            });

            await db.SaveChangesAsync();
        });

        return (appId, apiKey);
    }

    private async Task ExecuteDbAsync(Func<WebhookDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await action(db);
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(payload);
    }
}
