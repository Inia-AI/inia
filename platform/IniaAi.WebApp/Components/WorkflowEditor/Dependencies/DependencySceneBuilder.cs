using IniaAi.Core.Workflows.Dependencies;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using IniaAi.WebApp.Theme;

namespace IniaAi.WebApp.Components.WorkflowEditor.Dependencies;

/// <summary>
/// Shapes a <see cref="WorkflowDependencyMap"/> into what the dependency graph draws. The chain shows
/// one step each way from the focused block and a stub for every further step the reader has not
/// opened; the map shows sections as cards, with the reader's open sections drawn as their blocks.
/// Every block type is drawn.
/// </summary>
public static class DependencySceneBuilder
{
    public const string SECTION_CARD_PREFIX = "sec:";
    public const string SECTION_BOX_PREFIX = "box:";
    public const string VERIFICATION_GROUP_PREFIX = "vg:";

    public const string EMPTY_CHAIN_MESSAGE = "This block neither reads from nor feeds any other block.";
    public const string EMPTY_MAP_MESSAGE = "This workflow has no blocks yet.";

    private const int SUMMARY_TYPE_COUNT = 3;
    private const int MAX_LINK_TAGS = 12;
    private const string UNTITLED_SECTION = "Untitled section";

    public static DependencyScene Build(WorkflowDependencyMap map, DependencyViewState state)
    {
        if (state.Mode == DependencyViewModes.CHAIN
            && state.FocusBlockId is { } focusId
            && map.FindBlock(focusId) is not null)
        {
            return Chain(map, state, focusId);
        }

        return Map(map, state);
    }

    public static string SectionCardId(string sectionId)
    {
        return SECTION_CARD_PREFIX + sectionId;
    }

    public static string SectionBoxId(string sectionId)
    {
        return SECTION_BOX_PREFIX + sectionId;
    }

    /// <summary>The id <see cref="DependencyViewState.OpenSections"/> holds for the block's section.</summary>
    public static string SectionKey(DependencyBlock block)
    {
        return block.Block.Section?.Id ?? $"order-{block.SectionOrder}";
    }

    public static string BlockLabel(DependencyBlock block)
    {
        if (!string.IsNullOrWhiteSpace(block.Block.Name))
        {
            return block.Block.Name;
        }

        return string.IsNullOrWhiteSpace(block.Block.ReplacementTag)
            ? BlockTypeVisuals.ShortLabel(block.Block.Type)
            : $"{{{block.Block.ReplacementTag}}}";
    }

    public static string InputKindLabel(DependencyInputKind kind)
    {
        return kind switch
        {
            DependencyInputKind.Variable => "Workflow input",
            DependencyInputKind.Signature => "Signature field",
            DependencyInputKind.System => "System value",
            DependencyInputKind.Loop => "Loop item",
            _ => "Unresolved tag",
        };
    }

    private static DependencyScene Chain(WorkflowDependencyMap map, DependencyViewState state, string focusId)
    {
        var graph = new BlockGraph(map);
        var roles = new Dictionary<string, string>(StringComparer.Ordinal) { [focusId] = DependencyRoles.FOCUS };
        var links = new LinkSet();

        WalkChain(focusId, state.ExpandedUpstream, graph.Sources, roles, DependencyRoles.UPSTREAM,
            (node, hop) => links.AddHop(hop.NodeId, node, hop, DependencyRoles.UPSTREAM));
        WalkChain(focusId, state.ExpandedDownstream, graph.Targets, roles, DependencyRoles.DOWNSTREAM,
            (node, hop) => links.AddHop(node, hop.NodeId, hop, DependencyRoles.DOWNSTREAM));

        HashSet<string> cycleIds = CycleBlockIds(map);
        var cards = new Dictionary<string, DependencyCard>(StringComparer.Ordinal);
        foreach ((string id, string role) in roles)
        {
            cards[id] = BlockCard(map, map.FindBlock(id)!, role, state, cycleIds);
        }

        foreach ((string id, string role) in roles.ToList())
        {
            if (role == DependencyRoles.UPSTREAM && !state.ExpandedUpstream.Contains(id))
            {
                AddStub(map, cards, links, id, graph.Sources(id), roles, upstream: true);
            }
            else if (role == DependencyRoles.DOWNSTREAM && !state.ExpandedDownstream.Contains(id))
            {
                AddStub(map, cards, links, id, graph.Targets(id), roles, upstream: false);
            }
        }

        foreach (string blockId in roles.Keys.ToList())
        {
            AddInputsAndGates(map, state, cards, links, blockId, cycleIds);
        }

        List<DependencyGroup> groups = GroupVerifications(map, cards);

        return new DependencyScene(
            DependencyViewModes.CHAIN,
            focusId,
            [.. cards.Values],
            links.ToList(),
            groups,
            links.Count == 0 ? EMPTY_CHAIN_MESSAGE : null);
    }

