using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;

namespace IniaAi.Application.Queries.WorkflowReports.ListReportDataOutputs;

/// <summary>
/// Query to retrieve the JSON data outputs a run returned beside its report.
/// </summary>
/// <param name="Id">The unique identifier of the workflow execution.</param>
public sealed record ListReportDataOutputsQuery(string Id, string OrganizationId) : IRequest<ErrorOr<List<ReportDataOutputDto>>>;
