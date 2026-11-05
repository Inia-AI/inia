using IniaAi.Common.Content;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Services.Export.Models;

namespace IniaAi.Application.Common.Interfaces.Services;

public interface IExportService
{
    /// <summary>Exports the report, with the notice of the report's own organization at its foot.</summary>
    Task<BinaryContent> ExportAsync(WorkflowReport report, ExportFormat format);

    /// <summary>Exports a run's stored report, with the notice of <paramref name="organizationId"/>, the run's organization as the caller checked it, at its foot.</summary>
    Task<WorkflowOutputExportResult> ExportByIdAsync(string workflowExecutionId, string organizationId, ExportFormat format);
}
