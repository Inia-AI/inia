using FluentValidation;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Application.Commands.Documents.CreateTextDocument;

public class CreateTextDocumentCommandValidator : AbstractValidator<CreateTextDocumentCommand>
{
    public CreateTextDocumentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Name).NotEmpty().MaximumLength(Document.MAX_NAME_LENGTH);
        _ = RuleFor(x => x.Text).NotEmpty().MaximumLength(Document.MAX_TEXT_LENGTH);
        _ = RuleFor(x => x.Format).IsInEnum();
    }
}
