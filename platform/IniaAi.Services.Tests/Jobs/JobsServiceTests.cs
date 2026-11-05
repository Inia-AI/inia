using System.Collections.Concurrent;
using IniaAi.Configuration;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Services.Models.Jobs.Enums;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Tests.Jobs;

public class JobsServiceTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-1";
    private const int QUEUE_SETTLE_DELAY_MS = 300;
    private const int WORKER_TIMEOUT_SECONDS = 30;

    private ServiceProvider? _provider;
    private JobsService? _service;

    [Fact]
    public async Task CancelJob_AfterAParallelJobFinished_IsANoOpAsync()
    {
        JobsService service = CreateService(CompleteImmediately);

        var job = new Job(ORGANIZATION_ID, null);
        service.Execute(job, shouldUseQueue: false);
        await Eventually.UntilAsync(() => job.State is JobState.Done, "the parallel job to finish", cancellationToken: TestContext.Current.CancellationToken);

        service.CancelJob(job.Id);

        Assert.Equal(JobState.Done, job.State);
    }

    [Fact]
    public void ParallelExecution_SignalsCompletionAndDisposesTheCancellationSource()
    {
        Action<Job>? capturedOnFinish = null;
        CancellationToken capturedToken = default;
        JobsService service = CreateService((job, onFinish, token) =>
        {
            capturedOnFinish = onFinish;
            capturedToken = token;
        });

        var job = new Job(ORGANIZATION_ID, null);
        service.Execute(job, shouldUseQueue: false);

        Assert.NotNull(capturedOnFinish);

        job.SetState(JobState.Done);
        capturedOnFinish!.Invoke(job);

        _ = Assert.Throws<ObjectDisposedException>(() => capturedToken.WaitHandle);
    }

    [Fact]
    public async Task CancelJob_WhileTheJobIsStillQueued_CancelsIt_AndThePumpNeverRunsItAsync()
    {
        using var firstJobGate = new ManualResetEventSlim(false);
        var executedJobIds = new ConcurrentQueue<string>();
        JobsService service = CreateGatedSingleSlotService(firstJobGate, executedJobIds);

        var runningJob = new Job(ORGANIZATION_ID, null);
        var queuedJob = new Job(ORGANIZATION_ID, null);

        service.Execute(runningJob, shouldUseQueue: true);
        await Eventually.UntilAsync(() => executedJobIds.Contains(runningJob.Id), "the pump to pick up the first job", cancellationToken: TestContext.Current.CancellationToken);
        service.Execute(queuedJob, shouldUseQueue: true);

        service.CancelJob(queuedJob.Id);

        Assert.Equal(JobState.Cancelled, queuedJob.State);

        await ReleaseGateAndAssertNeverRunAsync(firstJobGate, runningJob, executedJobIds, queuedJob.Id);
    }

    [Fact]
    public async Task ConcurrentParallelExecutesAndCancels_DoNotCorruptTheServiceAsync()
    {
        JobsService service = CreateService(CompleteImmediately);

        const int WORKERS = 8;
        const int JOBS_PER_WORKER = 100;
        var failures = new ConcurrentQueue<Exception>();
        var allJobs = new ConcurrentQueue<Job>();

        Task[] workers = [.. Enumerable.Range(0, WORKERS).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < JOBS_PER_WORKER; i++)
            {
                try
                {
                    var job = new Job(ORGANIZATION_ID, null);
                    allJobs.Enqueue(job);
                    service.Execute(job, shouldUseQueue: false);
                    service.CancelJob(job.Id);
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }
            }
        }))];

        await Task.WhenAll(workers).WaitAsync(
            TimeSpan.FromSeconds(WORKER_TIMEOUT_SECONDS),
            TestContext.Current.CancellationToken);

        Assert.Empty(failures);
        Assert.All(allJobs, job => Assert.True(job.IsFinished));
    }

    [Fact]
    public async Task ConcurrentQueuedExecutes_EveryJobRunsToCompletionAsync()
    {
        // The pump reaps finished workers once per 100 ms tick, so throughput is
        // MaxConcurrentJobs per tick. Under a full-solution test run the ticks stretch, so this
        // wait asserts completion with the worker timeout, not the short poll timeout.
        JobsService service = CreateService(CompleteImmediately, maxConcurrentJobs: 8);

        const int WORKERS = 6;
        const int JOBS_PER_WORKER = 20;
        var allJobs = new ConcurrentQueue<Job>();

        Task[] workers = [.. Enumerable.Range(0, WORKERS).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < JOBS_PER_WORKER; i++)
            {
                var job = new Job(ORGANIZATION_ID, null);
                allJobs.Enqueue(job);
                service.Execute(job, shouldUseQueue: true);
            }
        }))];

        await Task.WhenAll(workers).WaitAsync(
            TimeSpan.FromSeconds(WORKER_TIMEOUT_SECONDS),
            TestContext.Current.CancellationToken);
        await Eventually.UntilAsync(
            () => allJobs.All(job => job.State is JobState.Done),
            "every queued job to run to completion",
            TimeSpan.FromSeconds(WORKER_TIMEOUT_SECONDS),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CancelAllQueuedJobs_CancelsAndReturnsEveryWaitingJob_AndThePumpNeverRunsThemAsync()
    {
        using var firstJobGate = new ManualResetEventSlim(false);
        var executedJobIds = new ConcurrentQueue<string>();
        JobsService service = CreateGatedSingleSlotService(firstJobGate, executedJobIds);

        var runningJob = new Job(ORGANIZATION_ID, null);
        var queuedJob1 = new Job(ORGANIZATION_ID, null);
        var queuedJob2 = new Job(ORGANIZATION_ID, null);

        service.Execute(runningJob, shouldUseQueue: true);
        await Eventually.UntilAsync(() => executedJobIds.Contains(runningJob.Id), "the pump to pick up the first job", cancellationToken: TestContext.Current.CancellationToken);
        service.Execute(queuedJob1, shouldUseQueue: true);
        service.Execute(queuedJob2, shouldUseQueue: true);

        IReadOnlyList<Job> drainedJobs = service.CancelAllQueuedJobs();

        Assert.Equal([queuedJob1.Id, queuedJob2.Id], drainedJobs.Select(job => job.Id));
        Assert.All(drainedJobs, job => Assert.Equal(JobState.Cancelled, job.State));

        await ReleaseGateAndAssertNeverRunAsync(
            firstJobGate,
            runningJob,
            executedJobIds,
            queuedJob1.Id,
            queuedJob2.Id);
    }

    [Fact]
    public async Task ExecuteQueued_AfterTheShutdownDrain_CancelsTheJobInsteadOfRunningItAsync()
    {
        var executedJobIds = new ConcurrentQueue<string>();
        JobsService service = CreateService((job, onFinish, token) =>
        {
            executedJobIds.Enqueue(job.Id);
            job.SetState(JobState.Done);
            onFinish(job);
        });

        _ = service.CancelAllQueuedJobs();

        var lateJob = new Job(ORGANIZATION_ID, null);
        service.Execute(lateJob, shouldUseQueue: true);

        Assert.Equal(JobState.Cancelled, lateJob.State);

        // Waits on purpose: proves a cancelled job never starts; the queue has no idle signal.
        await Task.Delay(QUEUE_SETTLE_DELAY_MS, TestContext.Current.CancellationToken);
        Assert.Empty(executedJobIds);
    }

    [Fact]
    public async Task CancelJob_Twice_OnARunningParallelJob_CancelsOnceWithoutThrowingAsync()
    {
        JobsService service = CreateService((job, onFinish, token) => _ = token.Register(
            () => _ = Task.Run(() => onFinish(job))));

        var job = new Job(ORGANIZATION_ID, null);
        service.Execute(job, shouldUseQueue: false);

        service.CancelJob(job.Id);
        service.CancelJob(job.Id);

        Assert.Equal(JobState.Cancelled, job.State);
        await Eventually.UntilAsync(() => job.IsFinished, "the cancelled job to stay finished", cancellationToken: TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _service?.Dispose();
        _provider?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Private

    private sealed class ScriptedJobExecutor : IJobExecutor
    {
        private readonly Action<Job, Action<Job>, CancellationToken> _onExecute;

        public ScriptedJobExecutor(Action<Job, Action<Job>, CancellationToken> onExecute)
        {
            _onExecute = onExecute;
        }

        public void Execute(Job job, Action<Job> onFinish, CancellationToken cancellationToken)
        {
            _onExecute(job, onFinish, cancellationToken);
        }
    }

    private static void CompleteImmediately(Job job, Action<Job> onFinish, CancellationToken token)
    {
        job.SetState(JobState.Done);
        onFinish(job);
    }

    private JobsService CreateService(
        Action<Job, Action<Job>, CancellationToken> executorBehavior,
        int maxConcurrentJobs = 2)
    {
        var services = new ServiceCollection();
        _ = services.AddTransient<IJobExecutor>(_ => new ScriptedJobExecutor(executorBehavior));
        _provider = services.BuildServiceProvider();

        var settings = new PlatformSettings();
        settings.IniaAi.MaxConcurrentJobs = maxConcurrentJobs;

        _service = new JobsService(_provider, NullLogger<JobsService>.Instance, Options.Create(settings));

        return _service;
    }

    private JobsService CreateGatedSingleSlotService(
        ManualResetEventSlim gate,
        ConcurrentQueue<string> executedJobIds)
    {
        return CreateService(
            (job, onFinish, token) =>
            {
                executedJobIds.Enqueue(job.Id);
                _ = Task.Run(() =>
                {
                    gate.Wait();
                    job.SetState(JobState.Done);
                    onFinish(job);
                });
            },
            maxConcurrentJobs: 1);
    }

    private static async Task ReleaseGateAndAssertNeverRunAsync(
        ManualResetEventSlim gate,
        Job runningJob,
        ConcurrentQueue<string> executedJobIds,
        params string[] cancelledJobIds)
    {
        gate.Set();
        await Eventually.UntilAsync(() => runningJob.State is JobState.Done, "the running job to finish", cancellationToken: TestContext.Current.CancellationToken);
        // Waits on purpose: proves a cancelled job never starts; the queue has no idle signal.
        await Task.Delay(QUEUE_SETTLE_DELAY_MS, TestContext.Current.CancellationToken);

        foreach (string cancelledJobId in cancelledJobIds)
        {
            Assert.DoesNotContain(cancelledJobId, executedJobIds);
        }
    }

    #endregion
}
