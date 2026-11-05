namespace IniaAi.Testing;

/// <summary>
/// Waits for a condition that only polling can see, such as a row a background job writes. It checks
/// at once, then every few milliseconds, and fails at the deadline, so it returns as soon as the
/// condition holds. Await the task or a signal instead when the code under test offers one.
/// </summary>
public static class Eventually
{
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task UntilAsync(
        Func<bool> condition,
        string because,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? _defaultTimeout);

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {because}.");
            }

            // Waits on purpose: the poll interval, which ends as soon as the condition holds.
            await Task.Delay(_interval, cancellationToken);
        }
    }
}
