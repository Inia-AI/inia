using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class WorkflowExecutionsForListingSpec : Specification<WorkflowExecution>
{
    /// <param name="chosenWorkflowTypeIds">
    /// Workflow types to include. An empty set lists every execution in the organization; a
    /// non-empty set only narrows that set and never widens it.
    /// </param>
    public WorkflowExecutionsForListingSpec(string organizationId, IReadOnlyCollection<string> chosenWorkflowTypeIds)
    {
        _ = Query
            .Where(e =>
                e.OrganizationId == organizationId &&
                e.WorkflowTypeId != WorkflowType.GENERIC)
            .Include(x => x.Counterparty)
            .AsNoTracking();

        if (chosenWorkflowTypeIds.Count > 0)
        {
            _ = Query.Where(e => chosenWorkflowTypeIds.Contains(e.WorkflowTypeId!));
        }
    }
}
