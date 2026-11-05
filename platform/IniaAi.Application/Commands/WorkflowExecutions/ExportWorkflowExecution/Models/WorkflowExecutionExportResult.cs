using IniaAi.Common.Content;

namespace IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution.Models;

/// <summary>
/// Represents the result of a workflow execution export operation.
/// </summary>
public class WorkflowExecutionExportResult
{
    /// <summary>
    /// Gets or sets the binary content of the exported document.
    /// </summary>
    public BinaryContent Content { get; set; } = null!;

    /// <summary>
    /// Gets or sets the suggested filename for the exported document.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME content type of the exported document.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ID of the exported workflow execution.
    /// </summary>
    public string WorkflowExecutionId { get; set; } = string.Empty;
}
