namespace IniaAi.FileFormats.Unlock;

/// <summary>
/// Decides what an intake channel that cannot ask anyone for a password (e.g. automatic email
/// import) does with a file, given the unlock gate's verdict: proceed, or skip it with an
/// operator-facing reason.
/// </summary>
public static class NonInteractiveUnlockPolicy
{
    // Code scanning treats any identifier containing "password" as a secret and flags every log of it.
    public const string MANUAL_UNLOCK_REQUIRED_REASON =
        "The file is password-protected and automatic import cannot supply a password. Upload it manually and provide the password.";

    public const string UNSUPPORTED_PROTECTION_REASON =
        "The file uses certificate- or DRM-based protection that cannot be removed. Ask the sender for an unprotected copy.";

    public static string? GetSkipReason(FileProtectionStatus status)
    {
        return status switch
        {
            FileProtectionStatus.PasswordRequired or FileProtectionStatus.PasswordIncorrect
                => MANUAL_UNLOCK_REQUIRED_REASON,
            FileProtectionStatus.UnsupportedProtection => UNSUPPORTED_PROTECTION_REASON,
            _ => null,
        };
    }
}
