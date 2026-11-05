using FluentValidation;

namespace IniaAi.Application.Queries.AgentRuns.GetAgentRunTraceFiles;

public class GetAgentRunTraceFilesQueryValidator : AbstractValidator<GetAgentRunTraceFilesQuery>
{
    public GetAgentRunTraceFilesQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.BlockId).NotEmpty();
    }
}
