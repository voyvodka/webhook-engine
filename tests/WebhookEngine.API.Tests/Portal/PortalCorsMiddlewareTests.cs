using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebhookEngine.API.Middleware;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Tests.Portal;

/// <summary>
/// Contract tests for <c>PortalCorsMiddleware</c>. Stand up the real production
/// middleware in a minimal pipeline (PortalTokenAuth → PortalCors → terminal
/// pong) so the ordering invariant the production code documents is exercised
/// here too. Allowed-origin matching, preflight rejection of subdomain spoofing,
/// and the "no Origin → no CORS interference" branch are the security-load-bearing
/// behaviours that previously had zero coverage.
/// </summary>
public class PortalCorsMiddlewareTests : IClassFixture<PortalCorsMiddlewareTests.CorsMiddlewareFactory>
{
    private const string PingPath = "/api/v1/portal/_ping";
    private const string AllowedOrigin = "https://app.example.com";
    private const string DisallowedOrigin = "https://attacker.example";

    private readonly CorsMiddlewareFactory _factory;

    public PortalCorsMiddlewareTests(CorsMiddlewareFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Preflight_With_Allowed_Origin_Returns_204_And_Cors_Headers()
    {
        await SeedAppAsync(allowedOriginsJson: $"[\"{AllowedOrigin}\"]");

        var response = await SendAsync(HttpMethod.Options, PingPath, AllowedOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be(AllowedOrigin);
        response.Headers.GetValues("Access-Control-Allow-Methods").Should().ContainSingle().Which.Should().Contain("POST");
        response.Headers.GetValues("Access-Control-Allow-Headers").Should().ContainSingle().Which.Should().Contain("Authorization");
        response.Headers.GetValues("Access-Control-Max-Age").Should().ContainSingle().Which.Should().Be("600");
        response.Headers.Vary.Should().Contain("Origin");
    }

    [Fact]
    public async Task Preflight_With_Disallowed_Origin_Returns_403_Without_Cors_Headers()
    {
        await SeedAppAsync(allowedOriginsJson: $"[\"{AllowedOrigin}\"]");

        var response = await SendAsync(HttpMethod.Options, PingPath, DisallowedOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // CORS headers MUST NOT leak on a rejected preflight — a browser that
        // saw Allow-Origin: <attacker> would treat the response as authorised.
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Methods").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_With_Subdomain_Spoofed_Origin_Is_Rejected()
    {
        // Classic CORS bypass: allowlist contains "https://acme.com", attacker
        // sends Origin "https://acme.com.attacker.com". A naive
        // origin.StartsWith(allowed) check would accept it. Strict equality
        // (or, here, exact-string match) refuses it.
        await SeedAppAsync(allowedOriginsJson: "[\"https://acme.com\"]");

        var response = await SendAsync(HttpMethod.Options, PingPath, "https://acme.com.attacker.com");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_Origin_Match_Is_Case_Insensitive_Per_Rfc6454()
    {
        // RFC 6454 §4: scheme + host comparison is case-insensitive. Allowlist
        // stored with uppercase host; browser sends lowercased Origin.
        await SeedAppAsync(allowedOriginsJson: "[\"https://APP.EXAMPLE.com\"]");

        var response = await SendAsync(HttpMethod.Options, PingPath, "https://app.example.com");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle().Which.Should().Be("https://app.example.com");
    }

    [Fact]
    public async Task Request_Without_Origin_Header_Falls_Through_To_Next()
    {
        // Same-origin or non-browser caller: no Origin header. The middleware
        // must not interfere — the terminal handler should respond normally.
        await SeedAppAsync(allowedOriginsJson: $"[\"{AllowedOrigin}\"]");

        var response = await SendAsync(HttpMethod.Options, PingPath, origin: null);

        // Terminal pong handler returns 200 when reached.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Real_Request_With_Valid_Token_And_Allowed_Origin_Echoes_Cors_Headers()
    {
        var appId = await SeedAppAsync(allowedOriginsJson: $"[\"{AllowedOrigin}\"]");
        var token = PortalJwtFactory.Mint(appId, capabilities: ["endpoints:read"]);

        var response = await SendAsync(HttpMethod.Get, PingPath, AllowedOrigin, bearerToken: token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle().Which.Should().Be(AllowedOrigin);
        response.Headers.Vary.Should().Contain("Origin");
    }

    [Fact]
    public async Task Real_Request_With_Valid_Token_And_Disallowed_Origin_Has_No_Cors_Headers()
    {
        // Token is valid (request reaches the handler) but Origin is not on
        // the allowlist — browser will see the 200 without a matching
        // Allow-Origin and surface a CORS error, which is the correct UX.
        var appId = await SeedAppAsync(allowedOriginsJson: $"[\"{AllowedOrigin}\"]");
        var token = PortalJwtFactory.Mint(appId, capabilities: ["endpoints:read"]);

        var response = await SendAsync(HttpMethod.Get, PingPath, DisallowedOrigin, bearerToken: token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_Deny_Decision_Is_Cached_Within_Ttl()
    {
        // Regression for the deny-cache fix: previously every disallowed
        // OPTIONS bypassed any cache and re-scanned the portal-enabled apps.
        // Now both allow and deny outcomes are cached for LookupCacheTtlSeconds,
        // so a first preflight that returns 403 must keep returning 403 even
        // if the DB starts allowing the origin within the TTL window. Uses
        // a unique origin so the assertion isn't polluted by other tests
        // sharing the IClassFixture's MemoryCache.
        const string uniqueOrigin = "https://deny-cache-fix.example";

        await SeedAppAsync(allowedOriginsJson: "[\"https://other.example\"]");
        var first = await SendAsync(HttpMethod.Options, PingPath, uniqueOrigin);
        first.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Mutate DB to now allow the origin — without invalidating the CORS
        // cache the cached deny decision must still win until TTL elapses.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
            var app = await db.Applications.FirstAsync();
            app.AllowedPortalOriginsJson = $"[\"{uniqueOrigin}\"]";
            await db.SaveChangesAsync();
        }

        var second = await SendAsync(HttpMethod.Options, PingPath, uniqueOrigin);
        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? origin,
        string? bearerToken = null)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(method, path);
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return await client.SendAsync(request);
    }

    private async Task<Guid> SeedAppAsync(string? allowedOriginsJson)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var appId = Guid.NewGuid();
        db.Applications.Add(new Application
        {
            Id = appId,
            Name = $"cors-{appId:N}",
            ApiKeyPrefix = $"whe_{appId:N}".Substring(0, 12) + "_",
            ApiKeyHash = "deadbeef",
            SigningSecret = "whsec_test",
            PortalSigningKey = PortalJwtFactory.ValidSigningKey,
            AllowedPortalOriginsJson = allowedOriginsJson,
            IsActive = true
        });
        await db.SaveChangesAsync();
        return appId;
    }

    /// <summary>
    /// Production DI from <see cref="Program"/> with the application pipeline
    /// reduced to the two portal middlewares plus a terminal pong handler.
    /// Mirrors the production ordering: PortalTokenAuth → PortalCors → next.
    /// </summary>
    public sealed class CorsMiddlewareFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"PortalCorsTestDb_{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<WebhookDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<WebhookDbContext>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<WebhookDbContext>));
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

                services.AddDbContext<WebhookDbContext>(options =>
                    options.UseInMemoryDatabase(_dbName));
            });

            builder.Configure(app =>
            {
                app.UseMiddleware<PortalTokenAuthMiddleware>();
                app.UseMiddleware<PortalCorsMiddleware>();
                app.Run(async context =>
                {
                    if (context.Request.Path == PingPath)
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"message\":\"pong\"}");
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                });
            });
        }
    }
}
