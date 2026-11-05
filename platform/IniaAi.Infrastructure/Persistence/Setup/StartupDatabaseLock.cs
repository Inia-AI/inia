using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Persistence.Setup;

/// <summary>
/// Lets one host at a time migrate and seed a database, so hosts that start together need no start
/// order. EF Core's own PostgreSQL migration lock ends at each migration's commit while the pending
/// list is read once, and its SQL Server lock ends with the migration, before seeding starts. This
/// session-level lock covers both.
/// </summary>
internal static class StartupDatabaseLock
{
    public static DatabaseSessionLock? Acquire(DbContext context, ILogger logger)
    {
        DatabaseSessionLock? held = DatabaseSessionLock.TryAcquire(context, DatabaseLockName.Startup, wait: false);
        if (held is not null)
        {
            return held;
        }

        logger.LogInformation(
            "Another host is migrating or seeding database {Database}; waiting for its startup lock.",
            context.Database.GetDbConnection().Database);

        return DatabaseSessionLock.TryAcquire(context, DatabaseLockName.Startup, wait: true);
    }
}
