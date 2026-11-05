using IniaAi.Domain.Graph;

namespace IniaAi.Core.Workflows.Graph;

public interface IGraphRepository
{
    Task SaveGraphAsync(string runId, WorkflowGraph graph);
    Task<WorkflowGraph?> GetGraphAsync(string runId);
    Task<bool> ExistsAsync(string runId);
    Task DeleteGraphAsync(string runId);

    // Query methods for BFS traversal
    Task<List<GraphNode>> GetNodesAsync(string runId);
    Task<List<GraphEdge>> GetEdgesAsync(string runId);
}
