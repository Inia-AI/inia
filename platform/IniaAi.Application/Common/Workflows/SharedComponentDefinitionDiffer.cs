using IniaAi.Common.Serialization;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Diffs two stored shared-component definitions by wrapping each as a minimal one-section
/// workflow JSON — a section definition becomes the single section, a block definition the single
/// block of a bare section — so <see cref="WorkflowDiffService"/>'s tag-based matching and the
/// narrative arms apply to component history unchanged. Wrapping is raw JSON surgery on the stored
/// payload; definitions are never round-tripped through entity deserialization, so stored bytes
/// diff verbatim. Ref entries have no block content the tag matcher could pair, so their slots are
/// lifted out of the wrap entirely and re-emitted as synthesized per-slot rows carrying the
/// reference transition (<see cref="SharedComponentDiffFields.ID"/> before/after) — an inlining or
/// a conversion reads as one reference change instead of type-switch or content noise.
/// </summary>
public class SharedComponentDefinitionDiffer
{
    public const string BLOCK_ORDER_FIELD = "BlockOrder";

    private static readonly IReadOnlyList<string> _syncIgnoredBlockProperties =
    [
        .. SharedComponentContentNormalizer.SignatureBlockProperties,
        .. SharedComponentContentNormalizer.PerWorkflowBlockProperties,
    ];

    private readonly WorkflowDiffService _diffService;
    private readonly WorkflowNarrativeGenerator _narrativeGenerator;

    public SharedComponentDefinitionDiffer(
        WorkflowDiffService diffService,
        WorkflowNarrativeGenerator narrativeGenerator)
    {
        _diffService = diffService ?? throw new ArgumentNullException(nameof(diffService));
        _narrativeGenerator = narrativeGenerator ?? throw new ArgumentNullException(nameof(narrativeGenerator));
    }

