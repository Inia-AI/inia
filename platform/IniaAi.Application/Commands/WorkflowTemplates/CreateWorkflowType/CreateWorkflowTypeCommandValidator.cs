using FluentValidation;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.CreateWorkflowType;

public class CreateWorkflowTypeCommandValidator : AbstractValidator<CreateWorkflowTypeCommand>
{
    public CreateWorkflowTypeCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
                .WithMessage("Choose an organization before you add a workflow type: the new type is granted to it.");
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty().MaximumLength(WorkflowType.ID_MAX_LENGTH);
        _ = RuleFor(x => x.Name).NotEmpty().MaximumLength(WorkflowType.NAME_MAX_LENGTH);
        _ = RuleFor(x => x.CounterpartyTypeIds).NotNull().WithName(CounterpartyTerms.SINGULAR + " types");
    }
}
