using FluentValidation;
using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Application.Commands.CounterpartyTypes;

/// <summary>
/// The field rules the create and update validators share.
/// </summary>
public class CounterpartyTypeFieldsValidator : AbstractValidator<ICounterpartyTypeFields>
{
    public CounterpartyTypeFieldsValidator()
    {
        _ = RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required")
            .MaximumLength(CounterpartyType.NAME_MAX_LENGTH)
            .WithMessage($"Name must not exceed {CounterpartyType.NAME_MAX_LENGTH} characters");

        _ = RuleFor(x => x.Description)
            .MaximumLength(CounterpartyType.DESCRIPTION_MAX_LENGTH)
            .WithMessage($"Description must not exceed {CounterpartyType.DESCRIPTION_MAX_LENGTH} characters");
    }
}
