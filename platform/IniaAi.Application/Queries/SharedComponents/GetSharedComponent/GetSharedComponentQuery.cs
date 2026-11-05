using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Specifications.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;

namespace IniaAi.Application.Queries.SharedComponents.GetSharedComponent;

public record GetSharedComponentQuery(
    string OrganizationId,
    string UserId,
    string SharedComponentId) : IRequest<ErrorOr<SharedComponentSummaryDto>>;

public class GetSharedComponentQueryHandler
    : IRequestHandler<GetSharedComponentQuery, ErrorOr<SharedComponentSummaryDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public GetSharedComponentQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<SharedComponentSummaryDto>> Handle(
        GetSharedComponentQuery request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(request.SharedComponentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        return await SharedComponentProjections.GetSummaryDtoAsync(_coreUnitOfWork, component, cancellationToken);
    }
}

public class GetSharedComponentQueryValidator : AbstractValidator<GetSharedComponentQuery>
{
    public GetSharedComponentQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
    }
}
