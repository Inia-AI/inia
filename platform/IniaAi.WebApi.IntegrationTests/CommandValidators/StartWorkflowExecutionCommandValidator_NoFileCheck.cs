using FluentValidation;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;

namespace IniaAi.WebApi.IntegrationTests.CommandValidators;

public class StartWorkflowExecutionCommandValidator_NoFileCheck : AbstractValidator<StartWorkflowExecutionCommand>
{
    public StartWorkflowExecutionCommandValidator_NoFileCheck()
    {
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.Request).NotNull();

        _ = When(x => x.Request is not null, () =>
        {
            _ = RuleFor(x => x.Request.WorkflowId).NotEmpty();
            // Rule omitted intentionally:
            // No file or reference URL requirement
        });
    }
}
