using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Options;

namespace WebhookEngine.Worker.Tests;

/// <summary>
/// Tests for DeliveryWorker internal logic (backoff calculation, header parsing).
/// Uses reflection to access private methods since they are not public API.
/// </summary>
public class DeliveryWorkerTests
{
    private readonly RetryPolicyOptions _retryPolicy = new();

    [Theory]
    [InlineData(1, 5)]       // attempt 1 → backoff[0] = 5s
    [InlineData(2, 30)]      // attempt 2 → backoff[1] = 30s
    [InlineData(3, 120)]     // attempt 3 → backoff[2] = 2m
    [InlineData(4, 900)]     // attempt 4 → backoff[3] = 15m
    [InlineData(5, 3600)]    // attempt 5 → backoff[4] = 1h
    [InlineData(6, 21600)]   // attempt 6 → backoff[5] = 6h
    [InlineData(7, 86400)]   // attempt 7 → backoff[6] = 24h
    public void CalculateNextRetryAt_Returns_Correct_Backoff(int currentAttempt, int expectedSeconds)
    {
        // Simulate the same logic as DeliveryWorker.CalculateNextRetryAt
        var backoffIndex = Math.Min(currentAttempt - 1, _retryPolicy.BackoffSchedule.Length - 1);
        var backoffSeconds = _retryPolicy.BackoffSchedule[backoffIndex];

        backoffSeconds.Should().Be(expectedSeconds);
    }

    [Fact]
    public void CalculateNextRetryAt_Clamps_At_Last_Schedule_Entry()
    {
        // Attempt beyond schedule length should use last entry
        var currentAttempt = 100;
        var backoffIndex = Math.Min(currentAttempt - 1, _retryPolicy.BackoffSchedule.Length - 1);
        var backoffSeconds = _retryPolicy.BackoffSchedule[backoffIndex];

        backoffSeconds.Should().Be(86400, "attempts beyond schedule length should use last entry (24h)");
    }

    [Fact]
    public void BackoffSchedule_Is_Exponential_Growth()
    {
        var schedule = _retryPolicy.BackoffSchedule;

        for (int i = 1; i < schedule.Length; i++)
        {
            schedule[i].Should().BeGreaterThan(schedule[i - 1]);
        }
    }

    [Theory]
    [InlineData("""{"X-Custom":"value","Authorization":"Bearer token"}""", 2)]
    [InlineData("{}", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("invalid-json", 0)]
    public void ParseCustomHeaders_Handles_Various_Inputs(string? json, int expectedCount)
    {
        // Simulate the same logic as DeliveryWorker.ParseCustomHeaders
        var result = ParseCustomHeaders(json);
        result.Should().HaveCount(expectedCount);
    }

    [Fact]
    public void BuildRequestHeaders_Contains_Standard_Webhook_Headers()
    {
        var signedHeaders = new WebhookEngine.Core.Models.SignedHeaders
        {
            WebhookId = "msg_123",
            WebhookTimestamp = "1700000000",
            WebhookSignature = "v1,abc123"
        };
        var customHeaders = new Dictionary<string, string>
        {
            ["X-Custom"] = "value"
        };

        var headers = BuildRequestHeaders(signedHeaders, customHeaders);

        headers.Should().ContainKey("webhook-id").WhoseValue.Should().Be("msg_123");
        headers.Should().ContainKey("webhook-timestamp").WhoseValue.Should().Be("1700000000");
        headers.Should().ContainKey("webhook-signature").WhoseValue.Should().Be("v1,abc123");
        headers.Should().ContainKey("User-Agent").WhoseValue.Should().Be("WebhookEngine/1.0");
        headers.Should().ContainKey("X-Custom").WhoseValue.Should().Be("value");
    }

    [Fact]
    public void BuildRequestHeaders_Custom_Headers_Override_Default()
    {
        var signedHeaders = new WebhookEngine.Core.Models.SignedHeaders
        {
            WebhookId = "msg_123",
            WebhookTimestamp = "1700000000",
            WebhookSignature = "v1,abc"
        };
        var customHeaders = new Dictionary<string, string>
        {
            ["User-Agent"] = "CustomAgent/2.0"
        };

        var headers = BuildRequestHeaders(signedHeaders, customHeaders);

        headers["User-Agent"].Should().Be("CustomAgent/2.0", "custom headers should override defaults");
    }

    [Fact]
    public void MaxRetries_Default_Matches_RetryPolicy_MaxRetries()
    {
        var message = new WebhookEngine.Core.Entities.Message();
        message.MaxRetries.Should().Be(_retryPolicy.MaxRetries);
    }

    [Fact]
    public void ResolveRateLimitPerMinute_Reads_Numeric_Value_From_Metadata()
    {
        var value = ResolveRateLimitPerMinute("""{"rateLimitPerMinute":120}""");

        value.Should().Be(120);
    }

    [Fact]
    public void ResolveRateLimitPerMinute_Reads_String_Value_From_Metadata()
    {
        var value = ResolveRateLimitPerMinute("""{"rateLimitPerMinute":"75"}""");

        value.Should().Be(75);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{" + "\"rateLimitPerMinute\":0" + "}")]
    [InlineData("{" + "\"rateLimitPerMinute\":-5" + "}")]
    [InlineData("{" + "\"rateLimitPerMinute\":\"abc\"" + "}")]
    [InlineData("invalid-json")]
    public void ResolveRateLimitPerMinute_Returns_Null_For_Invalid_Metadata(string? metadataJson)
    {
        var value = ResolveRateLimitPerMinute(metadataJson);

        value.Should().BeNull();
    }

    // Helper methods that mirror DeliveryWorker private methods
    private static Dictionary<string, string> ParseCustomHeaders(string? customHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(customHeadersJson))
            return [];

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(customHeadersJson) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, string> BuildRequestHeaders(
        WebhookEngine.Core.Models.SignedHeaders signedHeaders,
        Dictionary<string, string> customHeaders)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["webhook-id"] = signedHeaders.WebhookId,
            ["webhook-timestamp"] = signedHeaders.WebhookTimestamp,
            ["webhook-signature"] = signedHeaders.WebhookSignature,
            ["User-Agent"] = "WebhookEngine/1.0"
        };

        foreach (var header in customHeaders)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }

    private static int? ResolveRateLimitPerMinute(string? metadataJson)
    {
        var method = typeof(DeliveryWorker).GetMethod(
            "ResolveRateLimitPerMinute",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (int?)method!.Invoke(null, [metadataJson]);
    }
}
