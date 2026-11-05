using FluentValidation;

namespace IniaAi.Application.Queries.ReportFeedback.ListWorkflowTypeFeedback;

/// <summary>
/// Validator for <see cref="ListWorkflowTypeFeedbackQuery"/>.
/// </summary>
public class ListWorkflowTypeFeedbackQueryValidator : AbstractValidator<ListWorkflowTypeFeedbackQuery>
{
    public const int MAX_TAKE = 200;

    public ListWorkflowTypeFeedbackQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.Take).GreaterThan(0).LessThanOrEqualTo(MAX_TAKE);
        _ = RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);

        _ = RuleFor(x => x.Cursor)
            .Must(cursor => ReportFeedbackCursor.TryDecode(cursor, out _, out _))
            .When(x => !string.IsNullOrEmpty(x.Cursor))
            .WithMessage("Cursor is not a value this endpoint returned.");
    }
}
