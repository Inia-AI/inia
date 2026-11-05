namespace IniaAi.Application.Auditing;

/// <summary>
/// What happened to an account's sign-in or its second factor.
/// </summary>
public enum AuthenticationAuditKind
{
    SignInSucceeded,
    SignInRefused,
    SignInLockedOut,
    SecondFactorRequired,
    TwoFactorSetupStarted,
    TwoFactorEnabled,
    TwoFactorDisabled,
    AuthenticatorReset,
    RecoveryCodesRegenerated,
    TrustedDeviceRemoved,
    AllTrustedDevicesRemoved,
}

/// <summary>
/// The sign-in methods an audit record names.
/// </summary>
public static class AuthenticationAuditMethods
{
    public const string PASSWORD_SIGN_IN = "password";
    public const string TOTP = "totp";
    public const string EMAIL = "email";
    public const string RECOVERY = "recovery";
}

/// <summary>
/// One audit record. <paramref name="UserId"/> is empty when the sign-in named no known account;
/// the record never carries an email address, a password or a code.
/// </summary>
public sealed record AuthenticationAuditEvent(
    AuthenticationAuditKind Kind,
    string? UserId,
    string? Method = null,
    string? ClientAddress = null);
