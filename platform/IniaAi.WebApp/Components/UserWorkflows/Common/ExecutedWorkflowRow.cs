using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.WebApp.Components.UserWorkflows.Common;

/// <summary>
/// One row of the executed-workflows list: the executed <see cref="Workflow"/> snapshot the grid
/// has always shown, plus the outcome of the execution that produced it. A snapshot with no
/// execution row keeps a null <see cref="Outcome"/> and reports
/// <see cref="WorkflowState.Undefined"/> rather than a guessed state. A failed run with no
/// snapshot has no <see cref="Workflow"/>: the row is the execution alone, named and dated from
/// it, and its one action deletes the run.
/// </summary>
public sealed record ExecutedWorkflowRow
{
    /// <summary>The executed workflow snapshot, or null for a failed run without one. Carries the row's identity for the grid's snapshot actions.</summary>
    public Workflow? Workflow { get; init; }

    /// <summary>The workflow type's display name, resolved from the snapshot or, without one, from the execution's type.</summary>
    public string? TypeName { get; init; }

    /// <summary>When the snapshot was written, or when the execution was created for a row without one.</summary>
    public DateTime CreatedAt => Workflow?.CreatedAt ?? Outcome?.CreatedAt ?? default;

    /// <summary>The snapshot's name, or the execution's title for a row without one.</summary>
    public string? Name => Workflow?.Name ?? Outcome?.Title;

    /// <summary>The snapshot's description; a row without a snapshot has none.</summary>
    public string? Description => Workflow?.Description;

    /// <summary>The workflow type the row belongs to, for the type filter.</summary>
    public string? WorkflowTypeId => Workflow?.WorkflowTypeId ?? Outcome?.WorkflowTypeId;

    /// <summary>The outcome of the execution that produced <see cref="Workflow"/>, when one was found.</summary>
    public ExecutionOutcome? Outcome { get; init; }

    /// <summary>The execution's id, or null when no execution row matched the snapshot.</summary>
    public string? ExecutionId => Outcome?.ExecutionId;

    /// <summary>The persisted execution state, or <see cref="WorkflowState.Undefined"/> when unknown.</summary>
    public WorkflowState State => Outcome?.State ?? WorkflowState.Undefined;

    /// <summary>The persisted failure kind, or null.</summary>
    public string? FailureKind => Outcome?.FailureKind;

    /// <summary>The persisted failure detail, or null.</summary>
    public string? FailureDetail => Outcome?.FailureDetail;

    /// <summary>The persisted warning kind on a run that completed with a warning, or null.</summary>
    public string? WarningKind => Outcome?.WarningKind;

    /// <summary>The persisted warning detail, or null.</summary>
    public string? WarningDetail => Outcome?.WarningDetail;

    /// <summary>When the execution reached its terminal state, or null while it has not.</summary>
    public DateTime? CompletedAt => Outcome?.CompletedAt;

    /// <summary>Whether this row has a failure worth disclosing.</summary>
    public bool HasFailure => ExecutionFailureText.HasFailure(FailureKind, FailureDetail);

    /// <summary>Whether this row completed with a warning worth disclosing.</summary>
    public bool HasWarning => ExecutionFailureText.HasFailure(WarningKind, WarningDetail);

    /// <summary>Whether the Attention column has anything to show for this row.</summary>
    public bool HasAttention => HasFailure || HasWarning;

    /// <summary>Whether the row's attention is a completed-with-warning, not a failure.</summary>
    public bool IsWarningOnly => !HasFailure && HasWarning;

    /// <summary>The kind the Attention column shows: the failure's, else the warning's.</summary>
    public string? AttentionKind => HasFailure ? FailureKind : HasWarning ? WarningKind : null;

    /// <summary>The detail behind <see cref="AttentionKind"/>.</summary>
    public string? AttentionDetail => HasFailure ? FailureDetail : HasWarning ? WarningDetail : null;
}
