using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Persistence.UserData.Repositories;

public class FileRepository : RepositoryBase<File>, IFileRepository
{
    public FileRepository(UserDataDbContext context)
        : base(context) { }
}
