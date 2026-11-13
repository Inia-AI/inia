using IniaAi.Configuration.Data;
using IniaAi.Infrastructure.Persistence;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Setup;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace IniaAi.Domain;

public class DatabaseMigrationHelper
{
    private const int CREATED_ELSEWHERE_CHECKS = 60;
    private static readonly TimeSpan _createdElsewhereCheckInterval = TimeSpan.FromSeconds(1);

    private const string SQUASH_RELEASE = "2.2.0";

    // Keep this until the next squash replaces it: a database that upgrades from an older release
    // later still needs its history moved.
    private static readonly Dictionary<(string Provider, string ContextType), string> _lastSquashedMigrations = new()
    {
        [(DatabaseProviders.MSSQL, CoreDbContext.SCHEMA_NAME)] = "20261016092237_WorkflowNotes",
        [(DatabaseProviders.MSSQL, UserDataDbContext.SCHEMA_NAME)] = "20261016092224_WorkflowNotes",
        [(DatabaseProviders.POSTGRE_SQL, CoreDbContext.SCHEMA_NAME)] = "20261016092307_WorkflowNotes",
        [(DatabaseProviders.POSTGRE_SQL, UserDataDbContext.SCHEMA_NAME)] = "20261016092256_WorkflowNotes",
    };

    private readonly ILogger<DatabaseMigrationHelper> _logger;

    public DatabaseMigrationHelper(ILogger<DatabaseMigrationHelper> logger)
    {
        _logger = logger;
    }

    public void MigrateDatabase(DbContext context, Action? seedUnderLock = null)
    {
        using DatabaseSessionLock? migrationLock = MigrateHoldingLock(context);
        seedUnderLock?.Invoke();
    }

    private DatabaseSessionLock? MigrateHoldingLock(DbContext context)
    {
        string contextType = context.GetType().Name.Replace("DbContext", string.Empty);
        DatabaseSessionLock? migrationLock = null;

        try
        {
            bool databaseExisted = context.Database.CanConnect();
            if (!databaseExisted)
            {
                _logger.LogInformation(
                    "{name} database does not exist. Creating database and applying migrations.",
                    contextType);
                CreateDatabase(context, contextType);
            }

            migrationLock = StartupDatabaseLock.Acquire(context, _logger);
            MoveToSquashedHistory(context, contextType);

            if (!databaseExisted)
            {
                context.Database.Migrate();
                _logger.LogInformation(
                    "{name} database created and migrations applied.",
                    contextType);
            }
            else if (context.Database.GetPendingMigrations().Any())
            {
                _logger.LogInformation(
                    "{name}: pending migrations detected. Applying pending migrations.",
                    contextType);
                context.Database.Migrate();
                _logger.LogInformation(
                    "{name}: Pending migrations applied.",
                    contextType);
            }
            else if (!AllTablesExist(context))
            {
                _logger.LogInformation(
                    "{name}: Missing tables detected. Reapplying migrations.",
                    contextType);
                context.Database.Migrate();
                _logger.LogInformation(
                    "{name}: Missing tables detected. Migrations reapplied.",
                    contextType);
            }
            else
            {
                _logger.LogInformation("{name} database is up to date.", contextType);
            }

            _ = ObsoleteRagSettingsCleanupHelper.DeleteObsoleteRagSettings(context, _logger);

            return migrationLock;
        }
        catch (Exception ex)
        {
            migrationLock?.Dispose();
            _logger.LogError(
                ex,
                "An error occurred while migrating the database {name}",
                contextType);

            throw;
        }
    }

