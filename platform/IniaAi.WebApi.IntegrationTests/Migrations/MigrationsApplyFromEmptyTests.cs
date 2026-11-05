using IniaAi.Domain;
using IniaAi.Domain.Entities.Tools;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace IniaAi.WebApi.IntegrationTests.Migrations;

// Most of the integration suite runs against PostgreSQL only, which cannot see
// provider-specific DDL limits — e.g. SQL Server rejecting a table with two ON DELETE CASCADE
// foreign keys as "multiple cascade paths" (error 1785) where Postgres accepts it. This guard
// applies the full migration set to a throwaway empty database on every provider, so that class
// of regression fails here instead of only on a fresh MSSQL deployment.
// It also guards the squashed history: startup moves a database migrated through the migrations
// a squash replaced onto the squashed history, and refuses a history from before the squash.
//
// Connection strings (with credentials) come only from the INIA_TEST_MSSQL / INIA_TEST_PG
// environment variables — never hard-coded. Each provider's case skips when its variable is unset.
public class MigrationsApplyFromEmptyTests
{
    private const string MSSQL = "MSSQL";
    private const string POSTGRESQL = "PostgreSQL";

    private const string MSSQL_MIGRATIONS_ASSEMBLY = "IniaAi.Migrations.Mssql";
    private const string PG_MIGRATIONS_ASSEMBLY = "IniaAi.Migrations.PostgreSql";

    private const string USER_DATA_SCHEMA = "UserData";
    private const string CORE_SCHEMA = "Core";

    private const string TEST_DB_PREFIX = "iniaai_migguard_";

    // Matches the app's migration command timeout (DatabaseConfigurator) so a full from-empty
    // apply on a slow runner doesn't hit the ~30s provider default and report a false failure.
    private const int MIGRATION_COMMAND_TIMEOUT_SECONDS = 300;

    private const string MSSQL_CONNECTION_ENV = "INIA_TEST_MSSQL";
    private const string MSSQL_ADMIN_DATABASE = "master";

    private const string PG_CONNECTION_ENV = "INIA_TEST_PG";
    private const string PG_ADMIN_DATABASE = "postgres";

    private const int HOST_COUNT = 4;
    private const string FAILED_SEED_MESSAGE = "The seed failed.";

    private const string EARLIER_MIGRATION_ID = "20260204060720_InitialCreate";

    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _lockReleaseTimeout = TimeSpan.FromSeconds(60);
    private static readonly DatabaseMigrationHelper _migrationHelper = new(NullLogger<DatabaseMigrationHelper>.Instance);

    private static readonly Dictionary<(string Provider, string Schema), string> _lastSquashedMigration = new()
    {
        [(MSSQL, CORE_SCHEMA)] = "20261016092237_WorkflowNotes",
        [(MSSQL, USER_DATA_SCHEMA)] = "20261016092224_WorkflowNotes",
        [(POSTGRESQL, CORE_SCHEMA)] = "20261016092307_WorkflowNotes",
        [(POSTGRESQL, USER_DATA_SCHEMA)] = "20261016092256_WorkflowNotes",
    };

    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task All_migrations_apply_to_an_empty_database(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        string? baseConnection = Environment.GetEnvironmentVariable(ConnectionEnvVar(provider));
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            Assert.Skip($"Set {ConnectionEnvVar(provider)} to a {provider} connection string to run this migration guard.");
        }

