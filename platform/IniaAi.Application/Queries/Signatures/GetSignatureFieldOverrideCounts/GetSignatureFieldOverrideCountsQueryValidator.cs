using FluentValidation;

namespace IniaAi.Application.Queries.Signatures.GetSignatureFieldOverrideCounts;

public class GetSignatureFieldOverrideCountsQueryValidator
    : AbstractValidator<GetSignatureFieldOverrideCountsQuery>
{
    public GetSignatureFieldOverrideCountsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty().MaximumLength(200);
    }
}
