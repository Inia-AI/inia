using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Provisions a throwaway PostgreSQL database migrated from scratch for Core-schema tests that the
/// InMemory provider cannot express (real bytea storage, ExecuteUpdate, payload clearing). The admin
/// connection is read from the WebApi project's git-ignored appsettings.Tests.json
/// (ConnectionStrings:AdminCoreDbConnection), or from the INIA_TEST_PG environment variable; the test
/// skips gracefully when neither is configured or no PostgreSQL is reachable. No credentials in source.
/// </summary>
public sealed class PostgresCoreDatabaseFixture : IAsyncLifetime, ICoreDatabaseFixture
{
    private const string CONNECTION_ENV_VAR = "INIA_TEST_PG";
    private const string ADMIN_CONNECTION_NAME = "AdminCoreDbConnection";
    private const string CORE_CONNECTION_NAME = "CoreDbConnection";
    private const string MIGRATIONS_ASSEMBLY = "IniaAi.Migrations.PostgreSql";
    private const string CORE_SCHEMA = "Core";

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
            .UseNpgsql(_testConnection, b =>
            {
                _ = b.MigrationsAssembly(MIGRATIONS_ASSEMBLY);
                _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, CORE_SCHEMA);
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
            .UseNpgsql(_testConnection, b =>
            {
                _ = b.MigrationsAssembly(MIGRATIONS_ASSEMBLY);
                _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, CORE_SCHEMA);
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
        string? baseConnection = ResolveAdminConnectionString();
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            DatabaseAvailable = false;
            return;
        }

        _adminConnection = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" }.ConnectionString;
        _databaseName = $"inia_core_test_{Guid.NewGuid():N}";

        try
        {
            await using var admin = new NpgsqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            _ = await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            DatabaseAvailable = false;
            return;
        }
        catch (System.Net.Sockets.SocketException)
        {
            DatabaseAvailable = false;
            return;
        }

        _testConnection = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName }.ConnectionString;
        Options = BuildOptions();

        await using CoreDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        DatabaseAvailable = true;
    }

    private static string? ResolveAdminConnectionString()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(CONNECTION_ENV_VAR);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        string? webApiDirectory = FindWebApiDirectory();
        if (webApiDirectory is null)
        {
            return null;
        }

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(webApiDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Tests.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetConnectionString(ADMIN_CONNECTION_NAME)
            ?? configuration.GetConnectionString(CORE_CONNECTION_NAME);
    }

    private static string? FindWebApiDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "IniaAi.WebApi");
            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (!DatabaseAvailable || string.IsNullOrEmpty(_databaseName))
        {
            return;
        }

        try
        {
            await using var admin = new NpgsqlConnection(_adminConnection);
            await admin.OpenAsync();

            await using var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid()",
                admin);
            _ = terminate.Parameters.AddWithValue("db", _databaseName);
            _ = await terminate.ExecuteNonQueryAsync();

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", admin);
            _ = await drop.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
        }
    }
}
