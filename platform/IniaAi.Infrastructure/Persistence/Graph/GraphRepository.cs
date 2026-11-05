using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Graph;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.Graph;

public class GraphRepository : IGraphRepository
{
    private readonly UserDataDbContext _context;

    public GraphRepository(UserDataDbContext context)
    {
        _context = context;
    }

    public async Task SaveGraphAsync(string runId, WorkflowGraph graph)
    {
        // Map WorkflowGraphNode (DTO) -> GraphNode (Entity)
        var nodeEntities = graph.Nodes.Select(n => new GraphNode
        {
            RunId = runId,
            Key = n.Id,
            Kind = n.Kind,
            Label = n.Label,
            Order = n.Order,
            DocumentClass = n.DocumentClass,
            ReplacementTag = n.ReplacementTag
        }).ToList();

        // Map WorkflowGraphEdge (DTO) -> GraphEdge (Entity)
        var edgeEntities = graph.Edges.Select(e => new GraphEdge
        {
            RunId = runId,
            SourceId = e.SourceId,
            TargetId = e.TargetId,
            Label = e.Label
        }).ToList();

        await _context.GraphNodes.AddRangeAsync(nodeEntities);
        await _context.GraphEdges.AddRangeAsync(edgeEntities);
    }

    public async Task<WorkflowGraph?> GetGraphAsync(string runId)
    {
        // Query all nodes for this runId
        List<GraphNode> nodeEntities = await _context.GraphNodes
            .Where(n => n.RunId == runId)
            .ToListAsync();

        // If no nodes found, graph doesn't exist
        if (nodeEntities.Count == 0)
        {
            return null;
        }

        // Query all edges for this runId
        List<GraphEdge> edgeEntities = await _context.GraphEdges
            .Where(e => e.RunId == runId)
            .ToListAsync();

        // Map GraphNode (Entity) -> WorkflowGraphNode (DTO)
        var nodes = nodeEntities.Select(n => new WorkflowGraphNode
        {
            Id = n.Key,
            Kind = n.Kind,
            Label = n.Label,
            Order = n.Order,
            DocumentClass = n.DocumentClass,
            ReplacementTag = n.ReplacementTag
        }).ToList();

        // Map GraphEdge (Entity) -> WorkflowGraphEdge (DTO)
        var edges = edgeEntities.Select(e => new WorkflowGraphEdge
        {
            SourceId = e.SourceId,
            TargetId = e.TargetId,
            Label = e.Label
        }).ToList();

        // Reconstruct WorkflowGraph DTO
        return new WorkflowGraph
        {
            Nodes = nodes,
            Edges = edges
        };
    }

    public async Task<bool> ExistsAsync(string runId)
    {
        return await _context.GraphNodes.AnyAsync(n => n.RunId == runId);
    }

    public async Task<List<GraphNode>> GetNodesAsync(string runId)
    {
        return await _context.GraphNodes
            .Where(n => n.RunId == runId)
            .ToListAsync();
    }

    public async Task<List<GraphEdge>> GetEdgesAsync(string runId)
    {
        return await _context.GraphEdges
            .Where(e => e.RunId == runId)
            .ToListAsync();
    }

    public async Task DeleteGraphAsync(string runId)
    {
        // Delete nodes (cascade will handle edges due to FK constraints, but we'll be explicit)
        List<GraphNode> nodes = await _context.GraphNodes
            .Where(n => n.RunId == runId)
            .ToListAsync();

        List<GraphEdge> edges = await _context.GraphEdges
            .Where(e => e.RunId == runId)
            .ToListAsync();

        _context.GraphEdges.RemoveRange(edges);
        _context.GraphNodes.RemoveRange(nodes);

        _ = await _context.SaveChangesAsync();
    }
}
