using FluentValidation;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Commands.Counterparties.CreateCounterparty;

/// <summary>
/// Validates the <see cref="CreateCounterpartyCommand"/>.
/// </summary>
public class CreateCounterpartyCommandValidator : AbstractValidator<CreateCounterpartyCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateCounterpartyCommandValidator"/> class.
    /// </summary>
    public CreateCounterpartyCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotNull().NotEmpty().WithMessage("OrganizationId is required.");

        _ = RuleFor(x => x.CounterpartyTypeIds)
            .NotEmpty().WithMessage($"At least one {CounterpartyTerms.SINGULAR_LOWER} type is required.");

        _ = RuleForEach(x => x.CounterpartyTypeIds)
            .NotEmpty().WithMessage(CounterpartyTerms.SINGULAR + " type ID must not be empty.");

        Include(new CounterpartyFieldsValidator());
    }
}
