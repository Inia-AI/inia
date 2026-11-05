using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteWorkflowReport;

/// <summary>
/// Validator for <see cref="DeleteWorkflowReportCommand"/>.
/// </summary>
public class DeleteWorkflowReportCommandValidator : AbstractValidator<DeleteWorkflowReportCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteWorkflowReportCommandValidator"/> class.
    /// </summary>
    public DeleteWorkflowReportCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("OrganizationId is required.");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Workflow Report ID is required.");
    }
}
