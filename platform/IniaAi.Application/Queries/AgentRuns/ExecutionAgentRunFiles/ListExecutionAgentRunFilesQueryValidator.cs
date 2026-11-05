using FluentValidation;

namespace IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;

public class ListExecutionAgentRunFilesQueryValidator : AbstractValidator<ListExecutionAgentRunFilesQuery>
{
    public ListExecutionAgentRunFilesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.ExecutionId).NotEmpty();
    }
}
