using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

// A throwaway UserData database on SQL Server or PostgreSQL, for the guards that catch a query one
// provider translates and the other refuses. Connection strings (with credentials) come only from
// the INIA_TEST_MSSQL / INIA_TEST_PG environment variables — never hard-coded. A provider whose
// variable is unset, or whose server is unreachable, skips its case.
internal static class BothProvidersUserDataDatabase
{
    public const string MSSQL = "MSSQL";
    public const string POSTGRESQL = "PostgreSQL";

    private const string MSSQL_MIGRATIONS_ASSEMBLY = "IniaAi.Migrations.Mssql";
    private const string PG_MIGRATIONS_ASSEMBLY = "IniaAi.Migrations.PostgreSql";

    private const string USER_DATA_SCHEMA = "UserData";

    private const int COMMAND_TIMEOUT_SECONDS = 300;

    private const string MSSQL_CONNECTION_ENV = "INIA_TEST_MSSQL";
    private const string MSSQL_ADMIN_DATABASE = "master";

    private const string PG_CONNECTION_ENV = "INIA_TEST_PG";
    private const string PG_ADMIN_DATABASE = "postgres";

    /// <summary>Options for a new, not yet created database named <paramref name="databasePrefix"/> plus a fresh id, or a skip.</summary>
    /// <param name="retryOnFailure">Retries a failed command as the app does, which refuses a transaction its strategy did not start.</param>
    public static async Task<DbContextOptions<UserDataDbContext>> OptionsOrSkipAsync(
        string provider,
        string databasePrefix,
        string guardName,
        CancellationToken cancellationToken,
        bool retryOnFailure = false)
    {
        string connectionEnvVar = provider == MSSQL ? MSSQL_CONNECTION_ENV : PG_CONNECTION_ENV;
        string? baseConnection = Environment.GetEnvironmentVariable(connectionEnvVar);
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            Assert.Skip($"Set {connectionEnvVar} to a {provider} connection string to run {guardName}.");
        }

        string adminDatabase = provider == MSSQL ? MSSQL_ADMIN_DATABASE : PG_ADMIN_DATABASE;
        await using UserDataDbContext probe = new(BuildOptions(provider, baseConnection, adminDatabase));
        if (!await probe.Database.CanConnectAsync(cancellationToken))
        {
            Assert.Skip($"{provider} server is not reachable — skipping {guardName} for this provider.");
        }

        return BuildOptions(provider, baseConnection, databasePrefix + Guid.NewGuid().ToString("N"), retryOnFailure);
    }

    private static DbContextOptions<UserDataDbContext> BuildOptions(string provider, string baseConnection, string databaseName, bool retryOnFailure = false)
    {
        DbContextOptionsBuilder<UserDataDbContext> builder = new();

        switch (provider)
        {
            case MSSQL:
                _ = builder.UseSqlServer(
                    new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString,
                    sqlServer =>
                    {
                        _ = sqlServer.MigrationsAssembly(MSSQL_MIGRATIONS_ASSEMBLY);
                        _ = sqlServer.MigrationsHistoryTable(HistoryRepository.DefaultTableName, USER_DATA_SCHEMA);
                        _ = sqlServer.CommandTimeout(COMMAND_TIMEOUT_SECONDS);
                        if (retryOnFailure)
                        {
                            _ = sqlServer.EnableRetryOnFailure();
                        }
                    });
                break;

            case POSTGRESQL:
                _ = builder.UseNpgsql(
                    new NpgsqlConnectionStringBuilder(baseConnection) { Database = databaseName }.ConnectionString,
                    npgsql =>
                    {
                        _ = npgsql.MigrationsAssembly(PG_MIGRATIONS_ASSEMBLY);
                        _ = npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, USER_DATA_SCHEMA);
                        _ = npgsql.CommandTimeout(COMMAND_TIMEOUT_SECONDS);
                        if (retryOnFailure)
                        {
                            _ = npgsql.EnableRetryOnFailure();
                        }
                    });
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
        }

        // Configured minimally, without the tenant and convention setup the app applies, so the
        // model does not match the snapshot. Irrelevant here: each guard reads and writes through
        // the real services and specs.
        _ = builder.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));

        return builder.Options;
    }
}
