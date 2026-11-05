using FluentValidation;

namespace IniaAi.Application.Queries.Documents.ListDocuments;

public class PaginatedResponseDocumentsQueryValidator : AbstractValidator<PaginatedResponseDocumentsQuery>
{
    public PaginatedResponseDocumentsQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}
