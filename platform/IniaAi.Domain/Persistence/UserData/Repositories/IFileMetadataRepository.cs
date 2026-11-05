using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Persistence.UserData.Repositories;

/// <summary>
/// Repository interface for FileMetadata entities.
/// </summary>
public interface IFileMetadataRepository : IRepository<FileMetadata>
{
    Task<bool> ExistsAsync(string id);
    Task<bool> ExistsByNameAndSizeAsync(string organizationId, string name, long size);
    Task<bool> ExistsByNameAndSizeAsync(string name, long size);
    Task<FileMetadata?> GetFileByNameAndSizeAsync(string name, long size);
    Task<FileMetadata?> GetFileByNameAndSizeAsync(string organizationId, string name, long size);
    Task<FileMetadata?> GetByIdWithoutBytesAsync(string id);
    Task<IEnumerable<FileMetadata>> GetByHashAsync(string hash);
    Task<IEnumerable<FileMetadata>> GetFilesByOrganizationIdAsync(string organizationId);
    Task<long> GetTotalFileSizeByOrganizationIdAsync(string organizationId);
    Task<FileMetadata?> GetByExternalStoragePathAsync(string externalStoragePath);
}