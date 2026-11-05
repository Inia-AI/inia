using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IniaAi.Core.Workflows.Scheduling;

/// <summary>
/// Single source of truth for the parallel-execution service registrations, so the three
/// composition roots (WebApi, WebApp, test builder) cannot drift. The cap, scheduler and block
/// writer are <b>Scoped</b> (one per workflow-execution scope) - that lifetime is what invariant 6
/// (per-workflow cap) and the per-block pooled-context writer depend on; registering them anywhere
/// as singletons would silently make the cap process-wide. <see cref="TryAdd"/> so a root that
/// already registers the graph builder (for GraphRAG) is not double-registered.
/// </summary>
public static class WorkflowSchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowScheduling(this IServiceCollection services)
    {
        services.TryAddTransient<IWorkflowGraphBuilder, WorkflowGraphBuilder>();
        services.TryAddScoped<IBlockConcurrencyCap, BlockConcurrencyCap>();
        services.TryAddScoped<LevelScheduler>();
        services.TryAddScoped<IExecutedBlockWriter, WorkflowBlockPersistence>();
        services.TryAddSingleton(serviceProvider => new InstalledBlockTypes(
            new BlockStrategies(serviceProvider.GetServices<BlockStrategyRegistration>()).ByType.Keys));
        return services;
    }
}
