using System.Diagnostics;
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
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.SharedComponents.ScanSharedComponentBootstrap;

/// <summary>
/// Sweeps every workflow template for blocks and sections that repeat verbatim without being in
/// the Library yet — the bootstrap path for a deployment whose prompt engineers hand-copied
/// content between templates before shared components existed. Matching is exact normalized-hash
/// only; candidates are nodes that are not actively Linked (broken-out copies count, disclosed via
/// their provenance component), and blocks inside an actively Linked section are the section
/// component's business, never independent candidates. The scan loads every template graph, so it
/// stays on demand at both mouths — the Library page's Scan button and
/// <c>GET /api/v1/shared-components/suggestions</c> — and reports what it cost.
/// </summary>
public record ScanSharedComponentBootstrapQuery(string OrganizationId, string UserId)
    : IRequest<ErrorOr<SharedComponentBootstrapScanResult>>;

public sealed record SharedComponentBootstrapScanResult(
    IReadOnlyList<SharedComponentBootstrapSuggestion> Suggestions,
    int ScannedWorkflowTypeCount,
    long ElapsedMilliseconds);

public sealed record SharedComponentBootstrapSuggestion(
    SharedComponentKind Kind,
    string ContentHash,
    string SuggestedName,
    bool CrossType,
    string? ExistingComponentId,
    string? ExistingComponentName,
    IReadOnlyList<SharedComponentBootstrapNode> Nodes);

public sealed record SharedComponentBootstrapNode(
    string WorkflowTypeId,
    string WorkflowTypeName,
    string TargetPath,
    string? TargetStableId,
    string? ProvenanceComponentId,
    string? ProvenanceComponentName,
    string SectionTitle,
    int SectionOrder,
    string? BlockName,
    int? BlockOrder,
    string? WorkflowTypeCategoryName)
{
    public string DisplayName => BlockOrder is null ? SectionTitle : BlockName ?? SectionTitle;
}

public class ScanSharedComponentBootstrapQueryValidator : AbstractValidator<ScanSharedComponentBootstrapQuery>
{
    public ScanSharedComponentBootstrapQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
    }
}

