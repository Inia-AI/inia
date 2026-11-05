using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Requests.WorkflowReports;

namespace IniaAi.Application.Commands.WorkflowReports.UpdateReportParagraph;

/// <summary>
/// Command to update the editable fields of a report paragraph.
/// </summary>
public sealed record UpdateReportParagraphCommand(
    string OrganizationId,
    string UserId,
    string Id,
    string ParagraphId,
    UpdateReportParagraphRequest Request) : IRequest<ErrorOr<Success>>;
