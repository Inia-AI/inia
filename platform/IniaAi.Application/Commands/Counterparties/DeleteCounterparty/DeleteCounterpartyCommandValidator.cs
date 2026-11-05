using FluentValidation;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Commands.Counterparties.DeleteCounterparty;

/// <summary>
/// Validator for the <see cref="DeleteCounterpartyCommand"/>.
/// </summary>
public class DeleteCounterpartyCommandValidator : AbstractValidator<DeleteCounterpartyCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteCounterpartyCommandValidator"/> class.
    /// </summary>
    public DeleteCounterpartyCommandValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage(CounterpartyTerms.SINGULAR + " ID is required");

        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required");
    }
}
