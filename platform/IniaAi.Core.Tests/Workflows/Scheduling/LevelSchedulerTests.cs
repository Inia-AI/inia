using System.Collections.Concurrent;
using IniaAi.Configuration;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Behavioural tests for <see cref="LevelScheduler"/>. Concurrency is proven by instrumentation
/// (atomic counters, event timestamps), never by wall-clock duration. Helper:
/// <c>SharedCounter</c> (atomic in-flight tracker with <c>InterlockedMax</c> CAS loop).
///
/// <c>IBlockConcurrencyCap</c> is mocked here by <c>Helpers.TestCap</c>. The scheduler's runBlock is
/// a plain delegate, so these tests need no <c>IBlockStrategy</c> / model types and stay fully
/// model-independent. The scheduler no longer takes permits: the cap binds in
/// <see cref="BlockUnitPermit"/>, so a test that means to prove the cap wraps its delegate in one,
/// and the cap's own contract is proven in <c>BlockUnitPermitTests</c>.
/// </summary>
public class LevelSchedulerTests
{
    private const int CAP_TIMEOUT_MS = 5_000;

    [Fact]
    public async Task RunAsync_WithPermitBracketedRunBlock_MaxObservedNeverExceedsCap()
    {
        const int CAP = 2;
        const int BLOCK_COUNT = 6;
        SharedCounter counter = new();
        TestCap cap = new(CAP);
        BlockUnitPermit permit = new(cap);
        SchedulingView view = new([new SchedulingLayer(BuildIds(BLOCK_COUNT))]);
        LevelScheduler scheduler = new(cap, NullLogger);

        await scheduler.RunAsync(
            view,
            (id, ct) => permit.RunUnitAsync(unitCt => counter.RunCountedDelayAsync(id, 50, unitCt), ct),
            TestContext.Current.CancellationToken);

        Assert.Equal(CAP, counter.MaxObserved);
    }

    [Fact]
    public async Task RunAsync_CapEqualsOne_ExitOrderMatchesSequentialOrder()
    {
        const int CAP = 1;
        SharedCounter counter = new();
        SchedulingView view = new([new SchedulingLayer(["A", "B", "C"])]);
        LevelScheduler scheduler = NewScheduler(CAP);

        await scheduler.RunAsync(view, (id, ct) => counter.RunCountedDelayAsync(id, 5, ct), TestContext.Current.CancellationToken);

        string[] exitOrder = [.. counter.Events
            .Where(e => !e.Enter)
            .OrderBy(e => e.TimestampTicks)
            .Select(e => e.Id)];
        Assert.Equal(new[] { "A", "B", "C" }, exitOrder);
    }

    [Fact]
    public async Task RunAsync_TwoLayers_AllLayerTwoEntriesAfterAllLayerOneExits()
    {
        SharedCounter counter = new();
        SchedulingView view = new(
        [
            new SchedulingLayer(["A1", "A2"]),
            new SchedulingLayer(["B1", "B2"]),
        ]);
        LevelScheduler scheduler = NewScheduler(maxParallelism: 4);

        await scheduler.RunAsync(view, (id, ct) => counter.RunCountedDelayAsync(id, 20, ct), TestContext.Current.CancellationToken);

        long latestLayer1Exit = counter.Events
            .Where(e => !e.Enter && (e.Id == "A1" || e.Id == "A2"))
            .Max(e => e.TimestampTicks);

        long earliestLayer2Enter = counter.Events
            .Where(e => e.Enter && (e.Id == "B1" || e.Id == "B2"))
            .Min(e => e.TimestampTicks);

        Assert.True(
            earliestLayer2Enter > latestLayer1Exit,
            $"earliest layer-2 enter ({earliestLayer2Enter}) must be strictly after latest layer-1 exit ({latestLayer1Exit}).");
    }

    [Fact]
    public async Task RunAsync_OneStubThrows_SiblingsStillCompleteAndSubsequentLayersProceed()
    {
        SharedCounter counter = new();
        ConcurrentBag<string> completed = [];
        SchedulingView view = new(
        [
            new SchedulingLayer(["A", "FAIL", "C"]),
            new SchedulingLayer(["D"]),
        ]);
        LevelScheduler scheduler = NewScheduler(maxParallelism: 3);

        await scheduler.RunAsync(view, async (id, ct) =>
        {
            counter.OnEnter(id);
            try
            {
                await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(10), ct).ConfigureAwait(false);
                if (id == "FAIL")
                {
                    throw new InvalidOperationException("stub failure");
                }

                completed.Add(id);
            }
            finally
            {
                counter.OnExit(id);
            }
        }, TestContext.Current.CancellationToken);

