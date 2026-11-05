using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
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
}
