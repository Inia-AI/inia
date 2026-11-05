using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Provisions a throwaway SQL Server database migrated from scratch for Core-schema tests that the
/// InMemory provider cannot express. The admin connection is read from the INIA_TEST_MSSQL
/// environment variable; the tests skip gracefully when it is unset or no SQL Server is reachable.
/// No credentials in source.
/// </summary>
public sealed class SqlServerCoreDatabaseFixture : IAsyncLifetime, ICoreDatabaseFixture
{
    private const string CONNECTION_ENV_VAR = "INIA_TEST_MSSQL";
    private const string ADMIN_DATABASE = "master";
    private const string MIGRATIONS_ASSEMBLY = "IniaAi.Migrations.Mssql";
    private const string CORE_SCHEMA = "Core";
    private const int MIGRATION_COMMAND_TIMEOUT_SECONDS = 300;

    private string _adminConnection = string.Empty;
    private string _testConnection = string.Empty;
    private string _databaseName = string.Empty;

    public bool DatabaseAvailable { get; private set; }

    public DbContextOptions<CoreDbContext> Options { get; private set; } = default!;

    public CoreDbContext CreateContext()
    {
        return new CoreDbContext(Options);
    }

    public DbContextOptions<CoreDbContext> BuildOptions(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlServer(_testConnection, b =>
            {
                _ = b.MigrationsAssembly(MIGRATIONS_ASSEMBLY);
                _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, CORE_SCHEMA);
                _ = b.CommandTimeout(MIGRATION_COMMAND_TIMEOUT_SECONDS);
            });
        if (interceptors.Length > 0)
        {
            _ = builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }

    public DbContextOptions<CoreDbContext> BuildRetryingOptions(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlServer(_testConnection, b =>
            {
                _ = b.MigrationsAssembly(MIGRATIONS_ASSEMBLY);
                _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, CORE_SCHEMA);
                _ = b.CommandTimeout(MIGRATION_COMMAND_TIMEOUT_SECONDS);
                _ = b.EnableRetryOnFailure();
            });
        if (interceptors.Length > 0)
        {
            _ = builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }

    public async ValueTask InitializeAsync()
    {
        string? baseConnection = Environment.GetEnvironmentVariable(CONNECTION_ENV_VAR);
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            DatabaseAvailable = false;
            return;
        }

        _adminConnection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = ADMIN_DATABASE }.ConnectionString;
        _databaseName = $"inia_core_test_{Guid.NewGuid():N}";

        try
        {
            await using var admin = new SqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{_databaseName}]", admin);
            _ = await create.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            DatabaseAvailable = false;
            return;
        }
        catch (System.Net.Sockets.SocketException)
        {
            DatabaseAvailable = false;
            return;
        }

        _testConnection = new SqlConnectionStringBuilder(_adminConnection) { InitialCatalog = _databaseName }.ConnectionString;
        Options = BuildOptions();

        await using CoreDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        DatabaseAvailable = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!DatabaseAvailable || string.IsNullOrEmpty(_databaseName))
        {
            return;
        }

        SqlConnection.ClearAllPools();

        try
        {
            await using var admin = new SqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var drop = new SqlCommand(
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]",
                admin);
            _ = await drop.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
        }
    }
}
