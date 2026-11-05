using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

public sealed class LatestWorkflowVersionIdByTypeSpec
    : Specification<WorkflowVersion, string>, ISingleResultSpecification<WorkflowVersion, string>
{
    public LatestWorkflowVersionIdByTypeSpec(string workflowTypeId)
    {
        _ = Query.HeadElectionOrderForType(workflowTypeId);

        Query.Select(v => v.Id);
    }
}
