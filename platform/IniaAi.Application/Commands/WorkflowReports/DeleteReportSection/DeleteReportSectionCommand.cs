using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteReportSection;

/// <summary>
/// Command to delete a report section.
/// </summary>
public sealed record DeleteReportSectionCommand(
    string OrganizationId,
    string UserId,
    string Id,
    string SectionId) : IRequest<ErrorOr<Success>>;
