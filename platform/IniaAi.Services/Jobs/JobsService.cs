using System.Collections.Concurrent;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Services.Models.BackgroundJobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Services.Models.Jobs.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Jobs;

public class JobsService : IJobsService, IDisposable
{
    private readonly Dictionary<string, BackgroundJob> _jobs = [];
    private readonly Queue<Job> _jobQueue = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly SemaphoreSlim _jobsLock = new(1, 1);
    private readonly SemaphoreSlim _concurrencyLimitSemaphore;
    private bool _isProcessingQueue;
    private bool _isQueueClosedForShutdown;
    private readonly ILogger<JobsService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
    private readonly PlatformSettings _platformSettings;
    private readonly CancellationTokenSource _queueProcessorCancellationTokenSource = new();

    private const int QueueCheckDelayMs = 100;

    public event EventHandler<JobStateChangeEventArgs>? JobStateChanged;
    public event EventHandler<JobStateChangeEventArgs>? JobStateChanging;
    public event EventHandler? JobCollectionChanged;

    public JobsService(
        IServiceProvider serviceProvider,
        ILogger<JobsService> logger,
        IOptions<PlatformSettings> platformSettings)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _platformSettings = platformSettings.Value;
        _concurrencyLimitSemaphore = new SemaphoreSlim(
            _platformSettings.IniaAi.MaxConcurrentJobs,
            _platformSettings.IniaAi.MaxConcurrentJobs);
    }

    public Job CreateOrganizationJob(
        string organizationId,
        string? workflowTypeId)
    {
        var job = new Job(organizationId, workflowTypeId);

        return job;
    }

    public Job CreateUserJob(User user, string? workflowTypeId)
    {
        Job job = CreateOrganizationJob(user.OrganizationId, workflowTypeId);
        job.SetUserId(user.Id);

        return job;
    }

    public Job CreateUserJob(string organizationId, string userId, string? workflowTypeId)
    {
        Job job = CreateUserJob(new User()
        {
            Id = userId,
            OrganizationId = organizationId,
            FirstName = "System",
            LastName = "User"
        }, workflowTypeId);

        return job;
    }

    public void Execute(Job job, bool shouldUseQueue)
    {
        _logger.LogTrace("Execute job: {job}", job);

        job.StateChanged += OnJobStateChanged;
        job.StateChanging += OnJobStateChanging;
        job.DeleteRequested += OnJobDeleteRequested;

        _jobsLock.Wait();
        try
        {
            _jobs[job.Id] = job;
        }
        finally
        {
            _ = _jobsLock.Release();
        }

        if (shouldUseQueue)
        {
            _logger.LogInformation("Queueing job {JobId} for sequential execution", job.Id);
            EnqueueJob(job);
        }
        else
        {
            _logger.LogInformation("Executing job {JobId} in parallel mode", job.Id);
            ExecuteJob(job);
        }

        JobCollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IEnumerable<Job>> GetJobsAsync()
    {
        await _jobsLock.WaitAsync();
        try
        {
            return [.. _jobs.Values.Cast<Job>()];
        }
        finally
        {
            _ = _jobsLock.Release();
        }
    }

    public BackgroundJob? GetJob(string jobId)
    {
        _jobsLock.Wait();
        try
        {
            return _jobs.TryGetValue(jobId, out BackgroundJob? job) ? job : null;
        }
        finally
        {
            _ = _jobsLock.Release();
        }
    }

    public void CancelJob(string jobId)
    {
        bool jobWasCancelled = false;

        _jobsLock.Wait();
        try
        {
            if (_jobs.TryGetValue(jobId, out BackgroundJob? job) && !job.IsFinished)
            {
                job.Cancel();
                jobWasCancelled = true;
            }
        }
        finally
        {
            _ = _jobsLock.Release();
        }

        if (_cancellationTokens.TryGetValue(jobId, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
                jobWasCancelled = true;
            }
            catch (ObjectDisposedException)
            {
                // The execution path that owns the source finished and disposed it after the
                // lookup — the job is already done, so there is nothing left to cancel.
            }
        }

        if (jobWasCancelled)
        {
            _logger.LogInformation("Cancelled job {JobId}", jobId);
        }
    }

    public void DeleteJob(string jobId)
    {
        _jobsLock.Wait();
        try
        {
            if (!_jobs.TryGetValue(jobId, out BackgroundJob? job))
            {
                throw new NullReferenceException($"Job not found: {jobId}");
            }

            job.Delete();
        }
        finally
        {
            _ = _jobsLock.Release();
        }
    }

    public IReadOnlyList<Job> CancelAllQueuedJobs()
    {
        _logger.LogInformation("Cancelling all queued jobs");

        List<Job> drainedJobs;

        _queueLock.Wait();
        try
        {
            _isQueueClosedForShutdown = true;
            drainedJobs = [.. _jobQueue];
            _jobQueue.Clear();
        }
        finally
        {
            _ = _queueLock.Release();
        }

        foreach (Job job in drainedJobs)
        {
            if (!job.IsFinished)
            {
                job.Cancel();
                _logger.LogInformation("Cancelled queued job {JobId}", job.Id);
            }
        }

        return drainedJobs;
    }

    public IEnumerable<BackgroundJob> GetUserJobs(string userId)
    {
        _jobsLock.Wait();
        try
        {
            return _jobs.Values.Where(j => j.UserId == userId).ToList();
        }
        finally
        {
            _ = _jobsLock.Release();
        }
    }

    public IEnumerable<BackgroundJob> GetOrganizationJobs(string organizationId)
    {
        _jobsLock.Wait();
        try
        {
            return _jobs.Values.Where(j => j.OrganizationId == organizationId).ToList();
        }
        finally
        {
            _ = _jobsLock.Release();
        }
    }

    public void Dispose()
    {
        _queueProcessorCancellationTokenSource.Cancel();
        _queueProcessorCancellationTokenSource.Dispose();
        _queueLock.Dispose();
        _jobsLock.Dispose();
        _concurrencyLimitSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Private

    private void EnqueueJob(Job job)
    {
        job.SetState(JobState.Queued);

        bool shouldStartPump = false;
        bool queueIsClosed = false;

        _queueLock.Wait();

        try
        {
            if (_isQueueClosedForShutdown)
            {
                queueIsClosed = true;
            }
            else
            {
                _jobQueue.Enqueue(job);
                _logger.LogInformation("Job {JobId} queued for execution", job.Id);

                if (!_isProcessingQueue)
                {
                    _isProcessingQueue = true;
                    shouldStartPump = true;
                }
            }
        }
        finally
        {
            _ = _queueLock.Release();
        }

        if (queueIsClosed)
        {
            job.Cancel();
            _logger.LogWarning(
                "Job {JobId} arrived after the shutdown drain; cancelled without running (execution: {WorkflowExecutionId})",
                job.Id,
                job.WorkflowExecutionId);

            return;
        }

        if (shouldStartPump)
        {
            // The pump outlives the caller, so it must not carry the caller's ambient state, such
            // as a rerun's RerunWrites flag, into every job it starts later.
            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(ProcessQueueAsync);
            }
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await PumpQueueAsync();
        }
        catch (OperationCanceledException)
        {
            // Dispose cancelled the pump mid-wait; the service is going down with it.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Queue pump crashed; clearing the processing flag so a later enqueue can restart it");

            await _queueLock.WaitAsync(CancellationToken.None);

            try
            {
                _isProcessingQueue = false;
            }
            finally
            {
                _ = _queueLock.Release();
            }
        }
    }

    private async Task PumpQueueAsync()
    {
        var processingTasks = new List<Task>();

        while (true)
        {
            _ = processingTasks.RemoveAll(t => t.IsCompleted);

            while (processingTasks.Count < _platformSettings.IniaAi.MaxConcurrentJobs)
            {
                Job? jobToProcess = null;

                await _queueLock.WaitAsync(_queueProcessorCancellationTokenSource.Token);

                try
                {
                    if (_jobQueue.Count == 0)
                    {
                        break;
                    }

                    jobToProcess = _jobQueue.Dequeue();
                }
                finally
                {
                    _ = _queueLock.Release();
                }

                if (jobToProcess is not null && jobToProcess.IsFinished)
                {
                    _logger.LogInformation(
                        "Skipping queued job {JobId}: it finished before it was picked up",
                        jobToProcess.Id);

                    continue;
                }

                if (jobToProcess is not null)
                {
                    Task processingTask = ProcessJobWithSemaphoreAsync(
                        jobToProcess);

                    processingTasks.Add(processingTask);

                    _logger.LogInformation(
                        "Started processing queued job {JobId} (Active: {ActiveJobs}/{MaxJobs})",
                        jobToProcess.Id,
                        processingTasks.Count,
                        _platformSettings.IniaAi.MaxConcurrentJobs);
                }
            }

            if (processingTasks.Count == 0)
            {
                await _queueLock.WaitAsync(_queueProcessorCancellationTokenSource.Token);
                try
                {
                    if (_jobQueue.Count == 0)
                    {
                        _isProcessingQueue = false;
                        _logger.LogInformation("Job queue is empty and no jobs processing, stopping queue processor");
                        return;
                    }
                }
                finally
                {
                    _ = _queueLock.Release();
                }
            }

            await Task.Delay(
                QueueCheckDelayMs,
                _queueProcessorCancellationTokenSource.Token);
        }
    }

    private async Task ProcessJobWithSemaphoreAsync(BackgroundJob jobToProcess)
    {
        await _concurrencyLimitSemaphore.WaitAsync(_queueProcessorCancellationTokenSource.Token);

        try
        {
            await ProcessSingleJobAsync(jobToProcess);
        }
        finally
        {
            _ = _concurrencyLimitSemaphore.Release();
        }
    }

    private async Task ProcessSingleJobAsync(BackgroundJob jobToProcess)
    {
        if (jobToProcess.IsFinished)
        {
            _logger.LogInformation(
                "Skipping queued job {JobId}: it finished before it was picked up",
                jobToProcess.Id);

            return;
        }

        _logger.LogInformation("Processing queued job {JobId}", jobToProcess.Id);

        if (jobToProcess.State is JobState.Queued)
        {
            jobToProcess.SetState(JobState.Preparing);
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource cts = new();
        _cancellationTokens[jobToProcess.Id] = cts;

        try
        {
            IJobExecutor executor = _serviceProvider.GetRequiredService<IJobExecutor>();

            if (jobToProcess is not Job job)
            {
                _logger.LogError("Invalid job type: {JobType}", jobToProcess.GetType());
                return;
            }

            executor.Execute(job, finishedJob => _ = tcs.TrySetResult(true), cts.Token);

            _ = await tcs.Task;

            _logger.LogInformation("Completed queued job {JobId}", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing queued job {JobId}", jobToProcess.Id);
        }
        finally
        {
            _ = _cancellationTokens.TryRemove(jobToProcess.Id, out _);
        }
    }

    private void ExecuteJob(Job job)
    {
        var cts = new CancellationTokenSource();
        _cancellationTokens[job.Id] = cts;

        IJobExecutor executor = _serviceProvider
            .GetRequiredService<IJobExecutor>();

        try
        {
            executor.Execute(
                job,
                finishedJob => ReleaseCancellationSource(finishedJob.Id),
                cts.Token);
        }
        catch
        {
            ReleaseCancellationSource(job.Id);
            throw;
        }
    }

    private void ReleaseCancellationSource(string jobId)
    {
        if (_cancellationTokens.TryRemove(jobId, out CancellationTokenSource? cts))
        {
            cts.Dispose();
        }
    }

    private void OnJobDeleteRequested(object? sender, EventArgs args)
    {
        if (sender is not Job job)
        {
            throw new ApplicationException($"Invalid sender type: {sender}");
        }

        if (!job.IsFinished && job.State is not JobState.None)
        {
            throw new InvalidOperationException($"It's not possible to delete running jobs.");
        }

        job.DeleteRequested -= OnJobDeleteRequested;

        _jobsLock.Wait();
        try
        {
            _ = _jobs.Remove(job.Id);
        }
        finally
        {
            _ = _jobsLock.Release();
        }

        JobCollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnJobStateChanging(object? sender, JobStateChangeEventArgs args)
    {
        if (sender is not Job job)
        {
            _logger.LogError("Invalid sender type: {sender}", sender);

            return;
        }

        JobStateChanging?.Invoke(this, args);
    }

    private void OnJobStateChanged(object? sender, JobStateChangeEventArgs args)
    {
        if (sender is not Job job)
        {
            _logger.LogError("Invalid sender type: {sender}", sender);

            return;
        }

        _logger.LogTrace("Job state changed: {job}", job);

        if (job.IsFinished)
        {
            job.StateChanged -= OnJobStateChanged;
            job.StateChanging -= OnJobStateChanging;

            if (job.State is JobState.Done)
            {
                job.ClearWorkData();

                JobCollectionChanged?.Invoke(this, EventArgs.Empty);

                _logger.LogInformation("Job {JobId} completed successfully", job.Id);
            }
        }

        JobStateChanged?.Invoke(this, args);
    }

    #endregion
}
