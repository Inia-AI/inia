using FluentValidation;

namespace IniaAi.Application.Commands.Documents.DeleteDocument;

public class DeleteDocumentCommandValidator : AbstractValidator<DeleteDocumentCommand>
{
    public DeleteDocumentCommandValidator()
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
    }
}