    /// <exception cref="JsonException">Either definition is not parseable JSON.</exception>
    public WorkflowDiff Compare(SharedComponentKind kind, string fromDefinitionJson, string toDefinitionJson)
    {
        if (kind == SharedComponentKind.Block)
        {
            return _diffService.Compare(
                WrapAsWorkflowJson(kind, fromDefinitionJson),
                WrapAsWorkflowJson(kind, toDefinitionJson));
        }

        JObject fromDefinition = JsonText.ParseObject(fromDefinitionJson);
        JObject toDefinition = JsonText.ParseObject(toDefinitionJson);
        Dictionary<string, RefEntry> fromRefs = CollectRefEntries(fromDefinition);
        Dictionary<string, RefEntry> toRefs = CollectRefEntries(toDefinition);
        HashSet<string> refTags = new(fromRefs.Keys.Concat(toRefs.Keys), StringComparer.Ordinal);

        WorkflowDiff diff = _diffService.Compare(
            WrapSectionJson(fromDefinition, refTags),
            WrapSectionJson(toDefinition, refTags));
        List<BlockDiff> refRows = SynthesizeRefRows(fromDefinition, toDefinition, fromRefs, toRefs, refTags);
        if (refRows.Count == 0)
        {
            return diff;
        }

        return diff with
        {
            Blocks = [.. diff.Blocks
                .Concat(refRows)
                .OrderBy(b => b.ReplacementTag, StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// The differences that make Library sync flag a copy: signature fields, each block's failure
    /// setting and presentation (names, titles, group colour, output title) are left out, as the comparison hash leaves
    /// them out, so a copy that only relabels its blocks shows no difference. The comparison hash
    /// counts the order of a section's blocks, which the tag-matched diff does not see, so a
    /// reorder comes back as a <see cref="BLOCK_ORDER_FIELD"/> change on the section.
    /// </summary>
    /// <exception cref="JsonException">Either definition is not parseable JSON.</exception>
    public WorkflowDiff CompareContent(SharedComponentKind kind, string libraryDefinitionJson, string copyDefinitionJson)
    {
        WorkflowDiff diff = Compare(
            kind,
            SharedComponentContentNormalizer.WithoutBlockProperties(libraryDefinitionJson, _syncIgnoredBlockProperties),
            SharedComponentContentNormalizer.WithoutBlockProperties(copyDefinitionJson, _syncIgnoredBlockProperties));

        WorkflowDiff content = diff with
        {
            Sections = [.. diff.Sections
                .Select(section => section with
                {
                    FieldDiffs = WithoutFields(section.FieldDiffs, SharedComponentContentNormalizer.SectionPresentationMembers),
                })
                .Where(section => section.ChangeKind != DiffChangeKind.Modified || section.FieldDiffs.Count > 0)],
            Blocks = [.. diff.Blocks
                .Select(block => block with
                {
                    FieldDiffs = WithoutFields(block.FieldDiffs, SharedComponentContentNormalizer.BlockPresentationMembers),
                })
                .Where(block => block.ChangeKind != DiffChangeKind.Modified || block.FieldDiffs.Count > 0)],
        };
        if (kind != SharedComponentKind.Section)
        {
            return content;
        }

        JObject copyDefinition = JsonText.ParseObject(copyDefinitionJson);
        FieldDiff? blockOrder = BlockOrderDifference(JsonText.ParseObject(libraryDefinitionJson), copyDefinition);
        if (blockOrder is null)
        {
            return content;
        }

        return content with
        {
            Sections = content.Sections.Count == 0
                ? [new SectionDiff(SectionTitle(copyDefinition), 0, DiffChangeKind.Modified) { FieldDiffs = [blockOrder] }]
                : [.. content.Sections.Select(section => section with { FieldDiffs = [.. section.FieldDiffs, blockOrder] })],
        };
    }

    /// <summary>
    /// The plain-language summary of what changed between two definitions, or null when either
    /// definition is unreadable or the diff has nothing to say.
    /// </summary>
    public string? Describe(SharedComponentKind kind, string fromDefinitionJson, string toDefinitionJson)
    {
        try
        {
            string narrative = _narrativeGenerator.Generate(Compare(kind, fromDefinitionJson, toDefinitionJson));

            return narrative.Length == 0 ? null : narrative;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FieldDiff? BlockOrderDifference(JObject libraryDefinition, JObject copyDefinition)
    {
        List<string> libraryOrder = SlotOrder(libraryDefinition);
        List<string> copyOrder = SlotOrder(copyDefinition);
        HashSet<string> inBoth = new(libraryOrder.Intersect(copyOrder, StringComparer.Ordinal), StringComparer.Ordinal);
        List<string> libraryShared = [.. libraryOrder.Where(inBoth.Contains)];
        List<string> copyShared = [.. copyOrder.Where(inBoth.Contains)];
        if (libraryShared.SequenceEqual(copyShared, StringComparer.Ordinal))
        {
            return null;
        }

        return new FieldDiff(
            BLOCK_ORDER_FIELD,
            OrderText(libraryDefinition, libraryShared),
            OrderText(copyDefinition, copyShared));
    }

    private static List<string> SlotOrder(JObject definition)
    {
        if (definition[nameof(Section.Entries)] is JArray { Count: > 0 } entries)
        {
            return [.. entries.OfType<JObject>().Select(entry => (string?)entry[nameof(SectionEntry.SlotTag)] ?? string.Empty)];
        }

        return [.. (definition[nameof(Section.Blocks)] as JArray ?? [])
            .OfType<JObject>()
            .OrderBy(block => (int?)block[nameof(Block.Order)] ?? 0)
            .Select(block => (string?)block[nameof(Block.ReplacementTag)] ?? string.Empty)];
    }

    private static string OrderText(JObject definition, List<string> slotTags)
    {
        return string.Join("\n", slotTags.Select(tag =>
            (string?)FindBlock(definition, tag)?[nameof(Block.Name)] is { Length: > 0 } name ? name : tag));
    }

    private static string SectionTitle(JObject definition)
    {
        return (string?)definition[nameof(Section.Title)] is { Length: > 0 } title
            ? title
            : (string?)definition[nameof(Section.Name)] ?? string.Empty;
    }

    private static IReadOnlyList<FieldDiff> WithoutFields(IReadOnlyList<FieldDiff> fields, IReadOnlyList<string> leftOut)
    {
        return [.. fields.Where(field => !leftOut.Contains(field.FieldName, StringComparer.Ordinal))];
    }

    private static string WrapAsWorkflowJson(SharedComponentKind kind, string definitionJson)
    {
        JObject definition = JsonText.ParseObject(definitionJson);
        JObject sectionNode = kind == SharedComponentKind.Block
            ? new JObject(new JProperty(nameof(Section.Blocks), new JArray(definition)))
            : definition;

        return new JObject(new JProperty(nameof(Workflow.Sections), new JArray(sectionNode)))
            .ToString(Formatting.None);
    }

    private static string WrapSectionJson(JObject definition, HashSet<string> refTags)
    {
        var section = (JObject)definition.DeepClone();
        _ = section.Remove(nameof(Section.Entries));
        if (refTags.Count > 0 && section[nameof(Section.Blocks)] is JArray blocks)
        {
            foreach (JObject block in blocks
                .OfType<JObject>()
                .Where(b => refTags.Contains((string?)b[nameof(Block.ReplacementTag)] ?? string.Empty))
                .ToList())
            {
                block.Remove();
            }
        }

        return new JObject(new JProperty(nameof(Workflow.Sections), new JArray(section)))
            .ToString(Formatting.None);
    }

    private static Dictionary<string, RefEntry> CollectRefEntries(JObject definition)
    {
        var refs = new Dictionary<string, RefEntry>(StringComparer.Ordinal);
        if (definition[nameof(Section.Entries)] is not JArray entries)
        {
            return refs;
        }

        foreach (JObject entry in entries.OfType<JObject>())
        {
            if ((string?)entry[nameof(SectionEntry.Kind)] != nameof(SectionEntryKind.ComponentRef))
            {
                continue;
            }

            string slotTag = (string?)entry[nameof(SectionEntry.SlotTag)] ?? string.Empty;
            string componentId = (string?)entry[nameof(SectionEntry.SharedComponentId)] ?? string.Empty;
            refs[slotTag] = new RefEntry(componentId, SummarizeBindings(entry[nameof(SectionEntry.Bindings)]));
        }

        return refs;
    }

    private static string? SummarizeBindings(JToken? bindings)
    {
        if (bindings is not JObject bindingsObject || !bindingsObject.HasValues)
        {
            return null;
        }

        return string.Join(", ", bindingsObject.Properties()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={(string?)p.Value}"));
    }

    private static List<BlockDiff> SynthesizeRefRows(
        JObject fromDefinition,
        JObject toDefinition,
        Dictionary<string, RefEntry> fromRefs,
        Dictionary<string, RefEntry> toRefs,
        HashSet<string> refTags)
    {
        var rows = new List<BlockDiff>();
        foreach (string tag in refTags.OrderBy(t => t, StringComparer.Ordinal))
        {
            bool inFrom = fromRefs.TryGetValue(tag, out RefEntry? fromRef);
            bool inTo = toRefs.TryGetValue(tag, out RefEntry? toRef);

            if (inFrom && inTo)
            {
                var fieldDiffs = new List<FieldDiff>();
                if (!string.Equals(fromRef!.ComponentId, toRef!.ComponentId, StringComparison.Ordinal))
                {
                    fieldDiffs.Add(new FieldDiff(SharedComponentDiffFields.ID, fromRef.ComponentId, toRef.ComponentId));
                }

                if (!string.Equals(fromRef.BindingsSummary, toRef.BindingsSummary, StringComparison.Ordinal))
                {
                    fieldDiffs.Add(new FieldDiff(
                        nameof(Block.SharedComponentBindings), fromRef.BindingsSummary, toRef.BindingsSummary));
                }

                if (fieldDiffs.Count > 0)
                {
                    rows.Add(new BlockDiff(tag, tag, string.Empty, DiffChangeKind.Modified, fieldDiffs)
                    {
                        IsLibraryLinked = true,
                    });
                }

                continue;
            }

            if (inFrom)
            {
                rows.Add(RefTransitionRow(
                    toDefinition, tag, fromRef!.ComponentId, null, DiffChangeKind.Removed, isLibraryLinked: false));

                continue;
            }

            rows.Add(RefTransitionRow(
                fromDefinition, tag, null, toRef!.ComponentId, DiffChangeKind.Added, isLibraryLinked: true));
        }

        return rows;
    }

    private static BlockDiff RefTransitionRow(
        JObject counterpartDefinition,
        string tag,
        string? fromComponentId,
        string? toComponentId,
        DiffChangeKind noCounterpartKind,
        bool isLibraryLinked)
    {
        JObject? counterpartBlock = FindBlock(counterpartDefinition, tag);

        return new BlockDiff(
            tag,
            tag,
            counterpartBlock is null ? string.Empty : TypeNameOf(counterpartBlock),
            counterpartBlock is null ? noCounterpartKind : DiffChangeKind.Modified,
            [new FieldDiff(SharedComponentDiffFields.ID, fromComponentId, toComponentId)])
        {
            IsLibraryLinked = isLibraryLinked,
        };
    }

    private static JObject? FindBlock(JObject definition, string tag)
    {
        return (definition[nameof(Section.Blocks)] as JArray)
            ?.OfType<JObject>()
            .FirstOrDefault(b => string.Equals((string?)b[nameof(Block.ReplacementTag)], tag, StringComparison.Ordinal));
    }

    private static string TypeNameOf(JObject block)
    {
        return (string?)block[nameof(Block.Type)] ?? string.Empty;
    }

    private sealed record RefEntry(string ComponentId, string? BindingsSummary);
}
