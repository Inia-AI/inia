using Microsoft.Data.Sqlite;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Removes a test's SQLite database file, with the write-ahead log and the shared-memory index
/// SQLite keeps beside it. A background job of the test can still hold the file open for a moment
/// after the test ends, and Windows refuses to delete an open file, so the delete is retried for
/// a while. A database file that stays locked is left in the temp folder with the files beside it:
/// a teardown that cannot tidy a temp file must not fail the test.
/// </summary>
public static class SqliteTestDatabaseFile
{
    private const int ATTEMPTS = 50;
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly string[] _sidecarSuffixes = ["-wal", "-shm"];

    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        if (!TryDelete(path))
        {
            return;
        }

        foreach (string suffix in _sidecarSuffixes)
        {
            _ = TryDelete(path + suffix);
        }
    }

    private static bool TryDelete(string path)
    {
        for (int attempt = 1; attempt <= ATTEMPTS; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException) when (attempt < ATTEMPTS)
            {
                // Waits on purpose: the OS releases the file on its own schedule, with no signal to await.
                Thread.Sleep(_retryDelay);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }
}
