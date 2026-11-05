using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Persistence.UserData.Repositories;

public interface IWorkflowExecutionRepository : IRepository<WorkflowExecution>
{
    /// <summary>
    /// Transitions the execution to <paramref name="terminalState"/> only while it is still
    /// active (Queued / ProcessingDocuments / Running), stamping lifecycle timestamps that
    /// are not already set. On relational providers the active-state check runs inside the
    /// UPDATE's WHERE clause, so a concurrent terminal transition can never be overwritten;
    /// non-relational providers fall back to load-check-save. Returns false when no active
    /// row matched. A <see cref="WorkflowState.Failed"/> transition also records
    /// <paramref name="failureKind"/> and <paramref name="failureDetail"/>, each truncated to its
    /// column limit; other terminal states leave those columns untouched.
    /// </summary>
    Task<bool> TryMarkTerminalIfActiveAsync(
        string workflowExecutionId,
        string organizationId,
        WorkflowState terminalState,
        string? failureKind = null,
        string? failureDetail = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the execution to <paramref name="state"/> only while it is still active, stamping the
    /// lifecycle timestamps that are not already set. This is how the engine writes its own
    /// progress and success states, so a run that was cancelled or failed elsewhere can never be
    /// resurrected or overwritten by a later write from the worker that lost the race. Returns
    /// false when no active row matched — the caller has been fenced. A non-null
    /// <paramref name="warningKind"/> rides the same write, so the completed-with-warning stamp
    /// is part of how the run concluded, never a later mutation of a terminal row.
    /// </summary>
    Task<bool> TryTransitionStateIfActiveAsync(
        WorkflowExecution execution,
        WorkflowState state,
        string? warningKind = null,
        string? warningDetail = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The execution's currently stored state, read without tracking so a caller that loaded the
    /// row earlier still sees what other writers have since published. Null when no such row
    /// exists. Used at block boundaries to notice a cancel or fence from another process.
    /// </summary>
    Task<WorkflowState?> GetStoredStateAsync(
        string workflowExecutionId,
        string organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes only the replay outcome: the unmatched payload and the freeze coverage counters.
    /// Deliberately narrow: a replay run records this after the run, when the row may already be
    /// terminal, and a full-entity write would carry a stale
    /// <see cref="WorkflowExecution.WorkflowState"/> along with it.
    /// </summary>
    Task UpdateReplayOutcomeAsync(
        string workflowExecutionId,
        string? organizationId,
        string? replayUnmatchedJson,
        int freezeReplayedCount,
        int freezeEligibleCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes only the counterparty link. Monitoring runs discover their counterparty mid-run and
    /// stamp it on an execution instance the engine has already detached, so it needs its own
    /// narrow writer — the state transitions deliberately write nothing but state and timestamps.
    /// </summary>
    Task UpdateCounterpartyIdAsync(
        string workflowExecutionId,
        string? organizationId,
        string? counterpartyId,
        CancellationToken cancellationToken = default);
}
