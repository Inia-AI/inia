using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Graph;

public class GraphBuilderService : IGraphBuilderService
{
    private readonly IGraphRepository _repository;
    private readonly IWorkflowGraphBuilder _builder;
    private readonly IUserDataUnitOfWork _userDataUow;
    private readonly ILogger<GraphBuilderService> _logger;

    public GraphBuilderService(
        IGraphRepository repository,
        IWorkflowGraphBuilder builder,
        IUserDataUnitOfWork userDataUow,
        ILogger<GraphBuilderService> logger)
    {
        _repository = repository;
        _builder = builder;
        _userDataUow = userDataUow;
        _logger = logger;
    }

    public async Task<WorkflowGraph> GetOrBuildGraphAsync(string runId, CancellationToken cancellationToken = default)
    {
        WorkflowGraph? existingGraph = await _repository.GetGraphAsync(runId);

        if (existingGraph is not null)
        {
            _logger.LogInformation(
                "Graph found in cache for runId={RunId}, nodeCount={NodeCount}, edgeCount={EdgeCount}",
                LogSanitizer.ForLog(runId),
                existingGraph.Nodes.Count,
                existingGraph.Edges.Count);

            return existingGraph;
        }

        _logger.LogInformation("Building graph for runId={RunId}", LogSanitizer.ForLog(runId));

        WorkflowExecution? execution = await _userDataUow
            .WorkflowExecutions
            .GetByIdAsync(runId, cancellationToken);

        if (execution is null)
        {
            _logger.LogError("WorkflowExecution not found for runId={RunId}", LogSanitizer.ForLog(runId));

            throw new InvalidOperationException("WorkflowExecution not found");
        }

        if (string.IsNullOrEmpty(execution.WorkflowId))
        {
            _logger.LogError(
                "WorkflowExecution has null or empty WorkflowId for runId={RunId}",
                LogSanitizer.ForLog(runId));

            throw new InvalidOperationException($"WorkflowExecution '{runId}' has no WorkflowId");
        }

        Dictionary<int, List<Block>> sectionsBlocks = await _userDataUow
            .Workflows
            .GetAllBlocksByWorkflowIdAsync(execution.WorkflowId);

        List<WorkflowVariable> variables = await _userDataUow
            .Workflows
            .GetAllVariablesByWorkflowIdAsync(execution.WorkflowId);

        WorkflowGraph graph = _builder.Build(sectionsBlocks, variables);

        // Check if graph was created concurrently before attempting to save
        WorkflowGraph? concurrentGraph = await _repository.GetGraphAsync(runId);

        if (concurrentGraph is not null)
        {
            _logger.LogInformation(
                "Graph was created concurrently for runId={RunId}, returning existing graph",
                LogSanitizer.ForLog(runId));

            return concurrentGraph;
        }

        try
        {
            await _repository.SaveGraphAsync(runId, graph);
            _ = await _userDataUow.CompleteAsync(cancellationToken);

            _logger.LogInformation(
                "Graph successfully built and saved for runId={RunId}, nodeCount={NodeCount}, edgeCount={EdgeCount}",
                LogSanitizer.ForLog(runId),
                graph.Nodes.Count,
                graph.Edges.Count);

            return graph;
        }
        catch (Exception ex) when (IsDuplicateKeyException(ex))
        {
            // Rare race condition: graph was created between our check and save attempt
            _logger.LogWarning(
                "Duplicate key exception caught for runId={RunId} (race condition), retrieving concurrently created graph",
                LogSanitizer.ForLog(runId));

            WorkflowGraph? savedGraph = await _repository.GetGraphAsync(runId);

            if (savedGraph is not null)
            {
                _logger.LogInformation(
                    "Successfully retrieved concurrently created graph for runId={RunId}",
                    LogSanitizer.ForLog(runId));

                return savedGraph;
            }

            _logger.LogError(
                "Graph for runId={RunId} was created concurrently but is now missing",
                LogSanitizer.ForLog(runId));

            throw new InvalidOperationException($"Graph for runId '{runId}' was created concurrently but is now missing", ex);
        }
    }

    /// <summary>
    /// Determines if an exception is a unique constraint violation (PostgreSQL duplicate key error).
    /// </summary>
    private static bool IsDuplicateKeyException(Exception ex)
    {
        string message = ex.Message;
        return message.Contains("23505") ||
               message.Contains("duplicate key") ||
               message.Contains("already exists") ||
               (ex.InnerException is not null && IsDuplicateKeyException(ex.InnerException));
    }
}
