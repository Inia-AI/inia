using FluentValidation;

namespace IniaAi.Application.Commands.WorkflowReports.UpdateReportSection;

/// <summary>
/// Validator for <see cref="UpdateReportSectionCommand"/>.
/// </summary>
public class UpdateReportSectionCommandValidator : AbstractValidator<UpdateReportSectionCommand>
{
    public UpdateReportSectionCommandValidator()
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
