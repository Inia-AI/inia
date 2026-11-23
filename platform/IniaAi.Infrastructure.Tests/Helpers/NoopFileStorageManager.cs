using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Enums;

namespace IniaAi.Infrastructure.Tests.Helpers;

/// <summary>
/// <see cref="IFileStorageManager"/> for tests whose subject must not touch stored
/// files: deletes succeed as no-ops, every retrieval or store is unsupported.
/// </summary>
internal sealed class NoopFileStorageManager : IFileStorageManager
{
    public string DefaultProvider => StorageProvider.Database.ToString();

    public StorageProvider DefaultProviderEnum => StorageProvider.Database;

    public Task DeleteFileAsync(string storageProvider, string organizationId, string category, string key)
    {
        return Task.CompletedTask;
    }

    public Task<Exception?> ReleaseFileAsync(string storageProvider, string organizationId, string category, string key)
    {
        return Task.FromResult<Exception?>(null);
    }

    public StorageProvider GetProviderForTenant(string organizationId)
    {
        return StorageProvider.Database;
    }

    public StorageProvider GetStorageProviderEnum(string providerName)
    {
        return StorageProvider.Database;
    }

    public Task<Stream> RetrieveFileAsync(string storageProvider, string organizationId, string category, string key)
    {
        throw new NotSupportedException();
    }

    public Task<byte[]> RetrieveFileBytesAsync(string storageProvider, string organizationId, string category, string key)
    {
        throw new NotSupportedException();
    }

    public Task<string> StoreFileAsync(StorageProvider storageProvider, string organizationId, string category, string key, Stream fileContent, string contentType)
    {
        throw new NotSupportedException();
    }

    public Task<string> StoreFileAsync(string storageProvider, string organizationId, string category, string key, Stream fileContent, string contentType)
    {
        throw new NotSupportedException();
    }

    public Task<string> StoreFileAsync(string storageProvider, string organizationId, string category, string key, byte[] fileContent, string contentType)
    {
        throw new NotSupportedException();
    }
}
