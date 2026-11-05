using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowTemplates.DeleteDocumentClass;

public class DeleteDocumentClassCommandValidator : AbstractValidator<DeleteDocumentClassCommand>
{
    private const int KEY_MAX_LENGTH = 450;

    public DeleteDocumentClassCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.Key).NotEmpty().MaximumLength(KEY_MAX_LENGTH);
    }
}
