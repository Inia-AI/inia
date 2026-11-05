using IniaAi.FileFormats.Inspection;

namespace IniaAi.FileFormats.Unlock;

public static class DocumentUnlockerExtensions
{
    /// <summary>Unlocks a file whose container no inspection has named yet: the bytes name it.</summary>
    public static async Task<FileUnlockResult> TryUnlockAsync(
        this IDocumentUnlocker unlocker,
        byte[] fileBytes,
        string? password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EncryptedContainer? container = EncryptedContainerProbe.Detect(fileBytes);
        return container is null
            ? FileUnlockResult.NotEncrypted()
            : await unlocker.TryUnlockAsync(fileBytes, container.Value, password, cancellationToken);
    }
}
