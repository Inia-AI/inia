using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Application.Documents;

/// <summary>
/// Stores a document's bytes under its file metadata id and records the storage path on the
/// metadata. A document that already has a storage path is left alone and null is returned.
/// Call it before the commit that first saves a new document, so the document and its path
/// commit together: two intakes of the same file share one document, and one that found it
/// committed without a path would store its bytes again under the same key.
/// </summary>
public static class DocumentStorage
{
    public static async Task<string?> StoreBytesAsync(
        IFileStorageManager storage,
        Document document,
        byte[] bytes)
    {
        if (document.FileMetadata is null || !string.IsNullOrEmpty(document.FileMetadata.ExternalStoragePath))
        {
            return null;
        }

        using MemoryStream content = new(bytes);
        string storagePath = await storage.StoreFileAsync(
            document.FileMetadata.StorageProvider.ToString(),
            document.OrganizationId,
            StorageCategories.Documents,
            document.FileMetadata.Id,
            content,
            document.MediaType.ToContentType());

        document.FileMetadata.ExternalStoragePath = storagePath;

        return storagePath;
    }
}
