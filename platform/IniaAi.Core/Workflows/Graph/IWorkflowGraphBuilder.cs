using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Graph;

public interface IWorkflowGraphBuilder
{
    /// <param name="fenceBlockIds">
    /// Blocks whose handler side effects (SharedInputs flags, CounterpartyId, AllPagesTopics) are
    /// invisible to placeholder analysis. Each is pinned at its authored position: edges from every
    /// earlier-authored block into it, and from it to every later-authored block.
    /// </param>
    WorkflowGraph Build(
        Dictionary<int, List<Block>> allBlocks,
        List<WorkflowVariable> variables,
        IReadOnlySet<string>? fenceBlockIds = null);
}
