using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.SharedComponents;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace IniaAi.Application.Queries.SharedComponents.GetSharedComponentInstanceDiff;

/// <summary>
/// What a linked copy in a template holds that its Library component's head does not: the
/// "Show differences" view of a Library sync row. The diff runs from the head to the copy and
/// leaves out what the Library sync comparison leaves out (signature fields and presentation), so
/// a row flagged as different shows the fields that made it so.
/// </summary>
public record GetSharedComponentInstanceDiffQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string? TargetPath,
    string? TargetStableId) : IRequest<ErrorOr<WorkflowDiff>>;

public class GetSharedComponentInstanceDiffQueryHandler
    : IRequestHandler<GetSharedComponentInstanceDiffQuery, ErrorOr<WorkflowDiff>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ISharedComponentEditor _editor;
    private readonly SharedComponentReconciler _reconciler;
    private readonly SharedComponentDefinitionDiffer _definitionDiffer;

    public GetSharedComponentInstanceDiffQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ISharedComponentEditor editor,
        SharedComponentReconciler reconciler,
        SharedComponentDefinitionDiffer definitionDiffer)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        _definitionDiffer = definitionDiffer ?? throw new ArgumentNullException(nameof(definitionDiffer));
    }

    public async Task<ErrorOr<WorkflowDiff>> Handle(
        GetSharedComponentInstanceDiffQuery request,
        CancellationToken cancellationToken)
    {
        Workflow? workflow = await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(request.WorkflowTypeId);
        if (workflow is null)
        {
            return WorkflowVersionErrors.UnknownType(request.WorkflowTypeId);
        }

        ErrorOr<SharedComponentTarget> target =
            _editor.ResolveTarget(workflow, request.TargetPath, request.TargetStableId);
        if (target.IsError)
        {
            return target.Errors;
        }

        ISharedComponentLinkable copy = (ISharedComponentLinkable?)target.Value.Section ?? target.Value.Block!;
        if (!copy.IsActivelyLinked())
        {
            return Error.NotFound(
                SharedComponentErrorCodes.TARGET_NOT_FOUND,
                $"{target.Value.CanonicalPath} in '{request.WorkflowTypeId}' is not linked to the Library.");
        }

        string componentId = copy.SharedComponentId!;
        SharedComponentVersion? head = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(componentId), cancellationToken);
        if (head is null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.NO_VERSIONS,
                $"Shared component '{componentId}' has no versions at all; its chain is corrupt.");
        }

        if (head.PayloadAbsentAt is not null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"The head version of shared component '{componentId}' has no stored content to compare.");
        }

        try
        {
            string copyDefinitionJson = target.Value.Section is { } section
                ? _reconciler.InstanceDefinitionJson(
                    section, await LoadLinkedDefinitionAsync(componentId, section.SharedComponentVersionHash, cancellationToken))
                : _reconciler.InstanceDefinitionJson(target.Value.Block!);

            return _definitionDiffer.CompareContent(
                target.Value.Kind, _reconciler.LibraryDefinitionJson(head.ContentJson), copyDefinitionJson);
        }
        catch (JsonException)
        {
            return Error.Unexpected(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"A stored definition of component '{componentId}' could not be read.");
        }
    }

    private async Task<Section?> LoadLinkedDefinitionAsync(
        string componentId,
        string? linkVersionHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(linkVersionHash))
        {
            return null;
        }

        SharedComponentVersion? linked = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => v.SharedComponentId == componentId
                && v.ContentHash == linkVersionHash
                && v.PayloadAbsentAt == null
                && v.ContentJsonCompressed != null)
            .OrderByDescending(v => v.SequenceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        return linked is null ? null : Section.FromJson(linked.ContentJson);
    }
}

public class GetSharedComponentInstanceDiffQueryValidator
    : AbstractValidator<GetSharedComponentInstanceDiffQuery>
{
    public GetSharedComponentInstanceDiffQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.TargetPath)
            .Must((query, targetPath) =>
                string.IsNullOrEmpty(targetPath) != string.IsNullOrEmpty(query.TargetStableId))
            .WithMessage($"Provide exactly one of targetPath or targetStableId. [{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
        _ = RuleFor(x => x.TargetPath)
            .Matches(SharedComponentEditor.TARGET_PATH_PATTERN)
            .When(x => !string.IsNullOrEmpty(x.TargetPath));
    }
}
