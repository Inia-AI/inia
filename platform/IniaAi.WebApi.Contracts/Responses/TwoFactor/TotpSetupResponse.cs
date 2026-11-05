namespace IniaAi.WebApi.Contracts.Responses.TwoFactor;

/// <summary>
/// Response containing TOTP setup information for authenticator app configuration.
/// </summary>
public class TotpSetupResponse
{
    /// <summary>
    /// The shared key formatted for manual entry (e.g., "ABCD EFGH IJKL MNOP").
    /// </summary>
    public string SharedKey { get; set; } = string.Empty;

    /// <summary>
    /// The otpauth:// URI for QR code generation.
    /// Format: otpauth://totp/IniaAi:{email}?secret={key}&amp;issuer=IniaAi&amp;digits=6
    /// </summary>
    public string AuthenticatorUri { get; set; } = string.Empty;
}
