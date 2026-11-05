using IniaAi.Common.Serialization;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Serialization;

/// <summary>
/// Normalizes a block or section into its shared-component definition form: a detached clone with
/// the root per-consumer fields zeroed — a block's Order, ReplacementTag and DataOutputName, a section's Order — so
/// instances differing only in placement or tagging produce identical definition payloads and content
/// hashes. Every block's failure setting (<see cref="BlockProperties.IsCritical"/>) is per-workflow too and is
/// cleared the same way, on a section's inner blocks as well, so marking a copy to stop the run is not drift and
/// never travels to another workflow. Document-class wiring is shared content, not a per-consumer field: a section's inner blocks
/// always carried theirs, and a block component carries its own for the same reason — a component that
/// dropped its wiring would silently re-wire every consumer it grafts into. Presentation — a section's Name, Title
/// and GroupColor, a block's Name and OutputTitleTemplate — stays in the PAYLOAD so a fresh insert
/// lands labelled, but is stripped before hashing, so labels neither define component identity nor
/// count as drift; the graft methods preserve the consumer's own labels to match. Instance
/// identity and link fields never reach the payload (the canonicalizer's HASH profile strips them).
/// Comparing a MATERIALIZED instance against a definition is only meaningful after reverse-applying
/// the instance's bindings — the bindings-taking overloads do exactly that, renaming instance tags
/// back to their slot tags on the detached clone; raw hash equality holds just for unbound content.
/// </summary>
public sealed class SharedComponentContentNormalizer
{
    public static readonly IReadOnlyList<string> SectionPresentationMembers =
    [
        nameof(Section.Name),
        nameof(Section.Title),
        nameof(Section.GroupColor),
    ];

    public static readonly IReadOnlyList<string> BlockPresentationMembers =
    [
        nameof(Block.Name),
        nameof(Block.OutputTitleTemplate),
    ];

    public static readonly IReadOnlyList<string> PerWorkflowBlockProperties =
    [
        nameof(BlockProperties.IsCritical),
    ];

    public static readonly IReadOnlyList<string> SignatureBlockProperties =
    [
        nameof(IHasSignatureFields.SignatureFields),
    ];

    private readonly WorkflowCanonicalizer _canonicalizer;

    public SharedComponentContentNormalizer(WorkflowCanonicalizer canonicalizer)
    {
        _canonicalizer = canonicalizer;
    }