    private void MoveToSquashedHistory(DbContext context, string contextType)
    {
        string? provider = context.Database.IsNpgsql() ? DatabaseProviders.POSTGRE_SQL : context.Database.IsSqlServer() ? DatabaseProviders.MSSQL : null;
        if (provider is null || !_lastSquashedMigrations.TryGetValue((provider, contextType), out string? lastSquashed))
        {
            return;
        }

        string squashedMigration = context.Database.GetMigrations().First();
        List<string> applied = [.. context.Database.GetAppliedMigrations()];
        if (applied.Count == 0 || applied.Contains(squashedMigration))
        {
            return;
        }

        if (!applied.Contains(lastSquashed))
        {
            throw new InvalidOperationException(
                $"{contextType}: the migration history does not include '{lastSquashed}', the last migration of " +
                $"release {SQUASH_RELEASE}; its last applied migration is '{applied.Max(StringComparer.Ordinal)}'. " +
                $"Migrations up to '{lastSquashed}' are squashed into '{squashedMigration}'. Start release " +
                $"{SQUASH_RELEASE}, or a later release from before the squash, on this database once, then start this release.");
        }

        List<string> squashed = [.. applied.Where(id => string.CompareOrdinal(id, lastSquashed) <= 0)];
        IHistoryRepository history = context.GetService<IHistoryRepository>();
        string script = string.Concat(squashed.Select(history.GetDeleteScript))
            + history.GetInsertScript(new HistoryRow(squashedMigration, ProductInfo.GetVersion()));

        context.Database.CreateExecutionStrategy().Execute(() =>
        {
            using IDbContextTransaction transaction = context.Database.BeginTransaction();
            _ = context.Database.ExecuteSqlRaw(script);  // Raw SQL reviewed: EF-generated migration-history script; no request data
            transaction.Commit();
        });

        _logger.LogInformation(
            "{name}: migration history moved: {count} migrations up to {lastSquashed} replaced by {squashed}.",
            contextType,
            squashed.Count,
            lastSquashed,
            squashedMigration);
    }

    private void CreateDatabase(DbContext context, string contextType)
    {
        IRelationalDatabaseCreator creator = context.GetService<IRelationalDatabaseCreator>();
        try
        {
            creator.Create();
        }
        catch (DbException)
        {
            _logger.LogInformation(
                "{name}: database creation failed; waiting for a host that may be creating it.",
                contextType);
            if (!CreatedByAnotherHost(creator))
            {
                throw;
            }
        }
    }

    // A host that started at the same time may be creating the database. SQL Server reserves the
    // name at once but brings the database online only seconds later.
    private static bool CreatedByAnotherHost(IRelationalDatabaseCreator creator)
    {
        for (int check = 0; check < CREATED_ELSEWHERE_CHECKS; check++)
        {
            if (creator.Exists())
            {
                return true;
            }

            Thread.Sleep(_createdElsewhereCheckInterval);
        }

        return false;
    }


    private bool AllTablesExist(DbContext context)
    {
        var expectedTables = context.Model.GetEntityTypes()
            .Select(t => new { Schema = t.GetSchema(), Name = t.GetTableName() })
            .Where(t => !string.IsNullOrWhiteSpace(t.Name)) // filter out nulls
            .ToList();

        List<TableInfo> existingTables = GetDatabaseTables(context);

        var missingTables = expectedTables
            .Where(et => !existingTables.Any(t =>
                (string.IsNullOrEmpty(et.Schema) || string.Equals(t.SchemaName, et.Schema, StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(t.TableName, et.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingTables.Count != 0)
        {
            _logger.LogWarning(
                "Missing tables: {MissingTables}",
                string.Join(", ", missingTables.Select(mt => $"{mt.Schema ?? "(default schema)"}.{mt.Name}")));
        }

        return missingTables.Count == 0;
    }

    private static List<TableInfo> GetDatabaseTables(DbContext context)
    {
        string? databaseType = context.Database.ProviderName;

        return databaseType switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" =>
                [.. context.Database.SqlQuery<TableInfo>(
                    $"SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS TableName FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")],

            "Npgsql.EntityFrameworkCore.PostgreSQL" =>
                [.. context.Database.SqlQuery<TableInfo>(
                    $"SELECT table_schema AS SchemaName, table_name AS TableName FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')")],

            _ => throw new NotSupportedException($"Unsupported database provider: {databaseType}")
        };
    }

    private class TableInfo
    {
        public string? SchemaName { get; set; }
        public string? TableName { get; set; }
    }
}
