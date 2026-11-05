using System.Globalization;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Core.Workflows.Dependencies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Proposals;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using Newtonsoft.Json.Linq;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

public enum EditorAgentItemKind
{
    Edited,
    Added,
    Removed,
    Moved,
}

/// <summary>One block, or one other item, with every change the agent proposed to it.</summary>
/// <param name="Key">The item's group key, such as <c>Block:</c> and the block's key in the proposal.</param>
/// <param name="Notes">What the agent said about it in its summary.</param>
/// <param name="ReadBy">For a removed block, the blocks in the workflow that read its output.</param>
public sealed record EditorAgentReviewItem(
    string Key,
    string Title,
    string? Subtitle,
    EditorAgentItemKind Kind,
    IReadOnlyList<EditorAgentChangeView> Changes,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> ReadBy);

/// <summary>A block that arrives inside a new section, which is accepted or rejected with that section.</summary>
public sealed record EditorAgentNewBlock(string Name, string? Type, string? Instructions);

/// <summary>A section with its own changes, a reorder of its blocks, and the blocks the agent changed in it.</summary>
/// <param name="Changes">The section's own changes: added, removed, renamed.</param>
/// <param name="Reorder">The <c>Order</c> edits that change the order of the section's blocks; empty when it keeps its order.</param>
public sealed record EditorAgentReviewSection(
    string Key,
    string Title,
    EditorAgentItemKind Kind,
    IReadOnlyList<EditorAgentChangeView> Changes,
    IReadOnlyList<EditorAgentChangeView> Reorder,
    IReadOnlyList<string> OrderBefore,
    IReadOnlyList<string> OrderAfter,
    IReadOnlyList<string> Notes,
    IReadOnlyList<EditorAgentReviewItem> Blocks)
{
    /// <summary>For a new section, the blocks it brings.</summary>
    public IReadOnlyList<EditorAgentNewBlock> NewBlocks { get; init; } = [];

    public IEnumerable<EditorAgentChangeView> AllChanges => Changes.Concat(Reorder).Concat(Blocks.SelectMany(static block => block.Changes));
}

