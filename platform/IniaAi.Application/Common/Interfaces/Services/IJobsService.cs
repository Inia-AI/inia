using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Services.Models.BackgroundJobs;
using IniaAi.Services.Models.Jobs;

namespace IniaAi.Application.Common.Interfaces.Services;

public interface IJobsService : IDisposable
{
    /// <summary>
    /// Raised when a job's state has changed.
    /// </summary>
    event EventHandler<JobStateChangeEventArgs>? JobStateChanged;

    /// <summary>
    /// Raised when a job's state is about to change.
    /// </summary>
    event EventHandler<JobStateChangeEventArgs>? JobStateChanging;

    /// <summary>
    /// Raised when the collection of jobs has changed (added, removed, etc).
    /// </summary>
    event EventHandler? JobCollectionChanged;

    /// <summary>
    /// Creates a new job for an organization.
    /// </summary>
    Job CreateOrganizationJob(string organizationId, string? workflowTypeId);

    /// <summary>
    /// Creates a new job for a user.
    /// </summary>
    Job CreateUserJob(User user, string? workflowTypeId);

    /// <summary>
    /// Creates a new job for a user using user and organization IDs.
    /// </summary>
    Job CreateUserJob(string organizationId, string userId, string? workflowTypeId);

    /// <summary>
    /// Executes a job immediately or adds it to the queue.
    /// </summary>
    void Execute(Job job, bool shouldUseQueue);

    /// <summary>
    /// Retrieves all jobs.
    /// </summary>
    Task<IEnumerable<Job>> GetJobsAsync();

    /// <summary>
    /// Retrieves a specific job by ID.
    /// </summary>
    BackgroundJob? GetJob(string jobId);

    /// <summary>
    /// Cancels a specific job by ID.
    /// </summary>
    void CancelJob(string jobId);

    /// <summary>
    /// Deletes a specific job by ID.
    /// </summary>
    void DeleteJob(string jobId);

    /// <summary>
    /// Closes the queue, dequeues every job still waiting, cancels each one, and returns them so
    /// the caller can mark their execution rows terminal. A job enqueued after the drain is
    /// cancelled on arrival instead of running; jobs already picked up for processing are not
    /// affected.
    /// </summary>
    IReadOnlyList<Job> CancelAllQueuedJobs();

    IEnumerable<BackgroundJob> GetOrganizationJobs(string organizationId);

    IEnumerable<BackgroundJob> GetUserJobs(string userId);
}
