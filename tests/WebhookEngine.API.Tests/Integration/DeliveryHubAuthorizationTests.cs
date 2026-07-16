using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.API.Auth;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Tests.Integration;

// Regression guard: DeliveryHub fans cross-tenant events to Clients.All, so its
// negotiate endpoint must reject anonymous callers (asserted over the plain-HTTP
// negotiate POST, which the auth pipeline gates before any hub code runs).
public class DeliveryHubAuthorizationTests : IClassFixture<TestWebApplicationFactory>
{
    private const string DashboardEmail = "hub-admin@test.local";
    private const string DashboardPassword = "P@ssw0rd-123";
    private const string NegotiatePath = "/hubs/deliveries/negotiate?negotiateVersion=1";

    private readonly TestWebApplicationFactory _factory;

    public DeliveryHubAuthorizationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Load-bearing: pre-fix this returned 200 and leaked every tenant's events.
    // Cookie auth 401s only /api paths; /hubs challenges fall through to a 302 login redirect.
    [Fact]
    public async Task DeliveryHub_Negotiate_Without_Cookie_Redirects_To_Login()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();

        var response = await client.PostAsync(NegotiatePath, new StringContent(string.Empty));

        response.StatusCode.Should().NotBe(
            HttpStatusCode.OK,
            "an anonymous client must never complete the negotiate handshake for the cross-tenant delivery hub");
        response.StatusCode.Should().Be(
            HttpStatusCode.Redirect,
            "cookie auth challenges non-/api paths with a 302 to the login page");

        response.Headers.Location.Should().NotBeNull();
        var location = response.Headers.Location!.ToString();
        location.Should().Contain("/Account/Login", "the challenge redirects to the dashboard login path");
        location.Should().Contain("hubs", "the return-url must round-trip the original hub negotiate path");
    }

    // Positive counterpart: a cookie-authed operator must still negotiate — this stops
    // anyone from silencing the negative test by breaking auth for everyone.
    [Fact]
    public async Task DeliveryHub_Negotiate_With_Cookie_Returns_200_OK()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        var response = await client.PostAsync(NegotiatePath, new StringContent(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("connectionId", "a successful negotiate returns a SignalR connection descriptor");
    }

    // ── Plumbing (mirrors AuditLoggingTests) ───────────────────────────────

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
}
