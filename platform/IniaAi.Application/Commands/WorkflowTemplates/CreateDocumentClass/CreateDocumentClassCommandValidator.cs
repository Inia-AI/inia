using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowTemplates.CreateDocumentClass;

public class CreateDocumentClassCommandValidator : AbstractValidator<CreateDocumentClassCommand>
{
    private const int KEY_MAX_LENGTH = 450;
    private const int DISPLAY_NAME_MAX_LENGTH = 1000;

    public CreateDocumentClassCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.Key).NotEmpty().MaximumLength(KEY_MAX_LENGTH);
        _ = RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(DISPLAY_NAME_MAX_LENGTH);
        _ = RuleFor(x => x.Requirement).IsInEnum();
    }
}
