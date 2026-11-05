using FluentValidation;

namespace IniaAi.Application.Commands.CounterpartyTypes.DeleteCounterpartyType;

/// <summary>
/// Validates the <see cref="DeleteCounterpartyTypeCommand"/>.
/// </summary>
public class DeleteCounterpartyTypeCommandValidator : AbstractValidator<DeleteCounterpartyTypeCommand>
{
    public DeleteCounterpartyTypeCommandValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage(CounterpartyTypeRules.ID_REQUIRED);
    }
}
