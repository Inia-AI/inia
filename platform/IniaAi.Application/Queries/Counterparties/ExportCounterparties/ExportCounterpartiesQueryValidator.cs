using FluentValidation;

namespace IniaAi.Application.Queries.Counterparties.ExportCounterparties;

public class ExportCounterpartiesQueryValidator : AbstractValidator<ExportCounterpartiesQuery>
{
    public ExportCounterpartiesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}
