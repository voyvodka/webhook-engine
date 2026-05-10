using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using WebhookEngine.API.Middleware;
using WebhookEngine.Core.Entities;
using WebhookEngine.Infrastructure.Data;

namespace WebhookEngine.API.Tests.Portal;

/// <summary>
/// Verifies the five contractual rejection / acceptance paths for
/// <c>PortalTokenAuthMiddleware</c>. Capability-scoping tests are
/// out of scope until the portal route group lands in Step 4.
///
/// Uses a custom <see cref="WebApplicationFactory{TEntryPoint}"/> that
/// stands up a self-contained pipeline around the middleware itself
/// rather than the production <c>Program.cs</c>. This sidesteps the
/// MVC application-part discovery quirk (test-project controllers
/// aren't picked up by <c>AddControllers().AddApplicationPart</c> after
/// <c>Program.cs</c> has already finalized its controller feature provider)
/// and isolates the middleware contract from the rest of the API surface.
/// </summary>
public class PortalTokenAuthMiddlewareTests : IClassFixture<PortalTokenAuthMiddlewareTests.MiddlewareFactory>
{
    private const string ValidSigningKey = "portal-signing-key-must-be-at-least-32-bytes-for-hs256!!!";
    private const string PingPath = "/api/v1/portal/_ping";

    private readonly MiddlewareFactory _factory;

