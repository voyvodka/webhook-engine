using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.API.Auth;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Tests.Integration;

public class TransformValidateEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private const string DashboardEmail = "admin@test.local";
    private const string DashboardPassword = "P@ssw0rd-123";

    private readonly TestWebApplicationFactory _factory;

    public TransformValidateEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Validate_Returns_Transformed_Payload_For_Valid_Expression()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/dashboard/transform/validate", new
        {
            expression = "{ id: order.id }",
            samplePayload = """{"order":{"id":"abc-123","total":99}}"""
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);
        var data = json.GetProperty("data");
        data.GetProperty("success").GetBoolean().Should().BeTrue();
        data.GetProperty("transformed").GetString().Should().Contain("\"id\"");
        data.GetProperty("transformed").GetString().Should().Contain("abc-123");
    }

    [Fact]
    public async Task Validate_Returns_Failure_With_Error_For_Invalid_Expression()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/dashboard/transform/validate", new
        {
            expression = "}}}invalid{{{",
            samplePayload = """{"a":1}"""
        });

        // The endpoint always returns 200 OK; success/error is encoded in the body
        // so the dashboard editor renders both outcomes through the same path.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);
        var data = json.GetProperty("data");
        data.GetProperty("success").GetBoolean().Should().BeFalse();
        data.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Validate_Returns_422_When_Expression_Is_Empty()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/dashboard/transform/validate", new
        {
            expression = "",
            samplePayload = """{"a":1}"""
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Validate_Requires_Dashboard_Authentication()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        // Note: no AuthenticateDashboardAsync — request must be rejected by the
        // cookie-auth scheme before reaching the controller.

        var response = await client.PostAsJsonAsync("/api/v1/dashboard/transform/validate", new
        {
            expression = "@",
            samplePayload = """{"a":1}"""
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect, HttpStatusCode.Forbidden);
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

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
