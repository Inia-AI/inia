using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// A test's database file goes together with the write-ahead log and the shared-memory index
/// SQLite keeps beside it, which stay behind when a connection is open as the test ends.
/// </summary>
public class SqliteTestDatabaseFileTests
{
    [Fact]
    public void Delete_RemovesTheDatabaseAndTheFilesBesideIt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sqlite-test-database-file-{Guid.NewGuid():N}.db");
        string[] files = [path, $"{path}-wal", $"{path}-shm"];
        foreach (string file in files)
        {
            File.WriteAllBytes(file, [1]);
        }

        SqliteTestDatabaseFile.Delete(path);

        Assert.All(files, file => Assert.False(File.Exists(file), $"{file} is still there."));
    }
}
