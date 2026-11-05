using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IniaAi.Infrastructure.Persistence;

/// <summary>
/// A lock that one database session at a time holds: a PostgreSQL advisory lock or a SQL Server
/// application lock. Its connection is unpooled, so disposing it ends the session and releases the
/// lock, as a dying host's session does. Other providers have no such lock; their databases serve
/// one process, so there every caller gets a lock that holds nothing.
/// </summary>
internal sealed class DatabaseSessionLock : IDisposable
{
    private const string POSTGRES_TRY_ACQUIRE_SQL = "SELECT pg_try_advisory_lock(@key)";
    private const string POSTGRES_ACQUIRE_SQL = "SELECT pg_advisory_lock(@key)";
    private const string POSTGRES_KEY_PARAMETER = "key";

    private const string SQL_SERVER_ACQUIRE_SQL = """
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeout;
        SELECT @result;
        """;
    private const string SQL_SERVER_RESOURCE_PARAMETER = "@resource";
    private const string SQL_SERVER_TIMEOUT_PARAMETER = "@timeout";
    private const int SQL_SERVER_NO_WAIT = 0;
    private const int SQL_SERVER_WAIT_FOREVER = -1;
    private const int SQL_SERVER_TIMED_OUT = -1;

    private const int WAIT_WITHOUT_TIMEOUT = 0;

    private readonly DbConnection? _connection;

    private DatabaseSessionLock(DbConnection? connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// The lock, or null when another session holds it. With <paramref name="wait"/> the call waits
    /// for the holder to let go and never returns null.
    /// </summary>
    public static DatabaseSessionLock? TryAcquire(DbContext context, DatabaseLockName name, bool wait)
    {
        bool isPostgres = context.Database.IsNpgsql();
        if (!isPostgres && !context.Database.IsSqlServer())
        {
            return new DatabaseSessionLock(null);
        }

        string connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("A database session lock needs the context's connection string.");

        return context.Database.CreateExecutionStrategy().Execute(() => Open(isPostgres, connectionString, name, wait));
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    private static DatabaseSessionLock? Open(bool isPostgres, string connectionString, DatabaseLockName name, bool wait)
    {
        DbConnection connection = isPostgres
            ? new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString)
            : new SqlConnection(new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        DatabaseSessionLock? held = null;

        try
        {
            connection.Open();
            if (TryLock(connection, isPostgres, name, wait))
            {
                held = new DatabaseSessionLock(connection);
            }
        }
        finally
        {
            if (held is null)
            {
                connection.Dispose();
            }
        }

        return held;
    }

    private static bool TryLock(DbConnection connection, bool isPostgres, DatabaseLockName name, bool wait)
    {
        return isPostgres
            ? TryPostgresLock((NpgsqlConnection)connection, name, wait)
            : TrySqlServerLock((SqlConnection)connection, name, wait);
    }

    private static bool TryPostgresLock(NpgsqlConnection connection, DatabaseLockName name, bool wait)
    {
        using var command = new NpgsqlCommand(wait ? POSTGRES_ACQUIRE_SQL : POSTGRES_TRY_ACQUIRE_SQL, connection)
        {
            CommandTimeout = WAIT_WITHOUT_TIMEOUT,
        };
        _ = command.Parameters.AddWithValue(POSTGRES_KEY_PARAMETER, name.PostgresKey);
        if (wait)
        {
            _ = command.ExecuteNonQuery();

            return true;
        }

        return command.ExecuteScalar() is true;
    }

    private static bool TrySqlServerLock(SqlConnection connection, DatabaseLockName name, bool wait)
    {
        using var command = new SqlCommand(SQL_SERVER_ACQUIRE_SQL, connection) { CommandTimeout = WAIT_WITHOUT_TIMEOUT };
        _ = command.Parameters.AddWithValue(SQL_SERVER_RESOURCE_PARAMETER, name.SqlServerResource);
        _ = command.Parameters.AddWithValue(SQL_SERVER_TIMEOUT_PARAMETER, wait ? SQL_SERVER_WAIT_FOREVER : SQL_SERVER_NO_WAIT);
        int result = Convert.ToInt32(command.ExecuteScalar());
        if (result >= 0)
        {
            return true;
        }

        if (result == SQL_SERVER_TIMED_OUT && !wait)
        {
            return false;
        }

        throw new InvalidOperationException(
            $"SQL Server refused the {name.Description} lock (sp_getapplock returned {result}).");
    }
}

/// <summary>
/// One session lock's identity on each provider. Every lock needs its own key and resource.
/// </summary>
internal sealed record DatabaseLockName(long PostgresKey, string SqlServerResource, string Description)
{
    public static readonly DatabaseLockName Startup = new(7_265_102_811, "IniaAi.StartupDatabaseLock", "startup database");

    public static readonly DatabaseLockName TokenUsageTotals = new(7_265_102_812, "IniaAi.TokenUsageTotals", "usage totals");
}
