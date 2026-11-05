namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Provides an abstraction for storing files in external object storage,
/// such as MinIO or AWS S3, with organization-based bucket isolation.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Uploads a file stream to storage for a given organization and category.
    /// </summary>
    /// <param name="organizationId">The organization ID used to resolve the target bucket.</param>
    /// <param name="category">A logical category mapped to a storage bucket suffix.</param>
    /// <param name="key">The object key or file path within the bucket.</param>
    /// <param name="content">The file content stream.</param>
    /// <param name="contentType">The MIME content type.</param>
    /// <returns>The storage path or key that was uploaded.</returns>
    Task<string> UploadAsync(string organizationId, string category, string key, Stream content, string contentType);

    /// <summary>
    /// Downloads a file stream from storage for a given organization and category.
    /// </summary>
    /// <param name="organizationId">The organization ID used to resolve the target bucket.</param>
    /// <param name="category">A logical category mapped to a storage bucket suffix.</param>
    /// <param name="key">The object key or file path within the bucket.</param>
    /// <returns>The file content stream.</returns>
    Task<Stream> DownloadAsync(string organizationId, string category, string key);

    /// <summary>
    /// Deletes a file from storage for a given organization and category.
    /// </summary>
    /// <param name="organizationId">The organization ID used to resolve the target bucket.</param>
    /// <param name="category">A logical category mapped to a storage bucket suffix.</param>
    /// <param name="key">The object key or file path within the bucket.</param>
    Task DeleteAsync(string organizationId, string category, string key);
}
