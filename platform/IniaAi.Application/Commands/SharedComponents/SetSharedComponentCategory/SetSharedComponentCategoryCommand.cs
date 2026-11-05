using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.SharedComponents.SetSharedComponentCategory;

/// <summary>
/// Re-files one registry component under a different curated category. The target category must
/// exist — components are never moved back to uncategorized (that bucket only holds legacy rows
/// predating the catalogue). Returns the component's updated summary as the successor state.
/// </summary>
public record SetSharedComponentCategoryCommand(
    string OrganizationId,
    string UserId,
    string SharedComponentId,
    string CategoryId) : IRequest<ErrorOr<SharedComponentSummaryDto>>;

public class SetSharedComponentCategoryCommandHandler
    : IRequestHandler<SetSharedComponentCategoryCommand, ErrorOr<SharedComponentSummaryDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public SetSharedComponentCategoryCommandHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<SharedComponentSummaryDto>> Handle(
        SetSharedComponentCategoryCommand request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .FirstOrDefaultAsync(c => c.Id == request.SharedComponentId, cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        ErrorOr<Success> categoryGuard = await SharedComponentCategoryGuard.EnsureExistsAsync(
            _coreUnitOfWork, request.CategoryId, cancellationToken);
        if (categoryGuard.IsError)
        {
            return categoryGuard.Errors;
        }

        component.CategoryId = request.CategoryId;
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        return await SharedComponentProjections.GetSummaryDtoAsync(_coreUnitOfWork, component, cancellationToken);
    }
}

public class SetSharedComponentCategoryCommandValidator : AbstractValidator<SetSharedComponentCategoryCommand>
{
    public SetSharedComponentCategoryCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
        _ = RuleFor(x => x.CategoryId).NotEmpty();
    }
}
