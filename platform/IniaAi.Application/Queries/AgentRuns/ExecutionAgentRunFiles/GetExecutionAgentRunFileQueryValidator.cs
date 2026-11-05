using FluentValidation;
using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;

public class GetExecutionAgentRunFileQueryValidator : AbstractValidator<GetExecutionAgentRunFileQuery>
{
    private const int MAX_PATH_LENGTH = 450;

    public GetExecutionAgentRunFileQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.ExecutionId).NotEmpty();
        _ = RuleFor(x => x.BlockId).NotEmpty();
        _ = RuleFor(x => x.Path).NotEmpty().MaximumLength(MAX_PATH_LENGTH);
        _ = RuleFor(x => x.RunExecutionId)
            .MaximumLength(AgentRunIds.MAX_LENGTH)
            .When(x => x.RunExecutionId is not null);
        _ = RuleFor(x => x.Attempt)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Attempt.HasValue);
    }
}
