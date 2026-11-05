using FluentValidation;

namespace IniaAi.Application.Queries.ReportFeedback.ListReportFeedback;

/// <summary>
/// Validator for <see cref="ListReportFeedbackQuery"/>.
/// </summary>
public class ListReportFeedbackQueryValidator : AbstractValidator<ListReportFeedbackQuery>
{
    public ListReportFeedbackQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowExecutionId).NotEmpty();
    }
}
