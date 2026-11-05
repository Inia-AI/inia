using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;
using System.Text.Json;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents a workflow output element with associated metadata and content.
/// </summary>
public record WorkflowExecutionDto
{
    /// <summary>
    /// Gets the unique identifier of the workflow execution element.
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
    /// Gets why the execution ended <see cref="WorkflowState.Failed"/>: one of the persisted
    /// failure kinds, the same value the execution list carries. The kinds are
    /// <c>CriticalBlockFailure</c>, <c>ResultsLost</c>, <c>LifetimeExceeded</c>, <c>HostLost</c>,
    /// <c>VerificationGroupShape</c> or <c>VerificationUnsatisfiable</c>; and on a workflow type
    /// that screens its documents, <c>RejectedAtIntake</c> (the documents sent cannot make the
    /// report, and <c>FailureDetail</c> names what is missing) or <c>ScreeningUnavailable</c>
    /// (the document check itself failed, so sending the same documents again can succeed).
    /// Null on runs that did not fail and on failures the engine could not classify.
    /// </summary>
    public string? FailureKind { get; init; }

    /// <summary>
    /// Gets the human-readable explanation of the failure, truncated to the column limit when it
    /// was written. Null when there is nothing to explain.
    /// </summary>
    public string? FailureDetail { get; init; }

    /// <summary>
    /// Gets the identifier of the workflow type.
    /// </summary>
    public string? WorkflowTypeId { get; init; }

    /// <summary>
    /// Gets the name of the workflow type.
    /// </summary>
    public string? WorkflowTypeName { get; set; }

    /// <summary>
    /// Gets the identifier of the counterparty involved in the workflow.
    /// </summary>
    public string? CounterpartyId { get; init; } = default!;

    /// <summary>
    /// Gets the name of the counterparty.
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
    /// Gets the dynamic variables of the workflow execution.
    /// </summary>
    public Dictionary<string, JsonElement>? Variables { get; set; }

    /// <summary>
    /// Gets the job id for the workflow output root run.
    /// </summary>
    public string? JobId { get; init; }

    /// <summary>
    /// Gets the documents used in this workflow execution with their execution-specific settings.
    /// </summary>
    public List<WorkflowExecutionDocumentDto>? WorkflowExecutionDocuments { get; init; }

    /// <summary>
    /// Gets the documents the caller sent that the run took out of its input before processing
    /// them, each with <see cref="WorkflowExecutionDocumentDto.ExclusionReason"/> set. They are
    /// not in <see cref="WorkflowExecutionDocuments"/>. Populated only by the GET endpoint.
    /// </summary>
    public List<WorkflowExecutionDocumentDto>? ExcludedDocuments { get; init; }

    /// <summary>
    /// Resolved per-variable values from the workflow snapshot joined with this execution's
    /// supplied raw values. Each entry carries the display name, type, and a server-resolved
    /// display value (option key→display name for Dropdown/MultiSelect, confidential masking, etc.).
    /// Populated only by the GET endpoint; null on Start/Submit responses.
    /// </summary>
    public List<WorkflowExecutionVariableValueDto>? VariableValues { get; init; }

    /// <summary>
    /// Gets whether this workflow execution is marked as a baseline for regression testing.
    /// </summary>
    public bool IsBaseline { get; init; }

    /// <summary>
    /// Gets whether every live validation case behind the baseline is Active. Null when the
    /// execution is not a baseline.
    /// </summary>
    public bool? BaselineVerified { get; init; }

    /// <summary>
    /// Gets the completed execution whose external-fetch outputs this execution replayed (the
    /// freeze option). Non-null marks a frozen execution: a test artifact whose web-search, exchange
    /// rate and external API outputs are historical, never a normal report.
    /// </summary>
    public string? FreezeSourceExecutionId { get; init; }

    /// <summary>
    /// Gets the validation case whose stored freeze captures this execution replayed, when the
    /// freeze source was a case rather than a same-instance execution. The source execution id
    /// then names an execution of the deployment the case was imported from.
    /// </summary>
    public string? FreezeSourceCaseId { get; init; }

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
    /// UTC timestamp when the worker dequeued this execution and began processing
    /// (first state transition out of <see cref="WorkflowState.Queued"/>). Null while still queued.
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// UTC timestamp when the execution reached a terminal state
    /// (<see cref="WorkflowState.Draft"/> on success or <see cref="WorkflowState.Failed"/> on error).
    /// Null until then.
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// Wall-clock duration from dequeue to terminal state. Null while either
    /// <see cref="StartedAt"/> or <see cref="CompletedAt"/> is unset.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Files this submission's intake could not turn into documents (unsupported email parts,
    /// protected files, failed downloads), with reasons. Populated only on the submit response;
    /// the durable record is the Skipped rows in the execution log.
    /// </summary>
    public List<SkippedFileDto>? IntakeSkippedFiles { get; init; }

    /// <summary>
    /// Live activity of the execution's agent blocks, one entry per run currently held by
    /// the process's live feed. Populated only by the GET endpoint, and only while such
    /// entries exist; null otherwise. Settled blocks' durable record is the agent-activity
    /// digest endpoint.
    /// </summary>
    public List<AgentBlockActivityDto>? AgentActivity { get; init; }

    /// <summary>
    /// Plain-language activity of a running classic workflow, resolved from its version's
    /// cached activity script. Populated only by the GET endpoint, only while the execution is
    /// running, and only when the stamped template version carries a script; null otherwise.
    /// </summary>
    public WorkflowActivityDto? WorkflowActivity { get; init; }
}
