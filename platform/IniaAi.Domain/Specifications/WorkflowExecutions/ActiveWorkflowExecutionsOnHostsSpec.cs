using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.Common;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// Active executions whose job one of the named process starts holds. Rows without an organization
/// are excluded, because the guarded terminal write is organization-scoped, and so are soft-deleted
/// rows.
/// </summary>
public sealed class ActiveWorkflowExecutionsOnHostsSpec : Specification<WorkflowExecution>
{
    public ActiveWorkflowExecutionsOnHostsSpec(IReadOnlyCollection<string> hostBootIds)
    {
        _ = Query
            .Where(WorkflowExecutionExpressions.IsActive)
            .Where(WorkflowExecutionExpressions.IsInActiveState)
            .Where(e => e.OrganizationId != null)
            .Where(e => e.HostBootId != null && hostBootIds.Contains(e.HostBootId))
            .AsNoTracking();
    }
}
