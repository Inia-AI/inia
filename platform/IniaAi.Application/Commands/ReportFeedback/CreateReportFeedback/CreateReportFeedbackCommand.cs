using ErrorOr;
using IniaAi.WebApi.Contracts.Requests.ReportFeedback;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.ReportFeedback.CreateReportFeedback;

/// <summary>
/// Records one reader's feedback on a report of the caller's organization. The item belongs to
/// the report as a whole and carries the execution's workflow type; the reader's text selection,
/// when any, rides along as quoted text.
/// </summary>
/// <param name="OrganizationId">The caller's organization; the execution must belong to it.</param>
/// <param name="UserId">The author of the feedback.</param>
/// <param name="WorkflowExecutionId">The report the feedback is about.</param>
/// <param name="Request">The feedback content.</param>
public sealed record CreateReportFeedbackCommand(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId,
    CreateReportFeedbackRequest Request) : IRequest<ErrorOr<ReportFeedbackDto>>;
