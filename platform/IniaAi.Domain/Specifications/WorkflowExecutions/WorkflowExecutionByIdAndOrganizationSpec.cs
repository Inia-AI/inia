using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// Specification for retrieving a workflow execution by ID within organization scope,
/// including related counterparty data.
/// </summary>
public sealed class WorkflowExecutionByIdAndOrganizationSpec : Specification<WorkflowExecution>
{
    public WorkflowExecutionByIdAndOrganizationSpec(string id, string organizationId)
    {
        _ = Query
            .Where(x => x.Id == id && x.OrganizationId == organizationId)
            .Include(wo => wo.Counterparty)
            .AsNoTracking();
    }
}