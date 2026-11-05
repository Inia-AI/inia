using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqliteUserDataSchema = IniaAi.Testing.DependencyInjection.SqliteUserDataSchema;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// A throwaway SQLite UserData database with the real schema, for the tests that run the seeder.
/// </summary>
internal sealed class SqliteSeedDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteSeedDatabase()
    {
        _connection = SqliteUserDataSchema.OpenInMemoryDatabase();
        Options = new DbContextOptionsBuilder<UserDataDbContext>().UseSqlite(_connection).Options;
    }

    public DbContextOptions<UserDataDbContext> Options { get; }

    public static IEnumerable<string> RolesOf(UserDataDbContext context, string userId)
    {
        return context.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Join(context.Roles, userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name!)
            .AsEnumerable();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