    private static void WalkChain(
        string focusId,
        HashSet<string> expanded,
        Func<string, IReadOnlyList<Hop>> step,
        Dictionary<string, string> roles,
        string role,
        Action<string, Hop> addLink)
    {
        var queue = new Queue<string>([focusId]);
        while (queue.TryDequeue(out string? node))
        {
            if (node != focusId && !expanded.Contains(node))
            {
                continue;
            }

            // Only the links that lead back to the focus: all of the focus's own, and for every other
            // block the one link it was reached by. Side links between revealed blocks stay hidden.
            foreach (Hop hop in step(node))
            {
                bool reached = roles.TryAdd(hop.NodeId, role);
                if (reached)
                {
                    queue.Enqueue(hop.NodeId);
                }

                if (reached || node == focusId)
                {
                    addLink(node, hop);
                }
            }
        }
    }

    private static void AddStub(
        WorkflowDependencyMap map,
        Dictionary<string, DependencyCard> cards,
        LinkSet links,
        string nodeId,
        IReadOnlyList<Hop> hops,
        Dictionary<string, string> roles,
        bool upstream)
    {
        List<DependencyBlock> hidden = [.. hops
            .Where(hop => !roles.ContainsKey(hop.NodeId))
            .Select(hop => map.FindBlock(hop.NodeId)!)];
        if (hidden.Count == 0)
        {
            return;
        }

        List<string> sections = [.. hidden
            .OrderBy(block => block.SectionOrder)
            .GroupBy(block => SectionTitle(block), StringComparer.Ordinal)
            .Select(group => $"{group.Key} ({group.Count()})")];

        string stubId = upstream ? DependencyViewState.UpstreamStubId(nodeId) : DependencyViewState.DownstreamStubId(nodeId);
        cards[stubId] = new DependencyCard
        {
            Id = stubId,
            Kind = DependencyCardKinds.STUB,
            Label = $"+{hidden.Count} more in {sections.Count} {(sections.Count == 1 ? "section" : "sections")}",
            Detail = upstream ? "Feeds this block" : "Reads this block",
            Role = upstream ? DependencyRoles.UPSTREAM : DependencyRoles.DOWNSTREAM,
            Summary = sections,
        };

        if (upstream)
        {
            links.Add(stubId, nodeId, DependencyLinkStyles.STUB, DependencyRoles.UPSTREAM, []);
        }
        else
        {
            links.Add(nodeId, stubId, DependencyLinkStyles.STUB, DependencyRoles.DOWNSTREAM, []);
        }
    }

