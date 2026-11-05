namespace IniaAi.WebApi.Contracts.Requests.TwoFactor;

/// <summary>
/// Request to verify MFA during login.
/// </summary>
public class VerifyMfaRequest
{
    /// <summary>
    /// The MFA token received from the initial login attempt.
    /// </summary>
    public string MfaToken { get; set; } = string.Empty;

    /// <summary>
    /// The verification method: 'totp', 'email', or 'recovery'.
    /// </summary>
    public string Method { get; set; } = "totp";

    /// <summary>
    /// The verification code (6-digit for TOTP/email, or recovery code format).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Whether to trust this device for future logins (skip 2FA for 30 days).
    /// </summary>
    public bool TrustDevice { get; set; }

    /// <summary>
    /// Device fingerprint for trusted device tracking.
    /// </summary>
    public string? DeviceFingerprint { get; set; }
}
