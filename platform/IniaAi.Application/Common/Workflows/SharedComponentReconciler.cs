using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Classifies how a consumer's linked (or broken-out) copy relates to its shared component by
/// comparing three content hashes: the instance (reverse-normalized through its bindings, so
/// materialization-time slot renames never read as drift), the link stamp
/// (<c>SharedComponentVersionHash</c>) and the chain head. Link hashes participate in equality
/// only and are never resolved to a chain row — a foreign hash from a transferred file degrades
/// to inequality, not an error. A retired component and a missing (or version-less) registry row
/// short-circuit the compare. <see cref="ResolveDefinitionVersion"/> finds the definition a link
/// hash was stamped from, when this deployment's chain still carries its payload.
/// </summary>
public sealed class SharedComponentReconciler
{
    public const double TAG_SIMILARITY_RESCUE_THRESHOLD = 0.6;

    private readonly SharedComponentContentNormalizer _normalizer;

    public SharedComponentReconciler(SharedComponentContentNormalizer normalizer)
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public string ComputeInstanceContentHash(Block block)
    {
        return _normalizer.ComputeContentHash(block, block.SharedComponentBindings);
    }

    public string ComputeInstanceContentHash(Section section)
    {
        return _normalizer.ComputeContentHash(section, section.SharedComponentBindings);
    }

    public string ComputeSignatureInsensitiveInstanceContentHash(Block block)
    {
        return _normalizer.ComputeSignatureInsensitiveContentHash(block, block.SharedComponentBindings);
    }

    public SectionInstanceHashResult ComputeSignatureInsensitiveInstanceContentHash(
        Section section, Func<Section?>? resolveDLinkDefinition, string? signatureInsensitiveHeadHash = null)
    {
        string rawHash = _normalizer.ComputeSignatureInsensitiveContentHash(section, section.SharedComponentBindings);
        bool hasSlotRecords = section.SharedComponentSlotStates is { Count: > 0 };
        if (signatureInsensitiveHeadHash is not null
            && string.Equals(rawHash, signatureInsensitiveHeadHash, StringComparison.Ordinal))
        {
            return new SectionInstanceHashResult(rawHash, hasSlotRecords);
        }

        string? reconstructed = _normalizer.ReconstructDefinitionJson(section, resolveDLinkDefinition?.Invoke());
        if (reconstructed is not null)
        {
            return new SectionInstanceHashResult(
                _normalizer.ComputeSignatureInsensitiveContentHash(reconstructed),
                HasLocalOverrides: true);
        }

        return new SectionInstanceHashResult(rawHash, hasSlotRecords);
    }

    /// <summary>
    /// A stored Library definition in the form a copy's definition takes, so the two compare field
    /// by field.
    /// </summary>
    public string LibraryDefinitionJson(string storedDefinitionJson)
    {
        return _normalizer.RebuildDefinitionJson(storedDefinitionJson);
    }

    /// <summary>
    /// The definition a linked block copy holds, in the form its content hash is computed from.
    /// </summary>
    public string InstanceDefinitionJson(Block block)
    {
        return _normalizer.ToDefinitionJson(block, block.SharedComponentBindings);
    }

    /// <summary>
    /// The definition a linked section copy holds: rebuilt over the definition it links to when
    /// the copy hides or breaks out entries, as
    /// <see cref="ComputeSignatureInsensitiveInstanceContentHash(Section, Func{Section?}, string?)"/> hashes it.
    /// </summary>
    public string InstanceDefinitionJson(Section section, Section? dLinkDefinition)
    {
        return _normalizer.ReconstructDefinitionJson(section, dLinkDefinition)
            ?? _normalizer.ToDefinitionJson(section, section.SharedComponentBindings);
    }

    public SectionInstanceHashResult ComputeInstanceContentHash(
        Section section, Func<Section?>? resolveDLinkDefinition, string? headContentHash = null)
    {
        string rawHash = ComputeInstanceContentHash(section);
        bool hasSlotRecords = section.SharedComponentSlotStates is { Count: > 0 };
        if (headContentHash is not null
            && string.Equals(rawHash, headContentHash, StringComparison.Ordinal))
        {
            return new SectionInstanceHashResult(rawHash, hasSlotRecords);
        }

        string? reconstructed = _normalizer.ReconstructDefinitionJson(section, resolveDLinkDefinition?.Invoke());
        if (reconstructed is not null)
        {
            return new SectionInstanceHashResult(
                _normalizer.ComputeContentHashOfDefinitionJson(reconstructed),
                HasLocalOverrides: true);
        }

        return new SectionInstanceHashResult(rawHash, hasSlotRecords);
    }

    /// <summary>
    /// Whether a linked block holds edits that match neither its component's head nor the version
    /// its link stamp names. A stored hash passed as it is reports edits the copy does not hold when
    /// that version was stored before a model change; <see cref="StoredVersionHashes"/> gives the
    /// hash today's model writes for it.
    /// </summary>
    public bool HoldsLocalEdits(Block live, string componentId, string headContentHash, string? linkContentHash)
    {
        if (!string.Equals(live.SharedComponentId, componentId, StringComparison.Ordinal))
        {
            return false;
        }

        string instanceHash = ComputeInstanceContentHash(live);

        return !string.Equals(instanceHash, headContentHash, StringComparison.Ordinal)
            && !string.Equals(instanceHash, linkContentHash, StringComparison.Ordinal);
    }

