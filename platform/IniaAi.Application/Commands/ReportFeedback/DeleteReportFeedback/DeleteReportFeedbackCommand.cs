using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.ReportFeedback.DeleteReportFeedback;

/// <summary>
/// Deletes a feedback item for good, whatever its status. Any reader who can list the item may
/// delete it; the handler pins the item to the report and organization named here, the scope of
/// the org-scoped read.
/// </summary>
/// <param name="OrganizationId">The caller's organization; the item must belong to it.</param>
/// <param name="UserId">The reader deleting the item.</param>
/// <param name="WorkflowExecutionId">The report the item belongs to.</param>
/// <param name="FeedbackId">The item to delete.</param>
public sealed record DeleteReportFeedbackCommand(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId,
    string FeedbackId) : IRequest<ErrorOr<Deleted>>;
