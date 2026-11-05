using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution.Models;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Enums;

namespace IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution;

/// <summary>
/// Command to export a workflow execution in the specified format.
/// </summary>
public class ExportWorkflowExecutionCommand : IRequest<ErrorOr<WorkflowExecutionExportResult>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportWorkflowExecutionCommand"/> class.
    /// </summary>
    /// <param name="workflowExecutionId">The ID of the workflow execution to export.</param>
    /// <param name="format">The export format (Word, PDF, etc.).</param>
    /// <param name="userId">The ID of the user requesting the export.</param>
    /// <param name="organizationId">The ID of the user's organization.</param>
    public ExportWorkflowExecutionCommand(
        string workflowExecutionId,
        ExportFormat format,
        string userId,
        string organizationId)
    {
        WorkflowExecutionId = workflowExecutionId;
        Format = format;
        UserId = userId;
        OrganizationId = organizationId;
    }

    /// <summary>
    /// Gets the ID of the workflow execution to export.
    /// </summary>
    public string WorkflowExecutionId { get; }

    /// <summary>
    /// Gets the export format.
    /// </summary>
    public ExportFormat Format { get; }

    /// <summary>
    /// Gets the ID of the user requesting the export.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets the ID of the user's organization.
    /// </summary>
    public string OrganizationId { get; }
}
