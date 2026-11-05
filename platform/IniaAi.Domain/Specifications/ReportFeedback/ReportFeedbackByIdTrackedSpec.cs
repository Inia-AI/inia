using Ardalis.Specification;
using IniaAi.Domain.Entities.ReportFeedback;

namespace IniaAi.Domain.Specifications.ReportFeedback;

/// <summary>
/// One feedback item by id, pinned to the report and organization named in the request so a
/// status change or a delete cannot reach another tenant's item through a guessed id. Tracked
/// for update.
/// </summary>
public sealed class ReportFeedbackByIdTrackedSpec : Specification<ReportFeedbackItem>, ISingleResultSpecification<ReportFeedbackItem>
{
    public ReportFeedbackByIdTrackedSpec(string feedbackId, string workflowExecutionId, string organizationId)
    {
        _ = Query.Where(item => item.Id == feedbackId
            && item.WorkflowExecutionId == workflowExecutionId
            && item.OrganizationId == organizationId);
    }
}
