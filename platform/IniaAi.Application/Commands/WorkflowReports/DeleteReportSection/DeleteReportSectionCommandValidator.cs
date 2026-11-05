using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteReportSection;

/// <summary>
/// Validator for <see cref="DeleteReportSectionCommand"/>.
/// </summary>
public class DeleteReportSectionCommandValidator : AbstractValidator<DeleteReportSectionCommand>
{
    public DeleteReportSectionCommandValidator()
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

        _ = RuleFor(x => x.SectionId)
            .NotEmpty()
            .WithMessage("Report section ID is required.");
    }
}
