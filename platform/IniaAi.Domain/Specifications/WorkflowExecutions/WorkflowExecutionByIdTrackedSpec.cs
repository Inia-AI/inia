using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// Specification for retrieving a single workflow execution by ID and organization for updates,
/// including related counterparty data. Tracked (no AsNoTracking) since it's used for entity
/// modification.
/// </summary>
public sealed class WorkflowExecutionByIdTrackedSpec : Specification<WorkflowExecution>
{
    public WorkflowExecutionByIdTrackedSpec(string id, string organizationId)
    {
        _ = Query
            .Where(e => e.Id == id && e.OrganizationId == organizationId && !e.IsDeleted)
            .Include(e => e.Counterparty);
    }
}
