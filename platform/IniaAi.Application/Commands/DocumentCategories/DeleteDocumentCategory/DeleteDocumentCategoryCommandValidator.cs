using FluentValidation;

namespace IniaAi.Application.Commands.DocumentCategories.DeleteDocumentCategory;

/// <summary>
/// Validates the DeleteDocumentCategoryCommand.
/// </summary>
public class DeleteDocumentCategoryCommandValidator : AbstractValidator<DeleteDocumentCategoryCommand>
{
    public DeleteDocumentCategoryCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Document category ID is required");
    }
}
