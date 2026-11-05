using IniaAi.Configuration;
using IniaAi.Core.Workflows.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows.Scheduling.Helpers;

/// <summary>
/// <see cref="LevelScheduler"/> is sealed (not substitutable), so handler tests construct the real
/// one under an explicit parallelism cap.
/// </summary>
internal static class TestSchedulers
{
    internal static LevelScheduler NewRealScheduler(int maxBlockParallelism)
    {
        PlatformSettings capSettings = new() { MaxBlockParallelism = maxBlockParallelism };

        return NewSchedulerWithCap(new BlockConcurrencyCap(Options.Create(capSettings)));
    }

    /// <summary>
    /// A real scheduler over a cap the test holds, so permit accounting is observable end to end.
    /// </summary>
    internal static LevelScheduler NewSchedulerWithCap(IBlockConcurrencyCap cap)
    {
        return new LevelScheduler(cap, NullLogger<LevelScheduler>.Instance);
    }
}
