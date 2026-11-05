using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.EntityFrameworkCore;
using DomainLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Single set-based scan of live Core copies for one or more components, shared by the fan-out
/// engine and the registry read projections. One pass over blocks and sections carries both link
/// states: a copy counts as a consumer only while its own link state is Linked, while broken-out
/// copies are counted separately and never appear as consumers. A block linked to a component whose
/// parent section is itself Linked to a section component is attributed to that section (ViaSection
/// channel) — any direct copy in the same workflow type makes the whole type Direct.
/// </summary>
internal static class SharedComponentConsumerScan
{
    internal sealed record ComponentConsumerRow(string ComponentId, SharedComponentConsumer Consumer);

    internal sealed record ComponentScan(
        List<ComponentConsumerRow> Consumers,
        Dictionary<string, int> BrokenOutCounts);

    internal static async Task<List<SharedComponentConsumer>> GetConsumersForComponentAsync(
        ICoreUnitOfWork coreUnitOfWork,
        string componentId,
        CancellationToken cancellationToken)
    {
        List<ComponentConsumerRow> rows =
            (await ScanAsync(coreUnitOfWork, [componentId], cancellationToken)).Consumers;

        return [.. rows
            .Where(r => string.Equals(r.ComponentId, componentId, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(r.Consumer.WorkflowTypeId))
            .Select(r => r.Consumer)
            .OrderBy(c => c.WorkflowTypeName, StringComparer.Ordinal)
            .ThenBy(c => c.WorkflowTypeId, StringComparer.Ordinal)];
    }

    internal static async Task<ComponentScan> ScanAsync(
        ICoreUnitOfWork coreUnitOfWork,
        IReadOnlyCollection<string> componentIds,
        CancellationToken cancellationToken)
    {
        var blockPairs = await coreUnitOfWork.Repository<Block>()
            .GetAll()
            .AsNoTracking()
            .Where(b => b.SharedComponentId != null
                && componentIds.Contains(b.SharedComponentId)
                && (b.SharedComponentLinkState == DomainLinkState.Linked
                    || b.SharedComponentLinkState == DomainLinkState.BrokenOut))
            .Select(b => new
            {
                ComponentId = b.SharedComponentId!,
                LinkState = b.SharedComponentLinkState,
                WorkflowTypeId = b.Section == null ? null : b.Section.Workflow.WorkflowTypeId,
                WorkflowName = b.Section == null ? null : b.Section.Workflow.Name,
                ParentSectionComponentId = b.Section == null ? null : b.Section.SharedComponentId,
                ParentSectionLinkState = b.Section == null ? (DomainLinkState?)null : b.Section.SharedComponentLinkState,
            })
            .ToListAsync(cancellationToken);

        var sectionPairs = await coreUnitOfWork.Repository<Section>()
            .GetAll()
            .AsNoTracking()
            .Where(s => s.SharedComponentId != null
                && componentIds.Contains(s.SharedComponentId)
                && (s.SharedComponentLinkState == DomainLinkState.Linked
                    || s.SharedComponentLinkState == DomainLinkState.BrokenOut))
            .Select(s => new
            {
                ComponentId = s.SharedComponentId!,
                LinkState = s.SharedComponentLinkState,
                WorkflowTypeId = (string?)s.Workflow.WorkflowTypeId,
                WorkflowName = (string?)s.Workflow.Name,
            })
            .ToListAsync(cancellationToken);

        Dictionary<string, int> brokenOutCounts = blockPairs
            .Where(b => b.LinkState == DomainLinkState.BrokenOut)
            .Select(b => b.ComponentId)
            .Concat(sectionPairs
                .Where(s => s.LinkState == DomainLinkState.BrokenOut)
                .Select(s => s.ComponentId))
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        List<InstanceScan> instances =
        [
            .. blockPairs
                .Where(b => b.LinkState == DomainLinkState.Linked)
                .Select(b => new InstanceScan(
                    b.ComponentId,
                    b.WorkflowTypeId,
                    b.WorkflowName,
                    b.ParentSectionLinkState == DomainLinkState.Linked ? b.ParentSectionComponentId : null)),
            .. sectionPairs
                .Where(s => s.LinkState == DomainLinkState.Linked)
                .Select(s => new InstanceScan(s.ComponentId, s.WorkflowTypeId, s.WorkflowName, ViaComponentId: null)),
        ];

        List<string> typeIds = [.. instances
            .Where(i => i.WorkflowTypeId != null)
            .Select(i => i.WorkflowTypeId!)
            .Distinct(StringComparer.Ordinal)];
        Dictionary<string, WorkflowTypeFacet> typesById = (await coreUnitOfWork.Repository<WorkflowType>()
            .GetAll()
            .AsNoTracking()
            .Where(t => typeIds.Contains(t.Id))
            .Select(t => new WorkflowTypeFacet(
                t.Id, t.Name, t.CategoryId, t.Category != null ? t.Category.Name : null))
            .ToListAsync(cancellationToken))
            .ToDictionary(t => t.Id, StringComparer.Ordinal);

        List<string> viaIds = [.. instances
            .Where(i => i.ViaComponentId != null)
            .Select(i => i.ViaComponentId!)
            .Distinct(StringComparer.Ordinal)];
        Dictionary<string, string> viaNamesById = viaIds.Count == 0
            ? []
            : (await coreUnitOfWork.Repository<SharedComponent>()
                .GetAll()
                .AsNoTracking()
                .Where(c => viaIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(cancellationToken))
                .ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);

        List<ComponentConsumerRow> consumers = [.. instances
            .GroupBy(i => (i.ComponentId, i.WorkflowTypeId))
            .Select(g => BuildConsumerRow(g.Key.ComponentId, g.Key.WorkflowTypeId, [.. g], typesById, viaNamesById))];

        return new ComponentScan(consumers, brokenOutCounts);
    }

    private static ComponentConsumerRow BuildConsumerRow(
        string componentId,
        string? workflowTypeId,
        List<InstanceScan> instances,
        IReadOnlyDictionary<string, WorkflowTypeFacet> typesById,
        IReadOnlyDictionary<string, string> viaNamesById)
    {
        WorkflowTypeFacet? workflowType = workflowTypeId is null ? null : typesById.GetValueOrDefault(workflowTypeId);
        string fallbackName = instances.Select(i => i.WorkflowName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            ?? workflowTypeId
            ?? string.Empty;
        bool anyDirect = instances.Any(i => i.ViaComponentId is null);
        SharedComponentConsumerChannel channel = anyDirect
            ? SharedComponentConsumerChannel.Direct
            : SharedComponentConsumerChannel.ViaSection;
        string? viaId = null;
        string? viaName = null;
        if (channel == SharedComponentConsumerChannel.ViaSection)
        {
            viaId = instances
                .Where(i => i.ViaComponentId is not null)
                .GroupBy(i => i.ViaComponentId!, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .First()
                .Key;
            viaName = viaNamesById.GetValueOrDefault(viaId);
        }

        return new ComponentConsumerRow(
            componentId,
            new SharedComponentConsumer(
                workflowTypeId ?? string.Empty,
                string.IsNullOrWhiteSpace(workflowType?.Name) ? fallbackName : workflowType.Name,
                instances.Count,
                workflowType?.CategoryId,
                workflowType?.CategoryName,
                channel,
                viaId,
                viaName));
    }

    private sealed record InstanceScan(
        string ComponentId,
        string? WorkflowTypeId,
        string? WorkflowName,
        string? ViaComponentId);

    private sealed record WorkflowTypeFacet(string Id, string Name, string? CategoryId, string? CategoryName);
}
