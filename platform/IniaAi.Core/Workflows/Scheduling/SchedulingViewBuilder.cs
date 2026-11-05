using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Scheduling;

/// <summary>
/// Pure (no I/O, no async) transform from a <see cref="WorkflowGraph"/> into a
/// <see cref="SchedulingView"/>. Implements Kahn topological layering with barrier collapse.
/// Cycles (mutual forward references) are broken deterministically at the earliest-authored node
/// of the cycle, reproducing the sequential foreach where the forward placeholder resolved to
/// no-data; broken nodes are reported via <see cref="SchedulingView.ForcedCycleNodes"/>.
/// </summary>
public static class SchedulingViewBuilder
{
    /// <summary>
    /// Builds the scheduling view for the given graph. Within each layer, block IDs are sorted by
    /// <c>(SectionOrder, Order)</c> so the cap=1 execution order matches today's sequential
    /// foreach. Barrier nodes (see <see cref="IsBarrier"/>) occupy their own single-block layer.
    /// </summary>
    public static SchedulingView Build(WorkflowGraph graph)
    {
        var nodesById = graph.Nodes.ToDictionary(n => n.Id);

        // Sources (variables, document input, unresolved tags) are constants by dispatch time, so
        // their edges are pre-satisfied: a block whose only dependencies are sources belongs in the
        // first wave, not one barrier behind every dependency-free block.
        (Dictionary<string, int> incoming, Dictionary<string, List<string>> outgoing) =
            BuildAdjacency(graph, nodesById, includeSourceEdges: false);

        List<List<string>> rawLayers = [];
        List<string> ready = [.. incoming.Where(kv => kv.Value == 0).Select(kv => kv.Key)];
        HashSet<string> placed = new(StringComparer.Ordinal);
        List<string> forcedCycleNodes = [];
        int remaining = incoming.Count;

        while (remaining > 0)
        {
            if (ready.Count == 0)
            {
                // Cycle (mutual forward references): force the earliest-authored remaining node so
                // its forward placeholder resolves to no-data, exactly like the sequential foreach.
                string forced = ForceCycleNode(incoming, outgoing, nodesById, forcedCycleNodes);
                ready = [forced];
            }

            rawLayers.Add(ready);
            remaining -= ready.Count;

            List<string> next = [];
            foreach (string id in ready)
            {
                _ = placed.Add(id);
            }

            foreach (string id in ready)
            {
                foreach (string target in outgoing[id])
                {
                    if (!placed.Contains(target) && --incoming[target] == 0)
                    {
                        next.Add(target);
                    }
                }
            }

            ready = next;
        }

        List<SchedulingLayer> finalLayers = [];
        foreach (List<string> layer in rawLayers)
        {
            List<string> schedulable = [.. layer
                .Where(id => IsSchedulable(nodesById[id]))
                .OrderBy(id => nodesById[id].SectionOrder ?? int.MaxValue)
                .ThenBy(id => nodesById[id].Order ?? int.MaxValue)
                .ThenBy(id => id, StringComparer.Ordinal)];

            if (schedulable.Count == 0)
            {
                continue;
            }

            SplitOnBarriers(schedulable, nodesById, finalLayers);
        }

        IReadOnlyList<string> sequentialOrder = BuildSequentialOrder(graph, nodesById, forcedCycleNodes);

        return new SchedulingView(finalLayers, sequentialOrder)
        {
            ForcedCycleNodes = forcedCycleNodes.Distinct().ToList(),
        };
    }

    /// <summary>
    /// Picks the minimum-priority node of a cycle that nothing else still blocks, zeroes its
    /// in-degree (dropping its unsatisfied back-edges) and records it so the caller can log the
    /// cycle. A blocked node downstream of a cycle is never picked: forcing it would run it with
    /// no-data and leave the cycle in place. Edges that close no cycle therefore never change
    /// which nodes are forced.
    /// </summary>
    private static string ForceCycleNode(
        Dictionary<string, int> incoming,
        Dictionary<string, List<string>> outgoing,
        Dictionary<string, WorkflowGraphNode> nodesById,
        List<string> forcedCycleNodes)
    {
        List<string> blocked = [.. incoming.Where(kv => kv.Value > 0).Select(kv => kv.Key)];
        string forced = UnblockedCycleMembers(blocked, outgoing)
            .OrderBy(id => NodePriority(nodesById[id]))
            .First();

        incoming[forced] = 0;
        forcedCycleNodes.Add(forced);

        return forced;
    }

