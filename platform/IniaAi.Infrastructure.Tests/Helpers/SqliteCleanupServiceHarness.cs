using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Infrastructure.Tests.Helpers;

/// <summary>
/// <see cref="SoftDeleteCleanupService"/> over a fresh SQLite schema, for sweep tests that
/// need a real relational provider (set-based ExecuteDelete and ExecuteUpdate). Stored files
/// are ignored unless the test hands in a storage manager of its own.
/// </summary>
internal sealed class SqliteCleanupServiceHarness : IDisposable
{
    public SqliteUserDataDbContext Context { get; }
    public SoftDeleteCleanupService Service { get; }

    private readonly SqliteConnection _connection;

    public SqliteCleanupServiceHarness(IFileStorageManager? storage = null)
    {
        _connection = SqliteUserDataSchema.OpenInMemoryDatabase();

        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection)
            .Options;
        Context = new SqliteUserDataDbContext(options);

        Service = new SoftDeleteCleanupService(
            Context,
            storage ?? new NoopFileStorageManager(),
            [new ScreeningRecordRetention(Context), new ScreeningBacktestRetention(Context)],
            NullLogger<SoftDeleteCleanupService>.Instance);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
