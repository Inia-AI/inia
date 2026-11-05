using IniaAi.Configuration.Data;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Setup;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// EF Core applies migrations in id order, and the startup step that moves old databases onto the
/// squashed history takes the first migration as the squashed one. A migration whose id sorts before
/// InitialCreate would break a fresh database and that step, and only this guard runs in CI.
/// </summary>
public class SquashedMigrationOrderTests
{
    private const string POSTGRES_CONNECTION = "Host=localhost;Database=migration-order-probe;Username=probe;Password=probe";
    private const string MSSQL_CONNECTION = "Server=localhost;Database=migration-order-probe;User Id=probe;Password=probe;TrustServerCertificate=True";
    private const string INITIAL_CREATE_SUFFIX = "_InitialCreate";

    [Theory]
    [InlineData(DatabaseProviders.MSSQL, CoreDbContext.SCHEMA_NAME)]
    [InlineData(DatabaseProviders.MSSQL, UserDataDbContext.SCHEMA_NAME)]
    [InlineData(DatabaseProviders.POSTGRE_SQL, CoreDbContext.SCHEMA_NAME)]
    [InlineData(DatabaseProviders.POSTGRE_SQL, UserDataDbContext.SCHEMA_NAME)]
    public void EveryContext_OnEveryProvider_StartsWithTheOnlyInitialCreate(string provider, string schema)
    {
        using DbContext context = CreateContext(provider, schema);

        List<string> migrations = [.. context.Database.GetMigrations()];

        Assert.EndsWith(INITIAL_CREATE_SUFFIX, migrations[0], StringComparison.Ordinal);
        _ = Assert.Single(migrations, id => id.EndsWith(INITIAL_CREATE_SUFFIX, StringComparison.Ordinal));
    }

    private static DbContext CreateContext(string provider, string schema)
    {
        if (schema == CoreDbContext.SCHEMA_NAME)
        {
            return new CoreDbContext(BuildOptions<CoreDbContext>(provider, schema));
        }

        return new UserDataDbContext(BuildOptions<UserDataDbContext>(provider, schema));
    }

    private static DbContextOptions<TContext> BuildOptions<TContext>(string provider, string schema)
        where TContext : DbContext
    {
        DbContextOptionsBuilder<TContext> builder = new();

        if (provider == DatabaseProviders.MSSQL)
        {
            _ = builder.UseSqlServer(MSSQL_CONNECTION, b => DatabaseConfiguratorExtensions.ConfigureSqlServerProvider(b, schema));
        }
        else
        {
            _ = builder.UseNpgsql(POSTGRES_CONNECTION, b => DatabaseConfiguratorExtensions.ConfigureNpgsqlProvider(b, schema));
        }

        return builder.Options;
    }
}
