namespace IniaAi.FileFormats.Unlock;

/// <summary>
/// Outcome of probing/unlocking a potentially password-protected file at ingest.
/// </summary>
public enum FileProtectionStatus
{
    /// <summary>The file carries no open-password encryption; use the original bytes as-is.</summary>
    NotEncrypted,

    /// <summary>Encryption was removed; use <see cref="FileUnlockResult.DecryptedBytes"/> instead of the original bytes.</summary>
    Decrypted,

    /// <summary>The file requires an open password and none was supplied.</summary>
    PasswordRequired,

    /// <summary>The supplied password does not open the file.</summary>
    PasswordIncorrect,

    /// <summary>The file uses a protection scheme that cannot be unlocked with a password (e.g. certificate/DRM security handlers).</summary>
    UnsupportedProtection,
}
