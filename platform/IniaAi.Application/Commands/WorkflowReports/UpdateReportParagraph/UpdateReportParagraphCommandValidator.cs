using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowReports.UpdateReportParagraph;

/// <summary>
/// Validator for <see cref="UpdateReportParagraphCommand"/>.
/// </summary>
public class UpdateReportParagraphCommandValidator : AbstractValidator<UpdateReportParagraphCommand>
{
    public UpdateReportParagraphCommandValidator()
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
