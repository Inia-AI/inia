using FluentValidation;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecutionLogPlaceholders;

public class GetWorkflowExecutionLogPlaceholdersQueryValidator : AbstractValidator<GetWorkflowExecutionLogPlaceholdersQuery>
{
    public GetWorkflowExecutionLogPlaceholdersQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowExecutionId).NotEmpty();
        _ = RuleFor(x => x.LogId).NotEmpty();
    }
}