    public PortalTokenAuthMiddlewareTests(MiddlewareFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Portal_Request_With_Valid_Token_Reaches_The_Endpoint()
    {
        var app = await SeedAppAsync(ValidSigningKey);
        var token = MintToken(app.Id, ValidSigningKey, lifetime: TimeSpan.FromMinutes(5));

        var response = await SendPingAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("pong");
    }

    [Fact]
    public async Task Portal_Request_With_Expired_Token_Returns_401_With_TOKEN_EXPIRED()
    {
        var app = await SeedAppAsync(ValidSigningKey);
        var token = MintToken(
            app.Id,
            ValidSigningKey,
            lifetime: TimeSpan.FromMinutes(5),
            // Mint a token that already expired beyond the 30 s default skew.
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await SendPingAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("PORTAL_AUTH_TOKEN_EXPIRED");
    }

    [Fact]
    public async Task Portal_Request_With_Wrong_Signing_Key_Returns_401_With_INVALID_SIGNATURE()
    {
        var app = await SeedAppAsync(ValidSigningKey);
        const string wrongKey = "this-is-the-wrong-key-also-32-bytes-or-more!!!!!!!!!!!!!!";
        var token = MintToken(app.Id, wrongKey, lifetime: TimeSpan.FromMinutes(5));

        var response = await SendPingAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("PORTAL_AUTH_INVALID_SIGNATURE");
    }

    [Fact]
    public async Task Portal_Request_With_Token_For_Disabled_App_Returns_401_With_NOT_ENABLED()
    {
        // Portal disabled = PortalSigningKey is null on the application.
        var app = await SeedAppAsync(portalSigningKey: null);
        var token = MintToken(app.Id, ValidSigningKey, lifetime: TimeSpan.FromMinutes(5));

        var response = await SendPingAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("PORTAL_NOT_ENABLED");
    }

    [Fact]
    public async Task Portal_Request_With_Token_Lifetime_Exceeding_Limit_Returns_401_With_LIFETIME_TOO_LONG()
    {
        var app = await SeedAppAsync(ValidSigningKey);
        // Default MaxLifetimeMinutes = 15. Mint a 60-minute token; the
        // signature verifies, lifetime is currently valid, but the cap
        // rejects it.
        var token = MintToken(app.Id, ValidSigningKey, lifetime: TimeSpan.FromMinutes(60));

        var response = await SendPingAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("PORTAL_AUTH_LIFETIME_TOO_LONG");
    }

    [Fact]
    public async Task Portal_Request_With_Alg_None_Token_Is_Rejected_As_Invalid_Signature()
    {
        // Algorithm-confusion guard: an unsigned `alg: none` token MUST be
        // rejected by the HS256 allowlist, never silently trusted because
        // the signature is empty. This is the highest-CVSS class of bug
        // for HS256 verifiers (see CVE-2015-9235 family).
        // Microsoft.IdentityModel.Tokens 8.x maps the algorithm rejection
        // through SecurityTokenInvalidSignatureException, which the catch
        // ladder surfaces as PORTAL_AUTH_INVALID_SIGNATURE — the precise
        // error code is incidental; what matters is that the token is
        // rejected with a 401 and never reaches the protected handler.
        var app = await SeedAppAsync(ValidSigningKey);
        var token = MintUnsignedToken(app.Id, lifetime: TimeSpan.FromMinutes(5));

        var response = await SendPingAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("PORTAL_AUTH_INVALID_SIGNATURE");
    }

    [Fact]
    public async Task Portal_Request_With_HS384_Token_Is_Rejected_As_Invalid_Signature()
    {
        // Algorithm-confusion guard: even a valid HMAC token signed with the
        // correct key but a different SHA variant (HS384 / HS512) is rejected
        // because ValidAlgorithms pins HS256 only. As with the alg=none case,
        // Microsoft.IdentityModel.Tokens 8.x routes the rejection through
        // SecurityTokenInvalidSignatureException → PORTAL_AUTH_INVALID_SIGNATURE.
        var app = await SeedAppAsync(ValidSigningKey);
        var token = MintToken(
            app.Id,
            ValidSigningKey,
            lifetime: TimeSpan.FromMinutes(5),
            algorithm: SecurityAlgorithms.HmacSha384);

        var response = await SendPingAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("PORTAL_AUTH_INVALID_SIGNATURE");
    }

    private async Task<HttpResponseMessage> SendPingAsync(string token)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, PingPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<Application> SeedAppAsync(string? portalSigningKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = $"portal-test-{Guid.NewGuid():N}",
            ApiKeyPrefix = $"whe_{Guid.NewGuid():N}".Substring(0, 12) + "_",
            ApiKeyHash = "deadbeef",
            SigningSecret = "secret",
            PortalSigningKey = portalSigningKey
        };
        db.Applications.Add(app);
        await db.SaveChangesAsync();
        return app;
    }

    private static string MintToken(
        Guid appId,
        string signingKey,
        TimeSpan lifetime,
        DateTime? notBefore = null,
        DateTime? expires = null,
        IEnumerable<string>? capabilities = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var nbf = notBefore ?? DateTime.UtcNow;
        var exp = expires ?? nbf.Add(lifetime);

        var claims = new List<Claim>
        {
            new("appId", appId.ToString())
        };
        if (capabilities is not null)
        {
            foreach (var capability in capabilities)
            {
                claims.Add(new Claim("capabilities", capability));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, algorithm);
        var token = new JwtSecurityToken(
            issuer: null,
            audience: null,
            claims: claims,
            notBefore: nbf,
            expires: exp,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Hand-rolled unsigned ("alg":"none") JWT — JwtSecurityTokenHandler refuses
    /// to write one, so the test forges the three base64url segments directly.
    /// </summary>
    private static string MintUnsignedToken(Guid appId, TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow;
        var header = JsonSerializer.Serialize(new { alg = "none", typ = "JWT" });
        var payload = JsonSerializer.Serialize(new
        {
            appId = appId.ToString(),
            iat = now.ToUnixTimeSeconds(),
            nbf = now.ToUnixTimeSeconds(),
            exp = now.Add(lifetime).ToUnixTimeSeconds()
        });
        return $"{Base64Url(header)}.{Base64Url(payload)}.";
    }

    private static string Base64Url(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    /// <summary>
    /// Re-uses the production <see cref="Program"/> entry assembly so DI is
    /// wired identically, but replaces the application pipeline with a
    /// minimal one that runs only <c>PortalTokenAuthMiddleware</c> followed
    /// by a terminal "pong" delegate. Keeps the test focus on the
    /// middleware's contract.
    /// </summary>
    public sealed class MiddlewareFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"PortalTestDb_{Guid.NewGuid()}";

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
