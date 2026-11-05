using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using Microsoft.EntityFrameworkCore;
using ContractsConsumerChannel = IniaAi.WebApi.Contracts.Enums.SharedComponentConsumerChannel;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;

namespace IniaAi.Application.Queries.SharedComponents;

/// <summary>
/// Set-based head and consumer projections shared by the registry read queries and the promote
/// attach check. Heads are selected by highest SequenceNumber without loading payloads; consumer
/// counts and consuming-type facets cover live Core template copies whose link state is Linked —
/// broken-out copies keep the component id as provenance and must not count as consumers.
/// </summary>
internal static class SharedComponentProjections
{
    internal sealed record HeadTokens(
        string VersionId,
        string ContentHash,
        bool PayloadAbsent,
        DateTime CreatedAtUtc);

    private static IQueryable<SharedComponentVersion> HeadVersions(
        ICoreUnitOfWork coreUnitOfWork,
        IReadOnlyCollection<string> componentIds)
    {
        IQueryable<SharedComponentVersion> versions = coreUnitOfWork
            .Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking();

        return versions.Where(v => componentIds.Contains(v.SharedComponentId)
            && !versions.Any(o => o.SharedComponentId == v.SharedComponentId && o.SequenceNumber > v.SequenceNumber));
    }

    internal static async Task<Dictionary<string, HeadTokens>> GetHeadsAsync(
        ICoreUnitOfWork coreUnitOfWork,
        IReadOnlyCollection<string> componentIds,
        CancellationToken cancellationToken)
    {
        var heads = await HeadVersions(coreUnitOfWork, componentIds)
            .Select(v => new { v.SharedComponentId, v.Id, v.ContentHash, v.PayloadAbsentAt, v.CreatedAt })
            .ToListAsync(cancellationToken);

        return heads.ToDictionary(
            h => h.SharedComponentId,
            h => new HeadTokens(h.Id, h.ContentHash, h.PayloadAbsentAt != null, h.CreatedAt),
            StringComparer.Ordinal);
    }

    internal static async Task<Dictionary<string, SharedComponentVersion>> GetHeadVersionRowsAsync(
        ICoreUnitOfWork coreUnitOfWork,
        IReadOnlyCollection<string> componentIds,
        CancellationToken cancellationToken)
    {
        List<SharedComponentVersion> heads =
            await HeadVersions(coreUnitOfWork, componentIds).ToListAsync(cancellationToken);

        return heads.ToDictionary(head => head.SharedComponentId, StringComparer.Ordinal);
    }

    internal static async Task<Dictionary<string, SharedComponent>> GetComponentsByIdAsync(
        ICoreUnitOfWork coreUnitOfWork,
        IReadOnlyCollection<string> componentIds,
        CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0)
        {
            return new Dictionary<string, SharedComponent>(StringComparer.Ordinal);
        }

        return (await coreUnitOfWork
            .Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(component => componentIds.Contains(component.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(component => component.Id, StringComparer.Ordinal);
    }

    internal static async Task<Dictionary<string, List<string>>> GetTrailsByComponentAsync(
        ICoreUnitOfWork coreUnitOfWork,
        IReadOnlyCollection<string> componentIds,
        CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0)
        {
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        return (await coreUnitOfWork
            .Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(version => componentIds.Contains(version.SharedComponentId))
            .Select(version => new { version.SharedComponentId, version.SequenceNumber, version.ContentHash })
            .ToListAsync(cancellationToken))
            .GroupBy(row => row.SharedComponentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.SequenceNumber).Select(row => row.ContentHash).ToList(),
                StringComparer.Ordinal);
    }

    internal static async Task<Dictionary<string, Section>> GetHeadSectionDefinitionsAsync(
        ICoreUnitOfWork coreUnitOfWork,
        IReadOnlyCollection<string> componentIds,
        CancellationToken cancellationToken)
    {
        List<SharedComponentVersion> heads = await HeadVersions(coreUnitOfWork, componentIds)
            .Where(v => v.PayloadAbsentAt == null)
            .ToListAsync(cancellationToken);

        var definitions = new Dictionary<string, Section>(StringComparer.Ordinal);
        foreach (SharedComponentVersion head in heads)
        {
            if (Section.FromJson(head.ContentJson) is { } definition)
            {
                definitions[head.SharedComponentId] = definition;
            }
        }

        return definitions;
    }

    internal sealed record ConsumerFacets(
        Dictionary<string, int> Counts,
        Dictionary<string, List<ConsumingWorkflowTypeDto>> ConsumingTypes,
        Dictionary<string, int> ViaSectionCounts,
        Dictionary<string, int> BrokenOutCounts);

    internal static async Task<ConsumerFacets> GetConsumerFacetsAsync(
        ICoreUnitOfWork coreUnitOfWork,
        IReadOnlyCollection<string> componentIds,
        CancellationToken cancellationToken)
    {
        SharedComponentConsumerScan.ComponentScan scan =
            await SharedComponentConsumerScan.ScanAsync(coreUnitOfWork, componentIds, cancellationToken);
        List<SharedComponentConsumerScan.ComponentConsumerRow> rows = scan.Consumers;
        Dictionary<string, int> counts = rows
            .GroupBy(r => r.ComponentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Consumer.LinkedInstanceCount), StringComparer.Ordinal);
        Dictionary<string, int> viaSectionCounts = rows
            .Where(r => r.Consumer.Channel == SharedComponentConsumerChannel.ViaSection)
            .GroupBy(r => r.ComponentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Consumer.LinkedInstanceCount), StringComparer.Ordinal);

