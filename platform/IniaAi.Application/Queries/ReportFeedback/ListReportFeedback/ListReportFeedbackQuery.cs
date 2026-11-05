using ErrorOr;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.ReportFeedback.ListReportFeedback;

/// <summary>
/// Every feedback item on one report of the caller's organization, oldest first.
/// </summary>
/// <param name="OrganizationId">The caller's organization; items of other organizations are never returned.</param>
/// <param name="UserId">The caller.</param>
/// <param name="WorkflowExecutionId">The report.</param>
public sealed record ListReportFeedbackQuery(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId) : IRequest<ErrorOr<List<ReportFeedbackDto>>>;
