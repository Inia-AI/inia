using FluentValidation;

namespace IniaAi.Application.Commands.Documents.BulkDeleteDocuments;

public class BulkDeleteDocumentsCommandValidator : AbstractValidator<BulkDeleteDocumentsCommand>
{
    private const int MAX_IDS_PER_REQUEST = 500;

    public BulkDeleteDocumentsCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        _ = RuleFor(x => x.DocumentIds)
            .NotNull()
            .WithMessage("Document IDs are required");

        _ = RuleFor(x => x.DocumentIds)
            .Must(ids => ids != null && ids.Count > 0)
            .WithMessage("At least one document ID must be supplied");

        _ = RuleFor(x => x.DocumentIds)
            .Must(ids => ids == null || ids.Count <= MAX_IDS_PER_REQUEST)
            .WithMessage($"No more than {MAX_IDS_PER_REQUEST} IDs may be deleted per request");

        _ = RuleForEach(x => x.DocumentIds)
            .NotEmpty()
            .WithMessage("Document IDs may not be empty or whitespace");
    }
}
