using FluentValidation.TestHelper;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats.Markdown;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.ExportWorkflowExecution;

public class ExportWorkflowExecutionCommandValidatorTests
{
    private readonly ExportWorkflowExecutionCommandValidator _validator = new([new MarkdownReportWriter()]);

    [Fact]
    public void Validate_FormatWithAWriter_Passes()
    {
        TestValidationResult<ExportWorkflowExecutionCommand> result = _validator.TestValidate(Command(ExportFormat.Markdown));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(ExportFormat.Word, "There is no Word export.")]
    [InlineData(ExportFormat.Pdf, "There is no Pdf export.")]
    public void Validate_FormatWithoutAWriter_IsRefusedByName(ExportFormat format, string message)
    {
        TestValidationResult<ExportWorkflowExecutionCommand> result = _validator.TestValidate(Command(format));

        _ = result.ShouldHaveValidationErrorFor(command => command.Format)
            .WithErrorMessage(message);
    }

    private static ExportWorkflowExecutionCommand Command(ExportFormat format)
    {
        return new ExportWorkflowExecutionCommand("execution-1", format, "user-1", "org-1");
    }
}
