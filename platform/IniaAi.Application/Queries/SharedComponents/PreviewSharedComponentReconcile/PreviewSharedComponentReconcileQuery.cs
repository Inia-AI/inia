using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Specifications.SharedComponents;

namespace IniaAi.Application.Queries.SharedComponents.PreviewSharedComponentReconcile;

/// <summary>
/// Component-scoped impact preview — the read half behind "what happens if this definition
/// becomes the head". Always reports the blast radius (live consumers and the baseline
/// registrations whose freshness a fan-out would stale). With a candidate definition it also
/// reports the slot delta against the current head and the override rows the update would
/// delete (block dropped with a removed slot) or leave inert (signature-bearing matched slot
/// losing a placeholder). The candidate goes through the exact append normalization, so this
/// preview predicts what <see cref="SharedComponentFanOutService.AppendAndFanOutAsync"/> would
/// accept and store. Served over POST as a computation; nothing is written, and the apply path
/// re-computes everything inside its transaction — previews go stale by design.
/// </summary>
public record PreviewSharedComponentReconcileQuery(
    string OrganizationId,
    string UserId,
    string SharedComponentId,
    string? CandidateDefinitionJson) : IRequest<ErrorOr<SharedComponentReconcilePreviewResult>>;

public record SharedComponentReconcilePreviewResult(
    string SharedComponentId,
    string HeadVersionId,
    string HeadContentHash,
    string? CandidateContentHash,
    IReadOnlyList<SharedComponentConsumer> Consumers,
    SharedComponentUpdateImpact UpdateSharedImpact);

public class PreviewSharedComponentReconcileQueryHandler
    : IRequestHandler<PreviewSharedComponentReconcileQuery, ErrorOr<SharedComponentReconcilePreviewResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly SharedComponentFanOutService _fanOutService;

    public PreviewSharedComponentReconcileQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponentFanOutService fanOutService)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
    }

    public async Task<ErrorOr<SharedComponentReconcilePreviewResult>> Handle(
        PreviewSharedComponentReconcileQuery request,
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

        List<SharedComponentConsumer> consumers = await _fanOutService.GetConsumersAsync(
            component.Id, cancellationToken);
        if (request.CandidateDefinitionJson is null)
        {
            SharedComponentUpdateImpact blastRadius = await _fanOutService.GetUpdateImpactAsync(
                component, oldDefinition: null, candidate: null, consumers, cancellationToken);

            return new SharedComponentReconcilePreviewResult(
                component.Id, head.Id, head.ContentHash, null, consumers, blastRadius);
        }

        if (head.PayloadAbsentAt is not null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"Shared component '{component.Id}' has no stored head content to diff a candidate against.");
        }

        ErrorOr<SharedComponentFanOutService.NormalizedDefinition> candidate =
            _fanOutService.NormalizeDefinition(component, request.CandidateDefinitionJson);
        if (candidate.IsError)
        {
            return candidate.Errors;
        }

        ErrorOr<SharedComponentFanOutService.NormalizedDefinition> oldDefinition =
            _fanOutService.NormalizeDefinition(component, head.ContentJson);
        if (oldDefinition.IsError)
        {
            return Error.Unexpected(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"The stored definition of component '{component.Id}' could not be read.");
        }

        SharedComponentUpdateImpact impact = await _fanOutService.GetUpdateImpactAsync(
            component, oldDefinition.Value, candidate.Value, consumers, cancellationToken);

        return new SharedComponentReconcilePreviewResult(
            component.Id,
            head.Id,
            head.ContentHash,
            candidate.Value.ContentHash,
            consumers,
            impact);
    }
}

public class PreviewSharedComponentReconcileQueryValidator
    : AbstractValidator<PreviewSharedComponentReconcileQuery>
{
    public PreviewSharedComponentReconcileQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
        _ = RuleFor(x => x.CandidateDefinitionJson)
            .NotEmpty()
            .When(x => x.CandidateDefinitionJson is not null);
    }
}
