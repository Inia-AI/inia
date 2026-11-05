using FluentValidation;

namespace IniaAi.Application.Commands.Documents.UpdateDocument;

public class UpdateDocumentCommandValidator : AbstractValidator<UpdateDocumentCommand>
{
    private const int NAME_MAX_LENGTH = 250;
    private const int DESCRIPTION_MAX_LENGTH = 1000;

    public UpdateDocumentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        _ = RuleFor(x => x.DocumentId)
            .NotEmpty()
            .WithMessage("Document ID is required");

        _ = RuleFor(x => x.Name)
            .MaximumLength(NAME_MAX_LENGTH)
            .When(x => !string.IsNullOrWhiteSpace(x.Name))
            .WithMessage($"Name must not exceed {NAME_MAX_LENGTH} characters");

        _ = RuleFor(x => x.Description)
            .MaximumLength(DESCRIPTION_MAX_LENGTH)
            .When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage($"Description must not exceed {DESCRIPTION_MAX_LENGTH} characters");

        _ = RuleFor(x => x.WorkflowTypeId)
            .Must(workflowTypeId => !string.IsNullOrWhiteSpace(workflowTypeId))
            .When(x => x.IsStatic == true)
            .WithMessage("A static document needs a workflow type");

        _ = RuleFor(x => x.WorkflowTypeId)
            .Must(string.IsNullOrWhiteSpace)
            .When(x => x.IsStatic == false)
            .WithMessage("A document that is not static has no workflow type");

        _ = RuleFor(x => x.Class)
            .Must(documentClass => !string.IsNullOrWhiteSpace(documentClass))
            .When(x => x.IsStatic == true)
            .WithMessage("A static document needs a document class");

        _ = RuleFor(x => x.Class)
            .Must(string.IsNullOrWhiteSpace)
            .When(x => x.IsStatic == false)
            .WithMessage("A document that is not static has no document class");
    }
}
