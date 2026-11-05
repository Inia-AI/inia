namespace IniaAi.Domain.Entities.Validation;

public enum ValidationRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,

    /// <summary>
    /// Extraction present, verdicts incomplete — resumable from the persisted extraction.
    /// </summary>
    Partial = 4,

    /// <summary>
    /// Created by a suite act and waiting for the execution that becomes its candidate. No work
    /// has been handed to the run queue and none may be: there is nothing to extract from yet.
    /// </summary>
    AwaitingCandidate = 5,
}

/// <summary>
/// Groupings of <see cref="ValidationRunStatus"/> that more than one layer has to express. Named
/// here rather than in the lifecycle rules because a specification has to translate them and
/// cannot reach the Core layer.
/// </summary>
public static class ValidationRunStatuses
{
    /// <summary>The statuses a run has not finished in.</summary>
    public static readonly ValidationRunStatus[] IN_FLIGHT =
    [
        ValidationRunStatus.AwaitingCandidate,
        ValidationRunStatus.Pending,
        ValidationRunStatus.Running,
    ];
}
