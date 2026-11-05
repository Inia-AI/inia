using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Workflows;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Specifications.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.SharedComponents.GetSharedComponentVersions;

/// <summary>
/// <see cref="GetSharedComponentVersionsQuery.IncludeNarratives"/> computes each version's
/// plain-language change summary against its parent in the same request — component histories are
/// short, and one server-side pass beats a per-row diff round-trip from the client. Rows whose own
/// or parent payload is a hash-only lineage stub get a null narrative; the first version gets
/// <see cref="WorkflowNarrativeGenerator.FirstVersionSummary"/>. Without the flag the query stays
/// a payload-free projection.
/// </summary>
public record GetSharedComponentVersionsQuery(
    string OrganizationId,
    string UserId,
    string SharedComponentId,
    bool IncludeNarratives = false) : IRequest<ErrorOr<List<SharedComponentVersionDto>>>;

public class GetSharedComponentVersionsQueryHandler
    : IRequestHandler<GetSharedComponentVersionsQuery, ErrorOr<List<SharedComponentVersionDto>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDisplayNameResolver _nameResolver;
    private readonly SharedComponentDefinitionDiffer _definitionDiffer;

    public GetSharedComponentVersionsQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IUserDisplayNameResolver nameResolver,
        SharedComponentDefinitionDiffer definitionDiffer)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _nameResolver = nameResolver ?? throw new ArgumentNullException(nameof(nameResolver));
        _definitionDiffer = definitionDiffer ?? throw new ArgumentNullException(nameof(definitionDiffer));
    }

    public async Task<ErrorOr<List<SharedComponentVersionDto>>> Handle(
        GetSharedComponentVersionsQuery request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(request.SharedComponentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        return request.IncludeNarratives
            ? await ListWithNarrativesAsync(component, cancellationToken)
            : await ListAsync(component, cancellationToken);
    }

    private async Task<List<SharedComponentVersionDto>> ListAsync(
        SharedComponent component,
        CancellationToken cancellationToken)
    {
        var rows = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => v.SharedComponentId == component.Id)
            .OrderByDescending(v => v.SequenceNumber)
            .ThenByDescending(v => v.Id)
            .Select(v => new
            {
                v.Id,
                v.ContentHash,
                v.ParentVersionId,
                v.CreatedAt,
                v.CreatedByUserId,
                PayloadAbsent = v.PayloadAbsentAt.HasValue,
            })
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync(
            [.. rows.Select(r => r.CreatedByUserId)], cancellationToken);

        return rows.ConvertAll(r => new SharedComponentVersionDto(
            r.Id,
            r.ContentHash,
            r.ParentVersionId,
            r.CreatedAt,
            r.CreatedByUserId,
            names.GetValueOrDefault(r.CreatedByUserId, r.CreatedByUserId),
            r.PayloadAbsent));
    }

    private async Task<List<SharedComponentVersionDto>> ListWithNarrativesAsync(
        SharedComponent component,
        CancellationToken cancellationToken)
    {
        List<SharedComponentVersion> versions = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => v.SharedComponentId == component.Id)
            .OrderByDescending(v => v.SequenceNumber)
            .ThenByDescending(v => v.Id)
            .ToListAsync(cancellationToken);

        Dictionary<string, string?> payloadsById = versions.ToDictionary(
            v => v.Id,
            v => v.PayloadAbsentAt is null ? v.ContentJson : null,
            StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync(
            [.. versions.Select(v => v.CreatedByUserId)], cancellationToken);

        return versions.ConvertAll(v => new SharedComponentVersionDto(
            v.Id,
            v.ContentHash,
            v.ParentVersionId,
            v.CreatedAt,
            v.CreatedByUserId,
            names.GetValueOrDefault(v.CreatedByUserId, v.CreatedByUserId),
            v.PayloadAbsentAt.HasValue,
            ComputeNarrative(component.Kind, v, payloadsById)));
    }

    private string? ComputeNarrative(
        SharedComponentKind kind,
        SharedComponentVersion version,
        Dictionary<string, string?> payloadsById)
    {
        if (version.ParentVersionId is null)
        {
            return WorkflowNarrativeGenerator.FirstVersionSummary;
        }

        string? own = payloadsById.GetValueOrDefault(version.Id);
        string? parent = payloadsById.GetValueOrDefault(version.ParentVersionId);
        if (own is null || parent is null)
        {
            return null;
        }

        return _definitionDiffer.Describe(kind, parent, own);
    }
}

public class GetSharedComponentVersionsQueryValidator : AbstractValidator<GetSharedComponentVersionsQuery>
{
    public GetSharedComponentVersionsQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
    }
}
