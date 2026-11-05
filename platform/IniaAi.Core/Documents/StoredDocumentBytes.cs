using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Documents;

public static class StoredDocumentBytes
{
    /// <summary>
    /// Returns an uploaded document's file bytes. Uses the in-memory bytes when already loaded;
    /// otherwise fetches them from file storage.
    /// </summary>
    public static async Task<byte[]> ReadDocumentBytesAsync(
        this IFileStorageManager fileStorageManager,
        string organizationId,
        FileMetadata fileMetadata,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return fileMetadata.AreBytesLoaded
            ? fileMetadata.Bytes
            : await fileStorageManager.RetrieveFileBytesAsync(
                fileMetadata.StorageProvider.ToString(),
                organizationId,
                StorageCategories.Documents,
                fileMetadata.GetStoragePath());
    }
}
