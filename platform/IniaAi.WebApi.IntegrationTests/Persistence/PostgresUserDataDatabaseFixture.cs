using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Provisions a throwaway PostgreSQL database, migrated from scratch, for FK-enforcing
/// tests that the InMemory provider cannot express (cascade ordering, shadow-FK SetNull).
/// The admin connection is read from the WebApi project's git-ignored appsettings.Tests.json
/// (ConnectionStrings:AdminUserDataDbConnection), or from the INIA_TEST_PG environment variable;
/// the test skips gracefully when neither is configured or no PostgreSQL is reachable. No
/// credentials in source.
/// </summary>
public sealed class PostgresUserDataDatabaseFixture : IAsyncLifetime
{
    private const string CONNECTION_ENV_VAR = "INIA_TEST_PG";
    private const string ADMIN_CONNECTION_NAME = "AdminUserDataDbConnection";
    private const string USER_DATA_CONNECTION_NAME = "UserDataDbConnection";

    private string _adminConnection = string.Empty;
    private string _databaseName = string.Empty;

    public bool DatabaseAvailable { get; private set; }

    public DbContextOptions<UserDataDbContext> Options { get; private set; } = default!;

    public UserDataDbContext CreateContext()
    {
        return new UserDataDbContext(Options);
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
        _databaseName = $"inia_cleanup_fk_test_{Guid.NewGuid():N}";

        try
        {
            await using var admin = new NpgsqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
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

        string testConnection = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName }.ConnectionString;
        Options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseNpgsql(testConnection, b =>
            {
                _ = b.MigrationsAssembly("IniaAi.Migrations.PostgreSql");
                _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "UserData");
            })
            .Options;

        await using UserDataDbContext context = CreateContext();
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
            ?? configuration.GetConnectionString(USER_DATA_CONNECTION_NAME);
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
            await terminate.ExecuteNonQueryAsync();

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
        }
    }
}
