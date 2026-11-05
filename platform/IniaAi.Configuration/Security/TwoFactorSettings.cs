namespace IniaAi.Configuration.Security;

/// <summary>
/// Configuration settings for two-factor authentication.
/// </summary>
public class TwoFactorSettings
{
    /// <summary>
    /// The configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "TwoFactor";

    /// <summary>
    /// The validity period in minutes for MFA tokens issued during the login flow.
    /// Default is 5 minutes.
    /// </summary>
    public int MfaTokenValidityMinutes { get; set; } = 5;

    /// <summary>
    /// The number of days a trusted device remains valid.
    /// Default is 30 days.
    /// </summary>
    public int TrustedDeviceDays { get; set; } = 30;

    /// <summary>
    /// The number of recovery codes to generate when 2FA is enabled.
    /// Default is 10.
    /// </summary>
    public int RecoveryCodeCount { get; set; } = 10;

    /// <summary>
    /// Whether to enforce strict IP address validation for MFA tokens.
    /// When true, MFA token validation will fail if the IP address changes between token generation and verification.
    /// When false (default), IP mismatches are logged but validation proceeds.
    /// Set to false for users who may have dynamic IPs (mobile, VPN, etc.).
    /// </summary>
    public bool EnforceIpValidation { get; set; } = false;

    /// <summary>
    /// Settings for email-based OTP verification.
    /// </summary>
    public EmailOtpSettings EmailOtp { get; set; } = new();
}

/// <summary>
/// Configuration settings for email OTP verification.
/// </summary>
public class EmailOtpSettings
{
    /// <summary>
    /// The cooldown period in seconds before a new email OTP can be sent.
    /// Default is 60 seconds.
    /// </summary>
    public int CooldownSeconds { get; set; } = 60;
}
