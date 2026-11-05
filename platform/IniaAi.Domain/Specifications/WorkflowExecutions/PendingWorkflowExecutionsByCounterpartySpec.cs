using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.Common;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// Specification for retrieving pending workflow executions for a specific counterparty.
/// Used for dashboard and counterparty detail views.
/// Tracking behavior, ordering, and includes controlled by the caller.
/// </summary>
public sealed class PendingWorkflowExecutionsByCounterpartySpec : Specification<WorkflowExecution>
{
    public PendingWorkflowExecutionsByCounterpartySpec(string counterpartyId, string organizationId)
    {
        _ = Query
            .Where(WorkflowExecutionExpressions.ByOrganization(organizationId))
            .Where(WorkflowExecutionExpressions.ByCounterparty(counterpartyId))
            .Where(WorkflowExecutionExpressions.IsPending)
            .Where(WorkflowExecutionExpressions.IsActive);
    }
}