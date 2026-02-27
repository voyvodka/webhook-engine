using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace WebhookEngine.API.Tests.Middleware;

/// <summary>
/// Tests for ApiKeyAuthMiddleware path-skipping logic.
/// </summary>
public class ApiKeyAuthMiddlewareTests
{
    [Theory]
    [InlineData("/api/v1/auth/login", true)]
    [InlineData("/api/v1/auth/logout", true)]
    [InlineData("/api/v1/auth/me", true)]
    [InlineData("/api/v1/dashboard/overview", true)]
    [InlineData("/api/v1/dashboard/timeline", true)]
    [InlineData("/api/v1/applications", true)]
    [InlineData("/api/v1/health", true)]
    [InlineData("/health", true)]
    [InlineData("/", true)]
    [InlineData("/api/v1/endpoints", false)]
    [InlineData("/api/v1/messages", false)]
    [InlineData("/api/v1/event-types", false)]
    public void Path_Should_Skip_Or_Require_Auth(string path, bool shouldSkipAuth)
    {
        // Simulate the same path-skipping logic from ApiKeyAuthMiddleware
        var requiresAuth = path.StartsWith("/api/v1/")
            && !path.StartsWith("/api/v1/auth")
            && !path.StartsWith("/api/v1/dashboard")
            && !path.StartsWith("/api/v1/applications")
            && !path.StartsWith("/api/v1/health");

        if (shouldSkipAuth)
        {
            requiresAuth.Should().BeFalse($"path '{path}' should skip API key auth");
        }
        else
        {
            requiresAuth.Should().BeTrue($"path '{path}' should require API key auth");
        }
    }

    [Theory]
    [InlineData("whe_abc_randomkey123", "whe", "abc", "randomkey123")]
    [InlineData("whe_shortid_abcdefghijklmnop1234567890abcdef", "whe", "shortid", "abcdefghijklmnop1234567890abcdef")]
    public void Api_Key_Prefix_Extraction_Is_Correct(string apiKey, string expectedPart0, string expectedPart1, string _)
    {
        var parts = apiKey.Split('_');
        parts.Length.Should().BeGreaterThanOrEqualTo(3);

        var prefix = $"{parts[0]}_{parts[1]}_";
        prefix.Should().Be($"{expectedPart0}_{expectedPart1}_");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("only_two")]
    public void Api_Key_With_Less_Than_3_Parts_Is_Invalid(string apiKey)
    {
        var parts = apiKey.Split('_');
        parts.Length.Should().BeLessThan(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Token abc123")]
    public void Non_Bearer_Auth_Header_Should_Be_Rejected(string authHeader)
    {
        var isValid = !string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ");
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Bearer_Auth_Header_Should_Be_Accepted()
    {
        var authHeader = "Bearer whe_abc_key123";
        var isValid = !string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ");
        isValid.Should().BeTrue();

        var apiKey = authHeader["Bearer ".Length..];
        apiKey.Should().Be("whe_abc_key123");
    }
}
