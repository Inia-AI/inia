using FluentValidation;

namespace IniaAi.Application.Queries.DocumentCategories.ListDocumentCategories;

/// <summary>
/// Validator for <see cref="ListDocumentCategoriesQuery"/>.
/// </summary>
public class ListDocumentCategoriesQueryValidator : AbstractValidator<ListDocumentCategoriesQuery>
{
    public ListDocumentCategoriesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}
