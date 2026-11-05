using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows.Scheduling;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// The cap's binding site. Every case here turns red if the acquire or the release is
/// deleted from <see cref="BlockUnitPermit"/>, which is what the assertions relocated out of
/// <c>LevelSchedulerTests</c> exist to guarantee.
///
/// The cap cases carry an explicit timeout because a deleted release starves them rather than
/// failing them. xUnit honours <c>Timeout</c> only under the Conservative parallel algorithm; this
/// repository ships no <c>xunit.runner.json</c>, so the Conservative default applies.
/// </summary>
public class BlockUnitPermitTests
{
    private const int CAP_TIMEOUT_MS = 10_000;

    [Theory(Timeout = CAP_TIMEOUT_MS)]
    [InlineData(1, 4)]
    [InlineData(2, 6)]
    public async Task RunUnitAsync_MoreUnitsThanCap_HoldsAtMostCapAndReleasesEveryPermit(
        int maxParallelism,
        int unitCount)
    {
        TestCap cap = new(maxParallelism);
        BlockUnitPermit permit = new(cap);
        SharedCounter counter = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Task[] units = [.. Enumerable.Range(0, unitCount)
            .Select(i => permit.RunUnitAsync(unitCt => counter.RunCountedDelayAsync($"U{i}", 20, unitCt), ct))];
        await Task.WhenAll(units);

        Assert.Equal(maxParallelism, counter.MaxObserved);
        Assert.Equal(maxParallelism, cap.MaxConcurrentlyHeld);
        Assert.Equal(unitCount, cap.Acquired);
        Assert.Equal(unitCount, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
    }

    [Fact(Timeout = CAP_TIMEOUT_MS)]
    public async Task RunUnitAsync_UnitThrows_ReleasesThePermit()
    {
        TestCap cap = new(2);
        BlockUnitPermit permit = new(cap);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => permit.RunUnitAsync(
                _ => throw new InvalidOperationException("boom"),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, cap.Acquired);
        Assert.Equal(1, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
        Assert.Equal(1, cap.MaxConcurrentlyHeld);
    }

    [Fact(Timeout = CAP_TIMEOUT_MS)]
    public async Task RunUnitAsync_CancellationDuringTheUnit_ReleasesThePermit()
    {
        TestCap cap = new(2);
        BlockUnitPermit permit = new(cap);
        using CancellationTokenSource cts = new();

        // Cancelled from inside the unit, so the cancel always lands after the permit is held; a
        // timer can fire before the acquire on a loaded machine and test the wait instead.
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => permit.RunUnitAsync(
                async unitCt =>
                {
                    await cts.CancelAsync();
                    await Task.Delay(Timeout.Infinite, unitCt);
                },
                cts.Token));

        Assert.Equal(1, cap.Acquired);
        Assert.Equal(1, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
        Assert.Equal(1, cap.MaxConcurrentlyHeld);
    }

    [Fact(Timeout = CAP_TIMEOUT_MS)]
    public async Task RunUnitAsync_CancelledWhileWaitingToAcquire_ReleasesNothing()
    {
        TestCap cap = new(1);
        BlockUnitPermit permit = new(cap);
        TaskCompletionSource holderRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource waiterCts = new();

        // The uncontended acquire completes synchronously, so the holder owns the only permit by
        // the time this call returns and the waiter below is genuinely queued on the semaphore.
        Task holder = permit.RunUnitAsync(_ => holderRelease.Task, TestContext.Current.CancellationToken);
        Assert.Equal(1, cap.Acquired);

        Task waiter = permit.RunUnitAsync(_ => Task.CompletedTask, waiterCts.Token);
        await waiterCts.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.Equal(1, cap.Acquired);
        Assert.Equal(0, cap.Released);
        Assert.Equal(1, cap.CurrentlyHeld);

        holderRelease.SetResult();
        await holder;

        Assert.Equal(1, cap.Acquired);
        Assert.Equal(1, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
        Assert.Equal(1, cap.MaxConcurrentlyHeld);
    }

    [Fact(Timeout = CAP_TIMEOUT_MS)]
    public async Task RunUnitAsync_StartedFromInsideAUnit_ThrowsInsteadOfWaitingForever()
    {
        TestCap cap = new(1);
        BlockUnitPermit permit = new(cap);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => permit.RunUnitAsync(
                outerCt => permit.RunUnitAsync(_ => Task.CompletedTask, outerCt),
                TestContext.Current.CancellationToken));

        Assert.Contains("MaxBlockParallelism", ex.Message);
        Assert.Equal(1, cap.Acquired);
        Assert.Equal(1, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
    }

    [Fact(Timeout = CAP_TIMEOUT_MS)]
    public async Task RunNestedDispatchAsync_StartedFromInsideAUnit_Throws()
    {
        TestCap cap = new(1);
        BlockUnitPermit permit = new(cap);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => permit.RunUnitAsync(
                outerCt => permit.RunNestedDispatchAsync(_ => Task.CompletedTask, outerCt),
                TestContext.Current.CancellationToken));

        Assert.Equal(cap.Acquired, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
    }

    /// <summary>
    /// D26's serialized-legs property with no engine in the way, and the regression guard for the
    /// re-entrancy flag's containment: the legs are started one after another from a single frame,
    /// so a flag that leaked out of leg 0's synchronous prefix would make leg 1 throw.
    /// </summary>
    [Fact(Timeout = CAP_TIMEOUT_MS)]
    public async Task RunNestedDispatchAsync_AtCapOne_TakesNoPermitAndLegsSerialize()
    {
        const int LEG_COUNT = 3;
        TestCap cap = new(1);
        BlockUnitPermit permit = new(cap);
        SharedCounter counter = new();

        await permit.RunNestedDispatchAsync(async dispatchCt =>
        {
            Task[] legs = [.. Enumerable.Range(0, LEG_COUNT)
                .Select(i => permit.RunUnitAsync(legCt => counter.RunCountedDelayAsync($"L{i}", 20, legCt), dispatchCt))];
            await Task.WhenAll(legs);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, counter.MaxObserved);
        Assert.Equal(LEG_COUNT, cap.Acquired);
        Assert.Equal(LEG_COUNT, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
    }

    [Fact(Timeout = CAP_TIMEOUT_MS)]
    public async Task Unbounded_RunsTheUnit()
    {
        bool ran = false;

        await BlockUnitPermit.Unbounded.RunUnitAsync(
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        Assert.True(ran);
    }

    /// <summary>
    /// The off-engine permit enforces the same nesting invariant as the real one, so a strategy
    /// unit-tested against a hand-built context cannot pass locally and throw in a real run.
    /// </summary>
    [Fact(Timeout = CAP_TIMEOUT_MS)]
    public async Task Unbounded_UnitStartedFromInsideAUnit_ThrowsLikeTheRealPermit()
    {
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BlockUnitPermit.Unbounded.RunUnitAsync(
                outerCt => BlockUnitPermit.Unbounded.RunUnitAsync(_ => Task.CompletedTask, outerCt),
                TestContext.Current.CancellationToken));
    }
}
