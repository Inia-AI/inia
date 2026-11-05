using Ardalis.Specification;
using IniaAi.Domain.Entities.ReportFeedback;

namespace IniaAi.Domain.Specifications.ReportFeedback;

/// <summary>
/// Every feedback item on one report, oldest first, scoped to the caller's organization: the
/// normal read, which never crosses the tenant boundary.
/// </summary>
public sealed class ReportFeedbackByExecutionSpec : Specification<ReportFeedbackItem>
{
    public ReportFeedbackByExecutionSpec(string workflowExecutionId, string organizationId)
    {
        _ = Query
            .Where(item => item.WorkflowExecutionId == workflowExecutionId
                && item.OrganizationId == organizationId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .AsNoTracking();
    }
}
