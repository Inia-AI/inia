using FluentValidation;
using IniaAi.FileFormats.Word;

namespace IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution;

/// <summary>
/// Validator for the <see cref="ExportWorkflowExecutionCommand"/>.
/// </summary>
public class ExportWorkflowExecutionCommandValidator : AbstractValidator<ExportWorkflowExecutionCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportWorkflowExecutionCommandValidator"/> class.
    /// </summary>
    /// <param name="writers">The report file writers this build registers; a format without one is refused.</param>
    public ExportWorkflowExecutionCommandValidator(IEnumerable<IReportFileWriter> writers)
    {
        _ = RuleFor(x => x.WorkflowExecutionId)
            .NotEmpty()
            .WithMessage("WorkflowExecutionId is required.");

        _ = RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        _ = RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("OrganizationId is required.");

        _ = RuleFor(x => x.Format)
            .Must(ReportFileFormats.ByFormat(writers).ContainsKey)
            .WithMessage(command => ReportFileFormats.Unavailable(command.Format));
    }
}
