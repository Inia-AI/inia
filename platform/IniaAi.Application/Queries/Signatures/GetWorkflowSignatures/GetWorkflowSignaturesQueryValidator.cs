using FluentValidation;

namespace IniaAi.Application.Queries.Signatures.GetWorkflowSignatures;

public class GetWorkflowSignaturesQueryValidator : AbstractValidator<GetWorkflowSignaturesQuery>
{
    public GetWorkflowSignaturesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty().MaximumLength(250);
    }
}
