using Ardalis.Specification;
using IniaAi.Domain.Entities.ReportFeedback;

namespace IniaAi.Domain.Specifications.ReportFeedback;

/// <summary>
/// One keyset page of every organization's feedback on a workflow type, newest first on
/// <c>(CreatedAt, Id)</c>, optionally narrowed to one status. This is the one read of feedback
/// that crosses the tenant boundary on purpose; it serves editor roles only, leaves out the
/// organizations closed to the caller, and callers pass <c>take + 1</c> to learn whether more rows
/// remain.
/// </summary>
public sealed class ReportFeedbackByWorkflowTypePagedSpec : Specification<ReportFeedbackItem>
{
    public ReportFeedbackByWorkflowTypePagedSpec(
        string workflowTypeId,
        ReportFeedbackStatus? status,
        int take,
        DateTime? cursorCreatedAtUtc = null,
        string? cursorId = null,
        IQueryable<string>? closedOrganizationIds = null)
    {
        _ = Query.Where(item => item.WorkflowTypeId == workflowTypeId);
        if (closedOrganizationIds is not null)
        {
            _ = Query.Where(item => !closedOrganizationIds.Contains(item.OrganizationId));
        }

        if (status.HasValue)
        {
            ReportFeedbackStatus wanted = status.Value;
            _ = Query.Where(item => item.Status == wanted);
        }

        if (cursorCreatedAtUtc.HasValue && cursorId is not null)
        {
            DateTime cursorCreatedAt = cursorCreatedAtUtc.Value;
            _ = Query.Where(item => item.CreatedAt < cursorCreatedAt
                || (item.CreatedAt == cursorCreatedAt && string.Compare(item.Id, cursorId) < 0));
        }

        _ = Query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(take)
            .AsNoTracking();
    }
}
