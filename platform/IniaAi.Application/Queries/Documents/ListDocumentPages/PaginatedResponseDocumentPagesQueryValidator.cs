using FluentValidation;

namespace IniaAi.Application.Queries.Documents.ListDocumentPages;

public class PaginatedResponseDocumentPagesQueryValidator : AbstractValidator<PaginatedResponseDocumentPagesQuery>
{
    public PaginatedResponseDocumentPagesQueryValidator()
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
