namespace IniaAi.Core.Workflows.Graph;

public interface IGraphBuilderService
{
    Task<WorkflowGraph> GetOrBuildGraphAsync(string runId, CancellationToken cancellationToken = default);
}
