using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace IniaAi.Infrastructure.Tests.Helpers;

/// <summary>
/// Helper for generating RS256-signed JWTs and OIDC configuration for testing OIDC token validation.
/// No network calls are made — everything is self-contained using in-memory RSA keys.
/// </summary>
internal static class OidcTestTokenHelper
{
    public static RSA CreateTestRsaKey()
    {
        return RSA.Create(2048);
    }

    public static RsaSecurityKey CreateSecurityKey(RSA rsa)
    {
        return new RsaSecurityKey(rsa) { KeyId = "test-kid-1" };
    }

    public static string CreateSignedToken(
        RsaSecurityKey signingKey,
        string issuer,
        string audience,
        string subject,
        string? email = null,
        string? name = null,
        DateTime? issuedAt = null,
        DateTime? expires = null,
        IEnumerable<Claim>? verificationClaims = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var claims = new List<Claim> { new("sub", subject) };
        claims.AddRange(verificationClaims ?? [new Claim("email_verified", "true")]);

        if (email is not null)
        {
            claims.Add(new Claim("email", email));
        }

        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        DateTime effectiveIssuedAt = issuedAt ?? DateTime.UtcNow.AddMinutes(-5);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = effectiveIssuedAt,
            IssuedAt = effectiveIssuedAt,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity(claims)
        };

        return handler.CreateEncodedJwt(descriptor);
    }

    public static StaticConfigurationManager<OpenIdConnectConfiguration> CreateConfigManager(
        RsaSecurityKey key, string issuer)
    {
        var config = new OpenIdConnectConfiguration
        {
            Issuer = issuer
        };
        config.SigningKeys.Add(key);
        return new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
    }
}
