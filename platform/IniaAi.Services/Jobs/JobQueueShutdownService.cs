using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Services.Models.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IniaAi.Services.Jobs;

/// <summary>
/// Drains the in-memory job queue when the host stops — the admin restart stops the
/// application the same way — and marks each drained job's execution row Cancelled so a
/// restart cannot strand executions in Queued. Running jobs that are not report runs are
/// cancelled too, and the stop waits for them up to the host's shutdown timeout, so their work
/// can settle its own records on the way out.
/// </summary>
public class JobQueueShutdownService : IHostedService
{
    private static readonly TimeSpan DELEGATED_JOB_POLL = TimeSpan.FromMilliseconds(100);

    private readonly IJobsService _jobsService;
    private readonly IWorkflowOutputService _workflowOutputService;
    private readonly ILogger<JobQueueShutdownService> _logger;
    private int _stopped;

    public JobQueueShutdownService(
        IJobsService jobsService,
        IWorkflowOutputService workflowOutputService,
        ILogger<JobQueueShutdownService> logger)
    {
        _jobsService = jobsService;
        _workflowOutputService = workflowOutputService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // A host can be stopped twice: under the test host factory, app.Run stops the host and disposes its
        // services, then the factory stops it again. The queue was drained by the first stop.
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        IReadOnlyList<Job> cancelledJobs = _jobsService.CancelAllQueuedJobs();

        await CancelDelegatedJobsAsync(cancellationToken);

        if (cancelledJobs.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Shutdown drained {Count} queued job(s); marking their executions Cancelled",
            cancelledJobs.Count);

        int markedCount = 0;

        foreach (Job cancelledJob in cancelledJobs)
        {
            // Document-processing jobs queue without an execution row — nothing to mark.
            if (cancelledJob.WorkflowExecutionId is null)
            {
                continue;
            }

            // The host stops waiting when its shutdown timeout expires; marks issued past that
            // point run against a process already tearing down, so stop and say what was cut off.
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Shutdown timed out mid-drain: {MarkedCount} execution(s) marked Cancelled; execution {WorkflowExecutionId} and any after it stay Queued",
                    markedCount,
                    cancelledJob.WorkflowExecutionId);

                return;
            }

            await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(
                cancelledJob.WorkflowExecutionId,
                cancelledJob.OrganizationId);

            markedCount++;
        }
    }

    /// <summary>
    /// A delegated job leaves the job list when its work has returned, so an empty list is the
    /// signal that every one has settled.
    /// </summary>
    private async Task CancelDelegatedJobsAsync(CancellationToken cancellationToken)
    {
        List<string> running = [.. (await _jobsService.GetJobsAsync())
            .Where(static job => !job.ListedAsReport && !job.IsFinished)
            .Select(static job => job.Id)];

        if (running.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Shutdown is cancelling {Count} running delegated job(s)", running.Count);

        foreach (string jobId in running)
        {
            _jobsService.CancelJob(jobId);
        }

        try
        {
            while (running.Any(jobId => _jobsService.GetJob(jobId) is not null))
            {
                await Task.Delay(DELEGATED_JOB_POLL, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Shutdown timed out while {Count} delegated job(s) were still settling",
                running.Count(jobId => _jobsService.GetJob(jobId) is not null));
        }
    }
}
