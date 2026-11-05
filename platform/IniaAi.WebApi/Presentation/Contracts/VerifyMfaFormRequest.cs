using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Presentation.Contracts;

/// <summary>
/// Form request to verify MFA during login.
/// Supports form-urlencoded binding with snake_case property names for OAuth compatibility.
/// </summary>
public sealed class VerifyMfaFormRequest
{
    /// <summary>
    /// The MFA token received from the initial login attempt.
    /// </summary>
    [FromForm(Name = "mfa_token")]
    public string MfaToken { get; set; } = string.Empty;

    /// <summary>
    /// The verification method: 'totp', 'email', or 'recovery'.
    /// </summary>
    [FromForm(Name = "method")]
    public string Method { get; set; } = "totp";

    /// <summary>
    /// The verification code (6-digit for TOTP/email, or recovery code format).
    /// </summary>
    [FromForm(Name = "code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Whether to trust this device for future logins (skip 2FA for 30 days).
    /// </summary>
    [FromForm(Name = "trust_device")]
    public bool TrustDevice { get; set; }

    /// <summary>
    /// Device fingerprint for trusted device tracking.
    /// </summary>
    [FromForm(Name = "device_fingerprint")]
    public string? DeviceFingerprint { get; set; }
}
