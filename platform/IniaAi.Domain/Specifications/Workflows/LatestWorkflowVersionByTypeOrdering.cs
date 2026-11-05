using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// The shared head-election ordering of the per-type workflow version specs: newest CreatedAt
/// first, with content hash then id as deterministic tie-breakers. Every per-type version spec
/// orders through this chain — head specs so they all elect the same head row, list specs so the
/// head sorts first — and it lives here exactly once.
/// </summary>
public static class LatestWorkflowVersionByTypeOrdering
{
    public static ISpecificationBuilder<WorkflowVersion> HeadElectionOrderForType(
        this ISpecificationBuilder<WorkflowVersion> query,
        string workflowTypeId)
    {
        return query
            .Where(v => v.WorkflowTypeId == workflowTypeId)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.ContentHash)
            .ThenByDescending(v => v.Id)
            .AsNoTracking();
    }
}
