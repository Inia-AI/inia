using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.SharedComponents.Enums;

namespace IniaAi.Application.Commands.SharedComponents.RetireSharedComponent;

/// <summary>
/// Freezes a component and breaks out every Linked copy in the same transaction — a retired
/// component has zero Linked consumers by construction, never a third half-frozen state. Each
/// touched template gets a link-only save (content hash unchanged, provenance kept on the
/// broken-out copies). Retiring a block component additionally rewrites every section-component
/// definition that references it — the head content is baked in as an inline slot and every
/// Linked instance of those containers is restamped to the rewritten head in the same
/// transaction — so a retired component is referenced by nothing. Retiring an already-retired
/// component is an idempotent no-op.
/// </summary>
public record RetireSharedComponentCommand(
    string OrganizationId,
    string UserId,
    string SharedComponentId) : IRequest<ErrorOr<SharedComponentRetireResult>>;

/// <param name="BrokenOutInstanceCount">How many Linked copies this call flipped to BrokenOut.</param>
/// <param name="InlinedContainerIds">Section components whose definitions referenced the retired block and had its head content baked in as an inline slot.</param>
public sealed record SharedComponentRetireResult(
    string SharedComponentId,
    SharedComponentState State,
    int BrokenOutInstanceCount,
    IReadOnlyList<SharedComponentConsumerSaveResult> WorkflowSaves,
    IReadOnlyList<string> InlinedContainerIds);

public class RetireSharedComponentCommandHandler
    : IRequestHandler<RetireSharedComponentCommand, ErrorOr<SharedComponentRetireResult>>
{
    private readonly SharedComponentFanOutService _fanOutService;

    public RetireSharedComponentCommandHandler(SharedComponentFanOutService fanOutService)
    {
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
    }

    public async Task<ErrorOr<SharedComponentRetireResult>> Handle(
        RetireSharedComponentCommand request,
        CancellationToken cancellationToken)
    {
        return await _fanOutService.RetireAndBreakOutAsync(
            request.OrganizationId, request.UserId, request.SharedComponentId, cancellationToken);
    }
}

public class RetireSharedComponentCommandValidator : AbstractValidator<RetireSharedComponentCommand>
{
    public RetireSharedComponentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
    }
}