    public bool LinkedBlockHoldsLocalEdits(
        Block live,
        string componentId,
        string? oldDefinitionJson,
        string incomingHeadHash,
        string incomingDefinitionJson)
    {
        if (!HoldsLocalEdits(live, componentId, incomingHeadHash, live.SharedComponentVersionHash))
        {
            return false;
        }

        if (oldDefinitionJson is null)
        {
            return true;
        }

        string instanceHash = _normalizer.ComputeSignatureInsensitiveContentHash(
            _normalizer.ToDefinitionJson(live, live.SharedComponentBindings));

        return !string.Equals(
                instanceHash,
                _normalizer.ComputeSignatureInsensitiveContentHash(incomingDefinitionJson),
                StringComparison.Ordinal)
            && !string.Equals(
                instanceHash,
                _normalizer.ComputeStoredSignatureInsensitiveContentHash(oldDefinitionJson),
                StringComparison.Ordinal);
    }

    public List<string> OverwrittenInlineSlots(Section instance, Section? oldDefinition, Section incomingDefinition)
    {
        List<string> atRiskSlots = [];
        if (oldDefinition is null)
        {
            return atRiskSlots;
        }

        Dictionary<string, string>? bindings = instance.SharedComponentBindings;
        Dictionary<string, Block> liveByTag = new(StringComparer.Ordinal);
        foreach (Block block in instance.Blocks)
        {
            _ = liveByTag.TryAdd(block.ReplacementTag, block);
        }

        foreach (SectionEntry oldEntry in oldDefinition.EffectiveEntries())
        {
            if (oldEntry.Kind == SectionEntryKind.ComponentRef)
            {
                continue;
            }

            string slotTag = oldEntry.SlotTag;
            SharedComponentSlotState? slotState = instance.SharedComponentSlotStates?.GetValueOrDefault(slotTag);
            if (slotState is SharedComponentSlotState.Hidden or SharedComponentSlotState.BrokenOut)
            {
                continue;
            }

            Block? oldBlock = oldDefinition.FindBlockByReplacementTag(slotTag);
            string liveTag = bindings?.GetValueOrDefault(slotTag) ?? slotTag;
            if (oldBlock is null || !liveByTag.TryGetValue(liveTag, out Block? live))
            {
                continue;
            }

            string liveHash = SignatureInsensitiveBlockHash(live, bindings);
            if (string.Equals(liveHash, SignatureInsensitiveBlockHash(oldBlock, null), StringComparison.Ordinal))
            {
                continue;
            }

            Block? incomingBlock = incomingDefinition.EffectiveEntries()
                .Any(e => e.Kind != SectionEntryKind.ComponentRef
                    && string.Equals(e.SlotTag, slotTag, StringComparison.Ordinal))
                ? incomingDefinition.FindBlockByReplacementTag(slotTag)
                : null;
            if (incomingBlock is not null
                && string.Equals(liveHash, SignatureInsensitiveBlockHash(incomingBlock, null), StringComparison.Ordinal))
            {
                continue;
            }

            atRiskSlots.Add(slotTag);
        }

        return atRiskSlots;
    }

    private string SignatureInsensitiveBlockHash(Block block, IReadOnlyDictionary<string, string>? bindings)
    {
        return _normalizer.ComputeSignatureInsensitiveContentHash(block, bindings);
    }

    public SharedComponentRelationResult Classify(
        SharedComponent? component,
        string? headContentHash,
        string instanceContentHash,
        string? linkVersionHash,
        bool hasLocalOverrides = false)
    {
        if (component is null || string.IsNullOrWhiteSpace(headContentHash))
        {
            return new SharedComponentRelationResult(
                SharedComponentRelation.ComponentMissing, LinkHashStale: false, hasLocalOverrides);
        }

        if (component.State == SharedComponentState.Retired)
        {
            return new SharedComponentRelationResult(
                SharedComponentRelation.ComponentRetired, LinkHashStale: false, hasLocalOverrides);
        }

        bool instanceAtHead = string.Equals(instanceContentHash, headContentHash, StringComparison.Ordinal);
        bool instanceAtLink = string.Equals(instanceContentHash, linkVersionHash, StringComparison.Ordinal);
        bool linkAtHead = string.Equals(linkVersionHash, headContentHash, StringComparison.Ordinal);

        if (instanceAtHead)
        {
            return new SharedComponentRelationResult(
                SharedComponentRelation.InSync, LinkHashStale: !linkAtHead, hasLocalOverrides);
        }

        if (instanceAtLink)
        {
            return new SharedComponentRelationResult(
                SharedComponentRelation.HeadAhead, LinkHashStale: false, hasLocalOverrides);
        }

        if (linkAtHead)
        {
            return new SharedComponentRelationResult(
                SharedComponentRelation.LocalAhead, LinkHashStale: false, hasLocalOverrides);
        }

        return new SharedComponentRelationResult(
            SharedComponentRelation.Diverged, LinkHashStale: false, hasLocalOverrides);
    }

