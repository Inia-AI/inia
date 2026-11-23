using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Common.Services;

/// <summary>
/// High-level service for managing file storage across different providers.
/// </summary>
public class FileStorageManager : IFileStorageManager
{
    private readonly IFileStorageResolver _storageResolver;
    private readonly FileStorageSettings _fileStorageSettings;
    private readonly IStoredObjectDeletionRetries? _deletionRetries;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileStorageManager"/> class.
    /// </summary>
    /// <param name="storageResolver">The file storage resolver to get storage services.</param>
    /// <param name="fileStorageOptions">The file storage settings from configuration.</param>
    /// <param name="deletionRetries">Keeps a released file whose delete failed for a later retry. Every app host has it; a test host may leave it out.</param>
    public FileStorageManager(
        IFileStorageResolver storageResolver,
        IOptions<FileStorageSettings> fileStorageOptions,
        IStoredObjectDeletionRetries? deletionRetries = null)
    {
        _storageResolver = storageResolver;
        _fileStorageSettings = fileStorageOptions.Value;
        _deletionRetries = deletionRetries;
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
    /// Converts storage provider string to enum, in any letter case (<see cref="FileStorageProviders.Parse"/>).
    /// A name the platform does not know throws: startup refuses one in the settings, so it can only be a fault.
    /// </summary>
    /// <param name="providerName">The storage provider name.</param>
    /// <returns>The corresponding StorageProvider enum value.</returns>
    public StorageProvider GetStorageProviderEnum(string providerName)
    {
        return FileStorageProviders.ParseKnown(providerName);
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
        return await StoreFileAsync(FileStorageProviders.NameOf(storageProvider), organizationId, category, key, fileContent, contentType);
    }

    /// <summary>
    /// Stores a file using the specified storage provider. A stream that cannot say its length is
    /// buffered once here, because every store sends a file by its length.
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
        if (StoredObjectStream.LengthOf(fileContent) is not null)
        {
            return await storageService.UploadAsync(organizationId, category, key, fileContent, contentType);
        }

        await using var buffered = new MemoryStream();
        await fileContent.CopyToAsync(buffered);
        buffered.Position = 0;
        return await storageService.UploadAsync(organizationId, category, key, buffered, contentType);
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
    /// <returns>The file content as byte array, allocated once at the stored size when the store says it.</returns>
    public async Task<byte[]> RetrieveFileBytesAsync(
        string storageProvider,
        string organizationId,
        string category,
        string key)
    {
        await using Stream stream = await RetrieveFileAsync(storageProvider, organizationId, category, key);
        return await StoredObjectStream.ReadAllAsync(stream);
    }

    /// <summary>
    /// Deletes a file using the specified storage provider. A delete that fails throws; a caller
    /// whose rows no longer point at the file calls <see cref="ReleaseFileAsync"/> instead.
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
    /// Deletes a file that no row points at any more. A delete that fails is recorded, so a later
    /// retry deletes it: no row is left to find it by.
    /// </summary>
    /// <returns>Null when the file is gone, the failure when its delete waits for a retry. It never throws.</returns>
    public async Task<Exception?> ReleaseFileAsync(
        string storageProvider,
        string organizationId,
        string category,
        string key)
    {
        try
        {
            await DeleteFileAsync(storageProvider, organizationId, category, key);
            return null;
        }
        catch (Exception ex)
        {
            if (_deletionRetries is not null)
            {
                await _deletionRetries.RecordAsync(storageProvider, organizationId, category, key, ex);
            }

            return ex;
        }
    }
}
