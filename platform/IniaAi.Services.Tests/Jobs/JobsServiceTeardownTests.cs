using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Core.Documents;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.Services.Tests.Jobs;

/// <summary>
/// A test's teardown stops every job the test started before it disposes what the jobs use, also
/// the job a cancelled job starts as it ends, the way a conversation turn starts the next waiting
/// turn.
/// </summary>
public sealed class JobsServiceTeardownTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-teardown";
    private const string USER_ID = "user-teardown";

    private readonly ServiceProvider _provider;
    private readonly IJobsService _jobs;

    public JobsServiceTeardownTests()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddTransient<IJobExecutor>(provider => new JobExecutor(
            NullLogger<JobExecutor>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            documentProcessing: null!,
            workflowOutputService: null!,
            emailIntakeJobStage: null!,
            runInputStage: null!,
            runContentCheck: new PassRunContentCheck()));
        _provider = services.BuildServiceProvider();
        _jobs = new JobsService(_provider, NullLogger<JobsService>.Instance, Options.Create(new PlatformSettings()));
    }

    public void Dispose()
    {
        _jobs.Dispose();
        _provider.Dispose();
    }

    /// <summary>
    /// The job the first one starts as it ends can be cancelled before its thread picks it up, and
    /// then never runs its work; either way, none of its work is left running.
    /// </summary>
    [Fact]
    public async Task StopEveryJobAsync_CancelsTheRunningJobs_AndTheJobsTheyStartAsTheyEnd_ThenReturnsWithNothingRunning()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool nextStarted = false;
        bool nextSettled = false;
        DelegatedJob next = RunsUntilCancelled("next turn", onStarted: () => nextStarted = true, onSettled: () => nextSettled = true);
        DelegatedJob first = RunsUntilCancelled("first turn", onStarted: firstStarted.SetResult, onSettled: () => _jobs.Execute(next, shouldUseQueue: false));
        _jobs.Execute(first, shouldUseQueue: false);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await _jobs.StopEveryJobAsync();

        Assert.True(next.IsFinished);
        Assert.Equal(nextStarted, nextSettled);
        Assert.Empty(await _jobs.GetJobsAsync());
    }

    /// <summary>
    /// A workflow job stays listed after it ends; here the queue, closed for shutdown, cancels it on
    /// arrival.
    /// </summary>
    [Fact]
    public async Task StopEveryJobAsync_CountsAFinishedWorkflowJobAsStopped_ThoughItStaysListed()
    {
        _ = _jobs.CancelAllQueuedJobs();
        Job report = _jobs.CreateOrganizationJob(ORGANIZATION_ID, workflowTypeId: null);
        _jobs.Execute(report, shouldUseQueue: true);

        await _jobs.StopEveryJobAsync(TimeSpan.FromSeconds(5));

        Assert.True(report.IsFinished);
        Assert.Contains(report, await _jobs.GetJobsAsync());
    }

    [Fact]
    public async Task StopEveryJobAsync_WithNoJob_ReturnsAtOnce()
    {
        await _jobs.StopEveryJobAsync();

        Assert.Empty(await _jobs.GetJobsAsync());
    }

    private static DelegatedJob RunsUntilCancelled(string description, Action onStarted, Action onSettled)
    {
        return new DelegatedJob(ORGANIZATION_ID, USER_ID, description, async (_, token) =>
        {
            onStarted();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                onSettled();
            }
        });
    }
}
