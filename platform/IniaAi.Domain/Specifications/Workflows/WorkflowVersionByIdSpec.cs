using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

public sealed class WorkflowVersionByIdSpec
    : Specification<WorkflowVersion>, ISingleResultSpecification<WorkflowVersion>
{
    public WorkflowVersionByIdSpec(string versionId)
    {
        _ = Query
            .Where(v => v.Id == versionId)
            .AsNoTracking();
    }
}