    /// <summary>
    /// The members of the cycles among the blocked nodes that no other blocked node feeds. When the
    /// scheduler stalls every blocked node waits on another blocked node, so such a cycle exists.
    /// </summary>
    private static IEnumerable<string> UnblockedCycleMembers(
        List<string> blocked,
        Dictionary<string, List<string>> outgoing)
    {
        HashSet<string> blockedSet = new(blocked, StringComparer.Ordinal);
        Dictionary<string, List<string>> successors = blocked.ToDictionary(
            static id => id,
            id => outgoing[id].Where(blockedSet.Contains).ToList(),
            StringComparer.Ordinal);

        List<List<string>> components = StronglyConnectedComponents.Find(blocked, successors);
        Dictionary<string, int> componentOf = new(StringComparer.Ordinal);
        for (int index = 0; index < components.Count; index++)
        {
            foreach (string id in components[index])
            {
                componentOf[id] = index;
            }
        }

        HashSet<int> fed = [.. successors
            .SelectMany(entry => entry.Value
                .Where(target => componentOf[target] != componentOf[entry.Key])
                .Select(target => componentOf[target]))];

        List<string> members = [.. components
            .Where((_, index) => !fed.Contains(index))
            .SelectMany(static component => component)];

        return members.Count > 0 ? members : blocked;
    }

    /// <summary>
    /// Pins the cap=1 DOP-equivalence contract: emit blocks in the same order the original
    /// <c>foreach (section in workflow.Sections.OrderBy(Order)) foreach (block in
    /// section.Blocks.OrderBy(Order))</c> would have produced. Implemented as a priority-aware
    /// topological sort: a min-heap of ready nodes keyed by <c>(SectionOrder, Order, Id)</c>
    /// emits one node at a time, satisfying both (a) every DAG predecessor edge and (b) the
    /// natural section/block-Order priority whenever the DAG permits it. For the hand-authored
    /// workflows this contract pins (loops, parameterized variants, plain linear chains), the
    /// natural section/block-Order ordering is already a valid topological order, so the emission
    /// reproduces sequential foreach byte-for-byte.
    /// </summary>
    private static IReadOnlyList<string> BuildSequentialOrder(
        WorkflowGraph graph,
        Dictionary<string, WorkflowGraphNode> nodesById,
        List<string> forcedCycleNodes)
    {
        (Dictionary<string, int> incoming, Dictionary<string, List<string>> outgoing) =
            BuildAdjacency(graph, nodesById, includeSourceEdges: true);

        // Priority key: (SectionOrder, Order, Id). PriorityQueue is a min-heap.
        PriorityQueue<string, (int section, int order, string id)> ready = new();

        foreach (string id in nodesById.Keys)
        {
            if (incoming[id] == 0)
            {
                ready.Enqueue(id, NodePriority(nodesById[id]));
            }
        }

        List<string> emitted = [];
        HashSet<string> placed = new(StringComparer.Ordinal);
        int processed = 0;

        while (processed < nodesById.Count)
        {
            if (ready.Count == 0)
            {
                string forced = ForceCycleNode(incoming, outgoing, nodesById, forcedCycleNodes);
                ready.Enqueue(forced, NodePriority(nodesById[forced]));
            }

            string id = ready.Dequeue();
            _ = placed.Add(id);
            processed++;

            if (IsSchedulable(nodesById[id]))
            {
                emitted.Add(id);
            }

            foreach (string target in outgoing[id])
            {
                if (!placed.Contains(target) && --incoming[target] == 0)
                {
                    ready.Enqueue(target, NodePriority(nodesById[target]));
                }
            }
        }

        return emitted;
    }

