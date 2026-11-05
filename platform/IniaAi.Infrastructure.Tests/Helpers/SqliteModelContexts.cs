using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Helpers;

/// <summary>
/// Contexts whose only job is to build the EF model on SQLite for shape assertions;
/// nothing is ever created or queried through them.
/// </summary>
internal static class SqliteModelContexts
{
    private const string IN_MEMORY = "DataSource=:memory:";

    public static UserDataDbContext CreateUserDataContext()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(IN_MEMORY)
            .Options;
        return new UserDataDbContext(options);
    }

    public static CoreDbContext CreateCoreContext()
    {
        DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(IN_MEMORY)
            .Options;
        return new CoreDbContext(options);
    }
}
