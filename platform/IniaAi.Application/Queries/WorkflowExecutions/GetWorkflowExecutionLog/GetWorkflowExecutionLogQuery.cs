using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecutionLog;

/// <summary>
/// Query to retrieve a single execution log by its unique identifier.
/// </summary>
/// <param name="OrganizationId">The organization ID for tenant isolation.</param>
/// <param name="UserId">The user ID making the request.</param>
/// <param name="WorkflowExecutionId">The unique identifier of the workflow execution.</param>
/// <param name="LogId">The unique identifier of the workflow execution log.</param>
/// <param name="IncludePrompt">Whether to include prompt and placeholder processing (expensive). Default true.</param>
/// <param name="IncludeOutput">Whether to include output content. Default true.</param>
public sealed record GetWorkflowExecutionLogQuery(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId,
    string LogId,
    bool IncludePrompt = true,
    bool IncludeOutput = true) : IRequest<ErrorOr<WorkflowOutputExecutionLogDto>>;
