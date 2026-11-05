using FluentValidation;

namespace IniaAi.Application.Queries.Counterparties.ListCounterparties;

/// <summary>
/// Validator for the <see cref="ListCounterpartiesQuery"/>.
/// </summary>
public class ListCounterpartiesQueryValidator : AbstractValidator<ListCounterpartiesQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListCounterpartiesQueryValidator"/> class.
    /// </summary>
    public ListCounterpartiesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");
    }
}
