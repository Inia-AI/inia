using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

public sealed class WorkflowVersionsByTypeSpec : Specification<WorkflowVersion>
{
    public WorkflowVersionsByTypeSpec(string workflowTypeId)
    {
        _ = Query.HeadElectionOrderForType(workflowTypeId);
    }
}
