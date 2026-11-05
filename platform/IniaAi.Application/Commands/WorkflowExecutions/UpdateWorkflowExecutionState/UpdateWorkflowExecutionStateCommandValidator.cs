using FluentValidation;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.Application.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;

public class UpdateWorkflowExecutionStateCommandValidator : AbstractValidator<UpdateWorkflowExecutionStateCommand>
{
    public UpdateWorkflowExecutionStateCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowExecutionId).NotEmpty();
        _ = RuleFor(x => x.TargetState)
            .Equal(WorkflowState.Cancelled)
            .WithMessage("Only 'Cancelled' is supported as a target state.");
    }
}
