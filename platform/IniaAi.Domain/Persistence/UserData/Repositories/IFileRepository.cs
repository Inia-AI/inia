using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Domain.Persistence.UserData.Repositories;

/// <summary>
/// Represents a repository for managing files.
/// </summary>
public interface IFileRepository : IRepository<File>
{
}
