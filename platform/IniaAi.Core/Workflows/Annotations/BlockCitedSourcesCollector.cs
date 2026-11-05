using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// Every page and link that a block, or any block it cited, cited during the run: each occurrence
/// in chain order, the block's own sources first. An inherited fact is the upstream block's own
/// object, so each source object is taken once. It reads the run's annotation graph and never
/// writes into it, so a list built for one citation cannot leak into another. One instance serves
/// one report paragraph and builds each block's list once.
/// </summary>
public sealed class BlockCitedSourcesCollector(WorkflowAnnotationsContext context)
{
    private readonly Dictionary<string, List<BaseSource>> _citedSourcesByBlockId = [];

    public List<BaseSource> CitedSourcesOf(string blockId)
    {
        if (!_citedSourcesByBlockId.TryGetValue(blockId, out List<BaseSource>? citedSources))
        {
            citedSources = Collect(blockId);
            _citedSourcesByBlockId[blockId] = citedSources;
        }

        return citedSources;
    }

    /// <summary>
    /// One list per block that the citations' block sources reach, down their whole
    /// <see cref="BlockSource.UpstreamSources"/> chain, so the source panel can show a list at
    /// every step: one entry per page or link, each excerpt cut from its start. A block whose
    /// chain cited nothing gets none.
    /// </summary>
    public List<BlockCitedSources> ListsReachedBy(IEnumerable<Citation> citations)
    {
        var lists = new List<BlockCitedSources>();
        var listedBlockIds = new HashSet<string>();
        var pending = new Queue<BlockSource>(citations.SelectMany(c => c.Sources).OfType<BlockSource>());

        while (pending.TryDequeue(out BlockSource? blockSource))
        {
            if (!listedBlockIds.Add(blockSource.BlockId))
            {
                continue;
            }

            List<BaseSource> listed = [.. BlockAnnotationProcessor.DeduplicateSources(CitedSourcesOf(blockSource.BlockId))
                .Select(BlockAnnotationProcessor.HeadBoundedSource)];
            if (listed.Count > 0)
            {
                lists.Add(new BlockCitedSources { BlockId = blockSource.BlockId, Sources = listed });
            }

            foreach (BlockSource upstream in blockSource.UpstreamSources.OfType<BlockSource>())
            {
                pending.Enqueue(upstream);
            }
        }

        return lists;
    }

    private List<BaseSource> Collect(string blockId)
    {
        var collected = new List<BaseSource>();
        var collectedObjects = new HashSet<BaseSource>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<string>();
        var pending = new Queue<string>();
        pending.Enqueue(blockId);

        while (pending.TryDequeue(out string? current))
        {
            if (!visited.Add(current)
                || context.GetAnnotationsForBlock(current) is not BlockAnnotations annotations)
            {
                continue;
            }

            foreach (Fact fact in annotations.Facts)
            {
                foreach (BaseSource source in fact.Sources)
                {
                    if (source is BlockSource blockSource)
                    {
                        pending.Enqueue(blockSource.BlockId);
                    }
                    else if (collectedObjects.Add(source))
                    {
                        collected.Add(source);
                    }
                }
            }
        }

        return collected;
    }
}
