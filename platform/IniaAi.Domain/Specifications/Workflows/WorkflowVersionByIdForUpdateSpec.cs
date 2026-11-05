using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// Tracked variant of <see cref="WorkflowVersionByIdSpec"/> for the callers that mutate the
/// row — a no-tracking load silently discards the mutation at save time.
/// </summary>
public sealed class WorkflowVersionByIdForUpdateSpec
    : Specification<WorkflowVersion>, ISingleResultSpecification<WorkflowVersion>
{
    public WorkflowVersionByIdForUpdateSpec(string versionId)
    {
        _ = Query.Where(v => v.Id == versionId);
    }
}
