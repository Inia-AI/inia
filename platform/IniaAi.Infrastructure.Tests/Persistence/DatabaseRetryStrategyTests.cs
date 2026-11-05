using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Setup;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the production provider configuration: every context on every real provider must
/// use a retrying execution strategy, so transient connection failures (e.g. a brief RDS
/// stall) are absorbed instead of failing the operation on the first attempt.
/// </summary>
public class DatabaseRetryStrategyTests
{
    private const string POSTGRES_CONNECTION = "Host=localhost;Database=strategy-probe;Username=probe;Password=probe";
    private const string MSSQL_CONNECTION = "Server=localhost;Database=strategy-probe;User Id=probe;Password=probe;TrustServerCertificate=True";

    [Fact]
    public void UserDataContext_OnPostgres_UsesRetryingExecutionStrategy()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseNpgsql(
                POSTGRES_CONNECTION,
                b => DatabaseConfiguratorExtensions.ConfigureNpgsqlProvider(b, UserDataDbContext.SCHEMA_NAME))
            .Options;

        AssertRetryingStrategy(options, o => new UserDataDbContext(o), typeof(NpgsqlRetryingExecutionStrategy));
    }

    [Fact]
    public void CoreContext_OnPostgres_UsesRetryingExecutionStrategy()
    {
        DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(
                POSTGRES_CONNECTION,
                b => DatabaseConfiguratorExtensions.ConfigureNpgsqlProvider(b, CoreDbContext.SCHEMA_NAME))
            .Options;

        AssertRetryingStrategy(options, o => new CoreDbContext(o), typeof(NpgsqlRetryingExecutionStrategy));
    }

    [Fact]
    public void UserDataContext_OnSqlServer_UsesRetryingExecutionStrategy()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlServer(
                MSSQL_CONNECTION,
                b => DatabaseConfiguratorExtensions.ConfigureSqlServerProvider(b, UserDataDbContext.SCHEMA_NAME))
            .Options;

        AssertRetryingStrategy(options, o => new UserDataDbContext(o), typeof(SqlServerRetryingExecutionStrategy));
    }

    [Fact]
    public void CoreContext_OnSqlServer_UsesRetryingExecutionStrategy()
    {
        DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlServer(
                MSSQL_CONNECTION,
                b => DatabaseConfiguratorExtensions.ConfigureSqlServerProvider(b, CoreDbContext.SCHEMA_NAME))
            .Options;

        AssertRetryingStrategy(options, o => new CoreDbContext(o), typeof(SqlServerRetryingExecutionStrategy));
    }

    private static void AssertRetryingStrategy<TContext>(
        DbContextOptions<TContext> options,
        Func<DbContextOptions<TContext>, TContext> createContext,
        Type expectedStrategyType)
        where TContext : DbContext
    {
        using TContext context = createContext(options);
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        Assert.IsType(expectedStrategyType, strategy);
        Assert.True(strategy.RetriesOnFailure);
    }
}
