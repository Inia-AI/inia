using FluentValidation;

namespace IniaAi.Application.Commands.CounterpartyTypes.UpdateCounterpartyType;

/// <summary>
/// Validates the <see cref="UpdateCounterpartyTypeCommand"/>.
/// </summary>
public class UpdateCounterpartyTypeCommandValidator : AbstractValidator<UpdateCounterpartyTypeCommand>
{
    public UpdateCounterpartyTypeCommandValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage(CounterpartyTypeRules.ID_REQUIRED);

        Include(new CounterpartyTypeFieldsValidator());
    }
}
