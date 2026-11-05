using FluentValidation;

namespace IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;

/// <summary>
/// Validator for <see cref="DownloadExecutedWorkflowQuery"/>. Requires a non-empty
/// organization and user id plus exactly one of executionId, workflowId, or jobId.
/// </summary>
public class DownloadExecutedWorkflowQueryValidator : AbstractValidator<DownloadExecutedWorkflowQuery>
{
    public DownloadExecutedWorkflowQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("Organization ID is required.");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        _ = RuleFor(x => x)
            .Must(HaveExactlyOneIdentifier)
            .WithMessage("Provide exactly one of executionId, workflowId, or jobId.");
    }

    private static bool HaveExactlyOneIdentifier(DownloadExecutedWorkflowQuery query)
    {
        int provided = 0;
        if (!string.IsNullOrWhiteSpace(query.ExecutionId))
        {
            provided++;
        }

        if (!string.IsNullOrWhiteSpace(query.WorkflowId))
        {
            provided++;
        }

        if (!string.IsNullOrWhiteSpace(query.JobId))
        {
            provided++;
        }

        return provided == 1;
    }
}
