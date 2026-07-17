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

// A10 + A11 through the real HTTP pipeline on one representative list endpoint;
// the clamp and UTC normalization are shared, so this plus the unit tests suffice.
public class MessagesListInputBoundsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MessagesListInputBoundsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_With_Absurd_PageSize_Returns_200_And_Clamps_PageSize_To_100()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await SeedActiveAppAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var response = await client.GetAsync("/api/v1/messages?pageSize=2000000000");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an unbounded pageSize must not 500");
        var pagination = await ReadPaginationAsync(response);
        pagination.GetProperty("pageSize").GetInt32().Should().Be(100, "the clamp caps pageSize at MaxPageSize");
    }

    [Fact]
    public async Task List_With_Page_Zero_Returns_200_And_Floors_Page_To_1()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await SeedActiveAppAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var response = await client.GetAsync("/api/v1/messages?page=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "page=0 would produce a negative OFFSET pre-fix");
        var pagination = await ReadPaginationAsync(response);
        pagination.GetProperty("page").GetInt32().Should().Be(1, "the clamp floors page at 1");
    }

    [Fact]
    public async Task List_With_PageSize_Zero_Returns_200_And_Floors_PageSize_To_1()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await SeedActiveAppAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var response = await client.GetAsync("/api/v1/messages?pageSize=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "pageSize=0 must not divide-by-zero or 500");
        var pagination = await ReadPaginationAsync(response);
        pagination.GetProperty("pageSize").GetInt32().Should().Be(1, "the clamp floors pageSize at 1");
    }

    // InMemory EF bypasses Npgsql, so it does NOT reproduce the pre-fix raw 500
    // (Unspecified -> timestamptz) — TimestampBounds.AsUtc unit test is the real guard.
    [Fact]
    public async Task List_With_Naked_After_Date_Returns_200_And_Filters_By_Normalized_Utc()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var app = await SeedActiveAppAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.ApiKey);

        var messageId = await SeedMessageAsync(app.AppId, new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc));

        var inRange = await client.GetAsync("/api/v1/messages?after=2026-07-01");
        inRange.StatusCode.Should().Be(HttpStatusCode.OK, "a naked (timezone-less) after date must not 500");
        var inRangeIds = await ReadDataIdsAsync(inRange);
        inRangeIds.Should().ContainSingle().Which.Should().Be(messageId);

        var outOfRange = await client.GetAsync("/api/v1/messages?after=2026-08-01");
        outOfRange.StatusCode.Should().Be(HttpStatusCode.OK);
        var outOfRangeIds = await ReadDataIdsAsync(outOfRange);
        outOfRangeIds.Should().BeEmpty("the message predates the after filter");
    }

    // ── Helpers ─────────────────────────────────────────

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

    private async Task<(Guid AppId, string ApiKey)> SeedActiveAppAsync()
    {
        var appId = Guid.NewGuid();
        var appShort = appId.ToString("N")[..8];
        var apiKey = $"whe_{appShort}_{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        db.Applications.Add(new ApplicationEntity
        {
            Id = appId,
            Name = $"App-{appShort}",
            ApiKeyPrefix = $"whe_{appShort}_",
            ApiKeyHash = ComputeSha256(apiKey),
            SigningSecret = "secret_test_123",
            RetryPolicyJson = "{\"maxRetries\":7,\"backoffSchedule\":[5,30,120,900,3600,21600,86400]}",
            IsActive = true
        });
        await db.SaveChangesAsync();

        return (appId, apiKey);
    }

    private async Task<Guid> SeedMessageAsync(Guid appId, DateTime createdAtUtc)
    {
        var endpointId = Guid.NewGuid();
        var eventTypeId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
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
            Name = "order.created"
        });
        db.Messages.Add(new Message
        {
            Id = messageId,
            AppId = appId,
            EndpointId = endpointId,
            EventTypeId = eventTypeId,
            Payload = "{}",
            Status = MessageStatus.Delivered,
            MaxRetries = 7,
            CreatedAt = createdAtUtc,
            ScheduledAt = createdAtUtc
        });
        await db.SaveChangesAsync();

        return messageId;
    }

    private static async Task<JsonElement> ReadPaginationAsync(HttpResponseMessage response)
    {
        var json = await ParseJsonAsync(response);
        return json.GetProperty("meta").GetProperty("pagination");
    }

    private static async Task<List<Guid>> ReadDataIdsAsync(HttpResponseMessage response)
    {
        var json = await ParseJsonAsync(response);
        return json.GetProperty("data")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
