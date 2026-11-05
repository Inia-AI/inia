using IniaAi.Core.Workflows.Graph;

namespace IniaAi.Core.Workflows.Dependencies;

/// <summary>
/// The cycles among the links that order a run (data, document, counterparty), found on block
/// occurrences and folded onto template blocks. A parameterized variant is its own occurrence, so
/// <c>x_AR</c> reading <c>y_PAR</c> while <c>y_AR</c> reads <c>x_PAR</c> is no cycle, as in the run.
/// Gates are excluded: a gate reading its own section is evaluated before the section runs, which
/// is no cycle for the scheduler.
/// </summary>
internal static class DependencyCycles
{
    public static List<List<string>> Find(IReadOnlyList<DependencyBlock> blocks, IReadOnlyList<OrderingLink> links)
    {
        Dictionary<string, int> positionById = new(StringComparer.Ordinal);
        for (int position = 0; position < blocks.Count; position++)
        {
            positionById[blocks[position].Id] = position;
        }

        Dictionary<OrderingNode, List<OrderingNode>> successors = [];
        foreach (OrderingLink link in links)
        {
            if (!positionById.ContainsKey(link.From.BlockId) || !positionById.ContainsKey(link.To.BlockId))
            {
                continue;
            }

            if (!successors.TryGetValue(link.From, out List<OrderingNode>? next))
            {
                next = [];
                successors[link.From] = next;
            }

            next.Add(link.To);
            _ = successors.TryAdd(link.To, []);
        }

        return [.. StronglyConnectedComponents.Find([.. successors.Keys], successors)
            .Where(static component => component.Count > 1)
            .Select(component => component
                .Select(static node => node.BlockId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => positionById[id])
                .ToList())
            .DistinctBy(static ids => string.Join('\n', ids))
            .OrderBy(ids => positionById[ids[0]])];
    }
}

/// <summary>One scan of a block: its template id and the parameterized variant, null outside one.</summary>
internal readonly record struct OrderingNode(string BlockId, string? VariantKey);

/// <summary><see cref="To"/> waits for <see cref="From"/>.</summary>
internal readonly record struct OrderingLink(OrderingNode From, OrderingNode To);
