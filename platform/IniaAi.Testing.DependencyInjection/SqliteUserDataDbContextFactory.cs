using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Hands out SQLite-shaped user data contexts over one shared connection, for the code paths
/// that open a context of their own per unit of work rather than taking the scoped one.
/// </summary>
public sealed class SqliteUserDataDbContextFactory : IDbContextFactory<UserDataDbContext>
{
    private readonly DbContextOptions<UserDataDbContext> _options;

    public SqliteUserDataDbContextFactory(DbContextOptions<UserDataDbContext> options)
    {
        _options = options;
    }

    public UserDataDbContext CreateDbContext()
    {
        return new SqliteUserDataDbContext(_options);
    }
}
