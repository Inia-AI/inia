using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.Queries.SharedComponents.ListSharedComponents;

/// <summary>
/// Lists the shared-component registry with head tokens and consumer counts. The registry is one
/// global pool per deployment: the caller identity is validated but never used to scope the result;
/// access control is the endpoint's role gate. <paramref name="ContentHash"/> filters to components
/// whose HEAD content hash matches exactly — the bootstrap "identical content already shared" lookup.
/// <paramref name="Kind"/> narrows to one component kind; <paramref name="InsertableOnly"/> keeps
/// only components a template can materialize right now (Active with a stored, payload-present
/// head) — the single place that predicate is defined. <paramref name="CategoryId"/> narrows to
/// components filed under one curated category (exact id match; uncategorized rows never match).
/// <paramref name="WorkflowTypeId"/> keeps only components with at least one live linked copy in
/// that workflow type; <paramref name="WorkflowTypeCategoryId"/> keeps only components consumed by
/// at least one workflow type filed under that workflow-type category. Both are exact id matches
/// over the consuming-type facets every summary carries.
/// </summary>
public record ListSharedComponentsQuery(
    string OrganizationId,
    string UserId,
    string? ContentHash = null,
    ContractsKind? Kind = null,
    bool InsertableOnly = false,
    string? CategoryId = null,
    string? WorkflowTypeId = null,
    string? WorkflowTypeCategoryId = null) : IRequest<ErrorOr<List<SharedComponentSummaryDto>>>;

public class ListSharedComponentsQueryHandler
    : IRequestHandler<ListSharedComponentsQuery, ErrorOr<List<SharedComponentSummaryDto>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly SharedComponentContentNormalizer _normalizer;

    public ListSharedComponentsQueryHandler(ICoreUnitOfWork coreUnitOfWork, SharedComponentContentNormalizer normalizer)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public async Task<ErrorOr<List<SharedComponentSummaryDto>>> Handle(
        ListSharedComponentsQuery request,
        CancellationToken cancellationToken)
    {
        List<SharedComponent> components = [.. await _coreUnitOfWork.Repository<SharedComponent>()
            .FindAsync(new SharedComponentsListSpec(), cancellationToken)];
        if (request.Kind is not null)
        {
            components = [.. components.Where(c => (int)c.Kind == (int)request.Kind.Value)];
        }
        if (!string.IsNullOrEmpty(request.CategoryId))
        {
            components = [.. components.Where(c =>
                string.Equals(c.CategoryId, request.CategoryId, StringComparison.Ordinal))];
        }
        if (components.Count == 0)
        {
            return new List<SharedComponentSummaryDto>();
        }

        List<string> componentIds = [.. components.Select(c => c.Id)];
        Dictionary<string, SharedComponentProjections.HeadTokens> heads =
            await SharedComponentProjections.GetHeadsAsync(_coreUnitOfWork, componentIds, cancellationToken);

        StoredVersionHashes storedHashes = string.IsNullOrEmpty(request.ContentHash)
            ? StoredVersionHashes.None
            : await StoredVersionHashes.LoadContentHashesAsync(
                _coreUnitOfWork,
                _normalizer,
                heads.Select(pair => (pair.Key, pair.Value.ContentHash)),
                cancellationToken);
        List<SharedComponent> filtered = string.IsNullOrEmpty(request.ContentHash)
            ? components
            : [.. components.Where(c =>
                heads.TryGetValue(c.Id, out SharedComponentProjections.HeadTokens? head)
                && (string.Equals(head.ContentHash, request.ContentHash, StringComparison.Ordinal)
                    || string.Equals(storedHashes.Of(c.Id, head.ContentHash), request.ContentHash, StringComparison.Ordinal)))];
        if (request.InsertableOnly)
        {
            filtered = [.. filtered.Where(c =>
                c.State == SharedComponentState.Active
                && heads.TryGetValue(c.Id, out SharedComponentProjections.HeadTokens? head)
                && !head.PayloadAbsent)];
        }
        if (filtered.Count == 0)
        {
            return new List<SharedComponentSummaryDto>();
        }

        SharedComponentProjections.ConsumerFacets facets =
            await SharedComponentProjections.GetConsumerFacetsAsync(
                _coreUnitOfWork,
                [.. filtered.Select(c => c.Id)],
                cancellationToken);
        if (!string.IsNullOrEmpty(request.WorkflowTypeId))
        {
            filtered = [.. filtered.Where(c =>
                facets.ConsumingTypes.GetValueOrDefault(c.Id)?.Any(t =>
                    string.Equals(t.WorkflowTypeId, request.WorkflowTypeId, StringComparison.Ordinal)) == true)];
        }
        if (!string.IsNullOrEmpty(request.WorkflowTypeCategoryId))
        {
            filtered = [.. filtered.Where(c =>
                facets.ConsumingTypes.GetValueOrDefault(c.Id)?.Any(t =>
                    string.Equals(t.CategoryId, request.WorkflowTypeCategoryId, StringComparison.Ordinal)) == true)];
        }
        if (filtered.Count == 0)
        {
            return new List<SharedComponentSummaryDto>();
        }

        Dictionary<string, string> categoryNames =
            await SharedComponentProjections.GetCategoryNamesAsync(_coreUnitOfWork, cancellationToken);

        return filtered
            .Select(c => SharedComponentProjections.ToSummaryDto(
                c,
                heads.GetValueOrDefault(c.Id),
                facets.Counts.GetValueOrDefault(c.Id),
                categoryNames,
                facets.ConsumingTypes.GetValueOrDefault(c.Id) ?? [],
                facets.ViaSectionCounts.GetValueOrDefault(c.Id),
                facets.BrokenOutCounts.GetValueOrDefault(c.Id)))
            .ToList();
    }
}

public class ListSharedComponentsQueryValidator : AbstractValidator<ListSharedComponentsQuery>
{
    public ListSharedComponentsQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.ContentHash)
            .Matches(WorkflowCanonicalizer.CONTENT_HASH_PATTERN)
            .When(x => !string.IsNullOrEmpty(x.ContentHash));
    }
}