    /// <summary>
    /// The sanctioned decision set for one instance, leading with the arm the relation most
    /// suggests — menu order, not a preselect (rendering surfaces choose their own defaults,
    /// deliberately more conservative). Linked copies can always break out or stay put;
    /// adopting the head needs a live head, pushing local content needs deliberate local intent
    /// (a local edit, or any broken-out fork). A clean broken-out copy relinks instead of
    /// pushing; a missing or retired component leaves nothing to push to or adopt from.
    /// </summary>
    public static IReadOnlyList<SharedComponentReconcileAction> AvailableActions(
        SharedComponentLinkState linkState,
        SharedComponentRelation relation)
    {
        if (linkState == SharedComponentLinkState.BrokenOut)
        {
            return relation switch
            {
                SharedComponentRelation.InSync =>
                    [SharedComponentReconcileAction.Relink, SharedComponentReconcileAction.KeepLocal],
                SharedComponentRelation.ComponentMissing or SharedComponentRelation.ComponentRetired =>
                    [SharedComponentReconcileAction.KeepLocal],
                _ =>
                    [
                        SharedComponentReconcileAction.UpdateShared,
                        SharedComponentReconcileAction.AdoptHead,
                        SharedComponentReconcileAction.KeepLocal,
                    ],
            };
        }

        return relation switch
        {
            SharedComponentRelation.InSync =>
                [SharedComponentReconcileAction.BreakOut, SharedComponentReconcileAction.KeepLocal],
            SharedComponentRelation.HeadAhead =>
                [
                    SharedComponentReconcileAction.AdoptHead,
                    SharedComponentReconcileAction.BreakOut,
                    SharedComponentReconcileAction.KeepLocal,
                ],
            SharedComponentRelation.ComponentMissing =>
                [SharedComponentReconcileAction.BreakOut, SharedComponentReconcileAction.KeepLocal],
            SharedComponentRelation.ComponentRetired =>
                [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.BreakOut],
            _ =>
                [
                    SharedComponentReconcileAction.UpdateShared,
                    SharedComponentReconcileAction.AdoptHead,
                    SharedComponentReconcileAction.BreakOut,
                    SharedComponentReconcileAction.KeepLocal,
                ],
        };
    }

    /// <summary>
    /// The sanctioned decisions for one entry of a linked section copy, leading with the arm the
    /// entry's state most suggests — menu order, not a preselect (rendering surfaces choose their
    /// own defaults). Overrides and absences carry fixed arms; a definition-provided entry with a
    /// live block resolves by its relation; a Library-linked local insert reuses the standard
    /// block arms minus <see cref="SharedComponentReconcileAction.Relink"/> (never offered per
    /// entry) plus the entry-scoped <see cref="SharedComponentReconcileAction.UpdateShared"/>
    /// that appends a ref entry to the section definition.
    /// </summary>
    public static IReadOnlyList<SharedComponentReconcileAction> EntryAvailableActions(
        SharedComponentEntryOrigin origin,
        SharedComponentEntryOverrideState overrideState,
        SharedComponentRelation? relation,
        SharedComponentLinkState? insertLinkState,
        bool deletedByDefinition,
        bool liveMissing)
    {
        if (overrideState == SharedComponentEntryOverrideState.Hidden)
        {
            return [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.Unhide];
        }

        if (overrideState == SharedComponentEntryOverrideState.BrokenOut)
        {
            return [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.AdoptHead];
        }

        if (liveMissing)
        {
            return
            [
                SharedComponentReconcileAction.Hide,
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.KeepLocal,
            ];
        }

        if (deletedByDefinition)
        {
            return [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.UpdateShared];
        }

        if (origin == SharedComponentEntryOrigin.LocalInsert)
        {
            if (insertLinkState is null || relation is null)
            {
                return [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.UpdateShared];
            }

            List<SharedComponentReconcileAction> arms = [.. AvailableActions(insertLinkState.Value, relation.Value)
                .Where(a => a != SharedComponentReconcileAction.Relink)];
            if (!arms.Contains(SharedComponentReconcileAction.UpdateShared))
            {
                arms.Add(SharedComponentReconcileAction.UpdateShared);
            }

            return arms;
        }

        return relation switch
        {
            SharedComponentRelation.ComponentRetired or SharedComponentRelation.ComponentMissing =>
                [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.BreakOut],
            SharedComponentRelation.LocalAhead =>
                [
                    SharedComponentReconcileAction.KeepLocal,
                    SharedComponentReconcileAction.UpdateShared,
                    SharedComponentReconcileAction.AdoptHead,
                    SharedComponentReconcileAction.BreakOut,
                ],
            SharedComponentRelation.HeadAhead =>
                [
                    SharedComponentReconcileAction.AdoptHead,
                    SharedComponentReconcileAction.BreakOut,
                    SharedComponentReconcileAction.KeepLocal,
                ],
            SharedComponentRelation.Diverged =>
                [
                    SharedComponentReconcileAction.UpdateShared,
                    SharedComponentReconcileAction.AdoptHead,
                    SharedComponentReconcileAction.BreakOut,
                    SharedComponentReconcileAction.KeepLocal,
                ],
            _ => [SharedComponentReconcileAction.KeepLocal],
        };
    }

    public SharedComponentVersion? ResolveDefinitionVersion(
        IEnumerable<SharedComponentVersion> versions,
        string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return null;
        }