    private static void AddInputsAndGates(
        WorkflowDependencyMap map,
        DependencyViewState state,
        Dictionary<string, DependencyCard> cards,
        LinkSet links,
        string blockId,
        HashSet<string> cycleIds)
    {
        bool showInputs = state.InputsShownFor(blockId);
        bool showGates = state.GatesShownFor(blockId);
        string role = cards[blockId].Role;

        foreach (DependencyEdge edge in map.IncomingEdges(blockId))
        {
            if (edge.Kind == DependencyEdgeKind.Gate)
            {
                if (!showGates)
                {
                    continue;
                }

                if (map.FindBlock(edge.SourceId) is { } gateBlock)
                {
                    _ = cards.TryAdd(edge.SourceId, BlockCard(map, gateBlock, DependencyRoles.NONE, state, cycleIds));
                }
                else if (map.FindInput(edge.SourceId) is { } gateInput)
                {
                    _ = cards.TryAdd(edge.SourceId, InputCard(gateInput));
                }

                links.Add(edge.SourceId, blockId, DependencyLinkStyles.GATE, role, edge.Tags);

                continue;
            }

            if (map.FindInput(edge.SourceId) is not { } input)
            {
                continue;
            }

            if (input.Kind == DependencyInputKind.Unresolved || showInputs)
            {
                _ = cards.TryAdd(input.Id, InputCard(input));
                links.Add(input.Id, blockId, DependencyLinkStyles.INPUT, role, edge.Tags);
            }
        }
    }

    private static DependencyScene Map(WorkflowDependencyMap map, DependencyViewState state)
    {
        if (map.Blocks.Count == 0)
        {
            return new DependencyScene(DependencyViewModes.MAP, null, [], [], [], EMPTY_MAP_MESSAGE);
        }

        var graph = new BlockGraph(map);
        HashSet<string> cycleIds = CycleBlockIds(map);
        var cards = new Dictionary<string, DependencyCard>(StringComparer.Ordinal);
        var groups = new List<DependencyGroup>();
        var representative = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (IGrouping<string, DependencyBlock> section in map.Blocks.GroupBy(SectionKey, StringComparer.Ordinal))
        {
            DependencyBlock first = section.First();
            string title = SectionTitle(first);
            bool focused = section.Any(block => block.Id == state.FocusBlockId);

            if (state.OpenSections.Contains(section.Key))
            {
                string boxId = SectionBoxId(section.Key);
                groups.Add(new DependencyGroup(boxId, DependencyGroupKinds.SECTION, title));
                foreach (DependencyBlock block in section)
                {
                    representative[block.Id] = block.Id;
                    string role = block.Id == state.FocusBlockId ? DependencyRoles.FOCUS : DependencyRoles.NONE;
                    cards[block.Id] = BlockCard(map, block, role, state, cycleIds) with
                    {
                        GroupId = boxId,
                        InputsOpen = false,
                        GatesOpen = false,
                    };
                }

                continue;
            }

            string cardId = SectionCardId(section.Key);
            foreach (DependencyBlock block in section)
            {
                representative[block.Id] = cardId;
            }

            cards[cardId] = new DependencyCard
            {
                Id = cardId,
                Kind = DependencyCardKinds.SECTION,
                Label = title,
                Role = focused ? DependencyRoles.FOCUS : DependencyRoles.NONE,
                BlockCount = section.Count(),
                VariantCount = section.Max(block => block.VariantKeys.Count),
                InCycle = section.Any(block => cycleIds.Contains(block.Id)),
                Summary = [.. section
                    .GroupBy(block => block.Block.Type)
                    .OrderByDescending(group => group.Count())
                    .Take(SUMMARY_TYPE_COUNT)
                    .Select(group => $"{group.Count()} {BlockTypeVisuals.ShortLabel(group.Key)}")],
            };
        }

        var links = new LinkSet();
        foreach (DependencyBlock target in map.Blocks)
        {
            string targetRep = representative[target.Id];
            foreach (Hop hop in graph.Sources(target.Id))
            {
                if (representative[hop.NodeId] is { } sourceRep && sourceRep != targetRep)
                {
                    links.Add(sourceRep, targetRep, DependencyLinkStyles.DATA, DependencyRoles.NONE, hop.Tags);
                }
            }
        }

        if (state.ShowGates)
        {
            foreach (DependencyEdge edge in map.Edges.Where(static edge => edge.Kind == DependencyEdgeKind.Gate))
            {
                if (representative.GetValueOrDefault(edge.SourceId) is { } sourceRep
                    && representative.GetValueOrDefault(edge.TargetId) is { } targetRep
                    && sourceRep != targetRep)
                {
                    links.Add(sourceRep, targetRep, DependencyLinkStyles.GATE, DependencyRoles.NONE, edge.Tags);
                }
            }
        }

        return new DependencyScene(DependencyViewModes.MAP, state.FocusBlockId, [.. cards.Values], links.ToList(), groups, null);
    }

