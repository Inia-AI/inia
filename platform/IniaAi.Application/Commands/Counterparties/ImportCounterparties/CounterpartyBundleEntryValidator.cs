using FluentValidation;
using IniaAi.Common.Constants;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Commands.Counterparties.ImportCounterparties;

/// <summary>
/// What one bundled row must satisfy to land: the field rules every counterparty write shares,
/// plus the id widths. A row with no type is allowed, unlike an API create: the bundle is a
/// copy of what the source holds, and the source can hold a counterparty without a type.
/// </summary>
public class CounterpartyBundleEntryValidator : AbstractValidator<CounterpartyBundleEntryDto>
{
    private const int ID_MAX_LENGTH = 450;
    private const int TYPE_NAME_MAX_LENGTH = 250;

    public CounterpartyBundleEntryValidator()
    {
        _ = RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.")
            .MaximumLength(ID_MAX_LENGTH);

        _ = RuleFor(x => x.ParentId)
            .MaximumLength(ID_MAX_LENGTH);

        _ = RuleForEach(x => x.CounterpartyTypeNames)
            .NotEmpty().WithMessage(CounterpartyTerms.SINGULAR + " type name must not be empty.")
            .MaximumLength(TYPE_NAME_MAX_LENGTH)
            .WithName(CounterpartyTerms.SINGULAR + " type name");

        Include(new CounterpartyFieldsValidator());
    }
}
