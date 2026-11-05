using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Specifications.SharedComponents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.SharedComponents.RestoreSharedComponentVersion;

/// <summary>
/// Makes a stored version the component's new head by re-appending its server-stored payload
/// through the same append-and-fan-out choke point as any other definition change — callers pass
/// tokens only, never the payload. Restoring content identical to the current head dedups to a
/// no-op; repeating an older version's hash mid-chain is legal by design. The source version must
/// still hold its payload (<see cref="SharedComponentErrorCodes.PAYLOAD_ABSENT"/>, write
/// precondition channel).
/// </summary>
public record RestoreSharedComponentVersionCommand(
    string OrganizationId,
    string UserId,
    string SharedComponentId,
    string VersionId,
    string ExpectedHeadVersionId,
    bool AcknowledgeSignatureOrphans = false) : IRequest<ErrorOr<SharedComponentAppendResult>>;

public class RestoreSharedComponentVersionCommandHandler
    : IRequestHandler<RestoreSharedComponentVersionCommand, ErrorOr<SharedComponentAppendResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly SharedComponentFanOutService _fanOutService;

    public RestoreSharedComponentVersionCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponentFanOutService fanOutService)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
    }

    public async Task<ErrorOr<SharedComponentAppendResult>> Handle(
        RestoreSharedComponentVersionCommand request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(request.SharedComponentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        if (component.State != SharedComponentState.Active)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.RETIRED,
                $"Component '{component.Id}' is retired; its definition is frozen until unretired.");
        }

        SharedComponentVersion? source = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(
                new SharedComponentVersionByIdSpec(component.Id, request.VersionId), cancellationToken);
        if (source is null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.UNKNOWN_VERSION,
                $"Version '{request.VersionId}' does not exist on shared component '{component.Id}'.");
        }

        if (source.PayloadAbsentAt is not null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"Version '{source.Id}' of shared component '{component.Id}' is a hash-only lineage row"
                + " with no stored content to restore.");
        }

        return await _fanOutService.AppendAndFanOutAsync(
            request.OrganizationId,
            request.UserId,
            request.SharedComponentId,
            source.ContentJson,
            request.ExpectedHeadVersionId,
            request.AcknowledgeSignatureOrphans,
            cancellationToken);
    }
}

public class RestoreSharedComponentVersionCommandValidator
    : AbstractValidator<RestoreSharedComponentVersionCommand>
{
    public RestoreSharedComponentVersionCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
        _ = RuleFor(x => x.VersionId).NotEmpty();
        _ = RuleFor(x => x.ExpectedHeadVersionId).NotEmpty();
    }
}
