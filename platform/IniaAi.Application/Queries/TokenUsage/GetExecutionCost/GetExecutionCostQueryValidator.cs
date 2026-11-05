using FluentValidation;

namespace IniaAi.Application.Queries.TokenUsage.GetExecutionCost;

public class GetExecutionCostQueryValidator : AbstractValidator<GetExecutionCostQuery>
{
    public GetExecutionCostQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.ExecutionId).NotEmpty();
    }
}
