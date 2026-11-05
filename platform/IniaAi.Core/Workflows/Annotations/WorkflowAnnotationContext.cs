using System.Collections.Concurrent;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Annotations;

public class WorkflowAnnotationsContext
{
    private readonly ConcurrentDictionary<string, BlockAnnotations> _blockAnnotationsMap = [];

    public FactOrigins FactOrigins { get; } = new();

    /// <summary>
    /// The last write for a block id wins (D34): a verification redo leg re-processes its
    /// block's annotations, and downstream citation building must read the attempt that
    /// actually releases, never a superseded round's.
    /// </summary>
    public void AddBlockAnnotations(Block? block, BlockAnnotations? annotations)
    {
        if (block is null || annotations is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(block.Id))
        {
            SetAnnotationsForBlock(block.Id, annotations);
        }
    }

    /// <summary>The id-keyed form of the same last-write-wins write.</summary>
    public void SetAnnotationsForBlock(string blockId, BlockAnnotations annotations)
    {
        _blockAnnotationsMap[blockId] = annotations;
    }

    /// <summary>
    /// Gets annotations for a block by its ID.
    /// </summary>
    public BlockAnnotations? GetAnnotationsForBlock(string blockId)
    {
        return _blockAnnotationsMap.TryGetValue(blockId, out BlockAnnotations? annotations)
            ? annotations
            : null;
    }

    public List<BlockAnnotations> GetReferencedAnnotations(IEnumerable<Block> referencedBlocks)
    {
        if (!referencedBlocks.Any())
        {
            return [];
        }

        List<BlockAnnotations> result = [];

        foreach (Block block in referencedBlocks)
        {
            BlockAnnotations? annotations = GetAnnotationsForBlock(block.Id);

            if (annotations is null)
            {
                continue;
            }

            result.Add(annotations);
        }

        return result;
    }
}