    private static DependencyCard BlockCard(
        WorkflowDependencyMap map,
        DependencyBlock block,
        string role,
        DependencyViewState state,
        HashSet<string> cycleIds)
    {
        int inputCount = 0;
        var gateSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (DependencyEdge edge in map.IncomingEdges(block.Id))
        {
            if (edge.Kind == DependencyEdgeKind.Gate)
            {
                _ = gateSources.Add(edge.SourceId);
            }
            else if (map.FindInput(edge.SourceId) is { Kind: not DependencyInputKind.Unresolved })
            {
                inputCount++;
            }
        }

        return new DependencyCard
        {
            Id = block.Id,
            Kind = DependencyCardKinds.BLOCK,
            Label = BlockLabel(block),
            Detail = SectionTitle(block),
            TypeLabel = BlockTypeVisuals.ShortLabel(block.Block.Type),
            Hue = EditorHues.Block(block.Block.Type),
            Tag = string.IsNullOrWhiteSpace(block.Block.ReplacementTag) ? null : block.Block.ReplacementTag,
            Role = role,
            InputCount = inputCount,
            GateCount = gateSources.Count,
            InputsOpen = state.InputsShownFor(block.Id),
            GatesOpen = state.GatesShownFor(block.Id),
            InCycle = cycleIds.Contains(block.Id),
            VariantCount = block.VariantKeys.Count,
        };
    }

    private static DependencyCard InputCard(DependencyInput input)
    {
        bool unresolved = input.Kind == DependencyInputKind.Unresolved;

        return new DependencyCard
        {
            Id = input.Id,
            Kind = DependencyCardKinds.INPUT,
            Label = unresolved ? $"No block or input supplies {{{input.Tag}}}" : $"{{{input.Tag}}}",
            Detail = InputKindLabel(input.Kind),
            Tag = input.Tag,
            IsUnresolved = unresolved,
        };
    }

    private static List<DependencyGroup> GroupVerifications(WorkflowDependencyMap map, Dictionary<string, DependencyCard> cards)
    {
        List<DependencyGroup> groups = [];
        foreach (DependencyVerificationGroup group in map.VerificationGroups)
        {
            List<string> ids = [.. new[] { group.VerifierBlockId }
                .Concat(group.MemberBlockIds)
                .Distinct(StringComparer.Ordinal)
                .Where(id => cards.TryGetValue(id, out DependencyCard? card)
                    && card.Kind == DependencyCardKinds.BLOCK
                    && card.GroupId is null)];
            if (ids.Count < 2)
            {
                continue;
            }

            string groupId = VERIFICATION_GROUP_PREFIX + group.Key;
            string verifier = map.FindBlock(group.VerifierBlockId) is { } block ? BlockLabel(block) : group.Key;
            groups.Add(new DependencyGroup(groupId, DependencyGroupKinds.VERIFICATION, $"Checked by {verifier}"));
            foreach (string id in ids)
            {
                cards[id] = cards[id] with { GroupId = groupId };
            }
        }

        return groups;
    }

    private static HashSet<string> CycleBlockIds(WorkflowDependencyMap map)
    {
        return [.. map.Issues
            .Where(static issue => issue.Kind == DependencyIssueKind.Cycle)
            .SelectMany(static issue => issue.BlockIds)];
    }

    private static string SectionTitle(DependencyBlock block)
    {
        if (block.Block.Section is not { } section)
        {
            return UNTITLED_SECTION;
        }

        if (!string.IsNullOrWhiteSpace(section.Title))
        {
            return section.Title;
        }

        return string.IsNullOrWhiteSpace(section.Name) ? UNTITLED_SECTION : section.Name;
    }

