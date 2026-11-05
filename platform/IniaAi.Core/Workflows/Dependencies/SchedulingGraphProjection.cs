using IniaAi.Common.Constants;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Core.Workflows.Dependencies;

/// <summary>
/// Projects a <see cref="WorkflowDependencyMap"/> onto the <see cref="WorkflowGraph"/> the
/// scheduler layers. Inputs become pre-satisfied source nodes keyed by their tag, blocks keep
/// their ids.
/// </summary>
internal static class SchedulingGraphProjection
{
    private const string FENCE_LABEL = "fence";
    private const string DOCUMENT_LABEL_PREFIX = "document:";

    public static WorkflowGraph Project(
        WorkflowDependencyMap map,
        IReadOnlyList<DependencyTagRead> tagReads,
        IReadOnlySet<string>? fenceBlockIds,
        bool verificationGroupsRun)
    {
        var graph = new WorkflowGraph();
        Dictionary<string, WorkflowGraphNode> nodesById = new(StringComparer.Ordinal);

        foreach (DependencyBlock block in map.Blocks)
        {
            nodesById[block.Id] = BlockNode(block);
        }

        foreach (DependencyInput input in map.Inputs)
        {
            _ = nodesById.TryAdd(input.Tag, InputNode(input));
        }

        graph.Nodes.AddRange(nodesById.Values);

        var edges = new EdgeSet(graph, map.Blocks);

        foreach (DependencyEdge edge in map.Edges.Where(static edge => edge.Kind == DependencyEdgeKind.Data))
        {
            edges.Add(NodeIdOf(map, edge.SourceId), edge.TargetId, edge.Tags[0]);
        }

        if (verificationGroupsRun)
        {
            AddVerifierCoProducerEdges(map, tagReads, edges);
        }

        if (fenceBlockIds is { Count: > 0 })
        {
            AddFenceEdges(map, fenceBlockIds, edges);
        }

        foreach (DependencyEdge edge in map.Edges.Where(static edge =>
            edge.Kind is DependencyEdgeKind.Document or DependencyEdgeKind.Counterparty))
        {
            string label = edge.Kind == DependencyEdgeKind.Document
                ? DOCUMENT_LABEL_PREFIX + edge.Tags[0]
                : edge.Tags[0];

            edges.AddUnlessCycle(edge.SourceId, edge.TargetId, label);
        }

        return graph;
    }

    /// <summary>
    /// Makes each verifier a co-producer of every member's tag, so every downstream reader of a
    /// member waits for the loop to settle rather than for the member's first attempt. The edge
    /// from member to verifier needs nothing here: a verifier's placeholders already name its
    /// members.
    /// </summary>
    private static void AddVerifierCoProducerEdges(
        WorkflowDependencyMap map,
        IReadOnlyList<DependencyTagRead> tagReads,
        EdgeSet edges)
    {
        foreach (DependencyVerificationGroup group in map.VerificationGroups)
        {
            HashSet<string> memberTags = new(group.MemberTags, StringComparer.Ordinal);

            foreach (DependencyTagRead read in tagReads)
            {
                if (memberTags.Contains(read.BaseTag) && read.ReaderId != group.VerifierBlockId)
                {
                    edges.Add(group.VerifierBlockId, read.ReaderId, read.Placeholder);
                }
            }
        }
    }

    /// <summary>
    /// Pins each fence block at its authored position: every earlier-authored block must finish
    /// before it, and it must finish before every later-authored block starts. This reproduces the
    /// sequential foreach around blocks whose handler side effects placeholder analysis cannot see.
    /// </summary>
    private static void AddFenceEdges(WorkflowDependencyMap map, IReadOnlySet<string> fenceBlockIds, EdgeSet edges)
    {
        List<string> authoredOrder = [.. map.Blocks.Select(static block => block.Id)];

        for (int fenceIndex = 0; fenceIndex < authoredOrder.Count; fenceIndex++)
        {
            string fenceId = authoredOrder[fenceIndex];
            if (!fenceBlockIds.Contains(fenceId))
            {
                continue;
            }

            for (int other = 0; other < authoredOrder.Count; other++)
            {
                if (other == fenceIndex)
                {
                    continue;
                }

                if (other < fenceIndex)
                {
                    edges.Add(authoredOrder[other], fenceId, FENCE_LABEL);
                }
                else
                {
                    edges.Add(fenceId, authoredOrder[other], FENCE_LABEL);
                }
            }
        }
    }

