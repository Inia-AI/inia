using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Services.Models.BackgroundJobs;
using IniaAi.Services.Models.Jobs;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// The in-memory job queue as a test sees it: jobs are created as the real service creates them, and
/// what is queued or cancelled is recorded instead of run. Lookups the tests never reach throw.
/// </summary>
public sealed class RecordingJobsService : IJobsService
{
    public List<Job> ExecutedJobs { get; } = [];

    public List<string> CancelledJobIds { get; } = [];

#pragma warning disable CS0067 // Event never used — required by interface
    public event EventHandler<JobStateChangeEventArgs>? JobStateChanged;
    public event EventHandler<JobStateChangeEventArgs>? JobStateChanging;
    public event EventHandler? JobCollectionChanged;
#pragma warning restore CS0067

    public Job CreateOrganizationJob(string organizationId, string? workflowTypeId)
    {
        return new Job(organizationId, workflowTypeId);
    }

    public Job CreateUserJob(User user, string? workflowTypeId)
    {
        Job job = CreateOrganizationJob(user.OrganizationId, workflowTypeId);
        job.SetUserId(user.Id);

        return job;
    }

    public Job CreateUserJob(string organizationId, string userId, string? workflowTypeId)
    {
        return CreateUserJob(new User { Id = userId, OrganizationId = organizationId }, workflowTypeId);
    }

    public void Execute(Job job, bool shouldUseQueue)
    {
        ExecutedJobs.Add(job);
    }

    public void CancelJob(string jobId)
    {
        CancelledJobIds.Add(jobId);
    }

    public Task<IEnumerable<Job>> GetJobsAsync()
    {
        throw new NotImplementedException();
    }

    public BackgroundJob? GetJob(string jobId)
    {
        throw new NotImplementedException();
    }

    public void DeleteJob(string jobId)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyList<Job> CancelAllQueuedJobs()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<BackgroundJob> GetOrganizationJobs(string organizationId)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<BackgroundJob> GetUserJobs(string userId)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
    }
}
