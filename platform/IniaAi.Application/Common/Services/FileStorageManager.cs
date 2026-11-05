using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Common.Services;

/// <summary>
/// High-level service for managing file storage across different providers.
/// Demonstrates how to use the file storage abstraction with multiple storage options.
/// </summary>
public class FileStorageManager : IFileStorageManager
{
    private readonly IFileStorageResolver _storageResolver;
    private readonly FileStorageSettings _fileStorageSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileStorageManager"/> class.
    /// </summary>
    /// <param name="storageResolver">The file storage resolver to get storage services.</param>
    /// <param name="fileStorageOptions">The file storage settings from configuration.</param>
    public FileStorageManager(IFileStorageResolver storageResolver, IOptions<FileStorageSettings> fileStorageOptions)
    {
        _storageResolver = storageResolver;
        _fileStorageSettings = fileStorageOptions.Value;
    }

    /// <summary>
    /// Gets the default storage provider from configuration.
    /// </summary>
    public string DefaultProvider => _fileStorageSettings.DefaultProvider;

    /// <summary>
    /// Gets the default storage provider as enum from configuration.
    /// </summary>
    public StorageProvider DefaultProviderEnum => GetStorageProviderEnum(_fileStorageSettings.DefaultProvider);

    /// <summary>
    /// Gets storage provider for a specific tenant/organization, with fallback to default.
    /// </summary>
    /// <param name="organizationId">The organization ID to check for overrides.</param>
    /// <returns>Storage provider enum for the organization.</returns>
    public StorageProvider GetProviderForTenant(string organizationId)
    {
        // Check for tenant-specific override
        if (_fileStorageSettings.TenantOverrides?.TryGetValue(organizationId, out string? tenantProvider) == true)
        {
            return GetStorageProviderEnum(tenantProvider);
        }

        // Fall back to default provider
        return DefaultProviderEnum;
    }

    /// <summary>
    /// Converts storage provider string to enum.
    /// </summary>
    /// <param name="providerName">The storage provider name.</param>
    /// <returns>The corresponding StorageProvider enum value.</returns>
    public StorageProvider GetStorageProviderEnum(string providerName)
    {
        return providerName switch
        {
            "MinIO" or "Minio" => StorageProvider.MinIO,
            "S3" => StorageProvider.S3,
            "Database" => StorageProvider.Database,
            _ => StorageProvider.Database,
        };
    }

    /// <summary>
    /// Stores a file using the specified storage provider.
    /// </summary>
    /// <param name="storageProvider">The storage provider to use.</param>
    /// <param name="organizationId">Organization ID for file isolation.</param>
    /// <param name="category">File category (e.g., "documents", "images").</param>
    /// <param name="key">The file name.</param>
    /// <param name="fileContent">The file content as stream.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <returns>The storage path/identifier.</returns>
    public async Task<string> StoreFileAsync(
        StorageProvider storageProvider,
        string organizationId,
        string category,
        string key,
        Stream fileContent,
        string contentType)
    {
        IFileStorageService storageService = _storageResolver.Resolve(storageProvider);
        return await storageService.UploadAsync(organizationId, category, key, fileContent, contentType);
    }

    /// <summary>
    /// Stores a file using the specified storage provider.
    /// </summary>
    /// <param name="storageProvider">The storage provider to use ("Database", "S3", "Minio").</param>
    /// <param name="organizationId">Organization ID for file isolation.</param>
    /// <param name="category">File category (e.g., "documents", "images").</param>
    /// <param name="key">The file name.</param>
    /// <param name="fileContent">The file content as stream.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <returns>The storage path/identifier.</returns>
    public async Task<string> StoreFileAsync(
        string storageProvider,
        string organizationId,
        string category,
        string key,
        Stream fileContent,
        string contentType)
    {
        IFileStorageService storageService = _storageResolver.Resolve(storageProvider);
        return await storageService.UploadAsync(organizationId, category, key, fileContent, contentType);
    }

    /// <summary>
    /// Retrieves a file using the specified storage provider.
    /// </summary>
    /// <param name="storageProvider">The storage provider to use ("Database", "S3", "Minio").</param>
    /// <param name="organizationId">Organization ID for file isolation.</param>
    /// <param name="category">File category.</param>
    /// <param name="key">The file path/identifier returned from StoreFileAsync.</param>
    /// <returns>The file content as stream.</returns>
    public async Task<Stream> RetrieveFileAsync(
        string storageProvider,
        string organizationId,
        string category,
        string key)
    {
        IFileStorageService storageService = _storageResolver.Resolve(storageProvider);
        return await storageService.DownloadAsync(organizationId, category, key);
    }

    /// <summary>
    /// Stores a file using the specified storage provider from byte array.
    /// </summary>
    /// <param name="storageProvider">The storage provider to use ("Database", "S3", "Minio").</param>
    /// <param name="organizationId">Organization ID for file isolation.</param>
    /// <param name="category">File category (e.g., "documents", "images").</param>
    /// <param name="key">The file name.</param>
    /// <param name="fileContent">The file content as byte array.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <returns>The storage path/identifier.</returns>
    public async Task<string> StoreFileAsync(
        string storageProvider,
        string organizationId,
        string category,
        string key,
        byte[] fileContent,
        string contentType)
    {
        using var stream = new MemoryStream(fileContent);
        return await StoreFileAsync(storageProvider, organizationId, category, key, stream, contentType);
    }

    /// <summary>
    /// Retrieves a file using the specified storage provider as byte array.
    /// </summary>
    /// <param name="storageProvider">The storage provider to use ("Database", "S3", "Minio").</param>
    /// <param name="organizationId">Organization ID for file isolation.</param>
    /// <param name="category">File category.</param>
    /// <param name="key">The file path/identifier returned from StoreFileAsync.</param>
    /// <returns>The file content as byte array.</returns>
    public async Task<byte[]> RetrieveFileBytesAsync(
        string storageProvider,
        string organizationId,
        string category,
        string key)
    {
        using Stream stream = await RetrieveFileAsync(storageProvider, organizationId, category, key);
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Deletes a file using the specified storage provider.
    /// </summary>
    /// <param name="storageProvider">The storage provider to use ("Database", "S3", "Minio").</param>
    /// <param name="organizationId">Organization ID for file isolation.</param>
    /// <param name="category">File category.</param>
    /// <param name="key">The file path/identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DeleteFileAsync(
        string storageProvider,
        string organizationId,
        string category,
        string key)
    {
        IFileStorageService storageService = _storageResolver.Resolve(storageProvider);
        await storageService.DeleteAsync(organizationId, category, key);
    }

    /// <summary>
    /// Example method showing how to migrate from database storage to external storage.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="category">File category.</param>
    /// <param name="key">Database file ID.</param>
    /// <param name="targetProvider">Target storage provider.</param>
    /// <returns>New file path in target storage.</returns>
    public async Task<string> MigrateFileAsync(
        string organizationId,
        string category,
        string key,
        string targetProvider)
    {
        // Download from database
        IFileStorageService databaseService = _storageResolver.Resolve("Database");
        using Stream fileStream = await databaseService.DownloadAsync(organizationId, category, key);

        // Get file name (for database storage, we might need to construct it)
        string fileName = $"migrated-{key}";

        // Upload to target storage
        IFileStorageService targetService = _storageResolver.Resolve(targetProvider);
        string newPath = await targetService.UploadAsync(organizationId, category, fileName, fileStream, "application/octet-stream");

        // Optionally delete from database after successful migration await databaseService.DeleteAsync(organizationId, category, filePath);
        return newPath;
    }
}
