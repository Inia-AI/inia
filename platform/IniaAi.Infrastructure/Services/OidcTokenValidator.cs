using IniaAi.Application.Common.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Validates OIDC ID tokens using OIDC discovery and JWKS verification.
/// Uses standard Microsoft.IdentityModel libraries — no provider-specific SDK required.
/// </summary>
public class OidcTokenValidator : IOidcTokenValidator
{
    private readonly OidcAuthSettings _settings;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JwtSecurityTokenHandler _tokenHandler = new() { MapInboundClaims = false };

    /// <summary>
    /// Production constructor — auto-discovers JWKS from the OIDC issuer's well-known configuration.
    /// </summary>
    public OidcTokenValidator(IOptions<OidcAuthSettings> settings)
    {
        _settings = settings.Value;
        string normalizedIssuer = _settings.Issuer.TrimEnd('/');
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{normalizedIssuer}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());
    }

    /// <summary>
    /// Test constructor — accepts a custom configuration manager for testing without network calls.
    /// </summary>
    internal OidcTokenValidator(
        OidcAuthSettings settings,
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        _settings = settings;
        _configurationManager = configurationManager;
    }

    /// <inheritdoc />
    public async Task<OidcTokenValidationResult> ValidateTokenAsync(
        string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return new OidcTokenValidationResult(IsValid: false, Error: "Token is empty or null.");
        }

        try
        {
            OpenIdConnectConfiguration config =
                await _configurationManager.GetConfigurationAsync(ct);

            var validationParameters = new TokenValidationParameters
            {
                // Use the discovered issuer as authoritative — handles trailing slash
                // mismatches between config and provider (e.g., Auth0 uses trailing slash, Okta doesn't)
                ValidIssuer = config.Issuer,
                ValidAudience = _settings.ClientId,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            return ValidateAndExtractClaims(idToken, validationParameters);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Signing key not found — the provider may have rotated keys.
            // Request a JWKS refresh and retry once.
            _configurationManager.RequestRefresh();

            try
            {
                OpenIdConnectConfiguration refreshedConfig =
                    await _configurationManager.GetConfigurationAsync(ct);

                var retryParameters = new TokenValidationParameters
                {
                    ValidIssuer = refreshedConfig.Issuer,
                    ValidAudience = _settings.ClientId,
                    IssuerSigningKeys = refreshedConfig.SigningKeys,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(2)
                };

                return ValidateAndExtractClaims(idToken, retryParameters);
            }
            catch (Exception retryEx)
            {
                return new OidcTokenValidationResult(IsValid: false, Error: retryEx.Message);
            }
        }
        catch (Exception ex)
        {
            return new OidcTokenValidationResult(IsValid: false, Error: ex.Message);
        }
    }

    private OidcTokenValidationResult ValidateAndExtractClaims(
        string idToken, TokenValidationParameters validationParameters)
    {
        ClaimsPrincipal principal = _tokenHandler.ValidateToken(
            idToken, validationParameters, out SecurityToken _);

        // Email claim is required for user matching. Providers must be configured
        // to include the "email" scope. Some providers use "preferred_username" or "upn"
        // but we require "email" for consistent cross-provider behavior.
        string? email = principal.FindFirst("email")?.Value;
        string? subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        string? name = principal.FindFirst("name")?.Value;
        bool emailVerified = ExternalEmailVerification.IsVerified(
            principal.FindFirst(ExternalEmailVerification.EMAIL_VERIFIED_CLAIM)?.Value,
            principal.FindFirst(ExternalEmailVerification.ENTRA_DOMAIN_OWNER_VERIFIED_CLAIM)?.Value,
            _settings.TrustUnverifiedEmail);

        return new OidcTokenValidationResult(
            IsValid: true, Email: email, Subject: subject, Name: name, EmailVerified: emailVerified);
    }
}
