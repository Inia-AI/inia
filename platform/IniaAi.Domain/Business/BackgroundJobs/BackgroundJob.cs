using IniaAi.Domain.Entities.Workflows;
using IniaAi.Services.Models.BackgroundJobs.Enums;
using IniaAi.Services.Models.Jobs;
using IniaAi.Services.Models.Jobs.Enums;

namespace IniaAi.Services.Models.BackgroundJobs;

public abstract class BackgroundJob
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string? UserId { get; private set; }
    public string OrganizationId { get; private set; }
    public abstract BackgroundJobType Type { get; }

    /// <summary>
    /// Whether the job is a report run that job lists and report toasts show. A job that is not one
    /// reports through its own surface.
    /// </summary>
    public virtual bool ListedAsReport => true;
    public JobState State { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; private set; }
    public double Progress { get; private set; }
    public string Description { get; protected set; } = string.Empty;

    public event EventHandler<JobStateChangeEventArgs>? StateChanged;

    public event EventHandler<JobStateChangeEventArgs>? StateChanging;

    public event EventHandler? DeleteRequested;

    public bool IsFinished => State is JobState.Cancelled
        or JobState.Done
        or JobState.Error;

    protected BackgroundJob(string organizationId)
    {
        OrganizationId = organizationId;
    }

    protected BackgroundJob(string userId, string organizationId)
    {
        UserId = userId;
        OrganizationId = organizationId;
    }

    public void SetState(JobState newState, string? error = null)
    {
        State = newState;
        Error = error;

        if (newState is JobState.Done or JobState.Error)
        {
            CompletedAt = DateTime.UtcNow;
        }
    }

    public void UpdateProgress(double progress)
    {
        Progress = Math.Clamp(progress, 0, 100);
    }

    public void SetUserId(string userId)
    {
        UserId = userId;
    }

    public void SetOrganizationId(string organizationId)
    {
        OrganizationId = organizationId;
    }

    public void SetError(string error)
    {
        Error = error;
    }

    public void Delete()
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        SetStateInternal(JobState.Cancelled);
    }

    public void SetState(JobState nextState)
    {
        SetStateInternal(nextState);
    }

    public static string GetJobName(Workflow workflow, List<string?> nameParts)
    {
        string title = !string.IsNullOrWhiteSpace(workflow.Title)
            ? workflow.Title
            : workflow.WorkflowType?.Description ?? workflow.WorkflowType?.Name ?? "";

        var finalNameParts = new List<string>()
        {
            title,
        };

        foreach (string? namePart in nameParts)
        {
            if (string.IsNullOrWhiteSpace(namePart))
            {
                continue;
            }

            finalNameParts.Add(namePart);
        }

        return string.Join(" - ", finalNameParts);
    }

    #region Private

    private void SetStateInternal(JobState nextState)
    {
        if (State == nextState)
        {
            return;
        }

        if (IsFinished)
        {
            throw new InvalidOperationException("Cannot modify job state, as job is already in a final state.");
        }

        var eventArgs = new JobStateChangeEventArgs(this, State, nextState);

        StateChanging?.Invoke(this, eventArgs);

        State = nextState;

        StateChanged?.Invoke(this, eventArgs);
    }

    #endregion
}