        return versions
            .Where(v => v.PayloadAbsentAt is null
                && string.Equals(v.ContentHash, contentHash, StringComparison.Ordinal))
            .OrderByDescending(v => v.SequenceNumber)
            .FirstOrDefault();
    }

    /// <summary>
    /// Rewrites a linked section instance in place to match a new component definition. Slots are
    /// correlated old-vs-new definition by tag, with an unambiguous equal-content-hash pair rescued
    /// as a pure rename; matched and rescued slots are grafted onto their existing blocks (stable
    /// ids and instance tags kept, inner document classes adopted from the definition), new slots
    /// land as fresh blocks (slot tag when free in the workflow-wide namespace, else a slugged
    /// rename recorded in the bindings), and slots removed by the new definition drop their blocks.
    /// Local extra blocks survive, re-slotted after their nearest previously-matched neighbor; an
    /// extra with no matched block before it keeps the front of the section.
    /// Without the old definition (foreign link hash) slots correlate through the instance's
    /// bindings and nothing is ever removed. Bindings are rebuilt from scratch; definition-sourced
    /// tag references are rewritten through them while extras stay untouched.
    /// </summary>
    public SectionReconcileResult ApplySectionDefinition(
        Workflow workflow,
        Section instance,
        Section? oldDefinition,
        Section newDefinition,
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads = null,
        StoredVersionHashes? storedHashes = null)
    {
        storedHashes ??= StoredVersionHashes.None;
        (Dictionary<string, string> currentBindings, Dictionary<string, Block> instanceByTag) =
            SnapshotInstance(instance);
        List<Block> originalSequence = [.. instance.Blocks.OrderBy(b => b.Order)];

        (Dictionary<string, string> slotSources, IReadOnlyDictionary<string, string> renamedSlots, IReadOnlyList<string> removedSlotTags) =
            CorrelateSlots(oldDefinition, newDefinition, currentBindings, instanceByTag);

        if (instance.SharedComponentSlotStates is { Count: > 0 } && renamedSlots.Count > 0)
        {
            Dictionary<string, SharedComponentSlotState> rekeyed = new(StringComparer.Ordinal);
            foreach ((string slotTag, SharedComponentSlotState state) in instance.SharedComponentSlotStates)
            {
                rekeyed[renamedSlots.GetValueOrDefault(slotTag, slotTag)] = state;
            }

            instance.SharedComponentSlotStates = rekeyed;
        }

        Dictionary<string, SharedComponentSlotState> slotStates = instance.SharedComponentSlotStates
            ?? new Dictionary<string, SharedComponentSlotState>(StringComparer.Ordinal);

        List<Block> removedBlocks = [];
        List<Block> preservedLocalBlocks = [];
        foreach (string removedTag in removedSlotTags)
        {
            string expectedTag = currentBindings.GetValueOrDefault(removedTag, removedTag);
            if (!instanceByTag.TryGetValue(expectedTag, out Block? block))
            {
                continue;
            }

            if (slotStates.GetValueOrDefault(removedTag) == SharedComponentSlotState.BrokenOut)
            {
                preservedLocalBlocks.Add(block);
                continue;
            }

            if (instance.Blocks.Remove(block))
            {
                removedBlocks.Add(block);
            }
        }

        Dictionary<string, Block> claims = new(StringComparer.Ordinal);
        foreach ((string slotTag, string sourceTag) in slotSources)
        {
            if (slotStates.GetValueOrDefault(slotTag) == SharedComponentSlotState.Hidden)
            {
                continue;
            }

            string expectedTag = currentBindings.GetValueOrDefault(sourceTag, sourceTag);
            if (instanceByTag.TryGetValue(expectedTag, out Block? block) && !removedBlocks.Contains(block))
            {
                claims[slotTag] = block;
            }
        }

        Dictionary<string, Block> newDefBlocksByTag = DefinitionBlocksByTag(newDefinition);
        foreach (SectionEntry entry in newDefinition.EffectiveEntries())
        {
            string slotTag = entry.SlotTag;
            if (claims.ContainsKey(slotTag)
                || slotStates.GetValueOrDefault(slotTag) == SharedComponentSlotState.Hidden)
            {
                continue;
            }

            string expectedTag = currentBindings.GetValueOrDefault(slotTag, slotTag);
            if (!instanceByTag.TryGetValue(expectedTag, out Block? residentBlock)
                || removedBlocks.Contains(residentBlock)
                || claims.ContainsValue(residentBlock))
            {
                continue;
            }

            bool residentIsTheSlotContent = entry.Kind == SectionEntryKind.ComponentRef
                ? !string.IsNullOrEmpty(entry.SharedComponentId)
                    && string.Equals(residentBlock.SharedComponentId, entry.SharedComponentId, StringComparison.Ordinal)
                : !residentBlock.IsActivelyLinked()
                    && newDefBlocksByTag.TryGetValue(slotTag, out Block? defBlock)
                    && string.Equals(
                        _normalizer.ComputeContentHash(residentBlock),
                        _normalizer.ComputeContentHash(defBlock),
                        StringComparison.Ordinal);
            if (residentIsTheSlotContent)
            {
                claims[slotTag] = residentBlock;
            }
        }

        HashSet<Block> claimed = [.. claims.Values];
        List<(Block Extra, Block? Predecessor)> extras = [];
        Block? lastClaimed = null;
        foreach (Block block in originalSequence)
        {
            if (claimed.Contains(block))
            {
                lastClaimed = block;
            }
            else if (!removedBlocks.Contains(block))
            {
                extras.Add((block, lastClaimed));
            }
        }

        HashSet<string> reservedTags = SharedComponentEditor.ExistingTagNamespace(workflow);
        foreach ((Block extra, _) in extras)
        {
            _ = instance.Blocks.Remove(extra);
        }

        instance.ApplySharedDefinition(newDefinition);

        Dictionary<string, string> finalBindings = new(StringComparer.Ordinal);
        List<Block> slotBlocks = [];
        List<Block> addedBlocks = [];
        List<string> staleRefSlots = [];
        List<string> droppedRefSlots = [];
        List<string> overwrittenLocalRefSlots = [];
        List<(Block RefBlock, SectionEntry Entry)> hydratedRefs = [];
        foreach (SectionEntry entry in newDefinition.EffectiveEntries())
        {
            string slotTag = entry.SlotTag;
            if (slotStates.GetValueOrDefault(slotTag) == SharedComponentSlotState.Hidden)
            {
                continue;
            }

            if (entry.Kind == SectionEntryKind.ComponentRef)
            {
                SharedComponentRefHead? entryHead = null;
                if (!string.IsNullOrEmpty(entry.SharedComponentId) && refHeads is not null)
                {
                    _ = refHeads.TryGetValue(entry.SharedComponentId, out entryHead);
                }

                if (claims.TryGetValue(slotTag, out Block? existingRef))
                {
                    if (slotStates.GetValueOrDefault(slotTag) != SharedComponentSlotState.BrokenOut)
                    {
                        if (entryHead is not null)
                        {
                            if (HoldsLocalEdits(
                                existingRef,
                                entry.SharedComponentId!,
                                storedHashes.Of(entry.SharedComponentId, entryHead.ContentHash),
                                storedHashes.Of(entry.SharedComponentId, existingRef.SharedComponentVersionHash)))
                            {
                                overwrittenLocalRefSlots.Add(slotTag);
                            }

                            SharedComponentRefHeads.ReapplyRefDefinition(
                                existingRef, entry.SharedComponentId!, entryHead.ContentHash, entryHead.Definition);
                            hydratedRefs.Add((existingRef, entry));
                        }
                        else
                        {
                            staleRefSlots.Add(slotTag);
                        }
                    }

                    finalBindings[slotTag] = existingRef.ReplacementTag;
                    slotBlocks.Add(existingRef);
                }
                else if (entryHead is not null)
                {
                    string landedRefTag = reservedTags.Add(slotTag)
                        ? slotTag
                        : SharedComponentEditor.UniqueSlug(slotTag, reservedTags);
                    finalBindings[slotTag] = landedRefTag;

                    Block landedRef = SharedComponentRefHeads.MaterializeRefBlock(instance, landedRefTag, entry, entryHead);
                    hydratedRefs.Add((landedRef, entry));
                    slotBlocks.Add(landedRef);
                    addedBlocks.Add(landedRef);
                }
                else
                {
                    droppedRefSlots.Add(slotTag);
                }

                continue;
            }

            if (!newDefBlocksByTag.TryGetValue(slotTag, out Block? defBlock))
            {
                continue;
            }

            if (claims.TryGetValue(slotTag, out Block? existing))
            {
                if (slotStates.GetValueOrDefault(slotTag) != SharedComponentSlotState.BrokenOut)
                {
                    existing.ApplySharedDefinition(defBlock);
                    existing.SharedComponentId = null;
                    existing.SharedComponentLinkState = null;
                    existing.SharedComponentVersionHash = null;
                    existing.SharedComponentBindings = null;
                }

                finalBindings[slotTag] = existing.ReplacementTag;
                slotBlocks.Add(existing);
            }
            else
            {
                string landedTag = reservedTags.Add(slotTag)
                    ? slotTag
                    : SharedComponentEditor.UniqueSlug(slotTag, reservedTags);
                finalBindings[slotTag] = landedTag;

                Block landed = defBlock.CloneToSection(instance, -1, landedTag);
                landed.ReMintStableBlockId();
                slotBlocks.Add(landed);
                addedBlocks.Add(landed);
            }
        }

        foreach ((Block refBlock, SectionEntry entry) in hydratedRefs)
        {
            SharedComponentRefHeads.BindRefBlock(entry, refBlock, finalBindings);
        }

        instance.RenameTagReferences(finalBindings);

        HashSet<Block> extraBlocks = [.. extras.Select(e => e.Extra)];
        List<Block> finalSequence = [.. slotBlocks];
        foreach ((Block extra, Block? predecessor) in extras)
        {
            int insertAt = 0;
            if (predecessor is not null)
            {
                int predecessorIndex = finalSequence.IndexOf(predecessor);
                insertAt = predecessorIndex >= 0 ? predecessorIndex + 1 : finalSequence.Count;
            }

            while (insertAt < finalSequence.Count && extraBlocks.Contains(finalSequence[insertAt]))
            {
                insertAt++;
            }

            finalSequence.Insert(insertAt, extra);
        }

        instance.Blocks.Clear();
        for (int i = 0; i < finalSequence.Count; i++)
        {
            finalSequence[i].Order = i;
            instance.Blocks.Add(finalSequence[i]);
        }

        instance.SharedComponentBindings = finalBindings.Count > 0 ? finalBindings : null;

        if (instance.SharedComponentSlotStates is { Count: > 0 } overrideRecords)
        {
            HashSet<string> liveSlotTags = new(
                newDefinition.EffectiveEntries().Select(e => e.SlotTag),
                StringComparer.Ordinal);
            foreach (string staleTag in overrideRecords.Keys.Where(tag => !liveSlotTags.Contains(tag)).ToList())
            {
                _ = overrideRecords.Remove(staleTag);
            }

            if (overrideRecords.Count == 0)
            {
                instance.SharedComponentSlotStates = null;
            }
        }

        return new SectionReconcileResult(
            removedBlocks,
            addedBlocks,
            renamedSlots,
            oldDefinition is not null,
            preservedLocalBlocks,
            staleRefSlots,
            droppedRefSlots,
            overwrittenLocalRefSlots);
    }

    internal static string SlotFor(Section? section, Block live)
    {
        if (section?.SharedComponentBindings is { Count: > 0 } bindings)
        {
            foreach ((string slot, string liveTag) in bindings)
            {
                if (string.Equals(liveTag, live.ReplacementTag, StringComparison.Ordinal))
                {
                    return slot;
                }
            }
        }

        return live.ReplacementTag;
    }

    internal static Dictionary<string, string>? ComposeRefBlockBindings(
        SectionEntry entry,
        IReadOnlyDictionary<string, string> sectionBindings)
    {
        if (entry.Bindings is not { Count: > 0 })
        {
            return null;
        }

        Dictionary<string, string> composed = new(entry.Bindings.Count, StringComparer.Ordinal);
        foreach ((string placeholder, string localTag) in entry.Bindings)
        {
            composed[placeholder] = sectionBindings.GetValueOrDefault(localTag, localTag);
        }

        return composed;
    }

    /// <summary>
    /// The pure definition-vs-definition slot diff behind both the apply path and the reconcile
    /// previews. Slots correlate by tag; among the non-matching remainder, an unambiguous pair
    /// with equal per-block content hashes is rescued as a rename. What still remains is
    /// fuzzy-rescued — matched with new content — for inline slots of equal block type: first by
    /// unique trimmed case-insensitive block-name equality, then by same entry position with tag
    /// similarity of at least <see cref="TAG_SIMILARITY_RESCUE_THRESHOLD"/>. Tag order follows
    /// the old definition for matched and removed slots and the new definition for added slots.
    /// </summary>
    public SharedComponentSlotDelta ComputeSlotDelta(Section oldDefinition, Section newDefinition)
    {
        List<SectionEntry> oldEntries = oldDefinition.EffectiveEntries();
        List<SectionEntry> newEntries = newDefinition.EffectiveEntries();
        HashSet<string> oldTags = new(oldEntries.Select(e => e.SlotTag), StringComparer.Ordinal);
        HashSet<string> newTags = new(newEntries.Select(e => e.SlotTag), StringComparer.Ordinal);

        List<string> matchedSlotTags = [];
        List<SectionEntry> removedEntries = [];
        foreach (SectionEntry oldEntry in oldEntries)
        {
            if (newTags.Contains(oldEntry.SlotTag))
            {
                matchedSlotTags.Add(oldEntry.SlotTag);
            }
            else
            {
                removedEntries.Add(oldEntry);
            }
        }

        List<SectionEntry> addedEntries = [.. newEntries.Where(e => !oldTags.Contains(e.SlotTag))];
        Dictionary<string, List<SectionEntry>> removedByHash = GroupByEntryHash(oldDefinition, removedEntries);
        Dictionary<string, List<SectionEntry>> addedByHash = GroupByEntryHash(newDefinition, addedEntries);
        Dictionary<string, string> renamedSlots = new(StringComparer.Ordinal);
        foreach ((string hash, List<SectionEntry> removedGroup) in removedByHash)
        {
            if (removedGroup.Count == 1
                && addedByHash.TryGetValue(hash, out List<SectionEntry>? addedGroup)
                && addedGroup.Count == 1)
            {
                renamedSlots[removedGroup[0].SlotTag] = addedGroup[0].SlotTag;
            }
        }

        HashSet<string> renameTargets = new(renamedSlots.Values, StringComparer.Ordinal);
        List<SectionEntry> remainingOld = [.. removedEntries.Where(e => !renamedSlots.ContainsKey(e.SlotTag))];
        List<SectionEntry> remainingNew = [.. addedEntries.Where(e => !renameTargets.Contains(e.SlotTag))];
        List<SharedComponentSlotRescue> rescuedSlots = RescueSlots(
            oldDefinition, newDefinition, oldEntries, newEntries, remainingOld, remainingNew);

        HashSet<string> rescuedOldTags = new(rescuedSlots.Select(r => r.OldTag), StringComparer.Ordinal);
        HashSet<string> rescuedNewTags = new(rescuedSlots.Select(r => r.NewTag), StringComparer.Ordinal);
        List<string> removedSlotTags = [.. remainingOld
            .Where(e => !rescuedOldTags.Contains(e.SlotTag))
            .Select(e => e.SlotTag)];
        List<string> addedSlotTags = [.. remainingNew
            .Where(e => !rescuedNewTags.Contains(e.SlotTag))
            .Select(e => e.SlotTag)];

        return new SharedComponentSlotDelta(
            matchedSlotTags, addedSlotTags, removedSlotTags, renamedSlots, rescuedSlots);
    }

    private static List<SharedComponentSlotRescue> RescueSlots(
        Section oldDefinition,
        Section newDefinition,
        List<SectionEntry> oldEntries,
        List<SectionEntry> newEntries,
        List<SectionEntry> remainingOld,
        List<SectionEntry> remainingNew)
    {
        Dictionary<string, Block> oldByTag = DefinitionBlocksByTag(oldDefinition);
        Dictionary<string, Block> newByTag = DefinitionBlocksByTag(newDefinition);
        List<(SectionEntry Entry, Block Block)> oldCandidates = [.. remainingOld
            .Where(e => e.Kind == SectionEntryKind.Inline && oldByTag.ContainsKey(e.SlotTag))
            .Select(e => (e, oldByTag[e.SlotTag]))];
        List<(SectionEntry Entry, Block Block)> newCandidates = [.. remainingNew
            .Where(e => e.Kind == SectionEntryKind.Inline && newByTag.ContainsKey(e.SlotTag))
            .Select(e => (e, newByTag[e.SlotTag]))];
        if (oldCandidates.Count == 0 || newCandidates.Count == 0)
        {
            return [];
        }

        List<SharedComponentSlotRescue> rescues = [];
        HashSet<string> consumedOldTags = new(StringComparer.Ordinal);
        HashSet<string> consumedNewTags = new(StringComparer.Ordinal);

        ILookup<string, (SectionEntry Entry, Block Block)> oldByName = oldCandidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Block.Name))
            .ToLookup(c => c.Block.Name!.Trim(), StringComparer.OrdinalIgnoreCase);
        ILookup<string, (SectionEntry Entry, Block Block)> newByName = newCandidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Block.Name))
            .ToLookup(c => c.Block.Name!.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, (SectionEntry Entry, Block Block)> oldGroup in oldByName)
        {
            List<(SectionEntry Entry, Block Block)> newGroup = [.. newByName[oldGroup.Key]];
            if (oldGroup.Count() == 1 && newGroup.Count == 1)
            {
                (SectionEntry oldEntry, Block oldBlock) = oldGroup.Single();
                (SectionEntry newEntry, Block newBlock) = newGroup[0];
                if (oldBlock.Type == newBlock.Type)
                {
                    rescues.Add(new SharedComponentSlotRescue(
                        oldEntry.SlotTag, newEntry.SlotTag, SharedComponentSlotRescueReason.NameMatch));
                    _ = consumedOldTags.Add(oldEntry.SlotTag);
                    _ = consumedNewTags.Add(newEntry.SlotTag);
                }
            }
        }

        Dictionary<string, int> oldIndexByTag = EntryIndexByTag(oldEntries);
        Dictionary<string, int> newIndexByTag = EntryIndexByTag(newEntries);
        foreach ((SectionEntry oldEntry, Block oldBlock) in oldCandidates)
        {
            if (consumedOldTags.Contains(oldEntry.SlotTag))
            {
                continue;
            }

            int position = oldIndexByTag[oldEntry.SlotTag];
            foreach ((SectionEntry newEntry, Block newBlock) in newCandidates)
            {
                if (!consumedNewTags.Contains(newEntry.SlotTag)
                    && newIndexByTag[newEntry.SlotTag] == position
                    && oldBlock.Type == newBlock.Type
                    && TagSimilarity(oldEntry.SlotTag, newEntry.SlotTag) >= TAG_SIMILARITY_RESCUE_THRESHOLD)
                {
                    rescues.Add(new SharedComponentSlotRescue(
                        oldEntry.SlotTag,
                        newEntry.SlotTag,
                        SharedComponentSlotRescueReason.PositionAndTagSimilarity));
                    _ = consumedOldTags.Add(oldEntry.SlotTag);
                    _ = consumedNewTags.Add(newEntry.SlotTag);
                    break;
                }
            }
        }

        return [.. rescues.OrderBy(r => oldIndexByTag[r.OldTag])];
    }

    private static Dictionary<string, int> EntryIndexByTag(List<SectionEntry> entries)
    {
        Dictionary<string, int> indexByTag = new(StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            _ = indexByTag.TryAdd(entries[i].SlotTag, i);
        }

        return indexByTag;
    }

    private static double TagSimilarity(string oldTag, string newTag)
    {
        int distance = LevenshteinDistance(oldTag, newTag);

        return 1.0 - ((double)distance / Math.Max(oldTag.Length, newTag.Length));
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// Placeholders a new definition drops from signature-bearing matched slots, keyed by slot
    /// tag. Override rows keyed to a vanished placeholder are never deleted by a graft — the
    /// block survives — but injection stops consulting them, so the reconcile impact reports
    /// them as going inert. Hash-equal renamed slots never appear here — equal content implies
    /// identical placeholder sets — but fuzzy-rescued slots do, keyed by their new tag, because
    /// a rescue grafts different content.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ComputeInertPlaceholders(
        Section oldDefinition,
        Section newDefinition,
        IEnumerable<string> matchedSlotTags,
        IReadOnlyList<SharedComponentSlotRescue>? rescuedSlots = null)
    {
        Dictionary<string, Block> oldByTag = DefinitionBlocksByTag(oldDefinition);
        Dictionary<string, Block> newByTag = DefinitionBlocksByTag(newDefinition);
        Dictionary<string, IReadOnlyList<string>> inertBySlotTag = new(StringComparer.Ordinal);
        foreach (string slotTag in matchedSlotTags)
        {
            if (oldByTag.TryGetValue(slotTag, out Block? oldBlock)
                && newByTag.TryGetValue(slotTag, out Block? newBlock))
            {
                IReadOnlyList<string> removed = RemovedSignaturePlaceholders(oldBlock, newBlock);
                if (removed.Count > 0)
                {
                    inertBySlotTag[slotTag] = removed;
                }
            }
        }

        foreach (SharedComponentSlotRescue rescue in rescuedSlots ?? [])
        {
            if (oldByTag.TryGetValue(rescue.OldTag, out Block? oldBlock)
                && newByTag.TryGetValue(rescue.NewTag, out Block? newBlock))
            {
                IReadOnlyList<string> removed = RemovedSignaturePlaceholders(oldBlock, newBlock);
                if (removed.Count > 0)
                {
                    inertBySlotTag[rescue.NewTag] = removed;
                }
            }
        }

        return inertBySlotTag;
    }

    public static IReadOnlyList<string> RemovedSignaturePlaceholders(Block oldDefinition, Block newDefinition)
    {
        if (oldDefinition.Properties is not IHasSignatureFields oldSignatures
            || oldSignatures.SignatureFields is not { Count: > 0 } oldFields)
        {
            return [];
        }

        IEnumerable<string> keptPlaceholders =
            newDefinition.Properties is IHasSignatureFields { SignatureFields: not null } newSignatures
                ? newSignatures.SignatureFields.Keys
                : [];

        return [.. oldFields.Keys.Except(keptPlaceholders, StringComparer.Ordinal)];
    }

    private (Dictionary<string, string> SlotSources, IReadOnlyDictionary<string, string> RenamedSlots, IReadOnlyList<string> RemovedSlotTags)
        CorrelateSlots(
            Section? oldDefinition,
            Section newDefinition,
            Dictionary<string, string> currentBindings,
            Dictionary<string, Block> instanceByTag)
    {
        Dictionary<string, string> slotSources = new(StringComparer.Ordinal);

        if (oldDefinition is null)
        {
            foreach (Block defBlock in newDefinition.Blocks.OrderBy(b => b.Order))
            {
                string slotTag = defBlock.ReplacementTag;
                string expectedTag = currentBindings.GetValueOrDefault(slotTag, slotTag);
                if (instanceByTag.ContainsKey(expectedTag))
                {
                    slotSources[slotTag] = slotTag;
                }
            }

            return (slotSources, new Dictionary<string, string>(StringComparer.Ordinal), []);
        }

        SharedComponentSlotDelta delta = ComputeSlotDelta(oldDefinition, newDefinition);
        foreach (string matchedTag in delta.MatchedSlotTags)
        {
            slotSources[matchedTag] = matchedTag;
        }

        foreach ((string oldTag, string newTag) in delta.RenamedSlots)
        {
            slotSources[newTag] = oldTag;
        }

        if (delta.RescuedSlots.Count == 0)
        {
            return (slotSources, delta.RenamedSlots, delta.RemovedSlotTags);
        }

        Dictionary<string, string> retags = new(StringComparer.Ordinal);
        foreach ((string oldTag, string newTag) in delta.RenamedSlots)
        {
            retags[oldTag] = newTag;
        }

        foreach (SharedComponentSlotRescue rescue in delta.RescuedSlots)
        {
            slotSources[rescue.NewTag] = rescue.OldTag;
            retags[rescue.OldTag] = rescue.NewTag;
        }

        return (slotSources, retags, delta.RemovedSlotTags);
    }

    internal static (Dictionary<string, string> Bindings, Dictionary<string, Block> InstanceByTag) SnapshotInstance(
        Section instance)
    {
        Dictionary<string, string> bindings = instance.SharedComponentBindings is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(instance.SharedComponentBindings, StringComparer.Ordinal);
        Dictionary<string, Block> instanceByTag = new(StringComparer.Ordinal);
        foreach (Block block in instance.Blocks.OrderBy(b => b.Order))
        {
            if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
            {
                _ = instanceByTag.TryAdd(block.ReplacementTag, block);
            }
        }

        return (bindings, instanceByTag);
    }

    private static Dictionary<string, Block> DefinitionBlocksByTag(Section definition)
    {
        Dictionary<string, Block> byTag = new(StringComparer.Ordinal);
        foreach (Block block in definition.Blocks)
        {
            if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
            {
                _ = byTag.TryAdd(block.ReplacementTag, block);
            }
        }

        return byTag;
    }

    private Dictionary<string, List<SectionEntry>> GroupByEntryHash(
        Section definition,
        List<SectionEntry> entries)
    {
        Dictionary<string, List<SectionEntry>> byHash = new(StringComparer.Ordinal);
        foreach (SectionEntry entry in entries)
        {
            string hash = _normalizer.ComputeEntryContentHash(definition, entry);
            if (!byHash.TryGetValue(hash, out List<SectionEntry>? group))
            {
                group = [];
                byHash[hash] = group;
            }

            group.Add(entry);
        }

        return byHash;
    }
}

