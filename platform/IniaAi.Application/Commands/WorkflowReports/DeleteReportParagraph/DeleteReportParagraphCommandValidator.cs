using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteReportParagraph;

/// <summary>
/// Validator for <see cref="DeleteReportParagraphCommand"/>.
/// </summary>
public class DeleteReportParagraphCommandValidator : AbstractValidator<DeleteReportParagraphCommand>
{
    public DeleteReportParagraphCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("OrganizationId is required.");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        _ = RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Workflow execution ID is required.");

        _ = RuleFor(x => x.ParagraphId)
            .NotEmpty()
            .WithMessage("Report paragraph ID is required.");
    }
}
