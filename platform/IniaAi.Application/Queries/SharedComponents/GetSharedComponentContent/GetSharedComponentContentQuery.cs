using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Specifications.SharedComponents;

namespace IniaAi.Application.Queries.SharedComponents.GetSharedComponentContent;

public record GetSharedComponentContentQuery(
    string OrganizationId,
    string UserId,
    string SharedComponentId) : IRequest<ErrorOr<SharedComponentContentResult>>;

/// <summary>
/// The head version's definition JSON served verbatim, plus the tokens a later mutation must echo.
/// </summary>
public record SharedComponentContentResult(
    string Id,
    string HeadVersionId,
    string HeadContentHash,
    string ContentJson);

public class GetSharedComponentContentQueryHandler
    : IRequestHandler<GetSharedComponentContentQuery, ErrorOr<SharedComponentContentResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public GetSharedComponentContentQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<SharedComponentContentResult>> Handle(
        GetSharedComponentContentQuery request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(request.SharedComponentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        SharedComponentVersion? head = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(component.Id), cancellationToken);
        if (head is null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.NO_VERSIONS,
                $"Shared component '{component.Id}' has no versions at all; its chain is corrupt.");
        }

        if (head.PayloadAbsentAt is not null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"Shared component '{component.Id}' has no stored head content to serve.");
        }

        return new SharedComponentContentResult(component.Id, head.Id, head.ContentHash, head.ContentJson);
    }
}

public class GetSharedComponentContentQueryValidator : AbstractValidator<GetSharedComponentContentQuery>
{
    public GetSharedComponentContentQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
    }
}
