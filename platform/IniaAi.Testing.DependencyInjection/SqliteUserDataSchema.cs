using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Hands out SQLite user-data databases that already hold the schema. The schema is created once
/// per test process, in an in-memory template, and each database gets a copy: creating the schema
/// costs tens of milliseconds a database, a copy well under one. A copy runs no EF Core command, so
/// no interceptor of the test sees it.
/// </summary>
public static class SqliteUserDataSchema
{
    private const string IN_MEMORY = "DataSource=:memory:";
    private const string WRITE_AHEAD_LOG = "PRAGMA journal_mode = 'wal';";

    private static readonly Lazy<SqliteConnection> _template = new(CreateTemplate);
    private static readonly Lock _copyLock = new();

    /// <summary>
    /// Opens a new in-memory database with the schema. The database lives until the connection closes.
    /// </summary>
    public static SqliteConnection OpenInMemoryDatabase()
    {
        var database = new SqliteConnection(IN_MEMORY);
        try
        {
            database.Open();
            CopyInto(database);
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates the database file <paramref name="connectionString"/> names, with the schema, in the
    /// write-ahead-log mode EF Core gives a file it creates.
    /// </summary>
    public static void CreateFileDatabase(string connectionString)
    {
        using var database = new SqliteConnection(connectionString);
        database.Open();
        CopyInto(database);

        // A copy starts in rollback-journal mode. A test on a file database reads while job threads
        // write, through a connection each, and the write-ahead log lets those run side by side.
        using SqliteCommand writeAheadLog = database.CreateCommand();
        writeAheadLog.CommandText = WRITE_AHEAD_LOG;
        _ = writeAheadLog.ExecuteNonQuery();
    }

    private static void CopyInto(SqliteConnection database)
    {
        SqliteConnection template = _template.Value;

        // Test classes run in parallel, and one connection is not safe to use from two threads.
        lock (_copyLock)
        {
            template.BackupDatabase(database);
        }
    }

    private static SqliteConnection CreateTemplate()
    {
        var template = new SqliteConnection(IN_MEMORY);
        template.Open();

        using var context = new SqliteUserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(template)
            .Options);
        _ = context.Database.EnsureCreated();

        return template;
    }
}
