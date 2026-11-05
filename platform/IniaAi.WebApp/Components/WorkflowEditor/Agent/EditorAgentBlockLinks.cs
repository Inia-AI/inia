using IniaAi.Domain.Entities.Workflows;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>
/// The agent links every block it names as <c>[Name](block:&lt;StableBlockId&gt;)</c>. These turn
/// such a link back into the block in the editor's workflow.
/// </summary>
public static class EditorAgentBlockLinks
{
    public const string SCHEME = "block:";

    public static bool IsBlockLink(string? url)
    {
        return url is not null && url.StartsWith(SCHEME, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every id a block link may name in the workflow: each block's stable id and editor id.</summary>
    public static HashSet<string> Ids(Workflow workflow)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Block block in workflow.GetAllBlocks())
        {
            _ = ids.Add(block.Id);
            if (!string.IsNullOrWhiteSpace(block.StableBlockId))
            {
                _ = ids.Add(block.StableBlockId);
            }
        }

        return ids;
    }

    /// <summary>
    /// The block the id names: the one block holding it as its stable id, else the block with
    /// that editor id, else the first of several sharing the stable id; null when none has it.
    /// </summary>
    public static Block? Resolve(Workflow workflow, string blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return null;
        }

        List<Block> blocks = workflow.GetAllBlocks();
        List<Block> stable = [.. blocks.Where(block => string.Equals(block.StableBlockId, blockId, StringComparison.Ordinal))];
        if (stable.Count == 1)
        {
            return stable[0];
        }

        return blocks.FirstOrDefault(block => string.Equals(block.Id, blockId, StringComparison.Ordinal))
            ?? stable.FirstOrDefault();
    }
}
