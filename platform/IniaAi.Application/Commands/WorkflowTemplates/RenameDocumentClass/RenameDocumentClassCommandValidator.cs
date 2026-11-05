using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowTemplates.RenameDocumentClass;

public class RenameDocumentClassCommandValidator : AbstractValidator<RenameDocumentClassCommand>
{
    private const int KEY_MAX_LENGTH = 450;

    public RenameDocumentClassCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.Key).NotEmpty().MaximumLength(KEY_MAX_LENGTH);
        _ = RuleFor(x => x.NewKey).NotEmpty().MaximumLength(KEY_MAX_LENGTH);
    }
}
