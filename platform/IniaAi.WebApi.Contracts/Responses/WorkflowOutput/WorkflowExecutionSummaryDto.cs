using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents a workflow output element with associated metadata and content.
/// </summary>
public record WorkflowExecutionSummaryDto
{
    /// <summary>
    /// Gets the unique identifier of the workflow output element.
    /// </summary>
    public string Id { get; init; } = default!;

    /// <summary>
    /// Gets the identifier of the associated workflow.
    /// </summary>
    public string? WorkflowId { get; init; } = default!;

    /// <summary>
    /// Gets the creation date and time of the workflow output element.
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>
    /// Gets the current state of the workflow.
    /// </summary>
    public WorkflowState WorkflowState { get; init; }

    /// <summary>
    /// Gets why the execution ended <see cref="WorkflowState.Failed"/> — one of the persisted
    /// failure kinds, the same value the report carries. The kinds are
    /// <c>CriticalBlockFailure</c>, <c>ResultsLost</c>, <c>LifetimeExceeded</c>, <c>HostLost</c>,
    /// <c>VerificationGroupShape</c> or <c>VerificationUnsatisfiable</c>; and on a workflow type
    /// that screens its documents, <c>RejectedAtIntake</c> (the documents sent cannot make the
    /// report, and <c>FailureDetail</c> names what is missing) or <c>ScreeningUnavailable</c>
    /// (the document check itself failed, so sending the same documents again can succeed).
    /// Null on runs that did not fail and on failures the engine could not classify.
    /// </summary>
    public string? FailureKind { get; init; }

    /// <summary>
    /// Gets the human-readable explanation of the failure — which block failed and why —
    /// truncated to the column limit when it was written. Null when there is nothing to explain.
    /// </summary>
    public string? FailureDetail { get; init; }

    /// <summary>
    /// Gets why this completed execution still needs attention — currently
    /// <c>UnverifiedRelease</c>, a verification group whose work released unverified. Null on
    /// runs that completed clean and on runs that did not complete.
    /// </summary>
    public string? WarningKind { get; init; }

    /// <summary>
    /// Gets the human-readable explanation of the warning — which verification group released
    /// unverified and why. Null when there is no warning.
    /// </summary>
    public string? WarningDetail { get; init; }

    /// <summary>
    /// Gets the identifier of the workflow type.
    /// </summary>
    public string? WorkflowTypeId { get; init; }

    /// <summary>
    /// Gets the identifier of the counterparty involved in the workflow.
    /// </summary>
    public string? CounterpartyId { get; init; } = default!;

    /// <summary>
    /// Gets the name of the counterparty involved in the workflow.
    /// </summary>
    public string? CounterpartyName { get; init; }

    /// <summary>
    /// Gets the name of the analyst associated with the workflow output.
    /// </summary>
    public string? AnalystName { get; init; }

    /// <summary>
    /// Gets the date when the workflow output was reviewed.
    /// </summary>
    public DateTime? ReviewDate { get; init; }

    /// <summary>
    /// Gets the identifier of the user who created the workflow output.
    /// </summary>
    public string? UserId { get; init; } = default!;

    /// <summary>
    /// Gets the title of the workflow execution.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the job id for the workflow output root run.
    /// </summary>
    public string? JobId { get; init; }

    /// <summary>
    /// Gets whether this workflow execution is marked as a baseline for regression testing.
    /// </summary>
    public bool IsBaseline { get; init; }

    /// <summary>
    /// Gets whether the baseline behind this report is verified — at least one live validation
    /// case and every one of them Active. Null when the report is not a baseline. A verified
    /// baseline renders a solid star; an unverified one a dim star.
    /// </summary>
    public bool? BaselineVerified { get; init; }

    /// <summary>
    /// Gets the completed execution whose external-fetch outputs this execution replayed (the
    /// freeze option). Non-null marks a frozen execution: a test artifact whose web-search, exchange
    /// rate and external API outputs are historical, never a normal report.
    /// </summary>
    public string? FreezeSourceExecutionId { get; init; }

    /// <summary>
    /// Gets how many freeze-eligible blocks were served from a replayed output instead of a live
    /// fetch. Written when a replay run (frozen or a regression rerun) ends; null on a non-replay
    /// run.
    /// </summary>
    public int? FreezeReplayedCount { get; init; }

    /// <summary>
    /// Gets how many freeze-eligible blocks the execution ran (the replay coverage denominator).
    /// Written when a replay run (frozen or a regression rerun) ends; null on a non-replay run.
    /// </summary>
    public int? FreezeEligibleCount { get; init; }

    /// <summary>
    /// UTC timestamp when the worker dequeued the execution. Null while queued.
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// UTC timestamp when the execution reached a terminal state
    /// (Draft, Failed, Confirmed, or Cancelled). Null until terminal.
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// Wall-clock duration from dequeue to terminal state. Null while still running or queued.
    /// </summary>
    public TimeSpan? Duration { get; init; }
}
