using FluentValidation;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Commands.Counterparties.UpdateCounterparty;

/// <summary>
/// Validates the <see cref="UpdateCounterpartyCommand"/>.
/// </summary>
public class UpdateCounterpartyCommandValidator : AbstractValidator<UpdateCounterpartyCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateCounterpartyCommandValidator"/> class.
    /// </summary>
    public UpdateCounterpartyCommandValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.");

        _ = RuleFor(x => x.OrganizationId)
            .NotNull().NotEmpty().WithMessage("OrganizationId is required.");

        _ = RuleFor(x => x.CounterpartyTypeIds)
            .NotEmpty().WithMessage($"At least one {CounterpartyTerms.SINGULAR_LOWER} type is required.");

        _ = RuleForEach(x => x.CounterpartyTypeIds)
            .NotEmpty().WithMessage(CounterpartyTerms.SINGULAR + " type ID must not be empty.");

        Include(new CounterpartyFieldsValidator());
    }
}
