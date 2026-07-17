using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace WebhookEngine.API.Tests.Integration;

// B7 regression: POST /login carries [EnableRateLimiting("login")]. The limiter stays
// wired in the Testing host — TestWebApplicationFactory strips only hosted services and
// app.UseRateLimiter() runs in every environment.
public class LoginRateLimitTests : IDisposable
{
    // Fresh host per method: the "login" fixed-window limiter is a host singleton and its
    // 60 s window would bleed token state across methods under a shared IClassFixture.
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Login_Sixth_Bad_Attempt_In_Window_Returns_429_With_RateLimit_Envelope()
    {
        using var client = _factory.CreateClient();
        var badCredentials = new { email = "nobody@test.local", password = "wrong-password" };

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var allowed = await client.PostAsJsonAsync("/api/v1/auth/login", badCredentials);
            allowed.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized,
                $"attempt #{attempt} is within the permit limit and must reach auth (bad creds → 401, not 429)");
        }

        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", badCredentials);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the 6th attempt in the window trips the login limiter");
        limited.Headers.Contains("Retry-After").Should().BeTrue("the OnRejected handler emits Retry-After for rejected leases");

        var payload = await limited.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(payload);
        json.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Login_Policy_Does_Not_Rate_Limit_The_Me_Endpoint_Under_Burst()
    {
        using var client = _factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/api/v1/auth/me");
            statuses.Add(response.StatusCode);
        }

        statuses.Should().NotContain(
            HttpStatusCode.TooManyRequests,
            "the 'login' policy is scoped to POST /login — /me carries no rate-limit attribute");
        statuses.Should().OnlyContain(s => s == HttpStatusCode.Unauthorized);
    }
}
