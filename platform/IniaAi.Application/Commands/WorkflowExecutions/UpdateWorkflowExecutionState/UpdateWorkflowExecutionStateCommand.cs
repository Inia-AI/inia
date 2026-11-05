using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.Application.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;

public record UpdateWorkflowExecutionStateCommand(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId,
    WorkflowState TargetState) : IRequest<ErrorOr<Success>>;
