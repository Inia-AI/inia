namespace IniaAi.FileFormats.Unlock;

/// <summary>
/// Typed result of <see cref="IDocumentUnlocker.TryUnlockAsync"/>. Never carries the password.
/// </summary>
/// <param name="Status">The unlock outcome.</param>
/// <param name="DecryptedBytes">The decrypted working copy; set only when <paramref name="Status"/> is <see cref="FileProtectionStatus.Decrypted"/>.</param>
/// <param name="WasPasswordProtected">True when encryption was removed, including owner-only PDFs stripped without a prompt.</param>
/// <param name="ContainedSignature">True when a decrypted PDF contained a digital signature (invalidated by the strip; disclose to the uploader).</param>
public sealed record FileUnlockResult(
    FileProtectionStatus Status,
    byte[]? DecryptedBytes,
    bool WasPasswordProtected,
    bool ContainedSignature)
{
    public static FileUnlockResult NotEncrypted()
    {
        return new FileUnlockResult(FileProtectionStatus.NotEncrypted, null, false, false);
    }

    /// <summary>A file that stays locked, for <paramref name="status"/>'s reason.</summary>
    public static FileUnlockResult Locked(FileProtectionStatus status)
    {
        return new FileUnlockResult(status, null, false, false);
    }
}
