using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;

namespace IniaAi.Application.Queries.WorkflowReports.ListReportSections;

/// <summary>
/// Query to retrieve the section tree of a workflow report by execution identifier.
/// </summary>
/// <param name="Id">The unique identifier of the workflow execution.</param>
public sealed record ListReportSectionsQuery(string Id, string OrganizationId) : IRequest<ErrorOr<List<ReportSectionDto>>>;
