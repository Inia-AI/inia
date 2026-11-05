using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>The display names of the named workflow types, for a surface that holds their ids.</summary>
public sealed class WorkflowTypeNamesByIdsSpec : Specification<WorkflowType, WorkflowTypeName>
{
    public WorkflowTypeNamesByIdsSpec(IReadOnlyCollection<string> workflowTypeIds)
    {
        Query
            .Where(t => workflowTypeIds.Contains(t.Id))
            .AsNoTracking()
            .Select(t => new WorkflowTypeName(t.Id, t.Name));
    }
}

/// <summary>One workflow type's id and the words a reader sees for it.</summary>
public sealed record WorkflowTypeName(string Id, string Name);
