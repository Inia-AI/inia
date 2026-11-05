using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

public sealed class WorkflowConfigurationByIdSpec : Specification<Workflow>, ISingleResultSpecification<Workflow>
{
    public WorkflowConfigurationByIdSpec(string workflowId)
    {
        _ = Query
            .Where(w => w.Id == workflowId)
            .AsNoTracking();
    }
}
