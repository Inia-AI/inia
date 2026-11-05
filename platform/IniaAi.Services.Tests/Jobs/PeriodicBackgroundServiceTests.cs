using IniaAi.Services.Jobs;

namespace IniaAi.Services.Tests.Jobs;

/// <summary>
/// The loop the periodic sweeps share: a service that should not start never runs; one that does
/// runs at once and on every tick, survives a failed run, and stops when the host stops it.
/// </summary>
public class PeriodicBackgroundServiceTests
{
    private static readonly TimeSpan WaitCap = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_service_that_should_not_start_never_runs()
    {
        var service = new CountingService(shouldStart: false, runsBeforeSignal: 1);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, service.Runs);
        Assert.False(service.Started);
    }

    [Fact]
    public async Task It_runs_at_once_and_on_every_tick_and_a_failed_run_does_not_end_the_loop()
    {
        var service = new CountingService(shouldStart: true, runsBeforeSignal: 3) { FailFirstRun = true };

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.Signal.Task.WaitAsync(WaitCap, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Started);
        Assert.True(service.Runs >= 3);
        Assert.Equal(1, service.Failures);
    }

    [Fact]
    public async Task Stopping_the_host_during_a_run_ends_the_loop_without_a_failure()
    {
        var service = new CountingService(shouldStart: true, runsBeforeSignal: 1) { HangUntilStopped = true };

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.Signal.Task.WaitAsync(WaitCap, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, service.Failures);
        Assert.True(service.Stopped);
    }

    [Fact]
    public async Task Stopping_the_host_between_runs_still_reports_the_stop()
    {
        var service = new CountingService(shouldStart: true, runsBeforeSignal: 1, interval: TimeSpan.FromHours(1));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.Signal.Task.WaitAsync(WaitCap, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, service.Runs);
        Assert.True(service.Stopped);
    }

    private sealed class CountingService(bool shouldStart, int runsBeforeSignal, TimeSpan? interval = null) : PeriodicBackgroundService
    {
        public TaskCompletionSource Signal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailFirstRun { get; init; }

        public bool HangUntilStopped { get; init; }

        public int Runs { get; private set; }

        public int Failures { get; private set; }

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        protected override TimeSpan Interval => interval ?? TimeSpan.FromMilliseconds(1);

        protected override bool ShouldStart()
        {
            return shouldStart;
        }

        protected override void OnStarting()
        {
            Started = true;
        }

        protected override void OnStopped()
        {
            Stopped = true;
        }

        protected override async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            Runs++;
            if (Runs >= runsBeforeSignal)
            {
                _ = Signal.TrySetResult();
            }

            if (HangUntilStopped)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (FailFirstRun && Runs == 1)
            {
                throw new InvalidOperationException("The first run fails.");
            }
        }

        protected override void OnRunFailed(Exception exception)
        {
            Failures++;
        }
    }
}
