using FluentValidation;

namespace IniaAi.Application.Commands.CounterpartyTypes.CreateCounterpartyType;

/// <summary>
/// Validates the <see cref="CreateCounterpartyTypeCommand"/>.
/// </summary>
public class CreateCounterpartyTypeCommandValidator : AbstractValidator<CreateCounterpartyTypeCommand>
{
    public CreateCounterpartyTypeCommandValidator()
    {
        Include(new CounterpartyTypeFieldsValidator());
    }
}
