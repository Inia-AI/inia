using IniaAi.Core.Workflows.Dependencies;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Graph;

/// <param name="installedTypes">The block types this build runs. Null runs every type, as the paid build does.</param>
public class WorkflowGraphBuilder(InstalledBlockTypes? installedTypes = null) : IWorkflowGraphBuilder
{
    public WorkflowGraph Build(
        Dictionary<int, List<Block>> allBlocks,
        List<WorkflowVariable> variables,
        IReadOnlySet<string>? fenceBlockIds = null)
    {
        return WorkflowDependencyMap
            .ForDispatch(allBlocks, variables)
            .ToSchedulingGraph(fenceBlockIds, verificationGroupsRun: installedTypes?.IsUnavailable(BlockTypes.Verification) != true);
    }
}