public sealed record SharedComponentRelationResult(
    SharedComponentRelation Relation,
    bool LinkHashStale,
    bool HasLocalOverrides = false);

public sealed record SectionInstanceHashResult(
    string ContentHash,
    bool HasLocalOverrides);

public sealed record SharedComponentSlotDelta(
    IReadOnlyList<string> MatchedSlotTags,
    IReadOnlyList<string> AddedSlotTags,
    IReadOnlyList<string> RemovedSlotTags,
    IReadOnlyDictionary<string, string> RenamedSlots,
    IReadOnlyList<SharedComponentSlotRescue> RescuedSlots);

public enum SharedComponentSlotRescueReason
{
    NameMatch = 1,
    PositionAndTagSimilarity = 2,
}

public sealed record SharedComponentSlotRescue(
    string OldTag,
    string NewTag,
    SharedComponentSlotRescueReason Reason);

/// <param name="OverwrittenLocalRefSlots">
/// Ref slots whose live block held edits matching neither the referenced component's head nor its
/// own recorded link stamp, and which this apply replaced with the head definition. The caller owns
/// the decision about whether discarding that work needs acknowledging first.
/// </param>
public sealed record SectionReconcileResult(
    IReadOnlyList<Block> RemovedBlocks,
    IReadOnlyList<Block> AddedBlocks,
    IReadOnlyDictionary<string, string> RenamedSlots,
    bool OldDefinitionAvailable,
    IReadOnlyList<Block> PreservedLocalBlocks,
    IReadOnlyList<string> StaleRefSlots,
    IReadOnlyList<string> DroppedRefSlots,
    IReadOnlyList<string> OverwrittenLocalRefSlots);

public sealed record SharedComponentRefHead(
    string ContentHash,
    Block Definition);
