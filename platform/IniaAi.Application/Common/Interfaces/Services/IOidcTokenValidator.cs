namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Validates OIDC ID tokens (JWTs) by verifying the RS256 signature against the provider's JWKS endpoint,
/// and checking issuer, audience, and expiration claims.
/// </summary>
public interface IOidcTokenValidator
{
    /// <summary>
    /// Validates an OIDC ID token.
    /// </summary>
    /// <param name="idToken">The raw JWT string from the frontend.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the validated claims or an error.</returns>
    Task<OidcTokenValidationResult> ValidateTokenAsync(string idToken, CancellationToken ct = default);
}

/// <summary>
/// Result of validating an OIDC ID token.
/// </summary>
public record OidcTokenValidationResult(
    bool IsValid,
    string? Error = null,
    string? Email = null,
    string? Subject = null,
    string? Name = null,
    bool EmailVerified = false);
