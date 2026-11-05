using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.Common;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// Specification for retrieving recent workflow executions within an organization.
/// Shows active executions from the last specified days.
/// Tracking behavior, ordering, and includes controlled by the caller.
/// </summary>
public sealed class RecentWorkflowExecutionsSpec : Specification<WorkflowExecution>
{
    public RecentWorkflowExecutionsSpec(string organizationId, int daysBack = 30)
    {
        _ = Query
            .Where(WorkflowExecutionExpressions.ByOrganization(organizationId))
            .Where(WorkflowExecutionExpressions.IsActive)
            .Where(WorkflowExecutionExpressions.RecentDays(daysBack));
    }
}