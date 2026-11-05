using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.Workflows;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.SharedComponents.GetWorkflowSharedComponentReconcile;

/// <summary>
/// The reconcile surface of one template: every block or section carrying a shared-component
/// link record (Linked or BrokenOut — a node with a component id but no link state behaves as
/// plain local content and is not listed), classified against the live registry. Rows carry
/// both address forms, the three-hash compare inputs, the sanctioned decision set ordered
/// default-first, and whether this deployment's chain still holds a payload for the definition
/// the link hash was stamped from. <c>BaseVersionId</c> is the template's current head — the
/// anchor token a reconcile apply must echo. The registry is global per deployment, so
/// OrganizationId/UserId ride along per house convention without filtering.
/// </summary>
public record GetWorkflowSharedComponentReconcileQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId) : IRequest<ErrorOr<WorkflowSharedComponentReconcileResult>>;

public record WorkflowSharedComponentReconcileResult(
    string WorkflowTypeId,
    string? BaseVersionId,
    string? BaseContentHash,
    IReadOnlyList<SharedComponentInstanceReconcileRow> Instances);

public sealed record SharedComponentInstanceReconcileRow(
    string TargetPath,
    string? TargetStableId,
    SharedComponentKind Kind,
    string SharedComponentId,
    string? ComponentName,
    SharedComponentState? ComponentState,
    SharedComponentLinkState LinkState,
    SharedComponentRelation Relation,
    string InstanceContentHash,
    string? LinkVersionHash,
    string? HeadVersionId,
    string? HeadContentHash,
    bool LinkHashStale,
    bool OldDefinitionAvailable,
    IReadOnlyList<SharedComponentReconcileAction> AvailableActions,
    bool OrderDeviates = false,
    IReadOnlyList<SharedComponentEntryReconcileRow>? Entries = null,
    bool HasLocalOverrides = false,
    int LocalInsertCount = 0);

public sealed record SharedComponentEntryReconcileRow(
    string? EntryKey,
    string? EntryStableId,
    string? EntryPath,
    string DisplayName,
    SharedComponentEntryOrigin Origin,
    SharedComponentEntryOverrideState OverrideState,
    string? RefSharedComponentId,
    string? RefComponentName,
    SharedComponentState? RefComponentState,
    string? RefHeadVersionId,
    string? RefHeadContentHash,
    SharedComponentRelation? Relation,
    string? InstanceContentHash,
    bool DeletedByDefinition,
    IReadOnlyList<SharedComponentReconcileAction> AvailableActions,
    SharedComponentLinkState? InsertLinkState = null);

