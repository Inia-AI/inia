using FluentValidation;

namespace IniaAi.Application.Commands.DocumentCategories.CreateDocumentCategory;

/// <summary>
/// Validates the CreateDocumentCategoryCommand.
/// </summary>
public class CreateDocumentCategoryCommandValidator : AbstractValidator<CreateDocumentCategoryCommand>
{
    public CreateDocumentCategoryCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        _ = RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required")
            .MaximumLength(200)
            .WithMessage("Name must not exceed 200 characters");

        _ = RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .When(x => x.DisplayOrder.HasValue)
            .WithMessage("Display order must be a non-negative number");
    }
}
