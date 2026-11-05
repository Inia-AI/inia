using FluentValidation;

namespace IniaAi.Application.Commands.ReportFeedback.DeleteReportFeedback;

/// <summary>
/// Validator for <see cref="DeleteReportFeedbackCommand"/>.
/// </summary>
public class DeleteReportFeedbackCommandValidator : AbstractValidator<DeleteReportFeedbackCommand>
{
    public DeleteReportFeedbackCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowExecutionId).NotEmpty();
        _ = RuleFor(x => x.FeedbackId).NotEmpty();
    }
}
