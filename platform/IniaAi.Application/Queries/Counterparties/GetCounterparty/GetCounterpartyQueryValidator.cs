using FluentValidation;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Queries.Counterparties.GetCounterparty;

/// <summary>
/// Validator for the <see cref="GetCounterpartyQuery"/>.
/// </summary>
public class GetCounterpartyQueryValidator : AbstractValidator<GetCounterpartyQuery>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetCounterpartyQueryValidator"/> class.
    /// </summary>
    public GetCounterpartyQueryValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage(CounterpartyTerms.SINGULAR + " ID is required");

        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");
    }
}
