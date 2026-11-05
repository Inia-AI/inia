using System.Text.RegularExpressions;
using ErrorOr;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The shared engine behind promote and materialize: node addressing, promote preconditions,
/// definition building, link stamping, and materialization (landing a definition copy, rewriting
/// colliding tags, recording bindings). Commands stay thin over this surface and the GUI reuses
/// the commands — never this engine directly.
/// </summary>
public interface ISharedComponentEditor
{
    ErrorOr<SharedComponentTarget> ResolveTarget(Workflow workflow, string? targetPath, string? targetStableId);

    ErrorOr<Section?> ResolveMaterializeSection(
        Workflow workflow,
        SharedComponentKind kind,
        string? targetSectionPath,
        string? targetSectionStableId);

    ErrorOr<Success> ValidatePromotable(SharedComponentTarget target);

    SharedComponentDefinition BuildDefinition(SharedComponentTarget target);

    string BuildContainerRefDefinitionJson(Section containerHeadDefinition, string slotTag, string blockComponentId);

    ErrorOr<ConvertToRefDefinition?> BuildConvertToRefDefinition(
        SharedComponentTarget target,
        Section containerHeadDefinition);

    void StampLink(SharedComponentTarget target, string sharedComponentId, string versionHash);

    ErrorOr<MaterializedNode> Materialize(
        Workflow workflow,
        SharedComponentKind kind,
        string componentId,
        string componentName,
        string versionHash,
        string definitionJson,
        Section? targetSection,
        int? index,
        string? requestedTag,
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads = null);
}

/// <summary>
/// A resolved promote/materialize target. Exactly one of <see cref="Block"/> and
/// <see cref="Section"/> is set; <see cref="ParentSection"/> is the containing section when the
/// target is a block.
/// </summary>
public sealed record SharedComponentTarget(
    SharedComponentKind Kind,
    Block? Block,
    Section? Section,
    Section? ParentSection,
    string CanonicalPath);

public sealed record SharedComponentDefinition(
    SharedComponentKind Kind,
    string DefinitionJson,
    string ContentHash,
    string DefaultName);

public sealed record ConvertToRefDefinition(
    string SlotTag,
    string DefinitionJson,
    string ContentHash,
    string DefaultName);

public sealed record MaterializedNode(
    string CanonicalPath,
    string? ReplacementTag,
    IReadOnlyDictionary<string, string>? Bindings);

public class SharedComponentEditor : ISharedComponentEditor
{
    public const string TARGET_PATH_PATTERN = @"^Sections\[(\d+)\](?:\.Blocks\[(\d+)\])?$";
    public const string SECTION_PATH_PATTERN = @"^Sections\[(\d+)\]$";

