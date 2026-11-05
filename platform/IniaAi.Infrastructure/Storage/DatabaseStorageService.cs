using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// Database storage service implementation that stores files in the FileStorage table.
/// </summary>
public class DatabaseStorageService : IFileStorageService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="DatabaseStorageService"/>.
    /// </summary>
    public DatabaseStorageService(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(string organizationId, string category, string key, Stream content, string contentType)
    {
        // Convert stream to byte array
        using var memoryStream = new MemoryStream();
        await content.CopyToAsync(memoryStream);
        byte[] bytes = memoryStream.ToArray();

        // Create File entity to store the blob data
        var file = new File(bytes)
        {
            Id = key,
            OrganizationId = organizationId,
        };

        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        // Save to File table
        await unitOfWork.Files.AddAsync(file);
        _ = await unitOfWork.CompleteAsync();

        // Return the file ID as the storage path
        return key;
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string organizationId, string category, string path)
    {
        // Path is the File ID in database storage
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        File? file = await unitOfWork.Files.GetByIdAsync(path)
            ?? throw new FileNotFoundException($"File with ID '{path}' not found.");

        // Return byte array as stream
        return new MemoryStream(file.Bytes);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string organizationId, string category, string path)
    {
        // Path is the File ID in database storage. One set-based DELETE: loading the row first
        // would pull the whole blob into memory just to throw it away. A missing row deletes
        // zero rows, which is the wanted no-op. The organization predicate is defense in depth:
        // a key misrouted across tenants deletes nothing.
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = await context.Files
            .Where(file => file.Id == path && file.OrganizationId == organizationId)
            .ExecuteDeleteAsync();
    }
}
