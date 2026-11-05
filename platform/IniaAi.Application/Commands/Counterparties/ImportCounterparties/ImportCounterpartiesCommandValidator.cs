using FluentValidation;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Commands.Counterparties.ImportCounterparties;

/// <summary>
/// The shape of the request only. Each bundled row is judged by the handler, which reports a
/// bad row and lands the rest, so no row rule belongs here.
/// </summary>
public class ImportCounterpartiesCommandValidator : AbstractValidator<ImportCounterpartiesCommand>
{
    public ImportCounterpartiesCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Bundle).NotNull();

        When(x => x.Bundle is not null, () =>
        {
            _ = RuleFor(x => x.Bundle.Counterparties).NotNull().WithName(CounterpartyTerms.PLURAL);
            _ = RuleFor(x => x.Bundle.CounterpartyTypes).NotNull().WithName(CounterpartyTerms.SINGULAR + " types");
        });
    }
}
