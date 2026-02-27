using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebhookEngine.Infrastructure.Data;
using System.Text.Json;

namespace WebhookEngine.API.Tests.Integration;

/// <summary>
/// Integration tests using WebApplicationFactory with in-memory database.
/// Uses a custom factory that properly replaces Npgsql with InMemory provider.
/// </summary>
public class HealthEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_Endpoint_Returns_200_OK()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_Endpoint_Returns_Healthy_Status()
    {
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        var json = JsonSerializer.Deserialize<JsonElement>(content);
        json.GetProperty("status").GetString().Should().Be("healthy");
        json.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Health_Endpoint_Does_Not_Require_Auth()
    {
        // No auth headers at all
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
