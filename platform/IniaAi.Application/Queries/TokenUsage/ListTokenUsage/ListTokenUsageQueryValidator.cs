using FluentValidation;

namespace IniaAi.Application.Queries.TokenUsage.ListTokenUsage;

/// <summary>
/// Validator for the <see cref="ListTokenUsageQuery"/>.
/// </summary>
public class ListTokenUsageQueryValidator : AbstractValidator<ListTokenUsageQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListTokenUsageQueryValidator"/> class.
    /// </summary>
    public ListTokenUsageQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
    }
}
