using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// One keyset page of a workflow type's version history, newest-first on
/// <c>(CreatedAt, Id)</c>. The cursor is the last row of the previous page; keyset paging
/// (rather than skip/take) keeps pages stable while concurrent saves prepend new head
/// versions. Callers pass <c>take + 1</c> to detect whether more rows remain.
/// </summary>
public sealed class WorkflowVersionsByTypePagedSpec : Specification<WorkflowVersion>
{
    public WorkflowVersionsByTypePagedSpec(
        string workflowTypeId,
        int take,
        DateTime? cursorCreatedAtUtc = null,
        string? cursorId = null)
    {
        _ = Query.Where(v => v.WorkflowTypeId == workflowTypeId);

        if (cursorCreatedAtUtc.HasValue && cursorId is not null)
        {
            DateTime cursorCreatedAt = cursorCreatedAtUtc.Value;
            _ = Query.Where(v => v.CreatedAt < cursorCreatedAt
                || (v.CreatedAt == cursorCreatedAt && string.Compare(v.Id, cursorId) < 0));
        }

        _ = Query
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .Take(take)
            .AsNoTracking();
    }
}

/// <summary>
/// Companion count spec — all versions of the type, no ordering or paging; populates
/// <c>TotalCount</c> on the history page DTO.
/// </summary>
public sealed class WorkflowVersionsByTypeCountSpec : Specification<WorkflowVersion>
{
    public WorkflowVersionsByTypeCountSpec(string workflowTypeId)
    {
        _ = Query.Where(v => v.WorkflowTypeId == workflowTypeId);
    }
}
