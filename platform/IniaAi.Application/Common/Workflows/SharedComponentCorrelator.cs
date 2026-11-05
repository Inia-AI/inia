using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using Microsoft.EntityFrameworkCore;
using CorrelationMethod = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferCorrelationMethod;

namespace IniaAi.Application.Common.Workflows;

internal static class SharedComponentCorrelator
{
    internal sealed record Correlation(string? LocalComponentId, CorrelationMethod Method);

    private sealed record AnchorSlot(string StableId, int Kind);

    internal static async Task<IReadOnlyDictionary<string, Correlation>> CorrelateAsync(
        ICoreUnitOfWork coreUnitOfWork,
        Workflow localTemplate,
        Workflow bundleTemplate,
        IReadOnlyList<TransferComponentDto> bundleComponents,
        CancellationToken cancellationToken)
    {
        Dictionary<string, List<AnchorSlot>> bundleAnchors = EnumerateAnchors(bundleTemplate)
            .GroupBy(anchor => anchor.ComponentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(anchor => anchor.Slot).ToList(),
                StringComparer.Ordinal);

        var localAnchors = new Dictionary<(string StableId, int Kind), string>();
        foreach ((string componentId, AnchorSlot slot) in EnumerateAnchors(localTemplate))
        {
            _ = localAnchors.TryAdd((slot.StableId, slot.Kind), componentId);
        }

        List<string> bundleHashes = [.. bundleComponents
            .SelectMany(component => component.HashTrail)
            .Distinct(StringComparer.Ordinal)];

        var localIdsByHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var neededLocalIds = new HashSet<string>(bundleComponents.Select(c => c.ComponentId), StringComparer.Ordinal);
        if (bundleHashes.Count > 0)
        {
            List<HashRow> hashRows = await coreUnitOfWork
                .Repository<SharedComponentVersion>()
                .GetAll()
                .AsNoTracking()
                .Where(version => bundleHashes.Contains(version.ContentHash))
                .Select(version => new HashRow(version.SharedComponentId, version.ContentHash))
                .ToListAsync(cancellationToken);

            localIdsByHash = hashRows
                .GroupBy(row => row.ContentHash, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.ComponentId).Distinct(StringComparer.Ordinal).ToList(),
                    StringComparer.Ordinal);
            neededLocalIds.UnionWith(hashRows.Select(row => row.ComponentId));
        }

        Dictionary<string, int> localKindById = await LoadKindsAsync(coreUnitOfWork, neededLocalIds, cancellationToken);

        var map = new Dictionary<string, Correlation>(StringComparer.Ordinal);
        foreach (TransferComponentDto component in bundleComponents)
        {
            int kind = (int)component.Kind;
            string? matched = MatchByAnchor(component.ComponentId, kind, bundleAnchors, localAnchors);
            CorrelationMethod method = CorrelationMethod.Anchor;

            if (matched is null)
            {
                matched = MatchByHashTrail(component, kind, localIdsByHash, localKindById);
                method = CorrelationMethod.HashTrail;
            }

            if (matched is null && localKindById.TryGetValue(component.ComponentId, out int localKind) && localKind == kind)
            {
                matched = component.ComponentId;
                method = CorrelationMethod.GuidEquality;
            }

            map[component.ComponentId] = new Correlation(
                matched,
                matched is null ? CorrelationMethod.Create : method);
        }

        return map;
    }

    private static string? MatchByAnchor(
        string bundleComponentId,
        int kind,
        Dictionary<string, List<AnchorSlot>> bundleAnchors,
        Dictionary<(string StableId, int Kind), string> localAnchors)
    {
        if (!bundleAnchors.TryGetValue(bundleComponentId, out List<AnchorSlot>? slots))
        {
            return null;
        }

        foreach (AnchorSlot slot in slots)
        {
            if (slot.Kind == kind && localAnchors.TryGetValue((slot.StableId, slot.Kind), out string? localId))
            {
                return localId;
            }
        }

        return null;
    }

    private static string? MatchByHashTrail(
        TransferComponentDto component,
        int kind,
        Dictionary<string, List<string>> localIdsByHash,
        Dictionary<string, int> localKindById)
    {
        return component.HashTrail
            .SelectMany(hash => localIdsByHash.GetValueOrDefault(hash) ?? [])
            .Distinct(StringComparer.Ordinal)
            .Where(localId => localKindById.GetValueOrDefault(localId, -1) == kind)
            .OrderBy(localId => localId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static async Task<Dictionary<string, int>> LoadKindsAsync(
        ICoreUnitOfWork coreUnitOfWork,
        HashSet<string> componentIds,
        CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        return await coreUnitOfWork
            .Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(component => componentIds.Contains(component.Id))
            .Select(component => new { component.Id, component.Kind })
            .ToDictionaryAsync(row => row.Id, row => (int)row.Kind, StringComparer.Ordinal, cancellationToken);
    }

    private static IEnumerable<(string ComponentId, AnchorSlot Slot)> EnumerateAnchors(Workflow template)
    {
        foreach (Section section in template.Sections)
        {
            if (section.IsActivelyLinked() && !string.IsNullOrEmpty(section.StableSectionId))
            {
                yield return (section.SharedComponentId!, new AnchorSlot(section.StableSectionId!, (int)SharedComponentKind.Section));
            }
        }

        foreach (Block block in template.GetAllBlocks())
        {
            if (block.IsActivelyLinked() && !string.IsNullOrEmpty(block.StableBlockId))
            {
                yield return (block.SharedComponentId!, new AnchorSlot(block.StableBlockId!, (int)SharedComponentKind.Block));
            }
        }
    }

    private sealed record HashRow(string ComponentId, string ContentHash);
}
