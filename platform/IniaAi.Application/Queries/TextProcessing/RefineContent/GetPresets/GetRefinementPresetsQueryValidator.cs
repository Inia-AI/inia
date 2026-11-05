using FluentValidation;

namespace IniaAi.Application.Queries.TextProcessing.RefineContent.GetPresets;

public class GetRefinementPresetsQueryValidator : AbstractValidator<GetRefinementPresetsQuery>
{
    public GetRefinementPresetsQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}
