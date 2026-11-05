using System.Collections.Concurrent;
using System.Diagnostics;
using IniaAi.Testing;

namespace IniaAi.Core.Tests.Workflows.Scheduling.Helpers;

/// <summary>
/// Thread-safe instance counter used by parallel-execution tests to prove (or disprove) real
/// concurrency without ever consulting wall-clock duration. Every block strategy under test
/// calls <see cref="OnEnter"/> on entry and <see cref="OnExit"/> in a <c>finally</c>; tests
/// then assert on <see cref="MaxObserved"/> and the recorded <see cref="Events"/> ordering.
/// </summary>
internal sealed class SharedCounter
{
    private int _current;
    private int _maxObserved;

    /// <summary>
    /// Every enter/exit event in occurrence order (best effort: <see cref="ConcurrentBag{T}"/>
    /// does not preserve insertion order, but the <see cref="TimestampTicks"/> field does).
    /// Use <see cref="Stopwatch.GetTimestamp"/> for sub-microsecond resolution so ordering
    /// assertions are not defeated by <see cref="DateTime.UtcNow"/>'s ~15 ms granularity on
    /// some hosts (reviewer feedback).
    /// </summary>
    public ConcurrentBag<(string Id, long TimestampTicks, bool Enter)> Events { get; } = [];

    /// <summary>Max number of in-flight blocks observed at any instant.</summary>
    public int MaxObserved => Volatile.Read(ref _maxObserved);

    public void OnEnter(string id)
    {
        int now = Interlocked.Increment(ref _current);
        InterlockedMax(ref _maxObserved, now);
        Events.Add((id, Stopwatch.GetTimestamp(), true));
    }

    public void OnExit(string id)
    {
        _ = Interlocked.Decrement(ref _current);
        Events.Add((id, Stopwatch.GetTimestamp(), false));
    }

    /// <summary>
    /// Counts one occupancy window around a delay. The exit runs in a <c>finally</c>, so a
    /// cancelled delay still decrements and <see cref="MaxObserved"/> stays trustworthy.
    /// </summary>
    public async Task RunCountedDelayAsync(string id, int delayMs, CancellationToken cancellationToken)
    {
        OnEnter(id);
        try
        {
            await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            OnExit(id);
        }
    }

    /// <summary>
    /// Atomically raises <paramref name="target"/> to <paramref name="value"/> if larger.
    /// Lock-free CAS loop.
    /// </summary>
    internal static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}
