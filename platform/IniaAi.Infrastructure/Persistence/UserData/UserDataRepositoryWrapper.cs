using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;

namespace IniaAi.Infrastructure.Persistence.UserData;

/// <summary>
/// UserData-specific repository wrapper that uses UserDataDbContext
/// </summary>
public class UserDataRepositoryWrapper<T> : ArdaliaRepositoryWrapper<T, UserDataDbContext>, IUserDataRepository<T>
    where T : class
{
    public UserDataRepositoryWrapper(UserDataDbContext context) : base(context)
    {
    }
}