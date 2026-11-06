using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.WebApp.Services;

/// <summary>The file a download by name serves: the newest of that name, with or without its extension, among the readable organizations.</summary>
public static class FileMetadataByName
{
    public static async Task<FileMetadata?> NewestAsync(IUserDataUnitOfWork unitOfWork, string fileName, IReadOnlyList<string>? readable)
    {
        string lower = fileName.Trim().ToLowerInvariant();
        return await unitOfWork.FileMetadata.GetFirstOrDefaultAsync(new NewestFileMetadataByNameSpec(lower, readable))
            ?? await unitOfWork.FileMetadata.GetFirstOrDefaultAsync(new NewestFileMetadataByNameSpec(Path.GetFileNameWithoutExtension(lower), readable));
    }
}
