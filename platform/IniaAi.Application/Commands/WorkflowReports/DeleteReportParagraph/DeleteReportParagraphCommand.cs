using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteReportParagraph;

/// <summary>
/// Command to delete a report paragraph.
/// </summary>
public sealed record DeleteReportParagraphCommand(
    string OrganizationId,
    string UserId,
    string Id,
    string ParagraphId) : IRequest<ErrorOr<Success>>;