        Dictionary<string, List<ConsumingWorkflowTypeDto>> consumingTypes = rows
            .Where(r => !string.IsNullOrEmpty(r.Consumer.WorkflowTypeId))
            .GroupBy(r => r.ComponentId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(r => new ConsumingWorkflowTypeDto(
                        r.Consumer.WorkflowTypeId,
                        r.Consumer.WorkflowTypeName,
                        r.Consumer.LinkedInstanceCount,
                        r.Consumer.CategoryId,
                        r.Consumer.CategoryName,
                        (ContractsConsumerChannel)(int)r.Consumer.Channel,
                        r.Consumer.ViaSharedComponentId,
                        r.Consumer.ViaSharedComponentName))
                    .OrderBy(t => t.WorkflowTypeName, StringComparer.Ordinal)
                    .ThenBy(t => t.WorkflowTypeId, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        return new ConsumerFacets(counts, consumingTypes, viaSectionCounts, scan.BrokenOutCounts);
    }

    internal static async Task<SharedComponentSummaryDto> GetSummaryDtoAsync(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponent component,
        CancellationToken cancellationToken)
    {
        string[] componentIds = [component.Id];
        Dictionary<string, HeadTokens> heads =
            await GetHeadsAsync(coreUnitOfWork, componentIds, cancellationToken);
        ConsumerFacets facets =
            await GetConsumerFacetsAsync(coreUnitOfWork, componentIds, cancellationToken);
        Dictionary<string, string> categoryNames =
            await GetCategoryNamesAsync(coreUnitOfWork, cancellationToken);

        return ToSummaryDto(
            component,
            heads.GetValueOrDefault(component.Id),
            facets.Counts.GetValueOrDefault(component.Id),
            categoryNames,
            facets.ConsumingTypes.GetValueOrDefault(component.Id) ?? [],
            facets.ViaSectionCounts.GetValueOrDefault(component.Id),
            facets.BrokenOutCounts.GetValueOrDefault(component.Id));
    }

    internal static async Task<Dictionary<string, string>> GetCategoryNamesAsync(
        ICoreUnitOfWork coreUnitOfWork,
        CancellationToken cancellationToken)
    {
        List<SharedComponentCategory> categories = [.. await coreUnitOfWork
            .Repository<SharedComponentCategory>()
            .FindAsync(new SharedComponentCategoriesSpec(), cancellationToken)];

        return categories.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
    }

    internal static SharedComponentSummaryDto ToSummaryDto(
        SharedComponent component,
        HeadTokens? head,
        int consumerCount,
        IReadOnlyDictionary<string, string> categoryNames,
        List<ConsumingWorkflowTypeDto> consumingWorkflowTypes,
        int viaSectionConsumerCount = 0,
        int brokenOutInstanceCount = 0)
    {
        return new SharedComponentSummaryDto(
            component.Id,
            (ContractsKind)(int)component.Kind,
            component.Name,
            component.Description,
            component.CategoryId,
            component.CategoryId is null ? null : categoryNames.GetValueOrDefault(component.CategoryId),
            (ContractsState)(int)component.State,
            head?.VersionId,
            head?.ContentHash,
            head?.CreatedAtUtc,
            consumerCount,
            consumingWorkflowTypes,
            viaSectionConsumerCount,
            brokenOutInstanceCount);
    }
}
