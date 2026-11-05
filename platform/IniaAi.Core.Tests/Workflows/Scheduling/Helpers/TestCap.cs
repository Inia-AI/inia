using IniaAi.Core.Workflows.Scheduling;

namespace IniaAi.Core.Tests.Workflows.Scheduling.Helpers;

/// <summary>
/// Hand-rolled <see cref="IBlockConcurrencyCap"/> for tests: a process-local
/// <see cref="SemaphoreSlim"/> that tracks how many permits are held now, the most ever held at one
/// instant, and the acquire/release totals. Conservation is asserted as
/// <see cref="Acquired"/> == <see cref="Released"/>, so a spurious release fails the test even when
/// the held count still lands on zero. A wait cancelled before the permit is granted increments
/// neither.
/// </summary>
internal sealed class TestCap : IBlockConcurrencyCap
{
    private readonly SemaphoreSlim _semaphore;
    private int _currentlyHeld;
    private int _maxConcurrentlyHeld;
    private int _acquired;
    private int _released;

    public TestCap(int maxParallelism)
    {
        if (maxParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxParallelism));
        }

        MaxParallelism = maxParallelism;
        _semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
    }

    public int MaxParallelism { get; }
    public int CurrentlyHeld => Volatile.Read(ref _currentlyHeld);
    public int MaxConcurrentlyHeld => Volatile.Read(ref _maxConcurrentlyHeld);
    public int Acquired => Volatile.Read(ref _acquired);
    public int Released => Volatile.Read(ref _released);

    public async Task AcquireAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);

        _ = Interlocked.Increment(ref _acquired);
        int now = Interlocked.Increment(ref _currentlyHeld);
        SharedCounter.InterlockedMax(ref _maxConcurrentlyHeld, now);
    }

    public void Release()
    {
        _ = Interlocked.Increment(ref _released);
        _ = Interlocked.Decrement(ref _currentlyHeld);
        _ = _semaphore.Release();
    }
}
