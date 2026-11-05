using IniaAi.FileFormats.Inspection;

namespace IniaAi.FileFormats.Unlock;

/// <summary>Removes the password protection of an uploaded file. The result never carries the password.</summary>
public interface IDocumentUnlocker
{
    Task<FileUnlockResult> TryUnlockAsync(
        byte[] fileBytes,
        EncryptedContainer container,
        string? password,
        CancellationToken cancellationToken = default);
}
