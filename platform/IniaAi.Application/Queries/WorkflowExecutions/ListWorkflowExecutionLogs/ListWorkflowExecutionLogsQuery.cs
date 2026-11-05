using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutionLogs;

/// <summary>
/// Query to retrieve a lightweight summary list of execution logs for a workflow execution.
/// Returns summary data only - use GetWorkflowExecutionLogQuery for full details.
/// </summary>
/// <param name="OrganizationId">The organization ID for tenant isolation.</param>
/// <param name="UserId">The user ID making the request.</param>
/// <param name="Id">The unique identifier of the workflow execution.</param>
public sealed record ListWorkflowExecutionLogsQuery(
    string OrganizationId,
    string UserId,
    string Id) : IRequest<ErrorOr<List<WorkflowExecutionLogSummaryDto>>>;
