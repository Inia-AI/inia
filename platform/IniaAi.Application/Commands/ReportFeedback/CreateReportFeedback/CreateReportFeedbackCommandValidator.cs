using FluentValidation;
using IniaAi.Domain.Entities.ReportFeedback;

namespace IniaAi.Application.Commands.ReportFeedback.CreateReportFeedback;

/// <summary>
/// Validator for <see cref="CreateReportFeedbackCommand"/>.
/// </summary>
public class CreateReportFeedbackCommandValidator : AbstractValidator<CreateReportFeedbackCommand>
{
    public CreateReportFeedbackCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowExecutionId).NotEmpty();
        _ = RuleFor(x => x.Request).NotNull();

        _ = When(x => x.Request is not null, () =>
        {
            _ = RuleFor(x => x.Request.Category).IsInEnum();

            _ = RuleFor(x => x.Request.Comment)
                .NotEmpty()
                .MaximumLength(ReportFeedbackItem.MAX_COMMENT_LENGTH);

            _ = RuleFor(x => x.Request.QuotedText)
                .MaximumLength(ReportFeedbackItem.MAX_QUOTED_TEXT_LENGTH);
        });
    }
}
