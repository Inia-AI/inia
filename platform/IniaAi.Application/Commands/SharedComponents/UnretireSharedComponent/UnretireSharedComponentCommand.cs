using ErrorOr;
using FluentValidation;
using IniaAi.Application.Commands.SharedComponents.RetireSharedComponent;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.SharedComponents.UnretireSharedComponent;

/// <summary>
/// Reopens a retired component for new links and definition changes — a state flip only. The
/// break-outs its retirement performed are history facts: nobody is re-linked, so the component
/// comes back with zero consumers until copies are materialized or relinked again. Unretiring an
/// active component is an idempotent no-op.
/// </summary>
public record UnretireSharedComponentCommand(
    string OrganizationId,
    string UserId,
    string SharedComponentId) : IRequest<ErrorOr<SharedComponentRetireResult>>;

public class UnretireSharedComponentCommandHandler
    : IRequestHandler<UnretireSharedComponentCommand, ErrorOr<SharedComponentRetireResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public UnretireSharedComponentCommandHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<SharedComponentRetireResult>> Handle(
        UnretireSharedComponentCommand request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .FirstOrDefaultAsync(c => c.Id == request.SharedComponentId, cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        if (component.State != SharedComponentState.Active)
        {
            component.State = SharedComponentState.Active;
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
        }

        return new SharedComponentRetireResult(component.Id, component.State, 0, [], []);
    }
}

public class UnretireSharedComponentCommandValidator : AbstractValidator<UnretireSharedComponentCommand>
{
    public UnretireSharedComponentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
    }
}