    /// <summary>A block one link away, with every kind of read and every tag between the two.</summary>
    private sealed record Hop(string NodeId, HashSet<DependencyEdgeKind> Kinds, List<string> Tags);

    /// <summary>Block-to-block reads, one hop per neighbouring block. Gates are drawn separately.</summary>
    private sealed class BlockGraph(WorkflowDependencyMap map)
    {
        private readonly Dictionary<string, IReadOnlyList<Hop>> _sources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<Hop>> _targets = new(StringComparer.Ordinal);

        public IReadOnlyList<Hop> Sources(string blockId)
        {
            return Memo(_sources, blockId, upstream: true);
        }

        public IReadOnlyList<Hop> Targets(string blockId)
        {
            return Memo(_targets, blockId, upstream: false);
        }

        private IReadOnlyList<Hop> Memo(Dictionary<string, IReadOnlyList<Hop>> cache, string blockId, bool upstream)
        {
            if (!cache.TryGetValue(blockId, out IReadOnlyList<Hop>? hops))
            {
                hops = Neighbours(blockId, upstream);
                cache[blockId] = hops;
            }

            return hops;
        }

        private List<Hop> Neighbours(string start, bool upstream)
        {
            var found = new Dictionary<string, Hop>(StringComparer.Ordinal);
            IEnumerable<DependencyEdge> edges = upstream ? map.IncomingEdges(start) : map.OutgoingEdges(start);
            foreach (DependencyEdge edge in edges)
            {
                string other = upstream ? edge.SourceId : edge.TargetId;
                if (edge.Kind == DependencyEdgeKind.Gate || other == start || map.FindBlock(other) is null)
                {
                    continue;
                }

                if (found.TryGetValue(other, out Hop? hop))
                {
                    _ = hop.Kinds.Add(edge.Kind);
                    hop.Tags.AddRange(edge.Tags.Where(tag => !hop.Tags.Contains(tag)));

                    continue;
                }

                found[other] = new Hop(other, [edge.Kind], [.. edge.Tags]);
            }

            return [.. found.Values];
        }
    }

    /// <summary>One drawn link per (source, target, style), carrying every tag and the count it stands for.</summary>
    private sealed class LinkSet
    {
        private readonly Dictionary<(string, string, string), DependencyLink> _links = [];

        public int Count => _links.Count;

        public void AddHop(string sourceId, string targetId, Hop hop, string role)
        {
            _ = Add(sourceId, targetId, StyleOf(hop.Kinds), role, hop.Tags);
        }

        public DependencyLink Add(string sourceId, string targetId, string style, string role, IReadOnlyList<string> tags)
        {
            var key = (sourceId, targetId, style);
            if (_links.TryGetValue(key, out DependencyLink? existing))
            {
                DependencyLink merged = existing with
                {
                    Weight = existing.Weight + 1,
                    Tags = existing.Tags.Count >= MAX_LINK_TAGS
                        ? existing.Tags
                        : [.. existing.Tags.Union(tags, StringComparer.Ordinal).Take(MAX_LINK_TAGS)],
                };
                _links[key] = merged;

                return merged;
            }

            var link = new DependencyLink
            {
                Id = $"l{_links.Count}",
                SourceId = sourceId,
                TargetId = targetId,
                Style = style,
                Role = role,
                Tags = [.. tags.Distinct(StringComparer.Ordinal).Take(MAX_LINK_TAGS)],
            };
            _links[key] = link;

            return link;
        }

        public List<DependencyLink> ToList()
        {
            return [.. _links.Values];
        }

        private static string StyleOf(HashSet<DependencyEdgeKind> kinds)
        {
            if (kinds.Contains(DependencyEdgeKind.Data))
            {
                return DependencyLinkStyles.DATA;
            }

            return kinds.Contains(DependencyEdgeKind.Document) ? DependencyLinkStyles.DOCUMENT : DependencyLinkStyles.COUNTERPARTY;
        }
    }
}