        await SkipIfServerUnreachableAsync(provider, baseConnection, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<UserDataDbContext> userDataOptions = BuildUserDataOptions(provider, baseConnection, databaseName);
        DbContextOptions<CoreDbContext> coreOptions = BuildCoreOptions(provider, baseConnection, databaseName);

        try
        {
            await using (UserDataDbContext userData = new(userDataOptions))
            {
                await userData.Database.MigrateAsync(cancellationToken);
                Assert.Empty(await userData.Database.GetPendingMigrationsAsync(cancellationToken));
            }

            await using (CoreDbContext core = new(coreOptions))
            {
                await core.Database.MigrateAsync(cancellationToken);
                Assert.Empty(await core.Database.GetPendingMigrationsAsync(cancellationToken));
            }
        }
        finally
        {
            await using CoreDbContext cleanup = new(coreOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task Hosts_starting_together_apply_pending_migrations_once(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string baseConnection = await RequireServerAsync(provider, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<UserDataDbContext> userDataOptions = BuildUserDataOptions(provider, baseConnection, databaseName);
        DbContextOptions<CoreDbContext> coreOptions = BuildCoreOptions(provider, baseConnection, databaseName);

        try
        {
            await using (UserDataDbContext userData = new(userDataOptions))
            {
                CreateWithNoMigrationApplied(userData);
            }

            await using (CoreDbContext core = new(coreOptions))
            {
                CreateWithNoMigrationApplied(core);
            }

            await MigrateTogetherAsync(() => new UserDataDbContext(userDataOptions));
            await MigrateTogetherAsync(() => new CoreDbContext(coreOptions));

            await AssertNothingPendingAsync(userDataOptions, coreOptions, cancellationToken);
        }
        finally
        {
            await using CoreDbContext cleanup = new(coreOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task Hosts_starting_together_create_a_missing_database_once(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string baseConnection = await RequireServerAsync(provider, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<UserDataDbContext> userDataOptions = BuildUserDataOptions(provider, baseConnection, databaseName);
        DbContextOptions<CoreDbContext> coreOptions = BuildCoreOptions(provider, baseConnection, databaseName);

        try
        {
            await MigrateTogetherAsync(() => new UserDataDbContext(userDataOptions));
            await MigrateTogetherAsync(() => new CoreDbContext(coreOptions));

            await AssertNothingPendingAsync(userDataOptions, coreOptions, cancellationToken);
        }
        finally
        {
            await using CoreDbContext cleanup = new(coreOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task Hosts_starting_together_seed_the_core_database_once(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string baseConnection = await RequireServerAsync(provider, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<CoreDbContext> coreOptions = BuildCoreOptions(provider, baseConnection, databaseName);

        try
        {
            await MigrateTogetherAsync(() => new CoreDbContext(coreOptions), core => new CoreDbInitializer(core).Initialize());

            await using CoreDbContext seeded = new(coreOptions);
            Assert.Equal(1, await seeded.PdfOcrPrompts.CountAsync(cancellationToken));
            List<string?> templateTypeIds = await seeded.Workflows.Select(w => w.WorkflowTypeId).ToListAsync(cancellationToken);
            Assert.NotEmpty(templateTypeIds);
            Assert.Equal(templateTypeIds.Count, templateTypeIds.Distinct().Count());
        }
        finally
        {
            await using CoreDbContext cleanup = new(coreOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task A_seed_that_fails_releases_the_startup_lock(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string baseConnection = await RequireServerAsync(provider, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<CoreDbContext> coreOptions = BuildCoreOptions(provider, baseConnection, databaseName);

        try
        {
            await using (CoreDbContext failing = new(coreOptions))
            {
                InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                    () => _migrationHelper.MigrateDatabase(failing, () => throw new InvalidOperationException(FAILED_SEED_MESSAGE)));
                Assert.Equal(FAILED_SEED_MESSAGE, failure.Message);
            }

            await using CoreDbContext next = new(coreOptions);
            await Task.Run(() => _migrationHelper.MigrateDatabase(next), cancellationToken)
                .WaitAsync(_lockReleaseTimeout, cancellationToken);
        }
        finally
        {
            await using CoreDbContext cleanup = new(coreOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }


    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task A_database_at_the_end_of_the_old_migration_chain_moves_onto_the_squashed_history(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string baseConnection = await RequireServerAsync(provider, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<UserDataDbContext> userDataOptions = BuildUserDataOptions(provider, baseConnection, databaseName);
        const string ORGANIZATION = "org-squashed-history";

        try
        {
            await using (UserDataDbContext userData = new(userDataOptions))
            {
                _migrationHelper.MigrateDatabase(userData);
                await SeedConversionTypeAsync(userData, ORGANIZATION, cancellationToken);
                ReplaceHistory(userData, LaterReleaseHistory(userData, provider));
            }

            await using UserDataDbContext restarted = new(userDataOptions);
            MigrateAndAssertFullHistory(restarted);
            Assert.True(await restarted.ConversionTypes.AnyAsync(type => type.OrganizationId == ORGANIZATION, cancellationToken));
        }
        finally
        {
            await using UserDataDbContext cleanup = new(userDataOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task A_database_on_release_2_2_0_moves_onto_the_squashed_history_and_applies_the_later_migrations(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string baseConnection = await RequireServerAsync(provider, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<UserDataDbContext> userDataOptions = BuildUserDataOptions(provider, baseConnection, databaseName);
        DbContextOptions<CoreDbContext> coreOptions = BuildCoreOptions(provider, baseConnection, databaseName);
        const string ORGANIZATION = "org-release-2-2-0";

        try
        {
            await using (UserDataDbContext userData = new(userDataOptions))
            {
                MigrateToInitialCreateOnly(userData);
                await SeedConversionTypeAsync(userData, ORGANIZATION, cancellationToken);
                ReplaceHistory(userData, Release220History(userData, provider));
            }

            await using (CoreDbContext core = new(coreOptions))
            {
                MigrateToInitialCreateOnly(core);
                ReplaceHistory(core, Release220History(core, provider));
            }

            await using (UserDataDbContext restarted = new(userDataOptions))
            {
                MigrateAndAssertFullHistory(restarted);
                Assert.True(await restarted.ConversionTypes.AnyAsync(type => type.OrganizationId == ORGANIZATION, cancellationToken));
            }

            await using (CoreDbContext restarted = new(coreOptions))
            {
                MigrateAndAssertFullHistory(restarted);
                Assert.Equal(0, await restarted.OrganizationStoredKeys.CountAsync(cancellationToken));
            }
        }
        finally
        {
            await using CoreDbContext cleanup = new(coreOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task A_database_from_before_release_2_2_0_stops_startup_and_keeps_its_history(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string baseConnection = await RequireServerAsync(provider, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<CoreDbContext> coreOptions = BuildCoreOptions(provider, baseConnection, databaseName);

        try
        {
            await using (CoreDbContext core = new(coreOptions))
            {
                CreateWithNoMigrationApplied(core);
                ReplaceHistory(core, EARLIER_MIGRATION_ID);
            }

            await using CoreDbContext restarted = new(coreOptions);
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => _migrationHelper.MigrateDatabase(restarted));
            Assert.StartsWith($"{CORE_SCHEMA}: the migration history does not include '{LastSquashedMigration(restarted, provider)}'", failure.Message);
            Assert.Equal([EARLIER_MIGRATION_ID], restarted.Database.GetAppliedMigrations());
        }
        finally
        {
            await using CoreDbContext cleanup = new(coreOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData(MSSQL)]
    [InlineData(POSTGRESQL)]
    public async Task Hosts_starting_together_move_an_old_history_once(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string baseConnection = await RequireServerAsync(provider, cancellationToken);

        string databaseName = TEST_DB_PREFIX + Guid.NewGuid().ToString("N");
        DbContextOptions<CoreDbContext> coreOptions = BuildCoreOptions(provider, baseConnection, databaseName);

        try
        {
            await using (CoreDbContext core = new(coreOptions))
            {
                _migrationHelper.MigrateDatabase(core);
                ReplaceHistory(core, LaterReleaseHistory(core, provider));
            }

            await MigrateTogetherAsync(() => new CoreDbContext(coreOptions));

            await using CoreDbContext moved = new(coreOptions);
            Assert.Equal(moved.Database.GetMigrations(), moved.Database.GetAppliedMigrations());
        }
        finally
        {
            await using CoreDbContext cleanup = new(coreOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    private static async Task<string> RequireServerAsync(string provider, CancellationToken cancellationToken)
    {
        string? baseConnection = Environment.GetEnvironmentVariable(ConnectionEnvVar(provider));
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            Assert.Skip($"Set {ConnectionEnvVar(provider)} to a {provider} connection string to run this migration guard.");
        }

        await SkipIfServerUnreachableAsync(provider, baseConnection, cancellationToken);

        return baseConnection;
    }

    private static void CreateWithNoMigrationApplied(DbContext context)
    {
        context.GetService<IMigrator>().Migrate(Migration.InitialDatabase);
    }

    private static void MigrateToInitialCreateOnly(DbContext context)
    {
        context.GetService<IMigrator>().Migrate(context.Database.GetMigrations().First());
    }

    private static void MigrateAndAssertFullHistory(DbContext context)
    {
        _migrationHelper.MigrateDatabase(context);
        Assert.Equal(context.Database.GetMigrations(), context.Database.GetAppliedMigrations());
    }

    private static async Task SeedConversionTypeAsync(UserDataDbContext userData, string organization, CancellationToken cancellationToken)
    {
        _ = userData.ConversionTypes.Add(new ConversionType { Name = organization, DisplayName = organization, OrganizationId = organization });
        _ = await userData.SaveChangesAsync(cancellationToken);
    }

    private static string LastSquashedMigration(DbContext context, string provider)
    {
        return _lastSquashedMigration[(provider, context is CoreDbContext ? CORE_SCHEMA : USER_DATA_SCHEMA)];
    }

    private static string[] Release220History(DbContext context, string provider)
    {
        return [EARLIER_MIGRATION_ID, LastSquashedMigration(context, provider)];
    }

    private static string[] LaterReleaseHistory(DbContext context, string provider)
    {
        return [.. Release220History(context, provider), .. context.Database.GetMigrations().Skip(1)];
    }

    private static void ReplaceHistory(DbContext context, params string[] migrationIds)
    {
        IHistoryRepository history = context.GetService<IHistoryRepository>();
        string script = string.Concat(context.Database.GetAppliedMigrations().Select(history.GetDeleteScript))
            + string.Concat(migrationIds.Select(id => history.GetInsertScript(new HistoryRow(id, ProductInfo.GetVersion()))));

        _ = context.Database.ExecuteSqlRaw(script);
    }

    private static async Task MigrateTogetherAsync<TContext>(Func<TContext> createContext, Action<TContext>? seed = null)
        where TContext : DbContext
    {
        using var start = new Barrier(HOST_COUNT);

        await Task.WhenAll(Enumerable.Range(0, HOST_COUNT).Select(_ => Task.Run(() =>
        {
            using TContext context = createContext();
            Assert.True(start.SignalAndWait(_startTimeout), "Not every host reached the start line.");
            _migrationHelper.MigrateDatabase(context, seed is null ? null : () => seed(context));
        })));
    }

    private static async Task AssertNothingPendingAsync(
        DbContextOptions<UserDataDbContext> userDataOptions,
        DbContextOptions<CoreDbContext> coreOptions,
        CancellationToken cancellationToken)
    {
        await using UserDataDbContext userData = new(userDataOptions);
        Assert.Empty(await userData.Database.GetPendingMigrationsAsync(cancellationToken));

        await using CoreDbContext core = new(coreOptions);
        Assert.Empty(await core.Database.GetPendingMigrationsAsync(cancellationToken));
    }

    private static async Task SkipIfServerUnreachableAsync(string provider, string baseConnection, CancellationToken cancellationToken)
    {
        await using CoreDbContext probe = new(BuildCoreOptions(provider, baseConnection, AdminDatabase(provider)));

        if (!await probe.Database.CanConnectAsync(cancellationToken))
        {
            Assert.Skip($"{provider} server is not reachable — skipping the migration guard for this provider.");
        }
    }

    private static DbContextOptions<UserDataDbContext> BuildUserDataOptions(string provider, string baseConnection, string databaseName)
    {
        DbContextOptionsBuilder<UserDataDbContext> builder = new();
        ConfigureProvider(builder, provider, baseConnection, databaseName, USER_DATA_SCHEMA);
        return builder.Options;
    }

    private static DbContextOptions<CoreDbContext> BuildCoreOptions(string provider, string baseConnection, string databaseName)
    {
        DbContextOptionsBuilder<CoreDbContext> builder = new();
        ConfigureProvider(builder, provider, baseConnection, databaseName, CORE_SCHEMA);
        return builder.Options;
    }

    private static void ConfigureProvider<TContext>(
        DbContextOptionsBuilder<TContext> builder,
        string provider,
        string baseConnection,
        string databaseName,
        string historySchema)
        where TContext : DbContext
    {
        switch (provider)
        {
            case MSSQL:
                _ = builder.UseSqlServer(
                    MssqlConnectionString(baseConnection, databaseName),
                    sqlServer =>
                    {
                        _ = sqlServer.MigrationsAssembly(MSSQL_MIGRATIONS_ASSEMBLY);
                        _ = sqlServer.MigrationsHistoryTable(HistoryRepository.DefaultTableName, historySchema);
                        _ = sqlServer.CommandTimeout(MIGRATION_COMMAND_TIMEOUT_SECONDS);
                        _ = sqlServer.EnableRetryOnFailure();
                    });
                break;

            case POSTGRESQL:
                _ = builder.UseNpgsql(
                    PostgresConnectionString(baseConnection, databaseName),
                    npgsql =>
                    {
                        _ = npgsql.MigrationsAssembly(PG_MIGRATIONS_ASSEMBLY);
                        _ = npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, historySchema);
                        _ = npgsql.CommandTimeout(MIGRATION_COMMAND_TIMEOUT_SECONDS);
                        _ = npgsql.EnableRetryOnFailure();
                    });
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
        }

        // This context is configured minimally (no tenant/convention setup), so its model won't
        // match the snapshot — irrelevant here, since we only assert the migrations apply. Snapshot
        // fidelity is covered separately by `dotnet ef migrations has-pending-model-changes`.
        _ = builder.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    private static string ConnectionEnvVar(string provider)
    {
        return provider == MSSQL ? MSSQL_CONNECTION_ENV : PG_CONNECTION_ENV;
    }

    private static string AdminDatabase(string provider)
    {
        return provider == MSSQL ? MSSQL_ADMIN_DATABASE : PG_ADMIN_DATABASE;
    }

    private static string MssqlConnectionString(string baseConnection, string databaseName)
    {
        return new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
    }

    private static string PostgresConnectionString(string baseConnection, string databaseName)
    {
        return new NpgsqlConnectionStringBuilder(baseConnection) { Database = databaseName }.ConnectionString;
    }
}
