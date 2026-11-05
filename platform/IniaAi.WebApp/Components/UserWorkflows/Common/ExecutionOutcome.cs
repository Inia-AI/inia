using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.WebApp.Components.UserWorkflows.Common;

/// <summary>
/// What the executed-workflows list needs to know about the <see cref="WorkflowExecution"/> row
/// that produced a snapshot: its outcome and, when that outcome was a failure, why.
/// Read from the database rather than the in-process job list, so a run whose host died and was
/// failed by the janitor still reports its state after a restart.
/// </summary>
/// <param name="ExecutionId">The <see cref="WorkflowExecution.Id"/>.</param>
/// <param name="WorkflowId">The executed snapshot this outcome belongs to.</param>
/// <param name="CreatedAt">When the execution row was created — the tie-break when a snapshot has more than one.</param>
/// <param name="State">The persisted <see cref="WorkflowState"/>.</param>
/// <param name="FailureKind">One of <see cref="WorkflowExecutionFailureKinds"/>, or null.</param>
/// <param name="FailureDetail">The human-readable explanation, or null.</param>
/// <param name="WarningKind">One of <see cref="WorkflowExecutionWarningKinds"/> on a run that completed with a warning, or null.</param>
/// <param name="WarningDetail">The human-readable warning explanation, or null.</param>
/// <param name="CompletedAt">When the execution reached its terminal state — the "when" of a Cancelled or Failed row.</param>
/// <param name="Title">The execution's title, the name a run that failed before its snapshot is listed under.</param>
/// <param name="WorkflowTypeId">The execution's workflow type, for the same row's type column and the type filter.</param>
public sealed record ExecutionOutcome(
    string ExecutionId,
    string WorkflowId,
    DateTime CreatedAt,
    WorkflowState State,
    string? FailureKind,
    string? FailureDetail,
    string? WarningKind,
    string? WarningDetail,
    DateTime? CompletedAt,
    string? Title = null,
    string? WorkflowTypeId = null);
