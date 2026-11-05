using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.Common;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// Executions still in an active state (Undefined included — creation-default rows strand
/// too) whose life began at or before the cutoff, aged by <c>StartedAt</c> when set and
/// <c>CreatedAt</c> otherwise. Rows without an organization are excluded — the guarded
/// terminal write is organization-scoped, so the janitor could never mark them — and so are
/// soft-deleted rows.
/// </summary>
public sealed class AgedActiveWorkflowExecutionsSpec : Specification<WorkflowExecution>
{
    public AgedActiveWorkflowExecutionsSpec(DateTime startedAtOrCreatedAtCutoff)
    {
        _ = Query
            .Where(WorkflowExecutionExpressions.IsActive)
            .Where(WorkflowExecutionExpressions.IsInActiveState)
            .Where(e => e.OrganizationId != null)
            .Where(e => (e.StartedAt ?? e.CreatedAt) <= startedAtOrCreatedAtCutoff)
            .AsNoTracking();
    }
}
