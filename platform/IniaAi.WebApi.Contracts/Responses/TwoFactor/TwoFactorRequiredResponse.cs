namespace IniaAi.WebApi.Contracts.Responses.TwoFactor;

/// <summary>
/// Response when login requires MFA verification (HTTP 202).
/// </summary>
public class TwoFactorRequiredResponse
{
    /// <summary>
    /// Short-lived token for completing MFA verification.
    /// </summary>
    public string MfaToken { get; set; } = string.Empty;

    /// <summary>
    /// Available MFA methods for this user (e.g., ["totp", "email", "recovery"]).
    /// </summary>
    public List<string> AvailableMethods { get; set; } = [];

    /// <summary>
    /// Masked email for display (e.g., "j***@example.com").
    /// </summary>
    public string MaskedEmail { get; set; } = string.Empty;

    /// <summary>
    /// MFA token expiration time in seconds.
    /// </summary>
    public int ExpiresIn { get; set; } = 300;
}