    private static (int section, int order, string id) NodePriority(WorkflowGraphNode node)
    {
        // Sources (variables, document inputs, unknown tags) drain first so a block reading a
        // variable is not held back behind later-authored blocks.
        if (!IsSchedulable(node))
        {
            return (int.MinValue, int.MinValue, node.Id);
        }

        return (
            node.SectionOrder ?? int.MaxValue,
            node.Order ?? int.MaxValue,
            node.Id);
    }

    /// <summary>
    /// Builds the in-degree and adjacency maps from the graph edges, skipping edges whose endpoints
    /// are not nodes. With <paramref name="includeSourceEdges"/> false, edges from non-schedulable
    /// source nodes are skipped entirely (the layering treats them as pre-satisfied). Returns fresh
    /// maps each call so the layering pass and the sequential-order pass each mutate their own copy.
    /// </summary>
    private static (Dictionary<string, int> Incoming, Dictionary<string, List<string>> Outgoing) BuildAdjacency(
        WorkflowGraph graph,
        Dictionary<string, WorkflowGraphNode> nodesById,
        bool includeSourceEdges)
    {
        var incoming = nodesById.Keys.ToDictionary(id => id, _ => 0);
        var outgoing = nodesById.Keys.ToDictionary(id => id, _ => new List<string>());

        foreach (WorkflowGraphEdge edge in graph.Edges)
        {
            if (!incoming.ContainsKey(edge.SourceId) || !incoming.ContainsKey(edge.TargetId))
            {
                continue;
            }

            if (!includeSourceEdges && !IsSchedulable(nodesById[edge.SourceId]))
            {
                continue;
            }

            incoming[edge.TargetId]++;
            outgoing[edge.SourceId].Add(edge.TargetId);
        }

        return (incoming, outgoing);
    }

    private static void SplitOnBarriers(
        List<string> layer,
        Dictionary<string, WorkflowGraphNode> nodesById,
        List<SchedulingLayer> finalLayers)
    {
        List<string> currentRun = [];

        foreach (string id in layer)
        {
            if (IsBarrier(nodesById[id]))
            {
                if (currentRun.Count > 0)
                {
                    finalLayers.Add(new SchedulingLayer(currentRun));
                    currentRun = [];
                }

                finalLayers.Add(new SchedulingLayer([id]));
            }
            else
            {
                currentRun.Add(id);
            }
        }

        if (currentRun.Count > 0)
        {
            finalLayers.Add(new SchedulingLayer(currentRun));
        }
    }

    private static bool IsSchedulable(WorkflowGraphNode node)
    {
        // The PARAMETERIZED_SECTION branch is a defensive case: in production the graph builder never
        // emits parameterized-section nodes (WorkflowHandler segments and expands them into concrete
        // blocks before the graph is built). It is reachable only from test fixtures that construct a
        // graph with such a node directly, so it is kept rather than removed.
        return node.Kind is WorkflowGraphNodeKinds.BLOCK
            or WorkflowGraphNodeKinds.PARAMETERIZED_SECTION;
    }

    private static bool IsBarrier(WorkflowGraphNode node)
    {
        // See IsSchedulable: PARAMETERIZED_SECTION is a defensive / test-only branch the production
        // graph builder never produces, since parameterized sections are expanded upstream.
        if (node.Kind == WorkflowGraphNodeKinds.PARAMETERIZED_SECTION)
        {
            return true;
        }

        // A block that creates an output document writes the shared AllPages list during execution,
        // so it runs alone (no concurrent document-producer).
        if (node.ProducesDocument)
        {
            return true;
        }

        // Author opted this block into running alone (Block.RunsAlone).
        if (node.ManualBarrier)
        {
            return true;
        }

        return node.BlockType switch
        {
            BlockTypes.IntelligentSearch => true,
            BlockTypes.Json2Xlsx => true,
            BlockTypes.BuildTopicsForAllDocuments => true,
            BlockTypes.LoopStart => true,
            BlockTypes.LoopEnd => true,
            _ => false,
        };
    }
}
