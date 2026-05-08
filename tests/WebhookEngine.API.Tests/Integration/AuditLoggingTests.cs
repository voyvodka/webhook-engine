using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.API.Auth;
using WebhookEngine.Core.Entities;
using WebhookEngine.Core.Enums;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Tests.Integration;

public class AuditLoggingTests : IClassFixture<TestWebApplicationFactory>
{
    private const string DashboardEmail = "admin@test.local";
    private const string DashboardPassword = "P@ssw0rd-123";

    private readonly TestWebApplicationFactory _factory;

    public AuditLoggingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Application_Create_Writes_An_Audit_Row_With_The_Acting_User()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/v1/applications", new { name = "Audit Smoke App" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ParseJsonAsync(createResponse);
        var appId = created.GetProperty("data").GetProperty("id").GetGuid();

        await ExecuteDbAsync(async db =>
        {
            var rows = await db.AuditLogs
                .AsNoTracking()
                .Where(l => l.ResourceId == appId && l.Action == "application.created")
                .ToListAsync();

            rows.Should().HaveCount(1);
            var row = rows[0];
            row.ResourceType.Should().Be("application");
            row.UserId.Should().NotBeNull("dashboard auth resolves a user");
            row.AfterJson.Should().NotBeNullOrEmpty();
            row.BeforeJson.Should().BeNull("creates have no prior state");
        });
    }

    [Fact]
    public async Task Application_Update_Writes_Both_Before_And_After_Snapshots()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/v1/applications", new { name = "Original" });
        var appId = (await ParseJsonAsync(createResponse)).GetProperty("data").GetProperty("id").GetGuid();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/applications/{appId}", new { name = "Renamed" });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await ExecuteDbAsync(async db =>
        {
            var update = await db.AuditLogs
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.ResourceId == appId && l.Action == "application.updated");

            update.Should().NotBeNull();
            update!.BeforeJson.Should().NotBeNullOrEmpty();
            update.AfterJson.Should().NotBeNullOrEmpty();
            update.BeforeJson.Should().Contain("Original");
            update.AfterJson.Should().Contain("Renamed");
        });
    }

    [Fact]
    public async Task Audit_Log_List_Endpoint_Returns_Most_Recent_First_With_Pagination()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        await AuthenticateDashboardAsync(client);

        // Two creates produce two audit rows.
        var first = await client.PostAsJsonAsync("/api/v1/applications", new { name = "First" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await client.PostAsJsonAsync("/api/v1/applications", new { name = "Second" });
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await client.GetAsync("/api/v1/dashboard/audit-logs?action=application.created");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ParseJsonAsync(listResponse)).GetProperty("data");
        data.GetArrayLength().Should().Be(2);

        // Most recent first — the second create lands at index 0.
        var rows = data.EnumerateArray().ToList();
        rows[0].GetProperty("after").GetProperty("name").GetString().Should().Be("Second");
        rows[1].GetProperty("after").GetProperty("name").GetString().Should().Be("First");
    }

    // Application delete + cascade + audit messageCount is exercised against
    // a real Postgres in WebhookEngine.Infrastructure.Tests' AppDeleteCascadeTests
    // and AuditLoggingTests. The InMemory provider used here doesn't translate
    // ExecuteDeleteAsync (the path ApplicationRepository.DeleteAsync uses), so
    // an end-to-end HTTP delete test against this fixture would always 500 —
    // verifying the audit shape lives in the Testcontainers suite instead.

    [Fact]
    public async Task Audit_Log_List_Endpoint_Requires_Authentication()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/dashboard/audit-logs");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect, HttpStatusCode.Forbidden);
    }

    // ── Plumbing ───────────────────────────────────────

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

    private async Task ExecuteDbAsync(Func<WebhookDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await action(db);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
