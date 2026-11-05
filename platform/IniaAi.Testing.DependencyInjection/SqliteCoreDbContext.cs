using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// The Core context on SQLite, for a test that needs what the in-memory provider skips: foreign
/// keys and transactions that roll back. SQLite has no max-length column types, so they are dropped.
/// </summary>
public class SqliteCoreDbContext : CoreDbContext
{
    public SqliteCoreDbContext(DbContextOptions<CoreDbContext> options)
        : base(options)
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
            }
        }
    }
}
