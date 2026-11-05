using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.SharedComponents;
using IniaAi.Domain.Specifications.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.Commands.SharedComponents.PromoteSharedComponent;

public class PromoteSharedComponentCommandHandler
    : IRequestHandler<PromoteSharedComponentCommand, ErrorOr<PromoteSharedComponentResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ICoreDbContext _coreDbContext;
    private readonly WorkflowSavePipeline _savePipeline;
    private readonly ISharedComponentEditor _editor;
    private readonly SharedComponentFanOutService _fanOutService;
    private readonly SharedComponentContentNormalizer _normalizer;
    private readonly ILogger<PromoteSharedComponentCommandHandler> _logger;

    public PromoteSharedComponentCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ICoreDbContext coreDbContext,
        WorkflowSavePipeline savePipeline,
        ISharedComponentEditor editor,
        SharedComponentFanOutService fanOutService,
        SharedComponentContentNormalizer normalizer,
        ILogger<PromoteSharedComponentCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _coreDbContext = coreDbContext ?? throw new ArgumentNullException(nameof(coreDbContext));
        _savePipeline = savePipeline ?? throw new ArgumentNullException(nameof(savePipeline));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<PromoteSharedComponentResult>> Handle(
        PromoteSharedComponentCommand request,
        CancellationToken cancellationToken)
    {
        Workflow? workflow = await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(request.WorkflowTypeId);
        if (workflow is null)
        {
            return Error.NotFound(
                description: $"Workflow type '{request.WorkflowTypeId}' has no saved template content.");
        }

        ErrorOr<SharedComponentTarget> targetResult = _editor.ResolveTarget(
            workflow, request.TargetPath, request.TargetStableId);
        if (targetResult.IsError)
        {
            return targetResult.Errors;
        }

        SharedComponentTarget target = targetResult.Value;
        ErrorOr<Success> promotable = _editor.ValidatePromotable(target);
        if (promotable.IsError)
        {
            return promotable.Errors;
        }

        ErrorOr<ContainerRefPlan?> containerPlanResult =
            await ResolveContainerRefPlanAsync(target, request, cancellationToken);
        if (containerPlanResult.IsError)
        {
            return containerPlanResult.Errors;
        }

        if (containerPlanResult.Value is { } containerPlan)
        {
            return await ConvertContainerSlotToRefAsync(request, target, containerPlan, cancellationToken);
        }

        SharedComponentDefinition definition = _editor.BuildDefinition(target);

        return await _coreDbContext.CreateExecutionStrategy().ExecuteAsync(
            ct => PromoteToNewComponentInTransactionAsync(request, target, definition, workflow, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<PromoteSharedComponentResult>> PromoteToNewComponentInTransactionAsync(
        PromoteSharedComponentCommand request,
        SharedComponentTarget target,
        SharedComponentDefinition definition,
        Workflow workflow,
        CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();

        await using IDbContextTransaction transaction =
            await _coreDbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            ErrorOr<ResolvedComponent> resolved =
                await ResolveOrCreateComponentAsync(request, definition, cancellationToken);
            if (resolved.IsError)
            {
                return resolved.Errors;
            }

            (SharedComponent component, _, string componentVersionHash) = resolved.Value;
            bool wasAttached = request.AttachToComponentId is not null;

            _editor.StampLink(target, component.Id, componentVersionHash);

            ErrorOr<SaveWorkflowResult> saveResult = await _savePipeline.SaveCoreAsync(
                new SaveWorkflowCommand(
                    request.OrganizationId,
                    request.UserId,
                    request.WorkflowTypeId,
                    workflow,
                    request.BaseVersionId,
                    EnforceOptimisticConcurrency: true),
                narrativeOverride: null,
                cancellationToken);
            if (saveResult.IsError)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(
                    "Promote of {TargetPath} in {WorkflowTypeId} aborted: {ErrorCode}",
                    target.CanonicalPath,
                    LogSanitizer.ForLog(request.WorkflowTypeId),
                    saveResult.FirstError.Code);
                return saveResult.Errors;
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Promoted {TargetPath} in {WorkflowTypeId} to shared component {SharedComponentId} (attached={WasAttached})",
                target.CanonicalPath,
                LogSanitizer.ForLog(request.WorkflowTypeId),
                component.Id,
                wasAttached);

            return new PromoteSharedComponentResult(
                component.Id,
                component.Name,
                (ContractsKind)(int)definition.Kind,
                componentVersionHash,
                wasAttached,
                target.CanonicalPath,
                saveResult.Value.VersionId,
                saveResult.Value.ContentHash,
                saveResult.Value.WasDeduped);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                ex,
                "Concurrent save detected while promoting {TargetPath} in {WorkflowTypeId}",
                target.CanonicalPath,
                LogSanitizer.ForLog(request.WorkflowTypeId));
            WorkflowVersion? currentHead =
                await _savePipeline.TryReadCurrentHeadAsync(request.WorkflowTypeId, cancellationToken);
            return Error.Conflict(
                WorkflowSavePipeline.CONCURRENT_SAVE_CODE,
                "The workflow template was modified concurrently; re-read the current version and retry.",
                currentHead is null ? null : WorkflowSavePipeline.BuildHeadMetadata(currentHead));
        }
    }

    private async Task<ErrorOr<ContainerRefPlan?>> ResolveContainerRefPlanAsync(
        SharedComponentTarget target,
        PromoteSharedComponentCommand request,
        CancellationToken cancellationToken)
    {
        if (target.Kind != SharedComponentKind.Block
            || target.ParentSection is not { } parentSection
            || !parentSection.IsActivelyLinked())
        {
            return (ContainerRefPlan?)null;
        }

        string containerComponentId = parentSection.SharedComponentId!;
        SharedComponentVersion? containerHead = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(containerComponentId), cancellationToken);
        if (containerHead is null || containerHead.PayloadAbsentAt is not null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"The container section component '{containerComponentId}' has no stored head content; "
                + "its inner block cannot be shared until the section head is restored.");
        }

        Section? containerHeadDefinition = Section.FromJson(containerHead.ContentJson);
        if (containerHeadDefinition is null)
        {
            return Error.Unexpected(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"The stored head definition of the container section component '{containerComponentId}' "
                + "could not be read.");
        }

        ErrorOr<ConvertToRefDefinition?> convertResult =
            _editor.BuildConvertToRefDefinition(target, containerHeadDefinition);
        if (convertResult.IsError)
        {
            return convertResult.Errors;
        }

        if (convertResult.Value is not { } convert)
        {
            return (ContainerRefPlan?)null;
        }

        if (!string.Equals(request.ExpectedContainerHeadVersionId, containerHead.Id, StringComparison.Ordinal))
        {
            return Error.Conflict(
                SharedComponentErrorCodes.HEAD_MOVED,
                $"Promoting a block inside the Linked section rewrites the section component "
                + $"'{containerComponentId}'; its head version is '{containerHead.Id}'. Supply it as "
                + "expectedContainerHeadVersionId and re-apply.",
                new Dictionary<string, object>
                {
                    ["containerComponentId"] = containerComponentId,
                    ["containerHeadVersionId"] = containerHead.Id,
                });
        }

        WorkflowVersion? anchorHead = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(new LatestWorkflowVersionByTypeSpec(request.WorkflowTypeId), cancellationToken);
        Error? baseVersionConflict = WorkflowSavePipeline.CheckBaseVersion(request.BaseVersionId, anchorHead);
        if (baseVersionConflict is not null)
        {
            return baseVersionConflict.Value;
        }

        return new ContainerRefPlan(containerComponentId, containerHeadDefinition, containerHead.Id, convert);
    }

    private async Task<ErrorOr<PromoteSharedComponentResult>> ConvertContainerSlotToRefAsync(
        PromoteSharedComponentCommand request,
        SharedComponentTarget target,
        ContainerRefPlan plan,
        CancellationToken cancellationToken)
    {
        return await _coreDbContext.CreateExecutionStrategy().ExecuteAsync(
            ct => ConvertContainerSlotToRefInTransactionAsync(request, target, plan, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<PromoteSharedComponentResult>> ConvertContainerSlotToRefInTransactionAsync(
        PromoteSharedComponentCommand request,
        SharedComponentTarget target,
        ContainerRefPlan plan,
        CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();

        var definition = new SharedComponentDefinition(
            SharedComponentKind.Block,
            plan.Convert.DefinitionJson,
            plan.Convert.ContentHash,
            plan.Convert.DefaultName);

        Block? pendingDefinitionBlock = null;
        bool wasAttached = request.AttachToComponentId is not null;
        if (!wasAttached)
        {
            pendingDefinitionBlock = Block.FromJson(definition.DefinitionJson);
            if (pendingDefinitionBlock is null)
            {
                return Error.Unexpected(
                    SharedComponentErrorCodes.CORRUPT_DEFINITION,
                    $"The definition built for slot '{plan.Convert.SlotTag}' could not be read back.");
            }
        }

        ErrorOr<ResolvedComponent> resolved = await ResolveOrCreateComponentAsync(request, definition, cancellationToken);
        if (resolved.IsError)
        {
            return resolved.Errors;
        }

        (SharedComponent component, SharedComponentVersion? pendingVersion, string componentVersionHash) = resolved.Value;
        SharedComponentPendingRefTarget? pendingRefTarget = pendingVersion is null
            ? null
            : new SharedComponentPendingRefTarget(
                component.Id,
                new SharedComponentRefHead(definition.ContentHash, pendingDefinitionBlock!));

        string containerDefinitionJson = _editor.BuildContainerRefDefinitionJson(
            plan.ContainerHeadDefinition, plan.Convert.SlotTag, component.Id);
        ErrorOr<SharedComponentAppendResult> containerAppend = await _fanOutService.AppendAndFanOutWithinStrategyAsync(
            request.OrganizationId,
            request.UserId,
            plan.ContainerComponentId,
            containerDefinitionJson,
            plan.ExpectedHeadVersionId,
            acknowledgeSignatureOrphans: false,
            cancellationToken,
            pendingRefTarget);
        if (containerAppend.IsError)
        {
            if (pendingVersion is not null)
            {
                _coreDbContext.Entry(pendingVersion).State = EntityState.Detached;
                _coreDbContext.Entry(component).State = EntityState.Detached;
            }

            _logger.LogError(
                "Convert-to-ref container append failed for {ContainerComponentId} while promoting {TargetPath}: {ErrorCode}",
                plan.ContainerComponentId,
                target.CanonicalPath,
                containerAppend.FirstError.Code);

            return containerAppend.Errors;
        }

        SharedComponentConsumerSaveResult? anchorSave = containerAppend.Value.ConsumerSaves
            .FirstOrDefault(save => string.Equals(save.WorkflowTypeId, request.WorkflowTypeId, StringComparison.Ordinal));

        _logger.LogInformation(
            "Promoted definition slot {SlotTag} of {ContainerComponentId} to shared component "
            + "{SharedComponentId} from {WorkflowTypeId} (attached={WasAttached})",
            plan.Convert.SlotTag,
            plan.ContainerComponentId,
            component.Id,
            LogSanitizer.ForLog(request.WorkflowTypeId),
            wasAttached);

        return new PromoteSharedComponentResult(
            component.Id,
            component.Name,
            (ContractsKind)(int)SharedComponentKind.Block,
            componentVersionHash,
            wasAttached,
            target.CanonicalPath,
            anchorSave?.VersionId ?? request.BaseVersionId ?? string.Empty,
            anchorSave?.ContentHash ?? string.Empty,
            anchorSave?.WasDeduped ?? false);
    }

    private sealed record ContainerRefPlan(
        string ContainerComponentId,
        Section ContainerHeadDefinition,
        string ExpectedHeadVersionId,
        ConvertToRefDefinition Convert);

    private sealed record ResolvedComponent(
        SharedComponent Component,
        SharedComponentVersion? PendingVersion,
        string ComponentVersionHash);

    private async Task<ErrorOr<ResolvedComponent>> ResolveOrCreateComponentAsync(
        PromoteSharedComponentCommand request,
        SharedComponentDefinition definition,
        CancellationToken cancellationToken)
    {
        if (request.AttachToComponentId is not null)
        {
            ErrorOr<(SharedComponent Component, string HeadHash)> attach =
                await ResolveAttachTargetAsync(request.AttachToComponentId, definition, cancellationToken);
            if (attach.IsError)
            {
                return attach.Errors;
            }

            return new ResolvedComponent(attach.Value.Component, null, attach.Value.HeadHash);
        }

        if (request.CategoryId is not null)
        {
            ErrorOr<Success> categoryGuard = await SharedComponentCategoryGuard.EnsureExistsAsync(
                _coreUnitOfWork, request.CategoryId, cancellationToken);
            if (categoryGuard.IsError)
            {
                return categoryGuard.Errors;
            }
        }

        string name = request.Name ?? definition.DefaultName;
        if (name.Length > SharedComponent.NAME_MAX_LENGTH)
        {
            name = name[..SharedComponent.NAME_MAX_LENGTH];
        }

        var component = new SharedComponent
        {
            Kind = definition.Kind,
            Name = name,
            Description = request.Description,
            CategoryId = request.CategoryId,
            State = SharedComponentState.Active,
        };
        var pendingVersion = new SharedComponentVersion
        {
            SharedComponentId = component.Id,
            SequenceNumber = 1,
            ContentHash = definition.ContentHash,
            CreatedByUserId = request.UserId,
            ContentJson = definition.DefinitionJson,
        };

        _ = _coreDbContext.Add(component);
        _ = _coreDbContext.Add(pendingVersion);

        return new ResolvedComponent(component, pendingVersion, definition.ContentHash);
    }

    private async Task<ErrorOr<(SharedComponent Component, string HeadHash)>> ResolveAttachTargetAsync(
        string attachToComponentId,
        SharedComponentDefinition definition,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(attachToComponentId), cancellationToken);
        if (component is null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.UNKNOWN_COMPONENT,
                $"No shared component with id '{attachToComponentId}' exists to attach to.");
        }

        if (component.Kind != definition.Kind)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.KIND_MISMATCH,
                $"Component '{component.Id}' is a {component.Kind}; the target node is a {definition.Kind}.");
        }

        if (component.State != SharedComponentState.Active)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.RETIRED,
                $"Component '{component.Id}' is retired; attach to an active component.");
        }

        Dictionary<string, SharedComponentProjections.HeadTokens> heads =
            await SharedComponentProjections.GetHeadsAsync(_coreUnitOfWork, [component.Id], cancellationToken);
        SharedComponentProjections.HeadTokens? head = heads.GetValueOrDefault(component.Id);
        if (head is null || head.PayloadAbsent)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"Component '{component.Id}' has no stored head content to attach against.");
        }

        StoredVersionHashes storedHashes = await StoredVersionHashes.LoadContentHashesAsync(
            _coreUnitOfWork, _normalizer, [(component.Id, head.ContentHash)], cancellationToken);
        if (storedHashes.Of(component.Id, head.ContentHash) != definition.ContentHash)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.CONTENT_MISMATCH,
                $"The node's normalized content does not match the head of component '{component.Id}'.",
                new Dictionary<string, object>
                {
                    ["expectedHash"] = head.ContentHash,
                    ["actualHash"] = definition.ContentHash,
                });
        }

        return (component, head.ContentHash);
    }
}
