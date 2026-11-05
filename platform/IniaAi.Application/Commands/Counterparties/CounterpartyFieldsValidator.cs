using FluentValidation;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Commands.Counterparties;

/// <summary>
/// The field rules every counterparty write shares. The create and update command validators
/// include it, and the bundle import applies it to each row, so the API and the bundle cannot
/// drift on what a valid counterparty is.
/// </summary>
public class CounterpartyFieldsValidator : AbstractValidator<ICounterpartyFields>
{
    /// <summary>
    /// Upper bound for <c>LastFinancialAnalysisResult</c>. The persisted column is unbounded
    /// (<c>nvarchar(max)</c>); this cap only guards against pathological payload sizes while
    /// comfortably allowing long generated analysis text.
    /// </summary>
    public const int LAST_FINANCIAL_ANALYSIS_RESULT_MAX_LENGTH = 1_000_000;

    public CounterpartyFieldsValidator()
    {
        _ = RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(250)
            .Must(CounterpartyNameRules.IsValid)
            .WithMessage($"Name {CounterpartyNameRules.FORBIDDEN_CHARACTERS_MESSAGE}.");

        _ = RuleFor(x => x.NameVariants)
            .MaximumLength(500)
            .Must(CounterpartyNameRules.IsValidNameVariants)
            .WithMessage($"NameVariants {CounterpartyNameRules.NAME_VARIANTS_FORBIDDEN_CHARACTERS_MESSAGE}.");

        _ = RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .MaximumLength(100);

        _ = RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[0-9\s\-]+$").When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .MaximumLength(20);

        _ = RuleFor(x => x.Website)
            .MaximumLength(1000)
            .Must(BeAValidUri).When(x => !string.IsNullOrWhiteSpace(x.Website))
            .WithMessage("Website must be a valid URL.");

        _ = RuleFor(x => x.Address)
            .MaximumLength(1000);

        _ = RuleFor(x => x.LastFinancialAnalysisResult)
            .MaximumLength(LAST_FINANCIAL_ANALYSIS_RESULT_MAX_LENGTH);

        _ = RuleForEach(x => x.Properties)
            .Must(pair => pair.Key.Length <= CounterpartyPropertiesRules.KEY_MAX_LENGTH)
            .WithMessage($"Property keys must be at most {CounterpartyPropertiesRules.KEY_MAX_LENGTH} characters.")
            .Must(pair => (pair.Value?.Length ?? 0) <= CounterpartyPropertiesRules.VALUE_MAX_LENGTH)
            .WithMessage($"Property values must be at most {CounterpartyPropertiesRules.VALUE_MAX_LENGTH} characters.")
            .When(x => x.Properties is not null);
    }

    private static bool BeAValidUri(string? website)
    {
        return Uri.TryCreate(website, UriKind.Absolute, out Uri? uriResult)
            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}
