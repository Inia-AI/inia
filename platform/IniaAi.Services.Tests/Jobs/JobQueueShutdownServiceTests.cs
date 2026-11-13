using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IniaAi.Services.Tests.Jobs;

public class JobQueueShutdownServiceTests
{
    private const string ORGANIZATION_ID = "org-1";
    private const string EXECUTION_ID = "execution-1";

    private readonly IJobsService _jobsService = Substitute.For<IJobsService>();
    private readonly IWorkflowOutputService _workflowOutputService = Substitute.For<IWorkflowOutputService>();
    private readonly JobQueueShutdownService _service;

    public JobQueueShutdownServiceTests()
    {
        _service = new JobQueueShutdownService(
            _jobsService,
            _workflowOutputService,
            NullLogger<JobQueueShutdownService>.Instance);
        _ = _jobsService.GetJobsAsync().Returns(Task.FromResult<IEnumerable<Job>>([]));
    }

    [Fact]
    public async Task StopAsync_MarksEachDrainedExecutionCancelled_AndSkipsJobsWithoutOneAsync()
    {
        var jobWithExecution = new Job(ORGANIZATION_ID, null)
        {
            WorkflowExecutionId = EXECUTION_ID,
        };
        var jobWithoutExecution = new Job(ORGANIZATION_ID, null);
        _ = _jobsService.CancelAllQueuedJobs().Returns([jobWithExecution, jobWithoutExecution]);

        await _service.StopAsync(TestContext.Current.CancellationToken);

        await _workflowOutputService.Received(1).MarkExecutionAsCancelledIfActiveAsync(EXECUTION_ID, ORGANIZATION_ID);
        _ = Assert.Single(_workflowOutputService.ReceivedCalls());
    }

    [Fact]
    public async Task StopAsync_WhenTheShutdownTimeoutAlreadyExpired_StopsMarkingAsync()
    {
        var firstJob = new Job(ORGANIZATION_ID, null)
        {
            WorkflowExecutionId = EXECUTION_ID,
        };
        var secondJob = new Job(ORGANIZATION_ID, null)
        {
            WorkflowExecutionId = "execution-2",
        };
        _ = _jobsService.CancelAllQueuedJobs().Returns([firstJob, secondJob]);

        await _service.StopAsync(new CancellationToken(canceled: true));

        Assert.Empty(_workflowOutputService.ReceivedCalls());
    }

    [Fact]
    public async Task StopAsync_WithAnEmptyQueue_MarksNothingAsync()
    {
        _ = _jobsService.CancelAllQueuedJobs().Returns([]);

        await _service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_workflowOutputService.ReceivedCalls());
    }

    [Fact]
    public async Task StopAsync_ASecondTime_AfterTheFirstStopDisposedTheJobs_HasNothingLeftToDoAsync()
    {
        var settings = new PlatformSettings();
        settings.IniaAi.MaxConcurrentJobs = 1;
        await using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var jobsService = new JobsService(provider, NullLogger<JobsService>.Instance, Options.Create(settings));
        var service = new JobQueueShutdownService(jobsService, _workflowOutputService, NullLogger<JobQueueShutdownService>.Instance);
        await service.StopAsync(TestContext.Current.CancellationToken);
        jobsService.Dispose();

        Exception? secondStop = await Record.ExceptionAsync(() => service.StopAsync(TestContext.Current.CancellationToken));

        Assert.Null(secondStop);
    }

    [Fact]
    public async Task StopAsync_ASecondTime_WhileTheFirstStopDrains_WaitsForTheDrainAsync()
    {
        var queuedJob = new Job(ORGANIZATION_ID, null)
        {
            WorkflowExecutionId = EXECUTION_ID,
        };
        _ = _jobsService.CancelAllQueuedJobs().Returns([queuedJob]);
        var marking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(EXECUTION_ID, ORGANIZATION_ID).Returns(marking.Task);

        Task firstStop = _service.StopAsync(TestContext.Current.CancellationToken);
        Task secondStop = _service.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(secondStop.IsCompleted);
        marking.SetResult(true);
        await Task.WhenAll(firstStop, secondStop);
        _ = _jobsService.Received(1).CancelAllQueuedJobs();
    }
}
