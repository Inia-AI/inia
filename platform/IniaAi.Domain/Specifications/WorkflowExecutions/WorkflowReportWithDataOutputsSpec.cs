using Ardalis.Specification;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

public sealed class WorkflowReportWithDataOutputsSpec : Specification<WorkflowReport>, ISingleResultSpecification<WorkflowReport>
{
    public WorkflowReportWithDataOutputsSpec(string workflowExecutionId, string organizationId)
    {
        _ = Query
            .Where(r => r.WorkflowExecutionId == workflowExecutionId
                && r.OrganizationId == organizationId)
            .Include(r => r.DataOutputs)
            .AsNoTracking();
    }
}
