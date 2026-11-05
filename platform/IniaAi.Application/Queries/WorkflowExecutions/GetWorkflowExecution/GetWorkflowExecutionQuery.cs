using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;

/// <summary>
/// Query to retrieve an execution by its unique identifier.
/// </summary>
/// <param name="OrganizationId">The organization ID for tenant isolation.</param>
/// <param name="UserId">The user ID making the request.</param>
/// <param name="Id">The unique identifier of the workflow execution.</param>
public sealed record GetWorkflowExecutionQuery(
    string OrganizationId,
    string UserId,
    string Id) : IRequest<ErrorOr<WorkflowExecutionDto>>;
