using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Application.Queries.SharedComponents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using Microsoft.EntityFrameworkCore;
using DomainKind = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind;

namespace IniaAi.Application.Queries.WorkflowTransfer.ImportWorkflowTemplatePreview;

public class ImportWorkflowTemplatePreviewQueryHandler
    : IRequestHandler<ImportWorkflowTemplatePreviewQuery, ErrorOr<ImportWorkflowTemplatePreviewResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IWorkflowTemplateStructuralValidator _structuralValidator;
    private readonly SharedComponentFanOutService _fanOutService;

    public ImportWorkflowTemplatePreviewQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IWorkflowTemplateStructuralValidator structuralValidator,
        SharedComponentFanOutService fanOutService)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _structuralValidator = structuralValidator ?? throw new ArgumentNullException(nameof(structuralValidator));
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
    }

    public async Task<ErrorOr<ImportWorkflowTemplatePreviewResult>> Handle(
        ImportWorkflowTemplatePreviewQuery request,
        CancellationToken cancellationToken)
    {
        WorkflowTransferBundle bundle = request.Bundle;

        if (bundle.BundleFormatVersion > WorkflowTransferBundleFormat.CURRENT_VERSION)
        {
            return Error.Validation(
                SharedComponentErrorCodes.BUNDLE_FORMAT_UNSUPPORTED,
                $"The bundle's format version {bundle.BundleFormatVersion} is newer than this deployment "
                + $"supports ({WorkflowTransferBundleFormat.CURRENT_VERSION}). Update the platform to import it.");
        }

        WorkflowTypePresence presence = await WorkflowTypePresenceQuery.ReadAsync(
            _coreUnitOfWork.Repository<WorkflowType>().GetAll(),
            request.WorkflowTypeId,
            cancellationToken);
        if (presence == WorkflowTypePresence.Retired)
        {
            return WorkflowTypeErrorFactory.Deleted(request.WorkflowTypeId);
        }

        WorkflowVersion? head = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(new LatestWorkflowVersionByTypeSpec(request.WorkflowTypeId), cancellationToken);

        Workflow? localTemplate = null;
        if (head is not null)
        {
            localTemplate = Workflow.FromJson(head.WorkflowJson);
            if (localTemplate is null)
            {
                return Error.Unexpected(
                    SharedComponentErrorCodes.CORRUPT_DEFINITION,
                    $"The stored template head for '{request.WorkflowTypeId}' could not be parsed.");
            }
        }

        WorkflowStructuralValidationResult templateValidation = _structuralValidator.Validate(bundle.TemplateJson);
        if (!templateValidation.IsValid)
        {
            return templateValidation.Errors;
        }

        Workflow bundleTemplate = templateValidation.Workflow!;

        ErrorOr<Success> bundleShape = _fanOutService.ValidateBundleClosure(bundle);
        if (bundleShape.IsError)
        {
            return bundleShape.Errors;
        }

        IReadOnlyDictionary<string, SharedComponentCorrelator.Correlation> correlations =
            await SharedComponentCorrelator.CorrelateAsync(
                _coreUnitOfWork,
                localTemplate ?? new Workflow(request.WorkflowTypeId),
                bundleTemplate,
                bundle.Components,
                cancellationToken);

        List<string> localIds = [.. correlations.Values
            .Select(correlation => correlation.LocalComponentId)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)];

        Dictionary<string, SharedComponent> localComponentsById =
            await SharedComponentProjections.GetComponentsByIdAsync(_coreUnitOfWork, localIds, cancellationToken);
        Dictionary<string, SharedComponentVersion> localHeadsById =
            await SharedComponentProjections.GetHeadVersionRowsAsync(_coreUnitOfWork, localIds, cancellationToken);
        Dictionary<string, List<string>> localTrailsById =
            await SharedComponentProjections.GetTrailsByComponentAsync(_coreUnitOfWork, localIds, cancellationToken);

        Dictionary<string, TransferComponentDto> bundleComponentsById = bundle.Components
            .ToDictionary(component => component.ComponentId, StringComparer.Ordinal);
        var componentRows = new List<ImportComponentPreviewRow>();
        foreach (TransferComponentDto component in bundle.Components)
        {
            componentRows.Add(BuildComponentRow(
                component,
                correlations[component.ComponentId],
                localComponentsById,
                localHeadsById,
                localTrailsById,
                correlations,
                bundleComponentsById));
        }

        ImportTemplatePreviewRow templateRow = head is null
            ? new ImportTemplatePreviewRow(
                SharedComponentTransferRelation.New, null, bundle.TemplateHashTrail, [])
            : await BuildTemplateRowAsync(bundle, head, cancellationToken);

        return new ImportWorkflowTemplatePreviewResult(
            request.WorkflowTypeId,
            bundle.OriginWorkflowTypeId,
            head?.Id,
            templateRow,
            componentRows,
            SharedComponentFanOutService.SKIPPED_CARRIERS);
    }

    private ImportComponentPreviewRow BuildComponentRow(
        TransferComponentDto component,
        SharedComponentCorrelator.Correlation correlation,
        Dictionary<string, SharedComponent> localComponentsById,
        Dictionary<string, SharedComponentVersion> localHeadsById,
        Dictionary<string, List<string>> localTrailsById,
        IReadOnlyDictionary<string, SharedComponentCorrelator.Correlation> correlations,
        IReadOnlyDictionary<string, TransferComponentDto> bundleComponentsById)
    {
        var kind = (DomainKind)(int)component.Kind;
        List<string> refWarnings = BuildRefWarnings(
            component, correlations, localComponentsById, localHeadsById, bundleComponentsById);

        if (correlation.LocalComponentId is not { } localId
            || !localComponentsById.TryGetValue(localId, out SharedComponent? localComponent)
            || !localHeadsById.TryGetValue(localId, out SharedComponentVersion? localHead))
        {
            return new ImportComponentPreviewRow(
                component.ComponentId,
                null,
                kind,
                component.Name,
                SharedComponentTransferRelation.New,
                correlation.Method,
                null,
                component.HeadContentHash,
                null,
                null,
                component.HashTrail,
                null,
                refWarnings);
        }

        List<string> localTrail = localTrailsById.GetValueOrDefault(localId) ?? [];

        (SharedComponentTransferRelation relation, string? conflictCode) =
            localComponent.State == SharedComponentState.Retired
                ? (SharedComponentTransferRelation.LocalRetired, null)
                : ClassifyRelation(component, localComponent, localHead, localTrail, correlations);

        return new ImportComponentPreviewRow(
            component.ComponentId,
            localId,
            kind,
            component.Name,
            relation,
            correlation.Method,
            conflictCode,
            component.HeadContentHash,
            localHead.ContentHash,
            localHead.Id,
            component.HashTrail,
            localTrail,
            refWarnings);
    }

    private static List<string> BuildRefWarnings(
        TransferComponentDto component,
        IReadOnlyDictionary<string, SharedComponentCorrelator.Correlation> correlations,
        Dictionary<string, SharedComponent> localComponentsById,
        Dictionary<string, SharedComponentVersion> localHeadsById,
        IReadOnlyDictionary<string, TransferComponentDto> bundleComponentsById)
    {
        if ((DomainKind)(int)component.Kind != DomainKind.Section)
        {
            return [];
        }

        Section? section = Section.FromJson(component.HeadDefinitionJson);
        if (section is null)
        {
            return [];
        }

        List<string> warnings = [];
        foreach (string refTargetId in SharedComponentRefHeads.CollectRefTargetIds(section))
        {
            string? localId = correlations.GetValueOrDefault(refTargetId)?.LocalComponentId;
            SharedComponent? localTarget = null;
            SharedComponentVersion? localTargetHead = null;
            bool liveLocal = localId is not null
                && localComponentsById.TryGetValue(localId, out localTarget)
                && localHeadsById.TryGetValue(localId, out localTargetHead);

            DomainKind? kind;
            SharedComponentState? state;
            bool headPayloadAvailable;
            if (liveLocal)
            {
                kind = localTarget!.Kind;
                state = localTarget.State;
                headPayloadAvailable = localTargetHead!.PayloadAbsentAt is null;
            }
            else if (bundleComponentsById.TryGetValue(refTargetId, out TransferComponentDto? bundleTarget))
            {
                kind = (DomainKind)(int)bundleTarget.Kind;
                state = SharedComponentState.Active;
                headPayloadAvailable = true;
            }
            else
            {
                continue;
            }

            string? violation = SharedComponentRefHeads.ClassifyRefTarget(
                targetKnown: true, kind, state, headPayloadAvailable);
            switch (violation)
            {
                case SharedComponentErrorCodes.KIND_MISMATCH:
                    warnings.Add(liveLocal
                        ? $"References component '{refTargetId}', whose local counterpart is a {kind} "
                            + "component; section entries can only reference Block components, so the apply will fail. "
                            + $"[{violation}]"
                        : $"References component '{refTargetId}', which the bundle carries as a {kind} "
                            + "component; section entries can only reference Block components, so the apply will fail. "
                            + $"[{violation}]");
                    break;
                case SharedComponentErrorCodes.RETIRED:
                    warnings.Add(
                        $"References component '{refTargetId}', whose local counterpart is retired; the apply will "
                        + $"fail unless it is unretired first. [{violation}]");
                    break;
                case SharedComponentErrorCodes.PAYLOAD_ABSENT:
                    warnings.Add(
                        $"References component '{refTargetId}', whose local head has no stored payload; keeping the "
                        + "local version will fail the apply — take the bundle's version or restore the payload. "
                        + $"[{violation}]");
                    break;
                default:
                    break;
            }
        }

        return warnings;
    }

    private (SharedComponentTransferRelation Relation, string? ConflictCode) ClassifyRelation(
        TransferComponentDto component,
        SharedComponent localComponent,
        SharedComponentVersion localHead,
        List<string> localTrail,
        IReadOnlyDictionary<string, SharedComponentCorrelator.Correlation> correlations)
    {
        if (IsInSync(component, localComponent, localHead, correlations))
        {
            return (SharedComponentTransferRelation.InSync, null);
        }

        var localTrailSet = new HashSet<string>(localTrail, StringComparer.Ordinal);
        if (localTrailSet.Contains(component.HeadContentHash))
        {
            return (SharedComponentTransferRelation.LocalAhead, null);
        }

        if (component.HashTrail.Contains(localHead.ContentHash, StringComparer.Ordinal))
        {
            return (SharedComponentTransferRelation.BundleAhead, null);
        }

        bool zeroOverlap = !component.HashTrail.Any(localTrailSet.Contains);
        return (
            SharedComponentTransferRelation.Diverged,
            zeroOverlap ? SharedComponentErrorCodes.DIVERGED_LINEAGE : null);
    }

    private bool IsInSync(
        TransferComponentDto component,
        SharedComponent localComponent,
        SharedComponentVersion localHead,
        IReadOnlyDictionary<string, SharedComponentCorrelator.Correlation> correlations)
    {
        string localizedJson = SharedComponentTransferCompare.LocalizeHeadDefinition(component, correlations);
        ErrorOr<SharedComponentFanOutService.NormalizedDefinition> normalized =
            _fanOutService.NormalizeDefinition(localComponent, localizedJson);

        if (normalized.IsError)
        {
            return false;
        }

        if (string.Equals(normalized.Value.ContentHash, localHead.ContentHash, StringComparison.Ordinal))
        {
            return true;
        }

        return localHead.PayloadAbsentAt is null
            && _fanOutService.NormalizeDefinition(localComponent, localHead.ContentJson) is { IsError: false } localNormalized
            && string.Equals(normalized.Value.ContentHash, localNormalized.Value.ContentHash, StringComparison.Ordinal);
    }

    private async Task<ImportTemplatePreviewRow> BuildTemplateRowAsync(
        WorkflowTransferBundle bundle,
        WorkflowVersion head,
        CancellationToken cancellationToken)
    {
        List<string> localTemplateTrail = await _coreUnitOfWork
            .Repository<WorkflowVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(version => version.WorkflowTypeId == head.WorkflowTypeId)
            .OrderBy(version => version.CreatedAt)
            .ThenBy(version => version.ContentHash)
            .ThenBy(version => version.Id)
            .Select(version => version.ContentHash)
            .ToListAsync(cancellationToken);

        string? bundleTemplateHead = bundle.TemplateHashTrail.Count > 0 ? bundle.TemplateHashTrail[^1] : null;
        var localTrailSet = new HashSet<string>(localTemplateTrail, StringComparer.Ordinal);

        SharedComponentTransferRelation relation;
        string? conflictCode = null;

        if (bundleTemplateHead is not null
            && string.Equals(bundleTemplateHead, head.ContentHash, StringComparison.Ordinal))
        {
            relation = SharedComponentTransferRelation.InSync;
        }
        else if (bundleTemplateHead is not null && localTrailSet.Contains(bundleTemplateHead))
        {
            relation = SharedComponentTransferRelation.LocalAhead;
        }
        else if (bundle.TemplateHashTrail.Contains(head.ContentHash, StringComparer.Ordinal))
        {
            relation = SharedComponentTransferRelation.BundleAhead;
        }
        else
        {
            relation = SharedComponentTransferRelation.Diverged;
            bool zeroOverlap = !bundle.TemplateHashTrail.Any(localTrailSet.Contains);
            conflictCode = zeroOverlap ? SharedComponentErrorCodes.DIVERGED_LINEAGE : null;
        }

        return new ImportTemplatePreviewRow(relation, conflictCode, bundle.TemplateHashTrail, localTemplateTrail);
    }
}
