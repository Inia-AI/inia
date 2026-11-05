using FluentValidation;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.Application.Commands.ReportFeedback.UpdateReportFeedbackStatus;

/// <summary>
/// Validator for <see cref="UpdateReportFeedbackStatusCommand"/>: a dismissal must say why.
/// </summary>
public class UpdateReportFeedbackStatusCommandValidator : AbstractValidator<UpdateReportFeedbackStatusCommand>
{
    public UpdateReportFeedbackStatusCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowExecutionId).NotEmpty();
        _ = RuleFor(x => x.FeedbackId).NotEmpty();
        _ = RuleFor(x => x.Request).NotNull();

        _ = When(x => x.Request is not null, () =>
        {
            _ = RuleFor(x => x.Request.Status).IsInEnum();

            _ = RuleFor(x => x.Request.DismissReason)
                .NotEmpty()
                .When(x => x.Request.Status == ReportFeedbackStatusDto.Dismissed)
                .WithMessage("A dismiss reason is required when dismissing feedback.");

            _ = RuleFor(x => x.Request.DismissReason)
                .MaximumLength(ReportFeedbackItem.MAX_DISMISS_REASON_LENGTH);
        });
    }
}
