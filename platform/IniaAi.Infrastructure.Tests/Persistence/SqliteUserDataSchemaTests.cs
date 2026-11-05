using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// A file database starts the way EF Core starts a file it creates: with the schema, in
/// write-ahead-log mode, which lets the tests on it read while job threads write.
/// </summary>
public class SqliteUserDataSchemaTests
{
    private const string WRITE_AHEAD_LOG = "wal";

    [Fact]
    public async Task AFileDatabase_HoldsTheSchema_InWriteAheadLogMode()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string path = Path.Combine(Path.GetTempPath(), $"user-data-schema-{Guid.NewGuid():N}.db");
        string connectionString = $"DataSource={path};Pooling=False";

        try
        {
            SqliteUserDataSchema.CreateFileDatabase(connectionString);

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using SqliteCommand journalMode = connection.CreateCommand();
                journalMode.CommandText = "PRAGMA journal_mode";
                Assert.Equal(WRITE_AHEAD_LOG, await journalMode.ExecuteScalarAsync(cancellationToken));
            }

            DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
                .UseSqlite(connectionString)
                .Options;
            await using (var db = new SqliteUserDataDbContext(options))
            {
                _ = db.Organizations.Add(new Organization("Stored in the file"));
                _ = await db.SaveChangesAsync(cancellationToken);
            }

            await using var reader = new SqliteUserDataDbContext(options);
            Assert.Equal(1, await reader.Organizations.CountAsync(cancellationToken));
        }
        finally
        {
            SqliteTestDatabaseFile.Delete(path);
        }
    }
}
