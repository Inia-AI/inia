using IniaAi.Configuration;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Workflows.Scheduling;

/// <summary>
/// Per-workflow concurrency cap for parallel block execution. Registered scoped, so each workflow
/// execution gets its own cap of <see cref="PlatformSettings.MaxBlockParallelism"/> in-flight
/// blocks; total host concurrency is the cap times the number of concurrent workflows
/// (bounded separately by MaxConcurrentJobs).
/// </summary>
public interface IBlockConcurrencyCap
{
    /// <summary>
    /// Maximum number of in-flight block executions within a single workflow-execution scope.
    /// Immutable for the lifetime of the cap. <see cref="LevelScheduler"/> reads this to choose between the
    /// parallel layered dispatch (<c>&gt; 1</c>) and the DOP=1 sequential-order fallback
    /// (<c>== 1</c>) which guarantees byte-identical ordering vs the pre-scheduler engine.
    /// </summary>
    int MaxParallelism { get; }

    Task AcquireAsync(CancellationToken ct);
    void Release();
}

/// <summary>
/// Default <see cref="IBlockConcurrencyCap"/>. Backed by a <see cref="SemaphoreSlim"/> sized
/// from <see cref="PlatformSettings.MaxBlockParallelism"/>. One instance per workflow-execution
/// scope; the value is read when the scope is created.
/// </summary>
public sealed class BlockConcurrencyCap : IBlockConcurrencyCap
{
    private readonly SemaphoreSlim _semaphore;

    public BlockConcurrencyCap(IOptions<PlatformSettings> platformSettings)
    {
        int max = platformSettings.Value.MaxBlockParallelism;
        if (max <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(platformSettings),
                max,
                $"{nameof(PlatformSettings.MaxBlockParallelism)} must be > 0; got {max}.");
        }

        MaxParallelism = max;
        _semaphore = new SemaphoreSlim(max, max);
    }

    public int MaxParallelism { get; }

    public Task AcquireAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    public void Release() => _semaphore.Release();
}
