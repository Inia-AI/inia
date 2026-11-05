using FluentValidation;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;

/// <summary>
/// Validates <see cref="SaveWorkflowTemplateContentCommand"/>. The rules here are mirrored by
/// <c>ValidateWorkflowTemplateContentQueryValidator</c> so the rehearsal endpoint rejects exactly
/// what this accepts or refuses; a rule added here needs the same rule there.
/// </summary>
public class SaveWorkflowTemplateContentCommandValidator : AbstractValidator<SaveWorkflowTemplateContentCommand>
{
    public SaveWorkflowTemplateContentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty().MaximumLength(WorkflowType.ID_MAX_LENGTH);
        _ = RuleFor(x => x.WorkflowJson).NotEmpty();
        _ = RuleFor(x => x.NewTypeName)
            .MaximumLength(WorkflowType.NAME_MAX_LENGTH)
            .When(x => !string.IsNullOrEmpty(x.NewTypeName));
    }
}
