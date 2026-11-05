using FluentValidation;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;

public class ImportWorkflowTemplateApplyCommandValidator : AbstractValidator<ImportWorkflowTemplateApplyCommand>
{
    public ImportWorkflowTemplateApplyCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty();
        RuleFor(x => x.Bundle).NotNull();
        RuleFor(x => x.Bundle.TemplateJson).NotEmpty().When(x => x.Bundle is not null);
        RuleFor(x => x.Bundle.OriginWorkflowTypeId).NotEmpty().When(x => x.Bundle is not null);
        RuleFor(x => x.Bundle.WorkflowTypeName)
            .NotEmpty()
            .MaximumLength(WorkflowType.NAME_MAX_LENGTH)
            .When(x => x.Bundle is not null);
        RuleFor(x => x.Bundle.Components).NotNull().When(x => x.Bundle is not null);
        RuleFor(x => x.Bundle.Categories).NotNull().When(x => x.Bundle is not null);
        RuleFor(x => x.Bundle.TemplateHashTrail).NotNull().When(x => x.Bundle is not null);
        RuleForEach(x => x.ComponentDecisions).ChildRules(decision =>
        {
            decision.RuleFor(d => d.ComponentId).NotEmpty();
        });
    }
}