public class GetWorkflowSharedComponentReconcileQueryHandler
    : IRequestHandler<GetWorkflowSharedComponentReconcileQuery, ErrorOr<WorkflowSharedComponentReconcileResult>>
{
    private const string WORKFLOW_TEMPLATE_NOT_FOUND = "WorkflowTemplate.NotFound";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly SharedComponentReconciler _reconciler;
    private readonly SharedComponentContentNormalizer _normalizer;
    private readonly Dictionary<Section, Dictionary<string, string>> _entryHashesByDefinition =
        new(ReferenceEqualityComparer.Instance);

    public GetWorkflowSharedComponentReconcileQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponentReconciler reconciler,
        SharedComponentContentNormalizer normalizer)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public async Task<ErrorOr<WorkflowSharedComponentReconcileResult>> Handle(
        GetWorkflowSharedComponentReconcileQuery request,
        CancellationToken cancellationToken)
    {
        Workflow? workflow = await _coreUnitOfWork.Workflows
            .GetByTypeIdAsNoTrackingAsync(request.WorkflowTypeId);
        if (workflow is null)
        {
            return Error.NotFound(
                WORKFLOW_TEMPLATE_NOT_FOUND,
                $"Workflow template '{request.WorkflowTypeId}' was not found.");
        }

        List<LinkedNode> nodes = CollectLinkedNodes(workflow);
        List<string> componentIds = [.. nodes
            .Select(n => n.Linkable.SharedComponentId!)
            .Distinct(StringComparer.Ordinal)];
        Dictionary<string, SharedComponent> componentsById = componentIds.Count == 0
            ? new Dictionary<string, SharedComponent>(StringComparer.Ordinal)
            : (await _coreUnitOfWork.Repository<SharedComponent>()
                .GetAll()
                .AsNoTracking()
                .Where(c => componentIds.Contains(c.Id))
                .ToListAsync(cancellationToken))
                .ToDictionary(c => c.Id, StringComparer.Ordinal);
        Dictionary<string, SharedComponentProjections.HeadTokens> headsById = componentIds.Count == 0
            ? new Dictionary<string, SharedComponentProjections.HeadTokens>(StringComparer.Ordinal)
            : await SharedComponentProjections.GetHeadsAsync(_coreUnitOfWork, componentIds, cancellationToken);
        HashSet<(string ComponentId, string ContentHash)> payloadPresentHashes =
            await GetPayloadPresentHashesAsync(componentIds, cancellationToken);

        Dictionary<(string ComponentId, string ContentHash), Section> sectionDefinitions =
            await LoadSectionDefinitionsAsync(nodes, headsById, cancellationToken);
        (Dictionary<string, SharedComponent> refComponentsById,
            Dictionary<string, SharedComponentProjections.HeadTokens> refHeadsById) =
            await LoadRefTargetsAsync(nodes, headsById, sectionDefinitions, cancellationToken);
        StoredVersionHashes comparisonHashes =
            await LoadSignatureInsensitiveVersionHashesAsync(nodes, headsById, refHeadsById, cancellationToken);

        List<SharedComponentInstanceReconcileRow> rows = [];
        foreach (LinkedNode node in nodes)
        {
            SharedComponent? component = componentsById.GetValueOrDefault(node.Linkable.SharedComponentId!);
            SharedComponentProjections.HeadTokens? head =
                headsById.GetValueOrDefault(node.Linkable.SharedComponentId!);
            string? linkVersionHash = node.Linkable.SharedComponentVersionHash;
            Section? oldDefinition = string.IsNullOrWhiteSpace(linkVersionHash)
                ? null
                : sectionDefinitions.GetValueOrDefault((node.Linkable.SharedComponentId!, linkVersionHash));
            string? comparisonHeadHash = comparisonHashes.Of(node.Linkable.SharedComponentId, head?.ContentHash);
            string instanceContentHash;
            string comparisonInstanceHash;
            bool hasLocalOverrides = false;
            if (node.Block is not null)
            {
                instanceContentHash = _reconciler.ComputeInstanceContentHash(node.Block);
                comparisonInstanceHash = _reconciler.ComputeSignatureInsensitiveInstanceContentHash(node.Block);
            }
            else
            {
                SectionInstanceHashResult hashResult =
                    _reconciler.ComputeInstanceContentHash(node.Section!, () => oldDefinition, head?.ContentHash);
                instanceContentHash = hashResult.ContentHash;
                hasLocalOverrides = hashResult.HasLocalOverrides;
                comparisonInstanceHash = _reconciler.ComputeSignatureInsensitiveInstanceContentHash(
                    node.Section!, () => oldDefinition, comparisonHeadHash).ContentHash;
            }

            SharedComponentRelationResult relation = _reconciler.Classify(
                component,
                comparisonHeadHash,
                comparisonInstanceHash,
                comparisonHashes.Of(node.Linkable.SharedComponentId, linkVersionHash),
                hasLocalOverrides);
            bool linkHashStale = relation.Relation == SharedComponentRelation.InSync
                && head is not null
                && !string.Equals(linkVersionHash, head.ContentHash, StringComparison.Ordinal);
            bool oldDefinitionAvailable = !string.IsNullOrWhiteSpace(linkVersionHash)
                && payloadPresentHashes.Contains((node.Linkable.SharedComponentId!, linkVersionHash));

            IReadOnlyList<SharedComponentEntryReconcileRow>? entries = null;
            bool orderDeviates = false;
            if (node.Section is not null
                && node.Linkable.SharedComponentLinkState == SharedComponentLinkState.Linked
                && head is not null
                && sectionDefinitions.GetValueOrDefault(
                    (node.Linkable.SharedComponentId!, head.ContentHash)) is { } headDefinition)
            {
                (entries, orderDeviates) = BuildEntryRows(
                    node.TargetPath,
                    node.Section,
                    headDefinition,
                    oldDefinition,
                    refComponentsById,
                    refHeadsById,
                    comparisonHashes);
            }

            rows.Add(new SharedComponentInstanceReconcileRow(
                node.TargetPath,
                node.TargetStableId,
                node.Kind,
                node.Linkable.SharedComponentId!,
                component?.Name,
                component?.State,
                node.Linkable.SharedComponentLinkState!.Value,
                relation.Relation,
                instanceContentHash,
                linkVersionHash,
                head?.VersionId,
                head?.ContentHash,
                linkHashStale,
                oldDefinitionAvailable,
                SharedComponentReconciler.AvailableActions(
                    node.Linkable.SharedComponentLinkState!.Value,
                    relation.Relation),
                orderDeviates,
                entries,
                relation.HasLocalOverrides,
                entries?.Count(entry => entry.Origin == SharedComponentEntryOrigin.LocalInsert) ?? 0));
        }

        WorkflowVersion? baseVersion = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(new LatestWorkflowVersionByTypeSpec(request.WorkflowTypeId), cancellationToken);

        return new WorkflowSharedComponentReconcileResult(
            request.WorkflowTypeId,
            baseVersion?.Id,
            baseVersion?.ContentHash,
            rows);
    }

    private async Task<HashSet<(string ComponentId, string ContentHash)>> GetPayloadPresentHashesAsync(
        List<string> componentIds,
        CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0)
        {
            return [];
        }

        var chainRows = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => componentIds.Contains(v.SharedComponentId) && v.PayloadAbsentAt == null)
            .Select(v => new { v.SharedComponentId, v.ContentHash })
            .ToListAsync(cancellationToken);

        return [.. chainRows.Select(r => (r.SharedComponentId, r.ContentHash))];
    }

    private static List<LinkedNode> CollectLinkedNodes(Workflow workflow)
    {
        List<LinkedNode> nodes = [];
        List<Section> orderedSections = [.. workflow.Sections.OrderBy(s => s.Order)];
        for (int sectionRank = 0; sectionRank < orderedSections.Count; sectionRank++)
        {
            Section section = orderedSections[sectionRank];
            if (IsListed(section))
            {
                nodes.Add(new LinkedNode(
                    SharedComponentEditor.CanonicalPath(sectionRank),
                    section.StableSectionId,
                    SharedComponentKind.Section,
                    section,
                    Block: null,
                    Section: section));
                continue;
            }

            List<Block> orderedBlocks = [.. section.Blocks.OrderBy(b => b.Order)];
            for (int blockRank = 0; blockRank < orderedBlocks.Count; blockRank++)
            {
                Block block = orderedBlocks[blockRank];
                if (IsListed(block))
                {
                    nodes.Add(new LinkedNode(
                        SharedComponentEditor.CanonicalPath(sectionRank, blockRank),
                        block.StableBlockId,
                        SharedComponentKind.Block,
                        block,
                        Block: block,
                        Section: null));
                }
            }
        }

        return nodes;
    }

    private async Task<Dictionary<(string ComponentId, string ContentHash), Section>> LoadSectionDefinitionsAsync(
        List<LinkedNode> nodes,
        Dictionary<string, SharedComponentProjections.HeadTokens> headsById,
        CancellationToken cancellationToken)
    {
        HashSet<(string, string)> wanted = [];
        foreach (LinkedNode node in nodes.Where(n => n.Section is not null))
        {
            string componentId = node.Linkable.SharedComponentId!;
            if (headsById.GetValueOrDefault(componentId) is { } head)
            {
                _ = wanted.Add((componentId, head.ContentHash));
            }

            if (!string.IsNullOrWhiteSpace(node.Linkable.SharedComponentVersionHash))
            {
                _ = wanted.Add((componentId, node.Linkable.SharedComponentVersionHash));
            }
        }

        if (wanted.Count == 0)
        {
            return [];
        }

        List<string> componentIds = [.. wanted.Select(w => w.Item1).Distinct(StringComparer.Ordinal)];
        List<string> hashes = [.. wanted.Select(w => w.Item2).Distinct(StringComparer.Ordinal)];
        List<SharedComponentVersion> payloadRows = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => componentIds.Contains(v.SharedComponentId)
                && hashes.Contains(v.ContentHash)
                && v.PayloadAbsentAt == null
                && v.ContentJsonCompressed != null)
            .ToListAsync(cancellationToken);

        Dictionary<(string, string), Section> definitions = [];
        foreach (SharedComponentVersion row in payloadRows)
        {
            (string, string) key = (row.SharedComponentId, row.ContentHash);
            if (!wanted.Contains(key) || definitions.ContainsKey(key))
            {
                continue;
            }

            if (Section.FromJson(row.ContentJson) is { } parsed)
            {
                definitions[key] = parsed;
            }
        }

        return definitions;
    }

    private async Task<(Dictionary<string, SharedComponent> Components, Dictionary<string, SharedComponentProjections.HeadTokens> Heads)>
        LoadRefTargetsAsync(
            List<LinkedNode> nodes,
            Dictionary<string, SharedComponentProjections.HeadTokens> headsById,
            Dictionary<(string ComponentId, string ContentHash), Section> sectionDefinitions,
            CancellationToken cancellationToken)
    {
        HashSet<string> refIds = new(StringComparer.Ordinal);
        foreach (LinkedNode node in nodes.Where(n =>
            n.Section is not null && n.Linkable.SharedComponentLinkState == SharedComponentLinkState.Linked))
        {
            string componentId = node.Linkable.SharedComponentId!;
            if (headsById.GetValueOrDefault(componentId) is { } head
                && sectionDefinitions.GetValueOrDefault((componentId, head.ContentHash)) is { } headDefinition)
            {
                foreach (SectionEntry entry in headDefinition.EffectiveEntries()
                    .Where(e => e.Kind == SectionEntryKind.ComponentRef && !string.IsNullOrEmpty(e.SharedComponentId)))
                {
                    _ = refIds.Add(entry.SharedComponentId!);
                }
            }

            foreach (Block inner in node.Section!.Blocks.Where(IsListed))
            {
                _ = refIds.Add(inner.SharedComponentId!);
            }
        }

        if (refIds.Count == 0)
        {
            return (new Dictionary<string, SharedComponent>(StringComparer.Ordinal),
                new Dictionary<string, SharedComponentProjections.HeadTokens>(StringComparer.Ordinal));
        }

        List<string> ids = [.. refIds];
        Dictionary<string, SharedComponent> components = (await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);
        Dictionary<string, SharedComponentProjections.HeadTokens> heads =
            await SharedComponentProjections.GetHeadsAsync(_coreUnitOfWork, ids, cancellationToken);

        return (components, heads);
    }

    private async Task<StoredVersionHashes> LoadSignatureInsensitiveVersionHashesAsync(
            List<LinkedNode> nodes,
            Dictionary<string, SharedComponentProjections.HeadTokens> headsById,
            Dictionary<string, SharedComponentProjections.HeadTokens> refHeadsById,
            CancellationToken cancellationToken)
    {
        HashSet<(string, string)> wanted = [];
        foreach (LinkedNode node in nodes)
        {
            string componentId = node.Linkable.SharedComponentId!;
            if (headsById.GetValueOrDefault(componentId) is { } head)
            {
                _ = wanted.Add((componentId, head.ContentHash));
            }

            if (!string.IsNullOrWhiteSpace(node.Linkable.SharedComponentVersionHash))
            {
                _ = wanted.Add((componentId, node.Linkable.SharedComponentVersionHash!));
            }

            if (node.Section is null)
            {
                continue;
            }

            foreach (Block inner in node.Section.Blocks.Where(IsListed))
            {
                if (refHeadsById.GetValueOrDefault(inner.SharedComponentId!) is { } refHead)
                {
                    _ = wanted.Add((inner.SharedComponentId!, refHead.ContentHash));
                }

                if (!string.IsNullOrWhiteSpace(inner.SharedComponentVersionHash))
                {
                    _ = wanted.Add((inner.SharedComponentId!, inner.SharedComponentVersionHash!));
                }
            }
        }

        return await StoredVersionHashes.LoadSignatureInsensitiveHashesAsync(
            _coreUnitOfWork, _normalizer, wanted, cancellationToken);
    }

    private static string DefinitionEntryDisplayName(
        SharedComponentEntryOrigin origin,
        SharedComponent? refComponent,
        SectionEntry defEntry,
        string slot,
        IReadOnlyDictionary<string, Block> defBlocksByTag)
    {
        return origin == SharedComponentEntryOrigin.DefinitionRef
            ? refComponent?.Name ?? defEntry.SharedComponentId ?? slot
            : defBlocksByTag.GetValueOrDefault(slot)?.Name is { Length: > 0 } defName ? defName : slot;
    }

    private (IReadOnlyList<SharedComponentEntryReconcileRow> Entries, bool OrderDeviates) BuildEntryRows(
        string sectionPath,
        Section section,
        Section headDefinition,
        Section? oldDefinition,
        Dictionary<string, SharedComponent> refComponentsById,
        Dictionary<string, SharedComponentProjections.HeadTokens> refHeadsById,
        StoredVersionHashes comparisonHashes)
    {
        List<SectionEntry> defEntries = headDefinition.EffectiveEntries();
        Dictionary<string, string> bindings = section.SharedComponentBindings is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(section.SharedComponentBindings, StringComparer.Ordinal);
        Dictionary<string, SharedComponentSlotState> slotStates = section.SharedComponentSlotStates is null
            ? new Dictionary<string, SharedComponentSlotState>(StringComparer.Ordinal)
            : new Dictionary<string, SharedComponentSlotState>(section.SharedComponentSlotStates, StringComparer.Ordinal);
        List<Block> liveOrdered = [.. section.Blocks.OrderBy(b => b.Order)];
        Dictionary<string, Block> liveByTag = new(StringComparer.Ordinal);
        Dictionary<Block, int> liveRank = [];
        for (int i = 0; i < liveOrdered.Count; i++)
        {
            liveRank[liveOrdered[i]] = i;
            if (!string.IsNullOrWhiteSpace(liveOrdered[i].ReplacementTag))
            {
                _ = liveByTag.TryAdd(liveOrdered[i].ReplacementTag, liveOrdered[i]);
            }
        }

        Dictionary<string, Block> defBlocksByTag = new(StringComparer.Ordinal);
        foreach (Block defBlock in headDefinition.Blocks)
        {
            _ = defBlocksByTag.TryAdd(defBlock.ReplacementTag, defBlock);
        }

        Dictionary<string, string> headEntryHashByTag = EntryHashesByTag(headDefinition, defEntries);
        Dictionary<string, string>? oldEntryHashByTag = oldDefinition is null
            ? null
            : EntryHashesByTag(oldDefinition, oldDefinition.EffectiveEntries());

        List<SharedComponentEntryReconcileRow> rows = [];
        HashSet<Block> claimed = [];
        List<(int LiveRank, int DefRank)> claimedRanks = [];
        for (int defRank = 0; defRank < defEntries.Count; defRank++)
        {
            SectionEntry defEntry = defEntries[defRank];
            string slot = defEntry.SlotTag;
            SharedComponentSlotState? state = slotStates.TryGetValue(slot, out SharedComponentSlotState s)
                ? s
                : null;
            SharedComponentEntryOrigin origin = defEntry.Kind == SectionEntryKind.ComponentRef
                ? SharedComponentEntryOrigin.DefinitionRef
                : SharedComponentEntryOrigin.DefinitionInline;
            SharedComponent? refComponent = defEntry.SharedComponentId is null
                ? null
                : refComponentsById.GetValueOrDefault(defEntry.SharedComponentId);
            SharedComponentProjections.HeadTokens? refHead = defEntry.SharedComponentId is null
                ? null
                : refHeadsById.GetValueOrDefault(defEntry.SharedComponentId);

            if (state == SharedComponentSlotState.Hidden)
            {
                string hiddenName = DefinitionEntryDisplayName(origin, refComponent, defEntry, slot, defBlocksByTag);
                rows.Add(BuildEntryRow(
                    slot, null, null, hiddenName, origin, SharedComponentEntryOverrideState.Hidden,
                    defEntry.SharedComponentId, refComponent?.Name, refComponent?.State,
                    refHead?.VersionId, refHead?.ContentHash,
                    relation: null, instanceContentHash: null, deletedByDefinition: false));
                continue;
            }

            string liveTag = bindings.GetValueOrDefault(slot, slot);
            Block? live = liveByTag.GetValueOrDefault(liveTag);
            if (live is null)
            {
                string missingName = DefinitionEntryDisplayName(origin, refComponent, defEntry, slot, defBlocksByTag);
                rows.Add(BuildEntryRow(
                    slot, null, null, missingName, origin, SharedComponentEntryOverrideState.None,
                    defEntry.SharedComponentId, refComponent?.Name, refComponent?.State,
                    refHead?.VersionId, refHead?.ContentHash,
                    relation: null, instanceContentHash: null, deletedByDefinition: false,
                    liveMissing: true));
                continue;
            }

            _ = claimed.Add(live);
            claimedRanks.Add((liveRank[live], defRank));
            SharedComponentEntryOverrideState overrideState = state == SharedComponentSlotState.BrokenOut
                ? SharedComponentEntryOverrideState.BrokenOut
                : SharedComponentEntryOverrideState.None;
            string entryPath = $"{sectionPath}.Blocks[{liveRank[live]}]";
            string displayName = string.IsNullOrWhiteSpace(live.Name) ? liveTag : live.Name;

            if (origin == SharedComponentEntryOrigin.DefinitionRef)
            {
                string instanceHash = _reconciler.ComputeInstanceContentHash(live);
                SharedComponentRelationResult refRelation = _reconciler.Classify(
                    refComponent,
                    comparisonHashes.Of(defEntry.SharedComponentId, refHead?.ContentHash),
                    _reconciler.ComputeSignatureInsensitiveInstanceContentHash(live),
                    comparisonHashes.Of(live.SharedComponentId, live.SharedComponentVersionHash));
                rows.Add(BuildEntryRow(
                    slot, live.StableBlockId, entryPath, displayName, origin, overrideState,
                    defEntry.SharedComponentId, refComponent?.Name, refComponent?.State,
                    refHead?.VersionId, refHead?.ContentHash,
                    refRelation.Relation, instanceHash, deletedByDefinition: false));
                continue;
            }

            Dictionary<string, string>? entryBindings = string.Equals(liveTag, slot, StringComparison.Ordinal)
                ? null
                : new Dictionary<string, string>(StringComparer.Ordinal) { [slot] = liveTag };
            string liveEntryHash = _normalizer.ComputeContentHash(live, entryBindings);
            SharedComponentRelation inlineRelation = ClassifyEntry(
                _normalizer.ComputeSignatureInsensitiveContentHash(live, entryBindings),
                headEntryHashByTag.GetValueOrDefault(slot),
                oldEntryHashByTag?.GetValueOrDefault(slot));
            rows.Add(BuildEntryRow(
                slot, live.StableBlockId, entryPath, displayName, origin, overrideState,
                refSharedComponentId: null, refComponentName: null, refComponentState: null,
                refHeadVersionId: null, refHeadContentHash: null,
                inlineRelation, liveEntryHash, deletedByDefinition: false));
        }

        Dictionary<string, string> slotByLiveTag = new(StringComparer.Ordinal);
        foreach ((string slot, string liveTag) in bindings)
        {
            _ = slotByLiveTag.TryAdd(liveTag, slot);
        }

        HashSet<string> defTags = new(defEntries.Select(e => e.SlotTag), StringComparer.Ordinal);
        foreach (Block live in liveOrdered.Where(b => !claimed.Contains(b)))
        {
            string entryPath = $"{sectionPath}.Blocks[{liveRank[live]}]";
            string displayName = string.IsNullOrWhiteSpace(live.Name) ? live.ReplacementTag : live.Name;
            string? formerSlot = slotByLiveTag.GetValueOrDefault(live.ReplacementTag);
            bool deletedByDefinition = formerSlot is not null && !defTags.Contains(formerSlot);
            if (deletedByDefinition)
            {
                rows.Add(BuildEntryRow(
                    formerSlot, live.StableBlockId, entryPath, displayName,
                    SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
                    refSharedComponentId: null, refComponentName: null, refComponentState: null,
                    refHeadVersionId: null, refHeadContentHash: null,
                    relation: null, _reconciler.ComputeInstanceContentHash(live), deletedByDefinition: true));
                continue;
            }

            if (IsListed(live))
            {
                SharedComponent? insertComponent = refComponentsById.GetValueOrDefault(live.SharedComponentId!);
                SharedComponentProjections.HeadTokens? insertHead = refHeadsById.GetValueOrDefault(live.SharedComponentId!);
                string instanceHash = _reconciler.ComputeInstanceContentHash(live);
                SharedComponentRelationResult insertRelation = _reconciler.Classify(
                    insertComponent,
                    comparisonHashes.Of(live.SharedComponentId, insertHead?.ContentHash),
                    _reconciler.ComputeSignatureInsensitiveInstanceContentHash(live),
                    comparisonHashes.Of(live.SharedComponentId, live.SharedComponentVersionHash));
                rows.Add(BuildEntryRow(
                    entryKey: null, live.StableBlockId, entryPath, displayName,
                    SharedComponentEntryOrigin.LocalInsert, SharedComponentEntryOverrideState.None,
                    live.SharedComponentId, insertComponent?.Name, insertComponent?.State,
                    insertHead?.VersionId, insertHead?.ContentHash,
                    insertRelation.Relation, instanceHash, deletedByDefinition: false,
                    insertLinkState: live.SharedComponentLinkState));
                continue;
            }

            rows.Add(BuildEntryRow(
                entryKey: null, live.StableBlockId, entryPath, displayName,
                SharedComponentEntryOrigin.LocalInsert, SharedComponentEntryOverrideState.None,
                refSharedComponentId: null, refComponentName: null, refComponentState: null,
                refHeadVersionId: null, refHeadContentHash: null,
                relation: null, _reconciler.ComputeInstanceContentHash(live), deletedByDefinition: false));
        }

        bool orderDeviates = false;
        List<int> defRankByLiveOrder = [.. claimedRanks.OrderBy(r => r.LiveRank).Select(r => r.DefRank)];
        for (int i = 1; i < defRankByLiveOrder.Count; i++)
        {
            if (defRankByLiveOrder[i] < defRankByLiveOrder[i - 1])
            {
                orderDeviates = true;
                break;
            }
        }

        return (rows, orderDeviates);
    }

    private Dictionary<string, string> EntryHashesByTag(Section definition, List<SectionEntry> entries)
    {
        if (_entryHashesByDefinition.TryGetValue(definition, out Dictionary<string, string>? cached))
        {
            return cached;
        }

        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (SectionEntry entry in entries)
        {
            hashes[entry.SlotTag] = _normalizer.ComputeSignatureInsensitiveEntryContentHash(definition, entry);
        }

        _entryHashesByDefinition[definition] = hashes;

        return hashes;
    }

    private static SharedComponentEntryReconcileRow BuildEntryRow(
        string? entryKey,
        string? entryStableId,
        string? entryPath,
        string displayName,
        SharedComponentEntryOrigin origin,
        SharedComponentEntryOverrideState overrideState,
        string? refSharedComponentId,
        string? refComponentName,
        SharedComponentState? refComponentState,
        string? refHeadVersionId,
        string? refHeadContentHash,
        SharedComponentRelation? relation,
        string? instanceContentHash,
        bool deletedByDefinition,
        SharedComponentLinkState? insertLinkState = null,
        bool liveMissing = false)
    {
        return new SharedComponentEntryReconcileRow(
            entryKey, entryStableId, entryPath, displayName, origin, overrideState,
            refSharedComponentId, refComponentName, refComponentState,
            refHeadVersionId, refHeadContentHash,
            relation, instanceContentHash, deletedByDefinition,
            SharedComponentReconciler.EntryAvailableActions(
                origin, overrideState, relation, insertLinkState, deletedByDefinition, liveMissing),
            insertLinkState);
    }

    private static SharedComponentRelation ClassifyEntry(string liveHash, string? headHash, string? oldHash)
    {
        if (string.Equals(liveHash, headHash, StringComparison.Ordinal))
        {
            return SharedComponentRelation.InSync;
        }

        if (oldHash is null)
        {
            return SharedComponentRelation.Diverged;
        }

        if (string.Equals(liveHash, oldHash, StringComparison.Ordinal))
        {
            return SharedComponentRelation.HeadAhead;
        }

        return string.Equals(oldHash, headHash, StringComparison.Ordinal)
            ? SharedComponentRelation.LocalAhead
            : SharedComponentRelation.Diverged;
    }

    private static bool IsListed(ISharedComponentLinkable node)
    {
        return !string.IsNullOrEmpty(node.SharedComponentId) && node.SharedComponentLinkState is not null;
    }

    private sealed record LinkedNode(
        string TargetPath,
        string? TargetStableId,
        SharedComponentKind Kind,
        ISharedComponentLinkable Linkable,
        Block? Block,
        Section? Section);
}

public class GetWorkflowSharedComponentReconcileQueryValidator
    : AbstractValidator<GetWorkflowSharedComponentReconcileQuery>
{
    public GetWorkflowSharedComponentReconcileQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
    }
}
