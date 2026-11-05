using FluentValidation;

namespace IniaAi.Application.Commands.Documents.ProcessDocument;

public class ProcessDocumentCommandValidator : AbstractValidator<ProcessDocumentCommand>
{
    public ProcessDocumentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.DocumentId).NotEmpty().MaximumLength(450);
    }
}
