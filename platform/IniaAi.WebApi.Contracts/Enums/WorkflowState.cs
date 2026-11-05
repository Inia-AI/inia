namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Represents the current state of a workflow in the system.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.WorkflowExecutions.WorkflowState</c> — the two are
/// cast between each other by integer value at the application boundary. Drift is
/// guarded by <c>WorkflowStateEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum WorkflowState
{
    /// <summary>
    /// The workflow state is not defined.
    /// </summary>
    Undefined,

    /// <summary>
    /// The workflow is actively running.
    /// </summary>
    Running,

    /// <summary>
    /// The execution completed successfully and awaits analyst review.
    /// </summary>
    Draft,

    /// <summary>
    /// The completed execution has been reviewed and confirmed by an analyst.
    /// </summary>
    Confirmed,

    /// <summary>
    /// The workflow execution failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The workflow is currently processing documents.
    /// </summary>
    ProcessingDocuments,

    /// <summary>
    /// The workflow is currently queued for processing.
    /// </summary>
    Queued,

    /// <summary>
    /// The workflow execution was cancelled by a user or by the system.
    /// </summary>
    Cancelled,
}
