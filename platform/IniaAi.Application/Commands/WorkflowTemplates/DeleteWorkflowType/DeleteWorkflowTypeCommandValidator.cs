using FluentValidation;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.DeleteWorkflowType;

public class DeleteWorkflowTypeCommandValidator : AbstractValidator<DeleteWorkflowTypeCommand>
{
    public DeleteWorkflowTypeCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty().MaximumLength(WorkflowType.ID_MAX_LENGTH);
    }
}