/// <summary>
/// A proposal laid out for review: section by section in workflow order, each block once with all
/// its changes, then the workflow's other items. Renumbering the agent did not mean as a reorder is
/// left out; it applies with the change that caused it.
/// </summary>
public sealed record EditorAgentReviewOutline(
    IReadOnlyList<EditorAgentReviewSection> Sections,
    IReadOnlyList<EditorAgentReviewItem> Others,
    IReadOnlyList<EditorAgentChangeView> SectionReorder,
    IReadOnlyList<string> SectionOrderBefore,
    IReadOnlyList<string> SectionOrderAfter)
{
    private const string UNNAMED_BLOCK = "Unnamed block";
    private const string UNTITLED_SECTION = "Untitled section";
    private const string ORDER_FIELD = "Order";
    private const string NEW_PREFIX = "new-";

    public IEnumerable<EditorAgentChangeView> AllChanges =>
        Sections.SelectMany(static section => section.AllChanges)
            .Concat(Others.SelectMany(static item => item.Changes))
            .Concat(SectionReorder);

    public int BlockCount => Sections.Sum(static section => section.Blocks.Count + section.NewBlocks.Count);

    public static EditorAgentReviewOutline Build(EditorAgentProposalView proposal, Workflow workflow)
    {
        List<EditorAgentChangeView> shown = [.. proposal.Changes.Where(static view => !view.Change.Incidental)];
        Dictionary<string, Block> blocks = workflow.GetAllBlocks()
            .Where(static block => block.StableBlockId is not null)
            .GroupBy(static block => block.StableBlockId!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var sectionTitles = workflow.Sections
            .Where(static section => section.StableSectionId is not null)
            .GroupBy(static section => section.StableSectionId!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => Title(group.First()), StringComparer.Ordinal);
        foreach (ProposedChange added in shown.Select(static view => view.Change)
            .Where(static change => change is { Scope: ProposedChangeScope.Section, Kind: ProposedChangeKind.Added } && change.TargetKey is not null))
        {
            sectionTitles[added.TargetKey!] = Blank(added.Name) ?? UNTITLED_SECTION;
        }

        List<string> sectionKeys = [.. workflow.Sections.OrderBy(static section => section.Order)
            .Select(static section => section.StableSectionId)
            .OfType<string>()];
        foreach (ProposedChange change in shown.Select(static view => view.Change))
        {
            string? key = change.Scope switch
            {
                ProposedChangeScope.Section => change.TargetKey,
                ProposedChangeScope.Block => change.SectionKey,
                _ => null,
            };
            if (key is not null && !sectionKeys.Contains(key))
            {
                sectionKeys.Add(key);
            }
        }

        Lazy<WorkflowDependencyMap> map = new(() => WorkflowDependencyMap.ForTemplate(workflow));
        var sections = new List<EditorAgentReviewSection>();
        foreach (string sectionKey in sectionKeys)
        {
            List<EditorAgentChangeView> own = [.. shown.Where(view => view.Change.Scope == ProposedChangeScope.Section
                && view.Change.TargetKey == sectionKey
                && view.Change.Field != ORDER_FIELD)];
            List<EditorAgentChangeView> inSection = [.. shown.Where(view => view.Change.Scope == ProposedChangeScope.Block && view.Change.SectionKey == sectionKey)];
            List<EditorAgentChangeView> reorder = [.. inSection.Where(static view => view.Change.Field == ORDER_FIELD)];
            List<EditorAgentReviewItem> items = [.. inSection
                .Where(static view => view.Change.Field != ORDER_FIELD)
                .GroupBy(static view => view.Change.TargetKey ?? string.Empty, StringComparer.Ordinal)
                .OrderBy(group => Position(group.Key, blocks, proposal))
                .Select(group => BlockItem(group.Key, [.. group], proposal, blocks, map))];
            if (own.Count == 0 && reorder.Count == 0 && items.Count == 0)
            {
                continue;
            }

            Section? live = workflow.Sections.FirstOrDefault(section => section.StableSectionId == sectionKey);
            (List<string> before, List<string> after) = reorder.Count == 0 || live is null ? ([], []) : BlockOrder(live, proposal);
            sections.Add(new EditorAgentReviewSection(
                sectionKey,
                sectionTitles.GetValueOrDefault(sectionKey) ?? UNTITLED_SECTION,
                KindOf(own.Select(static view => view.Change)),
                own,
                reorder,
                before,
                after,
                Notes(proposal, entry => entry.Section is string named
                    && (named == sectionKey || SameTitle(named, sectionTitles.GetValueOrDefault(sectionKey)))),
                items)
            {
                NewBlocks = NewBlocksOf(own),
            });
        }

        List<EditorAgentChangeView> sectionReorder = [.. shown.Where(static view => view.Change is { Scope: ProposedChangeScope.Section, Field: ORDER_FIELD })];
        (List<string> sectionsBefore, List<string> sectionsAfter) = sectionReorder.Count == 0 ? ([], []) : SectionOrder(workflow, proposal);
        List<EditorAgentReviewItem> others = [.. EditorAgentChangeText.Group(
                proposal with { Changes = [.. shown.Where(static view => view.Change.Scope is not (ProposedChangeScope.Block or ProposedChangeScope.Section))] },
                workflow)
            .Select(static group => new EditorAgentReviewItem(
                group.Key, group.Title, group.Subtitle, KindOf(group.Changes.Select(static view => view.Change)), group.Changes, [], []))];

        return new EditorAgentReviewOutline(sections, others, sectionReorder, sectionsBefore, sectionsAfter);
    }

    private static EditorAgentReviewItem BlockItem(
        string key,
        List<EditorAgentChangeView> changes,
        EditorAgentProposalView proposal,
        Dictionary<string, Block> blocks,
        Lazy<WorkflowDependencyMap> map)
    {
        ProposedChange first = changes[0].Change;
        Block? live = blocks.GetValueOrDefault(key);
        string title = Blank(first.Name) ?? Blank(live?.Name) ?? first.Tag ?? live?.ReplacementTag ?? UNNAMED_BLOCK;
        string? type = first.BlockType is string named && Enum.TryParse(named, out Domain.Entities.Workflows.Enums.BlockTypes parsed)
            ? BlockTypeVisuals.Label(parsed)
            : live is null ? null : BlockTypeVisuals.Label(live.Type);
        EditorAgentItemKind kind = KindOf(changes.Select(static view => view.Change));
        string? tag = first.Tag ?? live?.ReplacementTag;

        return new EditorAgentReviewItem(
            EditorAgentChangeText.GroupKey(first),
            title,
            type is null ? null : $"{type} block",
            kind,
            changes,
            Notes(proposal, entry => entry.StableBlockId is string id && (id == key || id == tag)),
            kind == EditorAgentItemKind.Removed && live is not null ? ReadersOf(live, map.Value) : []);
    }

    /// <summary>A summary's name for a section: its title, or <c>new-</c> and its title for one the agent added.</summary>
    private static bool SameTitle(string named, string? title)
    {
        return title is not null
            && (string.Equals(named, title, StringComparison.OrdinalIgnoreCase)
                || string.Equals(named, NEW_PREFIX + title, StringComparison.OrdinalIgnoreCase));
    }

    private static List<EditorAgentNewBlock> NewBlocksOf(List<EditorAgentChangeView> sectionChanges)
    {
        ProposedChange? added = sectionChanges.Select(static view => view.Change).FirstOrDefault(static change => change.Kind == ProposedChangeKind.Added);
        if (added?.NewJson is not string json || JObject.Parse(json)["Blocks"] is not JArray blocks)
        {
            return [];
        }

        return [.. blocks.OfType<JObject>()
            .OrderBy(static block => block[ORDER_FIELD] is JValue { Type: JTokenType.Integer } order ? (long)order : long.MaxValue)
            .Select(static block => new EditorAgentNewBlock(
                Blank((string?)block["Name"]) ?? (string?)block["ReplacementTag"] ?? UNNAMED_BLOCK,
                TypeLabel(block["Type"]),
                EditorAgentChangeText.InstructionsOf(block["Properties"])))];
    }

    private static string? TypeLabel(JToken? type)
    {
        return type switch
        {
            JValue { Type: JTokenType.Integer } number when Enum.IsDefined(typeof(Domain.Entities.Workflows.Enums.BlockTypes), (int)number)
                => $"{BlockTypeVisuals.Label((Domain.Entities.Workflows.Enums.BlockTypes)(int)number)} block",
            JValue { Type: JTokenType.String } name when Enum.TryParse((string?)name, out Domain.Entities.Workflows.Enums.BlockTypes parsed)
                => $"{BlockTypeVisuals.Label(parsed)} block",
            _ => null,
        };
    }

    private static EditorAgentItemKind KindOf(IEnumerable<ProposedChange> changes)
    {
        List<ProposedChange> list = [.. changes];
        return list.Any(static change => change.Kind == ProposedChangeKind.Added) ? EditorAgentItemKind.Added
            : list.Any(static change => change.Kind == ProposedChangeKind.Removed) ? EditorAgentItemKind.Removed
            : list.Any(static change => change.Kind == ProposedChangeKind.Moved) ? EditorAgentItemKind.Moved
            : EditorAgentItemKind.Edited;
    }

    private static List<string> Notes(EditorAgentProposalView proposal, Func<ProposalSummaryEntry, bool> about)
    {
        return [.. proposal.Summary.Where(about).Select(static entry => entry.What).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The names of the blocks that read this block's output, which lose their input when it goes.</summary>
    private static List<string> ReadersOf(Block block, WorkflowDependencyMap map)
    {
        return [.. map.OutgoingEdges(block.Id)
            .Select(edge => map.FindBlock(edge.TargetId)?.Block)
            .OfType<Block>()
            .Where(reader => !ReferenceEquals(reader, block))
            .Select(static reader => Blank(reader.Name) ?? reader.ReplacementTag ?? UNNAMED_BLOCK)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Where a block sits once the proposal applies, so the cards follow the workflow's order.</summary>
    private static long Position(string key, Dictionary<string, Block> blocks, EditorAgentProposalView proposal)
    {
        ProposedChange? order = proposal.Changes.Select(static view => view.Change)
            .FirstOrDefault(change => change.TargetKey == key && change.Field == ORDER_FIELD);
        if (order?.NewJson is string json && long.TryParse(json, NumberStyles.Integer, CultureInfo.InvariantCulture, out long proposed))
        {
            return proposed;
        }

        ProposedChange? added = proposal.Changes.Select(static view => view.Change)
            .FirstOrDefault(change => change.TargetKey == key && change.Kind == ProposedChangeKind.Added);
        if (added?.NewJson is string addedJson && JObject.Parse(addedJson)[ORDER_FIELD] is JValue { Type: JTokenType.Integer } addedOrder)
        {
            return (long)addedOrder;
        }

        return blocks.TryGetValue(key, out Block? live) ? live.Order : long.MaxValue;
    }

    private static (List<string> Before, List<string> After) BlockOrder(Section section, EditorAgentProposalView proposal)
    {
        Dictionary<string, long> proposed = NewOrders(proposal, ProposedChangeScope.Block);
        List<Block> before = [.. section.Blocks.OrderBy(static block => block.Order)];
        List<Block> after = [.. before.OrderBy(block => block.StableBlockId is string id && proposed.TryGetValue(id, out long order) ? order : block.Order)];
        return ([.. before.Select(static block => Blank(block.Name) ?? block.ReplacementTag ?? UNNAMED_BLOCK)],
            [.. after.Select(static block => Blank(block.Name) ?? block.ReplacementTag ?? UNNAMED_BLOCK)]);
    }

    private static (List<string> Before, List<string> After) SectionOrder(Workflow workflow, EditorAgentProposalView proposal)
    {
        Dictionary<string, long> proposed = NewOrders(proposal, ProposedChangeScope.Section);
        List<Section> before = [.. workflow.Sections.OrderBy(static section => section.Order)];
        List<Section> after = [.. before.OrderBy(section => section.StableSectionId is string id && proposed.TryGetValue(id, out long order) ? order : section.Order)];
        return ([.. before.Select(Title)], [.. after.Select(Title)]);
    }

    private static Dictionary<string, long> NewOrders(EditorAgentProposalView proposal, ProposedChangeScope scope)
    {
        var orders = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (ProposedChange change in proposal.Changes.Select(static view => view.Change)
            .Where(change => change.Scope == scope && change.Field == ORDER_FIELD && change.TargetKey is not null))
        {
            if (long.TryParse(change.NewJson, NumberStyles.Integer, CultureInfo.InvariantCulture, out long order))
            {
                orders[change.TargetKey!] = order;
            }
        }

        return orders;
    }

    private static string Title(Section section)
    {
        return Blank(section.Title) ?? Blank(section.Name) ?? UNTITLED_SECTION;
    }

    private static string? Blank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
