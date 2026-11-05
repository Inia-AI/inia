using FluentValidation;
using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Application.Queries.AgentRuns.GetAgentRunActivity;

public class GetAgentRunActivityQueryValidator : AbstractValidator<GetAgentRunActivityQuery>
{
    public GetAgentRunActivityQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.BlockId).NotEmpty();
        _ = RuleFor(x => x.Round)
            .GreaterThanOrEqualTo(AgentRunIds.FIRST_ROUND)
            .When(x => x.Round.HasValue);
    }
}
