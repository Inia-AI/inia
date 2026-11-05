using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.UserData.Repositories;

/// <summary>
/// Repository implementation for FileMetadata entities.
/// </summary>
public class FileMetadataRepository : RepositoryBase<FileMetadata>, IFileMetadataRepository
{
    public FileMetadataRepository(UserDataDbContext context)
        : base(context) { }

    public async Task<bool> ExistsAsync(string id)
    {
        return await Entities.AnyAsync(f => f.Id == id);
    }

    public async Task<bool> ExistsByNameAndSizeAsync(string organizationId, string name, long size)
    {
        return await Entities.AnyAsync(f => f.OrganizationId == organizationId && f.Name == name && f.SizeBytes == size);
    }

    public async Task<bool> ExistsByNameAndSizeAsync(string name, long size)
    {
        return await Entities.AnyAsync(f => f.Name == name && f.SizeBytes == size);
    }

    public async Task<FileMetadata?> GetFileByNameAndSizeAsync(string name, long size)
    {
        return await Entities
            .FirstOrDefaultAsync(f => f.Name == name && f.SizeBytes == size);
    }

    public async Task<FileMetadata?> GetFileByNameAndSizeAsync(string organizationId, string name, long size)
    {
        return await Entities
            .FirstOrDefaultAsync(f => f.OrganizationId == organizationId && f.Name == name && f.SizeBytes == size);
    }

    public async Task<FileMetadata?> GetByIdWithoutBytesAsync(string id)
    {
        // FileMetadata doesn't have bytes, so this is just a regular GetById
        return await GetByIdAsync(id);
    }

    public async Task<IEnumerable<FileMetadata>> GetByHashAsync(string hash)
    {
        // Hash functionality would need to be implemented based on requirements
        // For now, return empty collection
        return [];
    }

    public async Task<IEnumerable<FileMetadata>> GetFilesByOrganizationIdAsync(string organizationId)
    {
        return await Entities
            .Where(f => f.OrganizationId == organizationId)
            .ToListAsync();
    }

    public async Task<long> GetTotalFileSizeByOrganizationIdAsync(string organizationId)
    {
        return await Entities
            .Where(f => f.OrganizationId == organizationId)
            .SumAsync(f => f.SizeBytes);
    }

    public async Task<FileMetadata?> GetByExternalStoragePathAsync(string externalStoragePath)
    {
        return await Entities
            .FirstOrDefaultAsync(f => f.ExternalStoragePath == externalStoragePath);
    }
}
