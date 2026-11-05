using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Specifications.SharedComponents;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace IniaAi.Application.Queries.SharedComponents.GetSharedComponentVersionDiff;

/// <summary>
/// The structured diff between two stored versions of one shared component — the Library detail's
/// "changes vs previous / vs head" rendering, served in-process to the GUI. Both versions must
/// belong to the component and both payloads must still be stored: hash-only lineage rows have no
/// content to diff (<see cref="SharedComponentErrorCodes.PAYLOAD_ABSENT"/>, read channel). The
/// registry is global per deployment, so OrganizationId/UserId ride along per house convention
/// without filtering.
/// </summary>
public record GetSharedComponentVersionDiffQuery(
    string OrganizationId,
    string UserId,
    string SharedComponentId,
    string FromVersionId,
    string ToVersionId) : IRequest<ErrorOr<SharedComponentVersionDiffResult>>;

public record SharedComponentVersionDiffResult(
    string SharedComponentId,
    string FromVersionId,
    string ToVersionId,
    WorkflowDiff Diff);

public class GetSharedComponentVersionDiffQueryHandler
    : IRequestHandler<GetSharedComponentVersionDiffQuery, ErrorOr<SharedComponentVersionDiffResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly SharedComponentDefinitionDiffer _definitionDiffer;

    public GetSharedComponentVersionDiffQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        SharedComponentDefinitionDiffer definitionDiffer)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _definitionDiffer = definitionDiffer ?? throw new ArgumentNullException(nameof(definitionDiffer));
    }

    public async Task<ErrorOr<SharedComponentVersionDiffResult>> Handle(
        GetSharedComponentVersionDiffQuery request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(request.SharedComponentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        List<SharedComponentVersion> rows = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => v.SharedComponentId == component.Id
                && (v.Id == request.FromVersionId || v.Id == request.ToVersionId))
            .ToListAsync(cancellationToken);
        SharedComponentVersion? from = rows.Find(v => v.Id == request.FromVersionId);
        if (from is null)
        {
            return UnknownVersion(request.FromVersionId, component.Id);
        }

        SharedComponentVersion? to = rows.Find(v => v.Id == request.ToVersionId);
        if (to is null)
        {
            return UnknownVersion(request.ToVersionId, component.Id);
        }

        if (from.PayloadAbsentAt is not null)
        {
            return PayloadAbsent(from.Id, component.Id);
        }

        if (to.PayloadAbsentAt is not null)
        {
            return PayloadAbsent(to.Id, component.Id);
        }

        try
        {
            WorkflowDiff diff = _definitionDiffer.Compare(component.Kind, from.ContentJson, to.ContentJson);

            return new SharedComponentVersionDiffResult(component.Id, from.Id, to.Id, diff);
        }
        catch (JsonException)
        {
            return Error.Unexpected(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"A stored definition of component '{component.Id}' could not be read.");
        }
    }

    private static Error UnknownVersion(string versionId, string componentId)
    {
        return Error.NotFound(
            SharedComponentErrorCodes.UNKNOWN_VERSION,
            $"Version '{versionId}' does not exist on shared component '{componentId}'.");
    }

    private static Error PayloadAbsent(string versionId, string componentId)
    {
        return Error.NotFound(
            SharedComponentErrorCodes.PAYLOAD_ABSENT,
            $"Version '{versionId}' of shared component '{componentId}' is a hash-only lineage row"
            + " with no stored content to diff.");
    }
}

public class GetSharedComponentVersionDiffQueryValidator
    : AbstractValidator<GetSharedComponentVersionDiffQuery>
{
    public GetSharedComponentVersionDiffQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
        _ = RuleFor(x => x.FromVersionId).NotEmpty();
        _ = RuleFor(x => x.ToVersionId).NotEmpty();
    }
}
