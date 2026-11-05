using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Tests.Jobs;

/// <summary>
/// Shutdown with a real job service and job executor: a running delegated job (an agent
/// conversation turn) is cancelled, and the stop waits for its work to settle.
/// </summary>
public sealed class JobQueueShutdownDelegatedJobTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-shutdown";
    private const string USER_ID = "user-shutdown";

    private readonly ServiceProvider _provider;
    private readonly IJobsService _jobs;

    public JobQueueShutdownDelegatedJobTests()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddTransient<IJobExecutor>(provider => new JobExecutor(
            NullLogger<JobExecutor>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            documentProcessing: null!,
            workflowOutputService: null!,
            emailIntakeJobStage: null!,
            runInputStage: null!));
        _provider = services.BuildServiceProvider();
        _jobs = new JobsService(_provider, NullLogger<JobsService>.Instance, Options.Create(new PlatformSettings()));
    }

    public void Dispose()
    {
        _jobs.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public async Task StopAsync_CancelsARunningDelegatedJob_AndWaitsForItToSettle()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool settled = false;
        var job = new DelegatedJob(ORGANIZATION_ID, USER_ID, "turn", async (_, token) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(200));
                settled = true;
            }
        });
        _jobs.Execute(job, shouldUseQueue: false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var shutdown = new JobQueueShutdownService(_jobs, workflowOutputService: null!, NullLogger<JobQueueShutdownService>.Instance);
        await shutdown.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(settled);
        Assert.Null(_jobs.GetJob(job.Id));
    }
}
