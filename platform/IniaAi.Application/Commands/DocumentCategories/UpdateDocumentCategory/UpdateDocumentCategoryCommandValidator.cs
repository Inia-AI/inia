using FluentValidation;

namespace IniaAi.Application.Commands.DocumentCategories.UpdateDocumentCategory;

/// <summary>
/// Validates the UpdateDocumentCategoryCommand.
/// </summary>
public class UpdateDocumentCategoryCommandValidator : AbstractValidator<UpdateDocumentCategoryCommand>
{
    public UpdateDocumentCategoryCommandValidator()
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

        _ = RuleFor(x => x.Name)
            .MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.Name))
            .WithMessage("Name must not exceed 200 characters");

        _ = RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .When(x => x.DisplayOrder.HasValue)
            .WithMessage("Display order must be a non-negative number");
    }
}
