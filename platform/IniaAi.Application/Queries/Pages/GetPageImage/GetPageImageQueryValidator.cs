using FluentValidation;

namespace IniaAi.Application.Queries.Pages.GetPageImage;

/// <summary>
/// Validator for <see cref="GetPageImageQuery"/>.
/// </summary>
public class GetPageImageQueryValidator : AbstractValidator<GetPageImageQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetPageImageQueryValidator"/> class.
    /// </summary>
    public GetPageImageQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.PageId).NotEmpty();
    }
}
