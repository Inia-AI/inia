using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Requests.WorkflowReports;

namespace IniaAi.Application.Commands.WorkflowReports.UpdateReportSection;

/// <summary>
/// Command to update the editable fields of a report section.
/// </summary>
public sealed record UpdateReportSectionCommand(
    string OrganizationId,
    string UserId,
    string Id,
    string SectionId,
    UpdateReportSectionRequest Request) : IRequest<ErrorOr<Success>>;
