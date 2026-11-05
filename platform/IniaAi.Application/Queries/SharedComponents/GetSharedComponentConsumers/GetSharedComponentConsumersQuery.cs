using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Specifications.SharedComponents;

namespace IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumers;

/// <summary>
/// The where-used projection behind the Library detail surface and
/// <c>GET /shared-components/{id}/consumers</c>: the Core templates holding live Linked copies of
/// the component. The registry is global per deployment, so OrganizationId/UserId ride along per
/// house convention without filtering.
/// </summary>
public record GetSharedComponentConsumersQuery(
    string OrganizationId,
    string UserId,
    string SharedComponentId) : IRequest<ErrorOr<List<SharedComponentConsumer>>>;

public class GetSharedComponentConsumersQueryHandler
    : IRequestHandler<GetSharedComponentConsumersQuery, ErrorOr<List<SharedComponentConsumer>>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly SharedComponentFanOutService _fanOutService;

    public GetSharedComponentConsumersQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponentFanOutService fanOutService)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
    }

    public async Task<ErrorOr<List<SharedComponentConsumer>>> Handle(
        GetSharedComponentConsumersQuery request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(request.SharedComponentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        return await _fanOutService.GetConsumersAsync(component.Id, cancellationToken);
    }
}

public class GetSharedComponentConsumersQueryValidator : AbstractValidator<GetSharedComponentConsumersQuery>
{
    public GetSharedComponentConsumersQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
    }
}
