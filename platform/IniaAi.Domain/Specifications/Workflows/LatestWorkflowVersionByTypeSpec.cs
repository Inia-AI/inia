using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

public sealed class LatestWorkflowVersionByTypeSpec
    : Specification<WorkflowVersion>, ISingleResultSpecification<WorkflowVersion>
{
    public LatestWorkflowVersionByTypeSpec(string workflowTypeId)
    {
        _ = Query.HeadElectionOrderForType(workflowTypeId);
    }
}
