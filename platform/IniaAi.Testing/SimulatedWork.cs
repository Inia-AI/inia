namespace IniaAi.Testing;

/// <summary>
/// Stands in for work that takes time in a test double: a slow reply, a held slot, a block that runs
/// long enough to overlap another. Keep the duration short; a test that only needs the work to end
/// should await a signal instead.
/// </summary>
public static class SimulatedWork
{
    public static Task TakesAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        // Waits on purpose: the duration is the work being simulated.
        return Task.Delay(duration, cancellationToken);
    }
}
