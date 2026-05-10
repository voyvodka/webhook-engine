using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace WebhookEngine.API.Tests.Portal;

/// <summary>
/// Shared JWT mint helpers for portal-route tests. Mirrors the production
/// <c>PortalTokenAuthMiddleware</c> claim shape (<c>appId</c> + repeated
/// <c>capabilities</c> claims) so test tokens flow through the production
/// validator unchanged. Kept separate from the middleware-tests file so the
/// new controller tests don't need to take a friend dependency on the older
/// test class.
/// </summary>
internal static class PortalJwtFactory
{
    public const string ValidSigningKey = "portal-signing-key-must-be-at-least-32-bytes-for-hs256!!!";

    public static string Mint(
        Guid appId,
        IEnumerable<string> capabilities,
        string signingKey = ValidSigningKey,
        TimeSpan? lifetime = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var window = lifetime ?? TimeSpan.FromMinutes(5);
        var nbf = notBefore ?? DateTime.UtcNow;
        var exp = expires ?? nbf.Add(window);

        var claims = new List<Claim>
        {
            new("appId", appId.ToString())
        };
        foreach (var capability in capabilities)
        {
            claims.Add(new Claim("capabilities", capability));
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
}
