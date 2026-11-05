using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Workflows;

internal static class SharedComponentRefHeads
{
    internal static List<string> CollectRefTargetIds(Section? sectionDefinition)
    {
        if (sectionDefinition?.Entries is not { Count: > 0 })
        {
            return [];
        }

        return [.. sectionDefinition.Entries
            .Where(e => e.Kind == SectionEntryKind.ComponentRef && !string.IsNullOrEmpty(e.SharedComponentId))
            .Select(e => e.SharedComponentId!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    internal static List<string> CollectUnresolvedRefTargetIds(
        Section? sectionDefinition,
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads)
    {
        return [.. CollectRefTargetIds(sectionDefinition)
            .Where(id => refHeads is null || !refHeads.ContainsKey(id))];
    }

    internal static string? ClassifyRefTarget(
        bool targetKnown,
        SharedComponentKind? kind,
        SharedComponentState? state,
        bool headPayloadAvailable)
    {
        if (!targetKnown)
        {
            return SharedComponentErrorCodes.UNKNOWN_COMPONENT;
        }

        if (kind != SharedComponentKind.Block)
        {
            return SharedComponentErrorCodes.KIND_MISMATCH;
        }

        if (state != SharedComponentState.Active)
        {
            return SharedComponentErrorCodes.RETIRED;
        }

        return headPayloadAvailable ? null : SharedComponentErrorCodes.PAYLOAD_ABSENT;
    }

    internal static async Task<IReadOnlyDictionary<string, SharedComponentRefHead>?> LoadAsync(
        ICoreUnitOfWork coreUnitOfWork,
        Section? sectionDefinition,
        CancellationToken cancellationToken)
    {
        List<string> targetIds = CollectRefTargetIds(sectionDefinition);
        if (targetIds.Count == 0)
        {
            return null;
        }

        IQueryable<SharedComponentVersion> versions = coreUnitOfWork
            .Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking();
        List<SharedComponentVersion> heads = await versions
            .Where(v => targetIds.Contains(v.SharedComponentId)
                && v.PayloadAbsentAt == null
                && !versions.Any(o => o.SharedComponentId == v.SharedComponentId && o.SequenceNumber > v.SequenceNumber))
            .ToListAsync(cancellationToken);

        Dictionary<string, SharedComponentRefHead> refHeads = new(StringComparer.Ordinal);
        foreach (SharedComponentVersion head in heads)
        {
            Block? definition = SharedComponentContentNormalizer.ReadBlockDefinitionForNewCopy(head.ContentJson);
            if (definition is not null)
            {
                refHeads[head.SharedComponentId] = new SharedComponentRefHead(head.ContentHash, definition);
            }
        }

        return refHeads.Count > 0 ? refHeads : null;
    }

    internal static Block MaterializeRefBlock(
        Section destination,
        string landedTag,
        SectionEntry entry,
        SharedComponentRefHead refHead)
    {
        Block landed = refHead.Definition.CloneToSection(destination, -1, landedTag);
        landed.ReMintStableBlockId();
        landed.SharedComponentId = entry.SharedComponentId;
        landed.SharedComponentLinkState = SharedComponentLinkState.Linked;
        landed.SharedComponentVersionHash = refHead.ContentHash;
        return landed;
    }

    internal static void ReapplyRefDefinition(
        Block live,
        string refComponentId,
        string contentHash,
        Block refDefinition)
    {
        live.ApplySharedDefinition(refDefinition);
        live.SharedComponentId = refComponentId;
        live.SharedComponentLinkState = SharedComponentLinkState.Linked;
        live.SharedComponentVersionHash = contentHash;
    }

    internal static void BindRefBlock(
        SectionEntry entry,
        Block refBlock,
        Dictionary<string, string> containerBindings)
    {
        Dictionary<string, string>? refBlockBindings =
            SharedComponentReconciler.ComposeRefBlockBindings(entry, containerBindings);
        refBlock.SharedComponentBindings = refBlockBindings;
        if (refBlockBindings is { Count: > 0 })
        {
            refBlock.ApplyReplacementTagRenames(refBlockBindings);
        }
    }
}
