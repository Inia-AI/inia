using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Specifications.SharedComponents;

namespace IniaAi.Application.Queries.SharedComponents.GetSharedComponentVersionContent;

/// <summary>
/// One stored version's definition JSON served verbatim — the head-content contract extended to
/// any point in the chain. Hash-only lineage rows have no content sub-resource
/// (<see cref="SharedComponentErrorCodes.PAYLOAD_ABSENT"/>, read channel).
/// </summary>
public record GetSharedComponentVersionContentQuery(
    string OrganizationId,
    string UserId,
    string SharedComponentId,
    string VersionId) : IRequest<ErrorOr<SharedComponentVersionContentResult>>;

public record SharedComponentVersionContentResult(
    string SharedComponentId,
    string VersionId,
    string ContentHash,
    string ContentJson);

public class GetSharedComponentVersionContentQueryHandler
    : IRequestHandler<GetSharedComponentVersionContentQuery, ErrorOr<SharedComponentVersionContentResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public GetSharedComponentVersionContentQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<SharedComponentVersionContentResult>> Handle(
        GetSharedComponentVersionContentQuery request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(request.SharedComponentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        SharedComponentVersion? version = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(
                new SharedComponentVersionByIdSpec(component.Id, request.VersionId), cancellationToken);
        if (version is null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.UNKNOWN_VERSION,
                $"Version '{request.VersionId}' does not exist on shared component '{component.Id}'.");
        }

        if (version.PayloadAbsentAt is not null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"Version '{version.Id}' of shared component '{component.Id}' is a hash-only lineage row"
                + " with no stored content to serve.");
        }

        return new SharedComponentVersionContentResult(
            component.Id, version.Id, version.ContentHash, version.ContentJson);
    }
}

public class GetSharedComponentVersionContentQueryValidator
    : AbstractValidator<GetSharedComponentVersionContentQuery>
{
    public GetSharedComponentVersionContentQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
        _ = RuleFor(x => x.VersionId).NotEmpty();
    }
}