        Assert.Contains("A", completed);
        Assert.Contains("C", completed);
        Assert.Contains("D", completed);
        Assert.DoesNotContain("FAIL", completed);
    }

    [Fact]
    public async Task RunAsync_Cancellation_ThrowsOperationCanceledException()
    {
        SharedCounter counter = new();
        using CancellationTokenSource cts = new();
        SchedulingView view = new([new SchedulingLayer(["A", "B", "C"])]);
        LevelScheduler scheduler = NewScheduler(maxParallelism: 3);

        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await scheduler.RunAsync(view, async (id, ct) =>
            {
                counter.OnEnter(id);
                try
                {
                    // Waits on purpose: hangs until the run is cancelled, and the cap turns a missed cancellation into a failure.
                    await Task.Delay(5_000, ct).ConfigureAwait(false);
                }
                finally
                {
                    counter.OnExit(id);
                }
            }, cts.Token);
        });
    }

    [Fact]
    public async Task RunAsync_RunBlockThrows_DoesNotLeakCapPermits()
    {
        const int CAP = 2;
        TestCap cap = new(CAP);
        BlockUnitPermit permit = new(cap);
        LevelScheduler scheduler = new(cap, NullLogger);

        // Layer 1: all blocks throw.
        SchedulingView poisoned = new([new SchedulingLayer(["X", "Y", "Z"])]);
        await scheduler.RunAsync(poisoned,
            (_, ct) => permit.RunUnitAsync(_ => throw new InvalidOperationException("boom"), ct),
            TestContext.Current.CancellationToken);

        // Layer 2: no blocks throw; they must acquire promptly.
        SharedCounter counter = new();
        SchedulingView healthy = new([new SchedulingLayer(["P", "Q", "R", "S"])]);
        using CancellationTokenSource ackTimeout = new(TimeSpan.FromSeconds(CAP_TIMEOUT_MS / 1000));

        await scheduler.RunAsync(healthy,
            (id, ct) => permit.RunUnitAsync(unitCt => counter.RunCountedDelayAsync(id, 5, unitCt), ct),
            ackTimeout.Token);

        Assert.Equal(4, counter.Events.Count(e => !e.Enter));
        Assert.Equal(CAP, cap.MaxConcurrentlyHeld);
        Assert.Equal(0, cap.CurrentlyHeld);
        Assert.Equal(cap.Acquired, cap.Released);
    }

    [Fact]
    public async Task RunAsync_CalledTwiceOnSameSchedulerAndView_BothComplete()
    {
        SharedCounter first = new();
        SharedCounter second = new();
        SchedulingView view = new([new SchedulingLayer(["A", "B"])]);
        LevelScheduler scheduler = NewScheduler(maxParallelism: 2);

        await scheduler.RunAsync(view, (id, ct) => first.RunCountedDelayAsync(id, 5, ct), TestContext.Current.CancellationToken);
        await scheduler.RunAsync(view, (id, ct) => second.RunCountedDelayAsync(id, 5, ct), TestContext.Current.CancellationToken);

        Assert.Equal(2, first.Events.Count(e => !e.Enter));
        Assert.Equal(2, second.Events.Count(e => !e.Enter));
    }

    [Fact]
    public async Task RunAsync_UnequalDurations_AllComplete()
    {
        SharedCounter counter = new();
        SchedulingView view = new([new SchedulingLayer(["fast1", "slow", "fast2"])]);
        LevelScheduler scheduler = NewScheduler(maxParallelism: 2);

        await scheduler.RunAsync(view, async (id, ct) =>
        {
            counter.OnEnter(id);
            try
            {
                int delayMs = id == "slow" ? 200 : 10;
                await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(delayMs), ct).ConfigureAwait(false);
            }
            finally
            {
                counter.OnExit(id);
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(3, counter.Events.Count(e => !e.Enter));
    }

    [Fact]
    public async Task RunAsync_OneSiblingThrowsOCE_AnotherThrowsRegular_SurfacesCancellation()
    {
        LevelScheduler scheduler = NewScheduler(maxParallelism: 2);
        SchedulingView view = new([new SchedulingLayer(["OCE", "BOOM"])]);

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await scheduler.RunAsync(view, (id, ct) =>
            {
                if (id == "OCE")
                {
                    throw new OperationCanceledException("planned cancellation");
                }

                throw new InvalidOperationException("regular failure");
            }, TestContext.Current.CancellationToken);
        });

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
    }

    [Fact]
    public void BlockConcurrencyCap_MaxBlockParallelismZero_ThrowsArgumentOutOfRange()
    {
        PlatformSettings zero = new() { MaxBlockParallelism = 0 };

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BlockConcurrencyCap(Options.Create(zero)));
    }

    [Fact]
    public void BlockConcurrencyCap_MaxBlockParallelismNegative_ThrowsArgumentOutOfRange()
    {
        PlatformSettings negative = new() { MaxBlockParallelism = -3 };

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BlockConcurrencyCap(Options.Create(negative)));
    }

    [Fact]
    public async Task RunAsync_TwoSiblingsThrowDistinctExceptions_BothLoggedAsError()
    {
        CapturingLogger logger = new();
        TestCap cap = new(maxParallelism: 2);
        LevelScheduler scheduler = new(cap, logger);
        SchedulingView view = new([new SchedulingLayer(["X", "Y"])]);

        await scheduler.RunAsync(view, (id, ct) =>
        {
            throw id == "X"
                ? new InvalidOperationException("x failed")
                : new ArgumentException("y failed");
        }, TestContext.Current.CancellationToken);

        // Both faulted tasks should reach the logger. An exception that escapes the per-block
        // runner's own catch-log-continue is unexpected, so the scheduler logs it at Error (not
        // Debug) — these test delegates throw directly to simulate that escape.
        IReadOnlyList<CapturedLog> errorEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Error)
            .ToArray();

        Assert.Equal(2, errorEntries.Count);
        Assert.Contains(errorEntries, e => ContainsException(e.Exception, typeof(InvalidOperationException)));
        Assert.Contains(errorEntries, e => ContainsException(e.Exception, typeof(ArgumentException)));
    }

    [Fact]
    public async Task RunAsync_ParallelLayer_ReturnsCountOfEscapedNonCancellationExceptions()
    {
        LevelScheduler scheduler = NewScheduler(maxParallelism: 3);
        SchedulingView view = new([new SchedulingLayer(["X", "OK", "Y"])]);

        int escaped = await scheduler.RunAsync(view, (id, ct) => id == "OK"
            ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException($"{id} failed")), TestContext.Current.CancellationToken);

        Assert.Equal(2, escaped);
    }

    [Fact]
    public async Task RunAsync_SequentialDispatch_ReturnsCountOfEscapedNonCancellationExceptions()
    {
        LevelScheduler scheduler = NewScheduler(maxParallelism: 1);
        SchedulingView view = new([new SchedulingLayer(["A", "FAIL1", "B", "FAIL2"])]);

        int escaped = await scheduler.RunAsync(view, (id, ct) => id.StartsWith("FAIL")
            ? throw new InvalidOperationException($"{id} failed")
            : Task.CompletedTask, TestContext.Current.CancellationToken);

        Assert.Equal(2, escaped);
    }

    [Fact]
    public async Task RunAsync_NoEscapes_ReturnsZero()
    {
        LevelScheduler scheduler = NewScheduler(maxParallelism: 2);
        SchedulingView view = new([new SchedulingLayer(["A", "B"])]);

        int escaped = await scheduler.RunAsync(view, (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken);

        Assert.Equal(0, escaped);
    }

    private static LevelScheduler NewScheduler(int maxParallelism)
    {
        return new LevelScheduler(new TestCap(maxParallelism), NullLogger);
    }

    private static ILogger<LevelScheduler> NullLogger { get; } =
        Microsoft.Extensions.Logging.Abstractions.NullLogger<LevelScheduler>.Instance;

    private static List<string> BuildIds(int count)
    {
        List<string> result = new(count);
        for (int i = 0; i < count; i++)
        {
            result.Add($"B{i:00}");
        }

        return result;
    }

    private static bool ContainsException(Exception? candidate, Type expected)
    {
        Exception? current = candidate;
        while (current is not null)
        {
            if (expected.IsInstanceOfType(current))
            {
                return true;
            }

            if (current is AggregateException agg)
            {
                foreach (Exception inner in agg.InnerExceptions)
                {
                    if (ContainsException(inner, expected))
                    {
                        return true;
                    }
                }

                return false;
            }

            current = current.InnerException;
        }

        return false;
    }

    private sealed record CapturedLog(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger<LevelScheduler>
    {
        private readonly ConcurrentBag<CapturedLog> _entries = [];

        public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string message = formatter is null ? string.Empty : formatter(state, exception);
            _entries.Add(new CapturedLog(logLevel, message, exception));
        }
    }
}
