using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// The blocks whose output can reach a report paragraph or a data output, following content
/// placeholders back from the output-source and data-output blocks. Only those blocks are citation
/// sources: a block whose output reaches neither would build facts nobody reads. Every block of a
/// loop section is a source too: a loop renames its copies when it runs, and the blocks after it
/// read them through the loop's own tags, which no placeholder names. A block the map never saw
/// counts as a source, and a workflow with neither an output-source nor a data-output block keeps
/// every block a source.
/// </summary>
internal sealed class CitationSources
{
    public static CitationSources Everyone { get; } = new([], []);

    private readonly HashSet<string> _reaching;
    private readonly HashSet<string> _known;

    private CitationSources(HashSet<string> reaching, HashSet<string> known)
    {
        _reaching = reaching;
        _known = known;
    }

    public static CitationSources Of(Workflow workflow)
    {
        List<Block> blocks = workflow.GetAllBlocks();
        List<Block> outputSources = [.. blocks.Where(block => block.IsOutputSource || block.IsDataOutput)];

        if (outputSources.Count == 0)
        {
            return Everyone;
        }

        ILookup<string, Block> blocksByKey = blocks.ToLookup(block => block.GetDependencyKey(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var reaching = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Block>(outputSources.Concat(workflow.Sections
            .Where(section => section.Blocks.Any(block => block.IsLoopBoundary))
            .SelectMany(section => section.Blocks)));

        while (pending.TryPop(out Block? block))
        {
            if (!visited.Add(block.Id))
            {
                continue;
            }

            _ = reaching.Add(block.GetDependencyKey());

            foreach (string tag in block.GetContentDependencyTags())
            {
                foreach (Block input in blocksByKey[tag])
                {
                    pending.Push(input);
                }
            }
        }

        return new CitationSources(reaching, [.. blocks.Select(block => block.GetDependencyKey())]);
    }

    /// <summary>
    /// The inputs whose output reaches <paramref name="reader"/>'s own output. An input read only
    /// through a RAG setting, such as a list of page numbers, chose pages and states nothing.
    /// </summary>
    public static IEnumerable<Block> ReadInContent(Block reader, IEnumerable<Block> inputs)
    {
        HashSet<string> tags = [.. reader.GetContentDependencyTags()];
        return inputs.Where(input => tags.Contains(input.GetDependencyKey()));
    }

    public bool Includes(Block block)
    {
        string key = block.GetDependencyKey();
        return _reaching.Contains(key) || !_known.Contains(key);
    }
}
