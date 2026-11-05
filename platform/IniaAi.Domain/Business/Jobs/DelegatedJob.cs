using IniaAi.Services.Models.BackgroundJobs.Enums;

namespace IniaAi.Services.Models.Jobs;

/// <summary>
/// A job that is not a workflow run: the job executor runs <see cref="Work"/> in a scope of its
/// own, on a background task, with the job's cancellation token. It rides the same in-memory job
/// service as workflow runs, so it is cancellable by job id and drained at shutdown, and it never
/// runs on the caller's thread or connection.
/// </summary>
public sealed class DelegatedJob : Job
{
    public DelegatedJob(
        string organizationId,
        string userId,
        string description,
        Func<IServiceProvider, CancellationToken, Task> work)
        : base(organizationId, workflowTypeId: null)
    {
        SetUserId(userId);
        Description = description;
        Name = description;
        Work = work;
    }

    public override BackgroundJobType Type => BackgroundJobType.Delegated;

    public override bool ListedAsReport => false;

    public Func<IServiceProvider, CancellationToken, Task> Work { get; }
}
