namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Persisted workflow execution state. Mirror of
/// <c>IniaAi.WebApi.Contracts.Enums.WorkflowState</c> — the two are cast between each
/// other by integer value at the application boundary. Drift is guarded by
/// <c>WorkflowStateEnumAlignmentTests</c>; keep both enums in lockstep.
/// </summary>
public enum WorkflowState
{
    Undefined,
    Running,
    Draft,
    Confirmed,
    Failed,
    ProcessingDocuments,
    Queued,
    Cancelled,
}
