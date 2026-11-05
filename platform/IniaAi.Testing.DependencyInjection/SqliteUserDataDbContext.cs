using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// <see cref="UserDataDbContext"/> for SQLite-backed tests: the shared model declares
/// SQL-Server-shaped column types ("max") and default-value SQL, which this strips so
/// the schema can be created on SQLite.
/// </summary>
public sealed class SqliteUserDataDbContext : UserDataDbContext
{
    public SqliteUserDataDbContext(DbContextOptions<UserDataDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entityType.GetProperties())
            {
                if (property.GetColumnType()?.Contains("max") == true)
                {
                    property.SetColumnType(null);
                }

                if (property.GetDefaultValueSql() is not null)
                {
                    property.SetDefaultValueSql(null);
                }
            }
        }
    }
}
