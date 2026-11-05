namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Service for generating and validating short-lived MFA tokens during the 2FA login flow.
/// </summary>
public interface IMfaTokenService
{
    /// <summary>
    /// Generates a short-lived MFA token for a user.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="email">The user's email.</param>
    /// <param name="ipAddress">The client's IP address.</param>
    /// <param name="scopes">The requested OAuth scopes to preserve through the MFA flow.</param>
    /// <param name="validityMinutes">Token validity in minutes. If null, uses configured default.</param>
    /// <returns>The encrypted MFA token.</returns>
    string GenerateMfaToken(string userId, string email, string? ipAddress, IEnumerable<string>? scopes = null, int? validityMinutes = null);

    /// <summary>
    /// Validates an MFA token and returns the payload if valid.
    /// </summary>
    /// <param name="token">The MFA token to validate.</param>
    /// <param name="expectedIpAddress">The expected IP address (optional, for additional security).</param>
    /// <returns>The token payload if valid, null otherwise.</returns>
    MfaTokenPayload? ValidateMfaToken(string token, string? expectedIpAddress = null);
}

/// <summary>
/// Payload stored in the MFA token.
/// </summary>
public class MfaTokenPayload
{
    /// <summary>
    /// Gets or sets the user's ID.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's email.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client's IP address.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Gets or sets the token expiration time.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the requested OAuth scopes from the original login request.
    /// </summary>
    public List<string> Scopes { get; set; } = [];
}
