using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowTemplates.UpdateDocumentClass;

public class UpdateDocumentClassCommandValidator : AbstractValidator<UpdateDocumentClassCommand>
{
    private const int KEY_MAX_LENGTH = 450;
    private const int DISPLAY_NAME_MAX_LENGTH = 1000;

    public UpdateDocumentClassCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.Key).NotEmpty().MaximumLength(KEY_MAX_LENGTH);
        _ = RuleFor(x => x.DisplayName!)
            .NotEmpty()
            .MaximumLength(DISPLAY_NAME_MAX_LENGTH)
            .When(x => x.DisplayName is not null);
        _ = RuleFor(x => x.Requirement).IsInEnum().When(x => x.Requirement.HasValue);
    }
}
