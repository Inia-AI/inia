using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.WorkflowExecutions;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetReRunInputs;

/// <summary>
/// The inputs a re-run of an execution would submit, checked against the workflow's current
/// template, so a client can start the re-run or ask the user for what the workflow now needs.
/// </summary>
public record GetReRunInputsQuery(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId)
    : IRequest<ErrorOr<ReRunInputsDto>>;
