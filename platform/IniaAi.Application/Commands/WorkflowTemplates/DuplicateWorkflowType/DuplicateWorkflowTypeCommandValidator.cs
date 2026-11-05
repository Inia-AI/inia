using FluentValidation;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.DuplicateWorkflowType;

public class DuplicateWorkflowTypeCommandValidator : AbstractValidator<DuplicateWorkflowTypeCommand>
{
    public DuplicateWorkflowTypeCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SourceWorkflowTypeId).NotEmpty().MaximumLength(WorkflowType.ID_MAX_LENGTH);
        _ = RuleFor(x => x.NewWorkflowTypeId)
            .NotEmpty()
            .MaximumLength(WorkflowType.ID_MAX_LENGTH)
            .NotEqual(x => x.SourceWorkflowTypeId)
                .WithMessage("The duplicate must use a workflow type id different from its source.");
        _ = RuleFor(x => x.NewName).NotEmpty().MaximumLength(WorkflowType.NAME_MAX_LENGTH);
    }
}
