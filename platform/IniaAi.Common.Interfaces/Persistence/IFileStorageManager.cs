using IniaAi.Domain.Enums;

namespace IniaAi.Common.Interfaces.Persistence;

public interface IFileStorageManager
{
    string DefaultProvider { get; }
    StorageProvider DefaultProviderEnum { get; }

    Task DeleteFileAsync(string storageProvider, string organizationId, string category, string key);
    Task<Exception?> ReleaseFileAsync(string storageProvider, string organizationId, string category, string key);
    StorageProvider GetProviderForTenant(string organizationId);
    StorageProvider GetStorageProviderEnum(string providerName);
    Task<Stream> RetrieveFileAsync(string storageProvider, string organizationId, string category, string key);
    Task<byte[]> RetrieveFileBytesAsync(string storageProvider, string organizationId, string category, string key);
    Task<string> StoreFileAsync(StorageProvider storageProvider, string organizationId, string category, string key, Stream fileContent, string contentType);
    Task<string> StoreFileAsync(string storageProvider, string organizationId, string category, string key, Stream fileContent, string contentType);
    Task<string> StoreFileAsync(string storageProvider, string organizationId, string category, string key, byte[] fileContent, string contentType);
}