    private static string NodeIdOf(WorkflowDependencyMap map, string sourceId)
    {
        return map.FindInput(sourceId)?.Tag ?? sourceId;
    }

    private static WorkflowGraphNode BlockNode(DependencyBlock block)
    {
        return new WorkflowGraphNode
        {
            Id = block.Id,
            Kind = WorkflowGraphNodeKinds.BLOCK,
            Label = block.Block.Name,
            Order = block.Block.Order,
            SectionOrder = block.SectionOrder,
            BlockType = block.Block.Type,
            ReplacementTag = string.IsNullOrWhiteSpace(block.Block.ReplacementTag) ? null : block.Block.ReplacementTag,
            DocumentClass = ToDocumentClass(block.Block.SupportedDocumentClasses),
            ProducesDocument = block.Block.GetCreatedDocumentClass() is not null,
            ManualBarrier = block.Block.RunsAlone,
            VerificationGroup = block.VerificationGroup,
        };
    }

    private static WorkflowGraphNode InputNode(DependencyInput input)
    {
        return new WorkflowGraphNode
        {
            Id = input.Tag,
            Kind = input.Kind switch
            {
                DependencyInputKind.Variable => WorkflowGraphNodeKinds.INPUT_VARIABLE,
                DependencyInputKind.Signature => WorkflowGraphNodeKinds.INPUT_SIGNATURE,
                DependencyInputKind.Loop => WorkflowGraphNodeKinds.INPUT_LOOP,
                DependencyInputKind.System when input.Tag == SharedInputKeys.INPUT_CONTEXT => WorkflowGraphNodeKinds.DOCUMENT_INPUT,
                DependencyInputKind.System => WorkflowGraphNodeKinds.INPUT_SYSTEM,
                _ => WorkflowGraphNodeKinds.UNKNOWN,
            },
            Label = input.Tag,
            Order = null,
        };
    }

    /// <summary>A block loaded without its class list (an older persisted row) has none.</summary>
    private static string? ToDocumentClass(List<string>? classes)
    {
        if (classes is null)
        {
            return null;
        }

        List<string> real = [.. classes.Where(static c => !string.Equals(c, DocumentClass.KEY_NONE, StringComparison.OrdinalIgnoreCase))];
        return real.Count > 0 ? string.Join("; ", real) : null;
    }

    /// <summary>
    /// The graph's edges, one per (source, target), with the block-to-block reachability the
    /// cycle guard needs kept up to date as edges land.
    /// </summary>
    private sealed class EdgeSet
    {
        private readonly WorkflowGraph _graph;
        private readonly HashSet<(string Source, string Target)> _keys = [];
        private readonly Dictionary<string, List<string>> _successors;

        public EdgeSet(WorkflowGraph graph, IReadOnlyList<DependencyBlock> blocks)
        {
            _graph = graph;
            _successors = blocks.ToDictionary(static block => block.Id, static _ => new List<string>(), StringComparer.Ordinal);
        }

        public void Add(string sourceId, string targetId, string label)
        {
            if (sourceId == targetId || !_keys.Add((sourceId, targetId)))
            {
                return;
            }

            _graph.Edges.Add(new WorkflowGraphEdge { SourceId = sourceId, TargetId = targetId, Label = label });

            if (_successors.TryGetValue(sourceId, out List<string>? next))
            {
                next.Add(targetId);
            }
        }

        /// <summary>
        /// Adds the edge unless the target already reaches the source, so a derived ordering
        /// never forces a cycle break (which resolves the broken block's inputs to no-data and is
        /// refused inside a verification group). The reader then runs as it did before.
        /// </summary>
        public void AddUnlessCycle(string sourceId, string targetId, string label)
        {
            if (sourceId == targetId
                || _keys.Contains((sourceId, targetId))
                || GraphReachability.Reaches(_successors, targetId, sourceId))
            {
                return;
            }

            Add(sourceId, targetId, label);
        }
    }
}
