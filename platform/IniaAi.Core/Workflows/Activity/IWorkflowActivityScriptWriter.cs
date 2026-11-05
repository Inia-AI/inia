using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Core.Workflows.Activity;

/// <summary>
/// Writes a workflow template's activity script — the once-per-version cheap-model pass behind
/// the classic run's activity line. Fail-open by contract: nothing here ever fails a run — a
/// malformed grouping or a script with nothing worth showing is a decline, an error or timeout
/// a transient failure, and either way the run keeps today's progress-only surface.
/// </summary>
public interface IWorkflowActivityScriptWriter
{
    Task<WorkflowActivityScriptWriteResult> WriteScriptAsync(
        Workflow workflow,
        CancellationToken cancellationToken = default);
}
