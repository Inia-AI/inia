using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.SharedComponents;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.SharedComponents.MaterializeSharedComponent;

public class MaterializeSharedComponentCommandHandler
    : IRequestHandler<MaterializeSharedComponentCommand, ErrorOr<MaterializeSharedComponentResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ISender _sender;
    private readonly ISharedComponentEditor _editor;
    private readonly IWorkflowTemplateStructuralValidator _structuralValidator;
    private readonly ILogger<MaterializeSharedComponentCommandHandler> _logger;

    public MaterializeSharedComponentCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ISender sender,
        ISharedComponentEditor editor,
        IWorkflowTemplateStructuralValidator structuralValidator,
        ILogger<MaterializeSharedComponentCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _structuralValidator = structuralValidator ?? throw new ArgumentNullException(nameof(structuralValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<MaterializeSharedComponentResult>> Handle(
        MaterializeSharedComponentCommand request,
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
                $"Component '{component.Id}' is retired and cannot be materialized.");
        }

        SharedComponentVersion? head = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(component.Id), cancellationToken);
        if (head is null || head.PayloadAbsentAt is not null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"Component '{component.Id}' has no stored head content to materialize.");
        }

        Workflow? workflow = await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(request.WorkflowTypeId);
        if (workflow is null)
        {
            return Error.NotFound(
                description: $"Workflow type '{request.WorkflowTypeId}' has no saved template content.");
        }

        ErrorOr<Section?> targetSection = _editor.ResolveMaterializeSection(
            workflow, component.Kind, request.TargetSectionPath, request.TargetSectionStableId);
        if (targetSection.IsError)
        {
            return targetSection.Errors;
        }

        Section? sectionDefinition = component.Kind == SharedComponentKind.Section
            ? Section.FromJson(head.ContentJson)
            : null;
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads =
            await SharedComponentRefHeads.LoadAsync(_coreUnitOfWork, sectionDefinition, cancellationToken);

        List<string> unresolvedRefTargets =
            SharedComponentRefHeads.CollectUnresolvedRefTargetIds(sectionDefinition, refHeads);
        if (unresolvedRefTargets.Count > 0)
        {
            string unresolvedList = string.Join(", ", unresolvedRefTargets.Select(id => $"'{id}'"));
            _logger.LogError(
                "Cannot materialize component {ComponentId}: referenced components {Unresolved} have no resolvable head",
                component.Id,
                unresolvedList);

            return Error.Conflict(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"Component '{component.Id}' references {unresolvedList} with no resolvable head content.");
        }

        ErrorOr<MaterializedNode> materialized = _editor.Materialize(
            workflow,
            component.Kind,
            component.Id,
            component.Name,
            head.ContentHash,
            head.ContentJson,
            targetSection.Value,
            request.Index,
            request.ReplacementTag,
            refHeads);
        if (materialized.IsError)
        {
            return materialized.Errors;
        }

        ErrorOr<SaveWorkflowResult> saveResult = await _sender.Send(
            new SaveWorkflowCommand(
                request.OrganizationId,
                request.UserId,
                request.WorkflowTypeId,
                workflow,
                request.BaseVersionId,
                EnforceOptimisticConcurrency: true),
            cancellationToken);
        if (saveResult.IsError)
        {
            _logger.LogWarning(
                "Materialize of component {SharedComponentId} into {WorkflowTypeId} aborted: {ErrorCode}",
                component.Id,
                LogSanitizer.ForLog(request.WorkflowTypeId),
                saveResult.FirstError.Code);

            return saveResult.Errors;
        }

        WorkflowStructuralValidationResult structural = _structuralValidator.Validate(workflow);

        _logger.LogInformation(
            "Materialized component {SharedComponentId} into {WorkflowTypeId} at {LandedPath}",
            component.Id,
            LogSanitizer.ForLog(request.WorkflowTypeId),
            materialized.Value.CanonicalPath);

        return new MaterializeSharedComponentResult(
            component.Id,
            head.ContentHash,
            materialized.Value.CanonicalPath,
            materialized.Value.ReplacementTag,
            materialized.Value.Bindings,
            saveResult.Value.VersionId,
            saveResult.Value.ContentHash,
            saveResult.Value.WasDeduped,
            [.. structural.Warnings, .. WorkflowStructuralValidationWarning.FromSaveWarnings(saveResult.Value.Warnings)]);
    }
}