    public static bool IsSectionDefinition(string definitionJson)
    {
        try
        {
            return JsonText.Parse(definitionJson) is JObject obj && IsSectionShaped(obj);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string ToDefinitionJson(Block block)
    {
        return ToDefinitionJson(block, bindings: null);
    }

    public string ToDefinitionJson(Block block, IReadOnlyDictionary<string, string>? bindings)
    {
        Block normalized = NormalizeBlock(block);
        if (bindings is { Count: > 0 })
        {
            normalized.ApplyReplacementTagRenames(InvertBindings(bindings));
        }

        return _canonicalizer.ToComponentCanonicalJson(normalized);
    }

    public string ToDefinitionJson(Section section)
    {
        return ToDefinitionJson(section, bindings: null);
    }

    public string ToDefinitionJson(Section section, IReadOnlyDictionary<string, string>? bindings)
    {
        Section normalized = NormalizeSection(section);
        Dictionary<string, string>? instanceTagToSlotTag = null;
        if (bindings is { Count: > 0 })
        {
            instanceTagToSlotTag = InvertBindings(bindings);
            normalized.ApplyReplacementTagRenames(instanceTagToSlotTag);
        }

        DeriveEntries(normalized, instanceTagToSlotTag);

        return _canonicalizer.ToComponentCanonicalJson(normalized);
    }

    public string ComputeContentHash(Block block)
    {
        return ComputeContentHash(block, bindings: null);
    }

    public string ComputeContentHash(Block block, IReadOnlyDictionary<string, string>? bindings)
    {
        return ComputeContentHashOfDefinitionJson(ToDefinitionJson(block, bindings));
    }

    public string ComputeEntryContentHash(Section definition, SectionEntry entry)
    {
        if (entry.Kind == SectionEntryKind.ComponentRef)
        {
            IEnumerable<string> boundPairs = entry.Bindings is not { Count: > 0 }
                ? []
                : entry.Bindings
                    .OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => $"{p.Key}={p.Value}");

            return _canonicalizer.ComputeContentHash(
                $"ref:{entry.SharedComponentId}:{string.Join("|", boundPairs)}");
        }

        Block? inlineBlock = definition.FindBlockByReplacementTag(entry.SlotTag);

        return inlineBlock is null
            ? _canonicalizer.ComputeContentHash($"missing-inline:{entry.SlotTag}")
            : ComputeContentHash(inlineBlock);
    }

    public string ComputeContentHashOfDefinitionJson(string definitionJson)
    {
        return _canonicalizer.ComputeContentHash(WithoutPresentation(definitionJson));
    }

    public string ComputeSignatureInsensitiveContentHash(string definitionJson)
    {
        return _canonicalizer.ComputeContentHash(WithoutSignatureFields(WithoutPresentation(definitionJson)));
    }

    /// <summary>
    /// The content hash a stored Library version has when today's model writes it. A version stored
    /// before the model dropped a field keeps that field in its payload and in its stored hash, and a
    /// copy of the same content written today has neither, so a live copy compares against this hash.
    /// The stored hash stays the version's identity.
    /// </summary>
    public string ComputeStoredContentHash(string storedDefinitionJson)
    {
        return ComputeContentHashOfDefinitionJson(RebuildDefinitionJson(storedDefinitionJson));
    }

    /// <summary>
    /// The signature-insensitive hash of a stored Library version, rebuilt as
    /// <see cref="ComputeStoredContentHash"/> rebuilds it.
    /// </summary>
    public string ComputeStoredSignatureInsensitiveContentHash(string storedDefinitionJson)
    {
        return ComputeSignatureInsensitiveContentHash(RebuildDefinitionJson(storedDefinitionJson));
    }

    /// <summary>
    /// A stored definition read into today's block or section model and written out again, the way
    /// a copy's definition is written. A field the model no longer has leaves the payload. An
    /// unreadable payload comes back unchanged.
    /// </summary>
    public string RebuildDefinitionJson(string definitionJson)
    {
        if (IsSectionDefinition(definitionJson))
        {
            return Section.FromJson(definitionJson) is { } section ? ToDefinitionJson(section) : definitionJson;
        }

        return Block.FromJson(definitionJson) is { } block ? ToDefinitionJson(block) : definitionJson;
    }

    public string ComputeSignatureInsensitiveContentHash(Block block, IReadOnlyDictionary<string, string>? bindings)
    {
        return ComputeSignatureInsensitiveContentHash(ToDefinitionJson(block, bindings));
    }

    public string ComputeSignatureInsensitiveContentHash(Section section, IReadOnlyDictionary<string, string>? bindings)
    {
        return ComputeSignatureInsensitiveContentHash(ToDefinitionJson(section, bindings));
    }

    public string ComputeSignatureInsensitiveEntryContentHash(Section definition, SectionEntry entry)
    {
        if (entry.Kind == SectionEntryKind.ComponentRef)
        {
            return ComputeEntryContentHash(definition, entry);
        }

        Block? inlineBlock = definition.FindBlockByReplacementTag(entry.SlotTag);

        return inlineBlock is null
            ? ComputeEntryContentHash(definition, entry)
            : ComputeSignatureInsensitiveContentHash(inlineBlock, bindings: null);
    }

    /// <summary>
    /// The definition without the per-workflow failure setting of its blocks. A version stored before the
    /// setting left definitions can still carry it; a diff of stored bytes drops it here, as the rebuilt
    /// comparison hash does.
    /// </summary>
    public static string WithoutPerWorkflowSettings(string definitionJson)
    {
        return WithoutBlockProperties(definitionJson, PerWorkflowBlockProperties);
    }

    public static string WithoutSignatureFields(string definitionJson)
    {
        return WithoutBlockProperties(definitionJson, SignatureBlockProperties);
    }

    /// <summary>
    /// The definition with the named members removed from the Properties of its block, or of each
    /// block of a section, in one pass. An unreadable definition comes back unchanged.
    /// </summary>
    public static string WithoutBlockProperties(string definitionJson, IReadOnlyCollection<string> propertyNames)
    {
        JObject? root;
        try
        {
            root = JsonText.Parse(definitionJson) as JObject;
        }
        catch (JsonException)
        {
            return definitionJson;
        }

        if (root is null)
        {
            return definitionJson;
        }

        if (IsSectionShaped(root))
        {
            if (root[nameof(Section.Blocks)] is JArray blocks)
            {
                foreach (JToken block in blocks)
                {
                    if (block is JObject blockObject)
                    {
                        RemoveBlockProperties(blockObject, propertyNames);
                    }
                }
            }
        }
        else
        {
            RemoveBlockProperties(root, propertyNames);
        }

        return root.ToString(Formatting.None);
    }

    /// <summary>
    /// A stored block definition read to build a new copy from. A version stored before the failure
    /// setting left definitions can still carry it, so the reader clears it and the new copy starts unset.
    /// </summary>
    public static Block? ReadBlockDefinitionForNewCopy(string definitionJson)
    {
        Block? block = Block.FromJson(definitionJson);
        if (block is not null)
        {
            ClearPerWorkflowSettings(block);
        }

        return block;
    }

    /// <summary>
    /// A stored section definition read to build a new copy from, with every inner block's failure
    /// setting cleared as <see cref="ReadBlockDefinitionForNewCopy"/> clears a block's.
    /// </summary>
    public static Section? ReadSectionDefinitionForNewCopy(string definitionJson)
    {
        Section? section = Section.FromJson(definitionJson);
        if (section is not null)
        {
            foreach (Block block in section.Blocks)
            {
                ClearPerWorkflowSettings(block);
            }
        }

        return section;
    }

    private static void RemoveBlockProperties(JObject blockObject, IReadOnlyCollection<string> propertyNames)
    {
        if (blockObject[nameof(Block.Properties)] is JObject properties)
        {
            foreach (string propertyName in propertyNames)
            {
                _ = properties.Remove(propertyName);
            }
        }
    }

    private static string WithoutPresentation(string definitionJson)
    {
        JObject? root;
        try
        {
            root = JsonText.Parse(definitionJson) as JObject;
        }
        catch (JsonException)
        {
            return definitionJson;
        }

        if (root is null)
        {
            return definitionJson;
        }

        if (IsSectionShaped(root))
        {
            RemoveMembers(root, SectionPresentationMembers);
            if (root[nameof(Section.Blocks)] is JArray blocks)
            {
                foreach (JToken block in blocks)
                {
                    if (block is JObject blockObject)
                    {
                        RemoveMembers(blockObject, BlockPresentationMembers);
                    }
                }
            }
        }
        else
        {
            RemoveMembers(root, BlockPresentationMembers);
        }

        return root.ToString(Formatting.None);
    }

    private static bool IsSectionShaped(JObject definition)
    {
        return definition[nameof(Section.Blocks)] is JArray || definition[nameof(Section.Entries)] is JArray;
    }

    private static void RemoveMembers(JObject node, IReadOnlyList<string> members)
    {
        foreach (string member in members)
        {
            _ = node.Remove(member);
        }
    }

    public string? ReconstructDefinitionJson(Section instance, Section? dLinkDefinition)
    {
        if (dLinkDefinition is null)
        {
            return null;
        }

        Dictionary<string, SharedComponentSlotState>? slotStates = instance.SharedComponentSlotStates;
        bool hasOverrideRecords = slotStates is { Count: > 0 };
        Dictionary<string, string>? bindings = instance.SharedComponentBindings;
        List<SectionEntry> dLinkEntries = dLinkDefinition.EffectiveEntries();
        HashSet<string> instanceTags = new(
            instance.Blocks.Select(b => b.ReplacementTag), StringComparer.Ordinal);
        bool slotsAccountedFor = dLinkEntries.All(e =>
            (bindings is not null && bindings.ContainsKey(e.SlotTag))
            || (slotStates is not null && slotStates.ContainsKey(e.SlotTag))
            || instanceTags.Contains(e.SlotTag));
        HashSet<string> landedSlotTags = new(
            dLinkEntries.Select(e => bindings?.GetValueOrDefault(e.SlotTag) ?? e.SlotTag),
            StringComparer.Ordinal);
        bool hasExtras = slotsAccountedFor
            && instance.Blocks.Any(b => !landedSlotTags.Contains(b.ReplacementTag));
        if (!hasOverrideRecords && !hasExtras)
        {
            return null;
        }

        Section normalized = NormalizeSection(instance);
        List<Block> extras = slotsAccountedFor
            ? [.. normalized.Blocks.Where(b => !landedSlotTags.Contains(b.ReplacementTag))]
            : [];
        if (bindings is { Count: > 0 })
        {
            normalized.ApplyReplacementTagRenames(InvertBindings(bindings));
        }

        foreach (Block extra in extras)
        {
            _ = normalized.Blocks.Remove(extra);
        }

        List<Block> sequence = [.. normalized.Blocks.OrderBy(b => b.Order)];
        if (slotStates is { Count: > 0 })
        {
            Dictionary<string, Block> dLinkBlocksByTag = new(StringComparer.Ordinal);
            foreach (Block dBlock in dLinkDefinition.Blocks)
            {
                _ = dLinkBlocksByTag.TryAdd(dBlock.ReplacementTag, dBlock);
            }

            Dictionary<string, int> dLinkEntryIndexByTag = new(StringComparer.Ordinal);
            for (int i = 0; i < dLinkEntries.Count; i++)
            {
                _ = dLinkEntryIndexByTag.TryAdd(dLinkEntries[i].SlotTag, i);
            }

            List<string> orderedOverrides = [.. slotStates.Keys
                .OrderBy(tag => dLinkEntryIndexByTag.GetValueOrDefault(tag, -1))];
            foreach (string slotTag in orderedOverrides)
            {
                int dLinkEntryIndex = dLinkEntryIndexByTag.GetValueOrDefault(slotTag, -1);
                SectionEntry? dLinkEntry = dLinkEntryIndex >= 0 ? dLinkEntries[dLinkEntryIndex] : null;
                Block backfill;
                if (dLinkEntry is { Kind: SectionEntryKind.ComponentRef })
                {
                    backfill = new Block(BlockTypes.AiQuery, normalized, slotTag)
                    {
                        SharedComponentId = dLinkEntry.SharedComponentId,
                        SharedComponentLinkState = SharedComponentLinkState.Linked,
                        SharedComponentBindings = dLinkEntry.Bindings is { Count: > 0 }
                            ? new Dictionary<string, string>(dLinkEntry.Bindings, StringComparer.Ordinal)
                            : null,
                    };
                }
                else if (dLinkBlocksByTag.TryGetValue(slotTag, out Block? dBlock))
                {
                    backfill = dBlock.CloneToSection(normalized, -1, slotTag);
                }
                else
                {
                    continue;
                }

                if (slotStates[slotTag] == SharedComponentSlotState.BrokenOut)
                {
                    int liveIndex = sequence.FindIndex(
                        b => string.Equals(b.ReplacementTag, slotTag, StringComparison.Ordinal));
                    if (liveIndex >= 0)
                    {
                        sequence[liveIndex] = backfill;
                        continue;
                    }
                }

                sequence.Insert(
                    Math.Clamp(dLinkEntryIndex < 0 ? sequence.Count : dLinkEntryIndex, 0, sequence.Count),
                    backfill);
            }
        }

        normalized.Blocks.Clear();
        for (int i = 0; i < sequence.Count; i++)
        {
            sequence[i].Order = i;
            ClearPerWorkflowSettings(sequence[i]);
            normalized.Blocks.Add(sequence[i]);
        }

        normalized.Entries = null;
        DeriveEntries(normalized, instanceTagToSlotTag: null);

        return _canonicalizer.ToComponentCanonicalJson(normalized);
    }

    public string ComputeContentHash(Section section)
    {
        return ComputeContentHash(section, bindings: null);
    }

    public string ComputeContentHash(Section section, IReadOnlyDictionary<string, string>? bindings)
    {
        return ComputeContentHashOfDefinitionJson(ToDefinitionJson(section, bindings));
    }

    private static Block NormalizeBlock(Block block)
    {
        var scratchParent = new Section();
        Block normalized = block.CloneToSection(scratchParent);
        normalized.Order = 0;
        normalized.ReplacementTag = string.Empty;
        normalized.DataOutputName = null;
        ClearPerWorkflowSettings(normalized);

        return normalized;
    }

    private static Section NormalizeSection(Section section)
    {
        var normalized = new Section();
        section.CloneTo(normalized);
        normalized.Order = 0;
        foreach (Block block in normalized.Blocks)
        {
            ClearPerWorkflowSettings(block);
        }

        return normalized;
    }

    private static void ClearPerWorkflowSettings(Block block)
    {
        block.Properties.IsCritical = null;
    }

    private static void DeriveEntries(
        Section normalized,
        IReadOnlyDictionary<string, string>? instanceTagToSlotTag)
    {
        if (normalized.Entries is { Count: > 0 })
        {
            return;
        }

        List<Block> ordered = [.. normalized.Blocks.OrderBy(b => b.Order)];
        List<SectionEntry> entries = new(ordered.Count);
        bool anyRef = false;

        foreach (Block block in ordered)
        {
            if (block.IsActivelyLinked())
            {
                anyRef = true;
                entries.Add(new SectionEntry
                {
                    SlotTag = block.ReplacementTag,
                    Kind = SectionEntryKind.ComponentRef,
                    SharedComponentId = block.SharedComponentId,
                    Bindings = ComposeEntryBindings(block.SharedComponentBindings, instanceTagToSlotTag),
                });
            }
            else
            {
                entries.Add(new SectionEntry
                {
                    SlotTag = block.ReplacementTag,
                    Kind = SectionEntryKind.Inline,
                });
            }
        }

        if (!anyRef)
        {
            return;
        }

        int inlineOrder = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            if (entries[i].Kind == SectionEntryKind.ComponentRef)
            {
                _ = normalized.Blocks.Remove(ordered[i]);
            }
            else
            {
                ordered[i].Order = inlineOrder++;
            }
        }

        normalized.Entries = entries;
    }

    private static Dictionary<string, string>? ComposeEntryBindings(
        Dictionary<string, string>? blockBindings,
        IReadOnlyDictionary<string, string>? instanceTagToSlotTag)
    {
        if (blockBindings is not { Count: > 0 })
        {
            return null;
        }

        Dictionary<string, string> composed = new(blockBindings.Count, StringComparer.Ordinal);
        foreach ((string placeholder, string instanceTag) in blockBindings)
        {
            string slotTag = instanceTagToSlotTag is not null
                && instanceTagToSlotTag.TryGetValue(instanceTag, out string? mapped)
                ? mapped
                : instanceTag;

            if (!string.Equals(placeholder, slotTag, StringComparison.Ordinal))
            {
                composed[placeholder] = slotTag;
            }
        }

        return composed.Count > 0 ? composed : null;
    }

    private static Dictionary<string, string> InvertBindings(IReadOnlyDictionary<string, string> bindings)
    {
        Dictionary<string, string> instanceTagToSlotTag = new(bindings.Count, StringComparer.Ordinal);
        foreach ((string slotTag, string instanceTag) in bindings)
        {
            instanceTagToSlotTag[instanceTag] = slotTag;
        }

        return instanceTagToSlotTag;
    }
}