public class ScanSharedComponentBootstrapQueryHandler
    : IRequestHandler<ScanSharedComponentBootstrapQuery, ErrorOr<SharedComponentBootstrapScanResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly SharedComponentContentNormalizer _normalizer;
    private readonly ILogger<ScanSharedComponentBootstrapQueryHandler> _logger;

    public ScanSharedComponentBootstrapQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponentContentNormalizer normalizer,
        ILogger<ScanSharedComponentBootstrapQueryHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<SharedComponentBootstrapScanResult>> Handle(
        ScanSharedComponentBootstrapQuery request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        List<WorkflowTypeRow> types = [.. (await _coreUnitOfWork.Repository<WorkflowType>()
            .GetAll()
            .AsNoTracking()
            .Where(WorkflowType.UserFacing)
            .Select(type => new { type.Id, type.Name, CategoryName = type.Category!.Name })
            .ToListAsync(cancellationToken))
            .Select(type => new WorkflowTypeRow(type.Id, type.Name, type.CategoryName))
            .OrderBy(type => type.Id, StringComparer.Ordinal)];

        Dictionary<(SharedComponentKind Kind, string Hash), List<SharedComponentBootstrapNode>> groups = [];
        int scannedTypes = 0;
        foreach (WorkflowTypeRow type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Workflow? graph = await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(type.Id);
            if (graph is null)
            {
                continue;
            }

            scannedTypes++;
            CollectCandidates(graph, type, groups);
        }

        Dictionary<(SharedComponentKind Kind, string Hash), SharedComponent> attachTargetsByKey =
            await LoadActiveHeadMatchesAsync([.. groups.Keys], cancellationToken);
        List<(SharedComponentKind Kind, string Hash)> suggestionKeys = [.. groups
            .Where(group => group.Value.Count >= 2 || attachTargetsByKey.ContainsKey(group.Key))
            .Select(group => group.Key)];
        Dictionary<string, SharedComponent> provenanceComponents = await SharedComponentProjections.GetComponentsByIdAsync(
            _coreUnitOfWork, CollectProvenanceIds(groups, suggestionKeys), cancellationToken);

        List<SharedComponentBootstrapSuggestion> suggestions = [];
        foreach ((SharedComponentKind kind, string hash) in suggestionKeys)
        {
            List<SharedComponentBootstrapNode> nodes = [.. groups[(kind, hash)]
                .Select(node => node.ProvenanceComponentId is { } provenanceId
                    && provenanceComponents.TryGetValue(provenanceId, out SharedComponent? provenance)
                        ? node with { ProvenanceComponentName = provenance.Name }
                        : node)
                .OrderBy(node => node.WorkflowTypeId, StringComparer.Ordinal)
                .ThenBy(node => node.TargetPath, StringComparer.Ordinal)];
            SharedComponent? attachTarget = attachTargetsByKey.GetValueOrDefault((kind, hash));

            suggestions.Add(new SharedComponentBootstrapSuggestion(
                kind,
                hash,
                nodes.Select(node => node.DisplayName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? $"Shared {kind}",
                nodes.Select(node => node.WorkflowTypeId).Distinct(StringComparer.Ordinal).Count() > 1,
                attachTarget?.Id,
                attachTarget?.Name,
                nodes));
        }

        suggestions = [.. suggestions
            .OrderByDescending(suggestion => suggestion.Nodes.Count)
            .ThenBy(suggestion => suggestion.Kind)
            .ThenBy(suggestion => suggestion.SuggestedName, StringComparer.Ordinal)
            .ThenBy(suggestion => suggestion.ContentHash, StringComparer.Ordinal)];
        stopwatch.Stop();
        _logger.LogInformation(
            "Bootstrap scan covered {ScannedTypes} workflow types in {ElapsedMs} ms and found {SuggestionCount} repeat groups",
            scannedTypes,
            stopwatch.ElapsedMilliseconds,
            suggestions.Count);

        return new SharedComponentBootstrapScanResult(suggestions, scannedTypes, stopwatch.ElapsedMilliseconds);
    }

    private void CollectCandidates(
        Workflow graph,
        WorkflowTypeRow type,
        Dictionary<(SharedComponentKind Kind, string Hash), List<SharedComponentBootstrapNode>> groups)
    {
        List<Section> orderedSections = [.. graph.Sections.OrderBy(section => section.Order)];
        for (int sectionIndex = 0; sectionIndex < orderedSections.Count; sectionIndex++)
        {
            Section section = orderedSections[sectionIndex];
            if (section.IsSeparator || section.Blocks.Count == 0 || section.IsActivelyLinked())
            {
                continue;
            }

            string sectionPath = $"Sections[{sectionIndex}]";
            string sectionTitle = string.IsNullOrWhiteSpace(section.Title) ? section.Name : section.Title;

            AddCandidate(
                groups,
                SharedComponentKind.Section,
                _normalizer.ComputeContentHash(section),
                new SharedComponentBootstrapNode(
                    type.Id,
                    type.Name,
                    sectionPath,
                    section.StableSectionId,
                    ProvenanceId(section),
                    ProvenanceComponentName: null,
                    sectionTitle,
                    section.Order,
                    BlockName: null,
                    BlockOrder: null,
                    type.CategoryName));

            List<Block> orderedBlocks = [.. section.Blocks.OrderBy(block => block.Order)];
            for (int blockIndex = 0; blockIndex < orderedBlocks.Count; blockIndex++)
            {
                Block block = orderedBlocks[blockIndex];
                if (block.IsActivelyLinked() || block.IsLoopBoundary)
                {
                    continue;
                }

                string blockName = string.IsNullOrWhiteSpace(block.Name) ? block.ReplacementTag : block.Name;

                AddCandidate(
                    groups,
                    SharedComponentKind.Block,
                    _normalizer.ComputeContentHash(block),
                    new SharedComponentBootstrapNode(
                        type.Id,
                        type.Name,
                        $"{sectionPath}.Blocks[{blockIndex}]",
                        block.StableBlockId,
                        ProvenanceId(block),
                        ProvenanceComponentName: null,
                        sectionTitle,
                        section.Order,
                        blockName,
                        block.Order,
                        type.CategoryName));
            }
        }
    }

    private static void AddCandidate(
        Dictionary<(SharedComponentKind Kind, string Hash), List<SharedComponentBootstrapNode>> groups,
        SharedComponentKind kind,
        string contentHash,
        SharedComponentBootstrapNode node)
    {
        if (!groups.TryGetValue((kind, contentHash), out List<SharedComponentBootstrapNode>? nodes))
        {
            nodes = [];
            groups[(kind, contentHash)] = nodes;
        }

        nodes.Add(node);
    }

    private static string? ProvenanceId(ISharedComponentLinkable node)
    {
        return node.SharedComponentLinkState == SharedComponentLinkState.BrokenOut
            ? node.SharedComponentId
            : null;
    }

    private static List<string> CollectProvenanceIds(
        Dictionary<(SharedComponentKind Kind, string Hash), List<SharedComponentBootstrapNode>> groups,
        List<(SharedComponentKind Kind, string Hash)> suggestionKeys)
    {
        return [.. suggestionKeys
            .SelectMany(key => groups[key])
            .Select(node => node.ProvenanceComponentId)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)];
    }

    private async Task<Dictionary<(SharedComponentKind Kind, string Hash), SharedComponent>> LoadActiveHeadMatchesAsync(
        List<(SharedComponentKind Kind, string Hash)> candidateKeys,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<(SharedComponentKind Kind, string Hash), SharedComponent>();
        if (candidateKeys.Count == 0)
        {
            return results;
        }

        List<SharedComponent> activeComponents = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(component => component.State == SharedComponentState.Active)
            .ToListAsync(cancellationToken);
        if (activeComponents.Count == 0)
        {
            return results;
        }

        List<string> activeIds = [.. activeComponents.Select(component => component.Id)];
        Dictionary<string, SharedComponentProjections.HeadTokens> heads =
            await SharedComponentProjections.GetHeadsAsync(_coreUnitOfWork, activeIds, cancellationToken);
        StoredVersionHashes storedHashes = await StoredVersionHashes.LoadContentHashesAsync(
            _coreUnitOfWork,
            _normalizer,
            heads.Select(pair => (pair.Key, pair.Value.ContentHash)),
            cancellationToken);

        var wantedKeys = new HashSet<(SharedComponentKind Kind, string Hash)>(candidateKeys);
        foreach (SharedComponent component in activeComponents.OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            if (!heads.TryGetValue(component.Id, out SharedComponentProjections.HeadTokens? head))
            {
                continue;
            }

            (SharedComponentKind Kind, string Hash) key = (component.Kind, storedHashes.Of(component.Id, head.ContentHash));
            if (wantedKeys.Contains(key))
            {
                _ = results.TryAdd(key, component);
            }
        }

        return results;
    }

    private sealed record WorkflowTypeRow(string Id, string Name, string? CategoryName);
}
