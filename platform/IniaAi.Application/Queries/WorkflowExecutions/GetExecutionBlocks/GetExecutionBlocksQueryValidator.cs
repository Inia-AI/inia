using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetExecutionBlocks;

public class GetExecutionBlocksQueryValidator : AbstractValidator<GetExecutionBlocksQuery>
{
    public GetExecutionBlocksQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.WorkflowExecutionId).NotEmpty();
    }
}