    private static readonly Regex _targetPathPattern = new(
        TARGET_PATH_PATTERN,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly WorkflowCanonicalizer _canonicalizer;
    private readonly SharedComponentContentNormalizer _normalizer;

    public SharedComponentEditor(WorkflowCanonicalizer canonicalizer, SharedComponentContentNormalizer normalizer)
    {
        _canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public ErrorOr<SharedComponentTarget> ResolveTarget(Workflow workflow, string? targetPath, string? targetStableId)
    {
        bool hasPath = !string.IsNullOrEmpty(targetPath);
        bool hasStableId = !string.IsNullOrEmpty(targetStableId);
        if (hasPath == hasStableId)
        {
            return Error.Validation(
                SharedComponentErrorCodes.INVALID_TARGET_LOCATOR,
                $"Provide exactly one of targetPath or targetStableId. [{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        }

        return hasPath
            ? ResolveByPath(workflow, targetPath!)
            : ResolveByStableId(workflow, targetStableId!);
    }

    public ErrorOr<Section?> ResolveMaterializeSection(
        Workflow workflow,
        SharedComponentKind kind,
        string? targetSectionPath,
        string? targetSectionStableId)
    {
        bool hasSectionLocator = !string.IsNullOrEmpty(targetSectionPath)
            || !string.IsNullOrEmpty(targetSectionStableId);
        if (kind != SharedComponentKind.Block)
        {
            return hasSectionLocator
                ? Error.Validation(
                    SharedComponentErrorCodes.INVALID_TARGET_LOCATOR,
                    "A section component lands at an index in the workflow and takes no section locator. "
                    + $"[{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]")
                : (Section?)null;
        }

        if (!hasSectionLocator)
        {
            return Error.Validation(
                SharedComponentErrorCodes.INVALID_TARGET_LOCATOR,
                "Materializing a block component requires targetSectionPath or targetSectionStableId. "
                + $"[{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        }

        ErrorOr<SharedComponentTarget> sectionTarget = ResolveTarget(
            workflow, targetSectionPath, targetSectionStableId);
        if (sectionTarget.IsError)
        {
            return sectionTarget.Errors;
        }

        if (sectionTarget.Value.Kind != SharedComponentKind.Section)
        {
            return Error.Validation(
                sectionTarget.Value.CanonicalPath,
                $"The target of a block materialization must be a section. [{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        }

        return sectionTarget.Value.Section;
    }

    /// <summary>
    /// A node with no link record promotes; so does a broken-out copy, which no longer follows its
    /// component and shares like local content, unless it sits inside an actively linked section,
    /// whose slot overrides Library sync resolves.
    /// </summary>
    public ErrorOr<Success> ValidatePromotable(SharedComponentTarget target)
    {
        ISharedComponentLinkable node = target.Kind == SharedComponentKind.Block
            ? target.Block!
            : target.Section!;
        bool sharesLikeLocalContent = node.SharedComponentLinkState == SharedComponentLinkState.BrokenOut
            && target.ParentSection?.IsActivelyLinked() != true;
        if (!string.IsNullOrEmpty(node.SharedComponentId) && !sharesLikeLocalContent)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.ALREADY_SHARED,
                $"The node at {target.CanonicalPath} already carries shared-component provenance; "
                + "re-linking or re-sharing it arrives with reconciliation.",
                new Dictionary<string, object>
                {
                    ["existingComponentId"] = node.SharedComponentId,
                    ["linkState"] = node.SharedComponentLinkState?.ToString() ?? string.Empty,
                });
        }

        return target.Kind == SharedComponentKind.Block
            ? ValidateBlockPromotable(target)
            : ValidateSectionPromotable(target);
    }

    public SharedComponentDefinition BuildDefinition(SharedComponentTarget target)
    {
        if (target.Kind == SharedComponentKind.Block)
        {
            string json = _normalizer.ToDefinitionJson(target.Block!);

            return new SharedComponentDefinition(
                SharedComponentKind.Block,
                json,
                _normalizer.ComputeContentHashOfDefinitionJson(json),
                string.IsNullOrWhiteSpace(target.Block!.Name) ? target.Block.ReplacementTag : target.Block.Name);
        }

        string sectionJson = _normalizer.ToDefinitionJson(target.Section!);
        string sectionName = string.IsNullOrWhiteSpace(target.Section!.Title)
            ? target.Section.Name
            : target.Section.Title;

        return new SharedComponentDefinition(
            SharedComponentKind.Section,
            sectionJson,
            _normalizer.ComputeContentHashOfDefinitionJson(sectionJson),
            sectionName);
    }

    public string BuildContainerRefDefinitionJson(
        Section containerHeadDefinition,
        string slotTag,
        string blockComponentId)
    {
        var rebuilt = new Section();
        containerHeadDefinition.CloneTo(rebuilt);
        rebuilt.Entries = [.. containerHeadDefinition.EffectiveEntries().Select(entry =>
            string.Equals(entry.SlotTag, slotTag, StringComparison.Ordinal)
                ? entry with { Kind = SectionEntryKind.ComponentRef, SharedComponentId = blockComponentId, Bindings = null }
                : entry)];

        Block? inlineBlock = rebuilt.Blocks
            .FirstOrDefault(b => string.Equals(b.ReplacementTag, slotTag, StringComparison.Ordinal));
        if (inlineBlock is not null)
        {
            _ = rebuilt.Blocks.Remove(inlineBlock);
        }

        List<Block> remaining = [.. rebuilt.Blocks.OrderBy(b => b.Order)];
        for (int i = 0; i < remaining.Count; i++)
        {
            remaining[i].Order = i;
        }

        return _canonicalizer.ToComponentCanonicalJson(rebuilt);
    }

    public ErrorOr<ConvertToRefDefinition?> BuildConvertToRefDefinition(
        SharedComponentTarget target,
        Section containerHeadDefinition)
    {
        Block block = target.Block!;
        Section parentSection = target.ParentSection!;
        string slotTag = SharedComponentReconciler.SlotFor(target.ParentSection, target.Block!);
        SectionEntry? entry = containerHeadDefinition.EffectiveEntries()
            .FirstOrDefault(e => string.Equals(e.SlotTag, slotTag, StringComparison.Ordinal));
        if (entry is null || entry.Kind != SectionEntryKind.Inline)
        {
            return (ConvertToRefDefinition?)null;
        }

        string occupantTag = parentSection.SharedComponentBindings?.GetValueOrDefault(slotTag) ?? slotTag;
        if (!string.Equals(occupantTag, block.ReplacementTag, StringComparison.Ordinal))
        {
            return (ConvertToRefDefinition?)null;
        }

        if (parentSection.SharedComponentSlotStates is { } slotOverrides
            && slotOverrides.TryGetValue(slotTag, out SharedComponentSlotState slotOverride))
        {
            if (slotOverride == SharedComponentSlotState.Hidden)
            {
                return (ConvertToRefDefinition?)null;
            }

            return Error.Validation(
                target.CanonicalPath,
                $"The definition slot '{slotTag}' carries a local override; resolve it via reconcile "
                + $"before sharing the slot. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        Block? definitionBlock = containerHeadDefinition.FindBlockByReplacementTag(slotTag);
        if (definitionBlock is null)
        {
            return Error.Unexpected(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"The container head definition lists slot '{slotTag}' as inline but holds no block for it.");
        }

        string liveHash = _normalizer.ComputeContentHash(block, parentSection.SharedComponentBindings);
        string slotHash = _normalizer.ComputeEntryContentHash(containerHeadDefinition, entry);
        if (!string.Equals(liveHash, slotHash, StringComparison.Ordinal))
        {
            return Error.Conflict(
                SharedComponentErrorCodes.CONTENT_MISMATCH,
                $"The block at {target.CanonicalPath} has drifted from the container definition slot "
                + $"'{slotTag}'; reconcile the section before sharing this slot.",
                new Dictionary<string, object>
                {
                    ["expectedHash"] = slotHash,
                    ["actualHash"] = liveHash,
                });
        }

        string definitionJson = _normalizer.ToDefinitionJson(definitionBlock);

        return new ConvertToRefDefinition(
            slotTag,
            definitionJson,
            _normalizer.ComputeContentHashOfDefinitionJson(definitionJson),
            string.IsNullOrWhiteSpace(definitionBlock.Name) ? definitionBlock.ReplacementTag : definitionBlock.Name);
    }

    public void StampLink(SharedComponentTarget target, string sharedComponentId, string versionHash)
    {
        ISharedComponentLinkable node = target.Kind == SharedComponentKind.Block
            ? target.Block!
            : target.Section!;
        Stamp(node, sharedComponentId, versionHash, bindings: null);
        if (target.Section is { } section)
        {
            section.SharedComponentSlotStates = null;
        }
    }

    private static void Stamp(
        ISharedComponentLinkable node,
        string sharedComponentId,
        string versionHash,
        Dictionary<string, string>? bindings)
    {
        node.SharedComponentId = sharedComponentId;
        node.SharedComponentLinkState = SharedComponentLinkState.Linked;
        node.SharedComponentVersionHash = versionHash;
        node.SharedComponentBindings = bindings;
    }

    public ErrorOr<MaterializedNode> Materialize(
        Workflow workflow,
        SharedComponentKind kind,
        string componentId,
        string componentName,
        string versionHash,
        string definitionJson,
        Section? targetSection,
        int? index,
        string? requestedTag,
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads = null)
    {
        return kind == SharedComponentKind.Block
            ? MaterializeBlock(workflow, componentId, componentName, versionHash, definitionJson, targetSection, index, requestedTag)
            : MaterializeSection(workflow, componentId, versionHash, definitionJson, index, refHeads);
    }

    private static ErrorOr<SharedComponentTarget> ResolveByPath(Workflow workflow, string targetPath)
    {
        Match match = _targetPathPattern.Match(targetPath);
        if (!match.Success)
        {
            return Error.Validation(
                SharedComponentErrorCodes.INVALID_TARGET_LOCATOR,
                $"'{targetPath}' is not a valid target path; expected Sections[i] or Sections[i].Blocks[j]. "
                + $"[{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        }

        List<Section> orderedSections = [.. workflow.Sections.OrderBy(s => s.Order)];
        if (!int.TryParse(match.Groups[1].Value, out int sectionRank) || sectionRank >= orderedSections.Count)
        {
            return TargetNotFound(workflow, targetPath);
        }

        Section section = orderedSections[sectionRank];
        if (!match.Groups[2].Success)
        {
            return new SharedComponentTarget(
                SharedComponentKind.Section, null, section, null, CanonicalPath(sectionRank));
        }

        List<Block> orderedBlocks = [.. section.Blocks.OrderBy(b => b.Order)];
        if (!int.TryParse(match.Groups[2].Value, out int blockRank) || blockRank >= orderedBlocks.Count)
        {
            return TargetNotFound(workflow, targetPath);
        }

        return new SharedComponentTarget(
            SharedComponentKind.Block,
            orderedBlocks[blockRank],
            null,
            section,
            CanonicalPath(sectionRank, blockRank));
    }

    private static ErrorOr<SharedComponentTarget> ResolveByStableId(Workflow workflow, string targetStableId)
    {
        Section? matchedSection = workflow.Sections
            .FirstOrDefault(s => s.StableSectionId == targetStableId);
        (Section Parent, Block Block)? matchedBlock = workflow.Sections
            .SelectMany(s => s.Blocks.Select(b => (Parent: s, Block: b)))
            .Where(pair => pair.Block.StableBlockId == targetStableId)
            .Select(pair => ((Section, Block)?)pair)
            .FirstOrDefault();

        if (matchedSection is not null && matchedBlock is not null)
        {
            return Error.Validation(
                SharedComponentErrorCodes.INVALID_TARGET_LOCATOR,
                $"Stable id '{targetStableId}' matches both a section and a block. "
                + $"[{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        }

        if (matchedSection is not null)
        {
            return new SharedComponentTarget(
                SharedComponentKind.Section,
                null,
                matchedSection,
                null,
                CanonicalSectionPath(workflow, matchedSection));
        }

        if (matchedBlock is not null)
        {
            (Section parent, Block block) = matchedBlock.Value;

            return new SharedComponentTarget(
                SharedComponentKind.Block,
                block,
                null,
                parent,
                CanonicalBlockPath(workflow, parent, block));
        }

        return TargetNotFound(workflow, targetStableId);
    }

    private static Error TargetNotFound(Workflow workflow, string target)
    {
        return Error.NotFound(
            SharedComponentErrorCodes.TARGET_NOT_FOUND,
            $"No section or block matches '{target}' in this template.",
            new Dictionary<string, object>
            {
                ["sectionCount"] = workflow.Sections.Count,
                ["blockCount"] = workflow.GetAllBlocks().Count(),
            });
    }

    private static ErrorOr<Success> ValidateBlockPromotable(SharedComponentTarget target)
    {
        if (target.ParentSection?.ExpandedFromParameterizedKey is not null)
        {
            return Error.Validation(
                target.CanonicalPath,
                "This block belongs to an expanded parameterized section variant; re-expanding would "
                + $"silently discard the link. Promote from the source section instead. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        return Result.Success;
    }

    private static ErrorOr<Success> ValidateSectionPromotable(SharedComponentTarget target)
    {
        Section section = target.Section!;
        if (section.IsSeparator)
        {
            return Error.Validation(
                target.CanonicalPath,
                $"Separator sections carry no shareable content. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        if (section.ExpandedFromParameterizedKey is not null)
        {
            return Error.Validation(
                target.CanonicalPath,
                "Expanded parameterized section variants cannot be promoted; promote the source section "
                + $"instead. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        if (section.Blocks.Count == 0)
        {
            return Error.Validation(
                target.CanonicalPath,
                $"An empty section carries no shareable slots. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        List<string> innerTags = [.. section.Blocks.Select(b => b.ReplacementTag)];
        if (innerTags.Any(string.IsNullOrWhiteSpace))
        {
            return Error.Validation(
                target.CanonicalPath,
                $"Every block inside a promoted section needs a non-empty ReplacementTag; the bindings "
                + $"contract keys on them. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        List<string> duplicates = [.. innerTags
            .GroupBy(t => t, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)];
        if (duplicates.Count > 0)
        {
            return Error.Validation(
                target.CanonicalPath,
                $"Inner ReplacementTags must be unique within a promoted section; duplicated: "
                + $"{string.Join(", ", duplicates)}. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        return Result.Success;
    }

    private ErrorOr<MaterializedNode> MaterializeBlock(
        Workflow workflow,
        string componentId,
        string componentName,
        string versionHash,
        string definitionJson,
        Section? targetSection,
        int? index,
        string? requestedTag)
    {
        if (targetSection is null)
        {
            return Error.Validation(
                SharedComponentErrorCodes.INVALID_TARGET_LOCATOR,
                $"Materializing a block component requires a target section. [{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        }

        if (targetSection.IsSeparator)
        {
            return Error.Validation(
                CanonicalSectionPath(workflow, targetSection),
                $"Blocks cannot be materialized into a separator section. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        if (targetSection.ExpandedFromParameterizedKey is not null)
        {
            return Error.Validation(
                CanonicalSectionPath(workflow, targetSection),
                "Blocks cannot be materialized into an expanded parameterized section variant; "
                + $"re-expanding would silently discard the copy. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        Block? payloadBlock = SharedComponentContentNormalizer.ReadBlockDefinitionForNewCopy(definitionJson);
        if (payloadBlock is null)
        {
            return Error.Unexpected(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"The stored definition of component '{componentId}' could not be read.");
        }

        HashSet<string> existingTags = ExistingTagNamespace(workflow);
        string resolvedTag;
        if (!string.IsNullOrWhiteSpace(requestedTag))
        {
            if (existingTags.Contains(requestedTag))
            {
                return Error.Validation(
                    CanonicalSectionPath(workflow, targetSection),
                    $"The tag '{requestedTag}' is already used in this template. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
            }

            resolvedTag = requestedTag;
        }
        else
        {
            resolvedTag = UniqueSlug(componentName, existingTags);
        }

        Block landed = payloadBlock.CopyToSection(targetSection, index ?? -1, resolvedTag);
        Stamp(landed, componentId, versionHash, bindings: null);

        return new MaterializedNode(
            CanonicalBlockPath(workflow, targetSection, landed),
            resolvedTag,
            null);
    }

    private ErrorOr<MaterializedNode> MaterializeSection(
        Workflow workflow,
        string componentId,
        string versionHash,
        string definitionJson,
        int? index,
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads)
    {
        Section? payloadSection = SharedComponentContentNormalizer.ReadSectionDefinitionForNewCopy(definitionJson);
        if (payloadSection is null)
        {
            return Error.Unexpected(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"The stored definition of component '{componentId}' could not be read.");
        }

        HashSet<string> existingTags = ExistingTagNamespace(workflow);
        Section landed = payloadSection.CloneToWorkflow(workflow, index ?? workflow.Sections.Count);

        HashSet<string> reservedTags = new(existingTags, StringComparer.Ordinal);
        foreach (Block block in landed.Blocks)
        {
            if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
            {
                _ = reservedTags.Add(block.ReplacementTag);
            }
        }

        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Block block in landed.Blocks)
        {
            if (string.IsNullOrWhiteSpace(block.ReplacementTag) || !existingTags.Contains(block.ReplacementTag))
            {
                continue;
            }

            renames[block.ReplacementTag] = UniqueSlug(block.ReplacementTag, reservedTags);
        }

        if (renames.Count > 0)
        {
            try
            {
                landed.ApplyReplacementTagRenames(renames);
            }
            catch (InvalidOperationException exception)
            {
                return Error.Validation(
                    CanonicalSectionPath(workflow, landed),
                    $"{exception.Message} [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
            }
        }

        List<SectionEntry> entries = payloadSection.EffectiveEntries();
        HydrateRefEntries(landed, entries, renames, reservedTags, refHeads);

        Stamp(landed, componentId, versionHash, TotalBindings(entries, landed, renames));

        return new MaterializedNode(
            CanonicalSectionPath(workflow, landed),
            null,
            renames.Count > 0 ? renames : null);
    }

    private static void HydrateRefEntries(
        Section landed,
        List<SectionEntry> entries,
        Dictionary<string, string> renames,
        HashSet<string> reservedTags,
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads)
    {
        List<(SectionEntry Entry, string LandedTag, SharedComponentRefHead Head)> refPlan = [];
        foreach (SectionEntry entry in entries)
        {
            if (entry.Kind != SectionEntryKind.ComponentRef
                || string.IsNullOrEmpty(entry.SharedComponentId)
                || refHeads is null
                || !refHeads.TryGetValue(entry.SharedComponentId, out SharedComponentRefHead? refHead))
            {
                continue;
            }

            string landedTag = reservedTags.Add(entry.SlotTag)
                ? entry.SlotTag
                : UniqueSlug(entry.SlotTag, reservedTags);
            if (!string.Equals(landedTag, entry.SlotTag, StringComparison.Ordinal))
            {
                renames[entry.SlotTag] = landedTag;
            }

            refPlan.Add((entry, landedTag, refHead));
        }

        if (refPlan.Count == 0)
        {
            return;
        }

        Dictionary<string, (SectionEntry Entry, string LandedTag, SharedComponentRefHead Head)> refBySlot =
            refPlan.ToDictionary(p => p.Entry.SlotTag, StringComparer.Ordinal);
        List<Block> inlineInOrder = [.. landed.Blocks.OrderBy(b => b.Order)];
        List<Block> sequence = [];
        int inlineIndex = 0;
        foreach (SectionEntry entry in entries)
        {
            if (entry.Kind != SectionEntryKind.ComponentRef)
            {
                if (inlineIndex < inlineInOrder.Count)
                {
                    sequence.Add(inlineInOrder[inlineIndex++]);
                }

                continue;
            }

            if (!refBySlot.TryGetValue(entry.SlotTag, out (SectionEntry Entry, string LandedTag, SharedComponentRefHead Head) planned))
            {
                continue;
            }

            Block landedRef = SharedComponentRefHeads.MaterializeRefBlock(landed, planned.LandedTag, planned.Entry, planned.Head);
            SharedComponentRefHeads.BindRefBlock(planned.Entry, landedRef, renames);
            sequence.Add(landedRef);
        }

        while (inlineIndex < inlineInOrder.Count)
        {
            sequence.Add(inlineInOrder[inlineIndex++]);
        }

        landed.Blocks.Clear();
        for (int i = 0; i < sequence.Count; i++)
        {
            sequence[i].Order = i;
            landed.Blocks.Add(sequence[i]);
        }
    }

    private static Dictionary<string, string>? TotalBindings(
        List<SectionEntry> entries,
        Section landed,
        Dictionary<string, string> renames)
    {
        HashSet<string> landedTags = new(
            landed.Blocks.Select(b => b.ReplacementTag),
            StringComparer.Ordinal);
        Dictionary<string, string> total = new(StringComparer.Ordinal);
        foreach (SectionEntry entry in entries)
        {
            string landedTag = renames.GetValueOrDefault(entry.SlotTag, entry.SlotTag);
            if (landedTags.Contains(landedTag))
            {
                total[entry.SlotTag] = landedTag;
            }
        }

        return total.Count > 0 ? total : null;
    }

    internal static HashSet<string> ExistingTagNamespace(Workflow workflow)
    {
        return new HashSet<string>(
            workflow.GetAllBlocks().Select(b => b.ReplacementTag)
                .Concat(workflow.Variables.Select(v => v.ReplacementTag))
                .Concat(workflow.DocumentClasses.Select(d => d.Key))
                .Where(tag => !string.IsNullOrWhiteSpace(tag)),
            StringComparer.Ordinal);
    }

    internal static string UniqueSlug(string source, HashSet<string> existingTags)
    {
        string slug = new(
            [.. source.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_')]);
        while (slug.Contains("__", StringComparison.Ordinal))
        {
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        }

        slug = slug.Trim('_');
        if (slug.Length == 0)
        {
            slug = "component";
        }

        string candidate = slug;
        int suffix = 2;
        while (!existingTags.Add(candidate))
        {
            candidate = $"{slug}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    internal static string CanonicalPath(int sectionRank, int? blockRank = null)
    {
        return blockRank is null
            ? $"Sections[{sectionRank}]"
            : $"Sections[{sectionRank}].Blocks[{blockRank}]";
    }

    private static string CanonicalSectionPath(Workflow workflow, Section section)
    {
        List<Section> ordered = [.. workflow.Sections.OrderBy(s => s.Order)];

        return CanonicalPath(ordered.IndexOf(section));
    }

    private static string CanonicalBlockPath(Workflow workflow, Section section, Block block)
    {
        List<Section> orderedSections = [.. workflow.Sections.OrderBy(s => s.Order)];
        List<Block> orderedBlocks = [.. section.Blocks.OrderBy(b => b.Order)];

        return CanonicalPath(orderedSections.IndexOf(section), orderedBlocks.IndexOf(block));
    }
}
