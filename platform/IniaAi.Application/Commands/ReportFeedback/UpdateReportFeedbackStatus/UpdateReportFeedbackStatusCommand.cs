using ErrorOr;
using IniaAi.WebApi.Contracts.Requests.ReportFeedback;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.ReportFeedback.UpdateReportFeedbackStatus;

/// <summary>
/// Moves a feedback item of the caller's organization to a new status. The HTTP mouth gates this
/// to editor roles; the handler pins the item to the report and organization named here.
/// </summary>
/// <param name="OrganizationId">The caller's organization; the item must belong to it.</param>
/// <param name="UserId">The editor making the change.</param>
/// <param name="WorkflowExecutionId">The report the item belongs to.</param>
/// <param name="FeedbackId">The item to update.</param>
/// <param name="Request">The new status and, when dismissing, the reason.</param>
public sealed record UpdateReportFeedbackStatusCommand(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId,
    string FeedbackId,
    UpdateReportFeedbackStatusRequest Request) : IRequest<ErrorOr<ReportFeedbackDto>>;
