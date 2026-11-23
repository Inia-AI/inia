namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// One file store: the database, or an object store such as AWS S3 or MinIO. A file's key is
/// unique on its own; each store decides where the organization and the category put it.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Uploads a file stream to storage for a given organization and category.
    /// </summary>
    /// <param name="organizationId">The organization the file belongs to.</param>
    /// <param name="category">The file's category (<c>StorageCategories</c>), which only MinIO with a bucket per organization puts in the location.</param>
    /// <param name="key">The file's key.</param>
    /// <param name="content">The file content stream, of known length: one that can seek, or a <see cref="StoredObjectStream"/>. <c>IFileStorageManager</c> buffers any other.</param>
    /// <param name="contentType">The MIME content type.</param>
    /// <returns>The storage path or key that was uploaded.</returns>
    Task<string> UploadAsync(string organizationId, string category, string key, Stream content, string contentType);

    /// <summary>
    /// Downloads a file stream from storage for a given organization and category.
    /// </summary>
    /// <param name="organizationId">The organization the file belongs to.</param>
    /// <param name="category">The file's category (<c>StorageCategories</c>), which only MinIO with a bucket per organization puts in the location.</param>
    /// <param name="key">The file's key.</param>
    /// <returns>The file content stream.</returns>
    Task<Stream> DownloadAsync(string organizationId, string category, string key);

    /// <summary>
    /// Deletes a file from storage for a given organization and category.
    /// </summary>
    /// <param name="organizationId">The organization the file belongs to.</param>
    /// <param name="category">The file's category (<c>StorageCategories</c>), which only MinIO with a bucket per organization puts in the location.</param>
    /// <param name="key">The file's key.</param>
    Task DeleteAsync(string organizationId, string category, string key);
}
