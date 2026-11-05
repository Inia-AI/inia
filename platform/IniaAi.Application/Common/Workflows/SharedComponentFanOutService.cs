using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.ApplySharedComponentReconcile;
using IniaAi.Application.Commands.SharedComponents.RetireSharedComponent;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Signatures;
using IniaAi.Application.Errors;
using IniaAi.Common.Constants;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Testing;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.SharedComponents;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.Application.Queries.SharedComponents;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using WireKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Fan-out engine for shared components. The read slice projects which Core templates hold live
/// Linked copies of a component (broken-out copies keep the component id as provenance and never
/// count) and which baseline registrations a definition change would stale — freshness computed at
/// read, blessed hash vs live head hash, resolved cross-context (registrations in UserData, heads
/// in Core). The write slice is <see cref="AppendAndFanOutAsync"/>, the single choke point through
/// which every definition change flows: it re-normalizes the submitted definition (agents cannot
/// sneak per-consumer or link fields into stored payloads), dedups against the head (hash AND
/// payload bytes AND head-payload-present — appending onto a payload-absent imported head never
/// dedups, so the chain regains a payload), and inside ONE transaction appends the version as a
/// context-level pending add and rewrites every Linked consumer in place — instance stable ids and
/// per-consumer fields kept verbatim. A moved head fails the friendly pre-check; the racing window
/// after it is closed by the unique (SharedComponentId, SequenceNumber) index, whose violation is
/// rolled back BEFORE the recovery head is re-read (an aborted PostgreSQL transaction poisons
/// in-transaction reads) and surfaced as a ConcurrentAppend conflict. Every graph this engine
/// grafts onto is loaded via GetByTypeIdAsNoTrackingAsync for two load-bearing reasons: canonical
/// JSON strips stable ids (a tracked canonical load would re-mint them all on save), and the graft
/// clears and re-clones owned collections such as TextInputs — on a tracked graph that would sever
/// required FK relationships mid-flight, where on a detached graph it is a plain list operation and
/// the save's delete-flush-then-insert replaces the rows wholesale.
/// </summary>
public sealed class SharedComponentFanOutService
{
    private const string DEFINITION_JSON_ERROR_KEY = "definitionJson";
    private const string DECISIONS_ERROR_KEY = "decisions";
    private const string WORKFLOW_TEMPLATE_NOT_FOUND = "WorkflowTemplate.NotFound";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreDbContext _coreDbContext;
    private readonly WorkflowSavePipeline _savePipeline;
    private readonly SharedComponentContentNormalizer _normalizer;
    private readonly SharedComponentReconciler _reconciler;
    private readonly WorkflowCanonicalizer _canonicalizer;
    private readonly IWorkflowTemplateStructuralValidator _structuralValidator;
    private readonly IBaselineReader _baselines;
    private readonly ISharedComponentEditor _sharedComponentEditor;
    private readonly AgentRuntimeSettings _agentRuntime;
    private readonly ILogger<SharedComponentFanOutService> _logger;
    private readonly WorkflowExecutionJanitorSettings? _janitor;
    private readonly int? _maxBlockParallelism;

    public SharedComponentFanOutService(
        ICoreUnitOfWork coreUnitOfWork,
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreDbContext coreDbContext,
        WorkflowSavePipeline savePipeline,
        SharedComponentContentNormalizer normalizer,
        SharedComponentReconciler reconciler,
        WorkflowCanonicalizer canonicalizer,
        IWorkflowTemplateStructuralValidator structuralValidator,
        ISharedComponentEditor sharedComponentEditor,
        IBaselineReader baselines,
        IOptions<AgentRuntimeSettings> agentRuntimeSettings,
        ILogger<SharedComponentFanOutService> logger,
        IOptions<WorkflowExecutionJanitorSettings>? janitorSettings = null,
        IOptions<PlatformSettings>? platformSettings = null)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _coreDbContext = coreDbContext ?? throw new ArgumentNullException(nameof(coreDbContext));
        _savePipeline = savePipeline ?? throw new ArgumentNullException(nameof(savePipeline));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        _canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
        _structuralValidator = structuralValidator ?? throw new ArgumentNullException(nameof(structuralValidator));
        _sharedComponentEditor = sharedComponentEditor ?? throw new ArgumentNullException(nameof(sharedComponentEditor));
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        _agentRuntime = (agentRuntimeSettings ?? throw new ArgumentNullException(nameof(agentRuntimeSettings))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _janitor = janitorSettings?.Value;
        _maxBlockParallelism = platformSettings?.Value.MaxBlockParallelism;
    }

    private List<Error>? AgentGuardErrors(NormalizedDefinition definition)
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateDefinition(
            definition.BlockDefinition,
            definition.SectionDefinition,
            _agentRuntime,
            runLimits: null,
            _janitor,
            _maxBlockParallelism);

        return violations.Count == 0
            ? null
            : violations.ConvertAll(SharedComponentErrorCodes.AgentViolation);
    }

    public Task<ErrorOr<SharedComponentAppendResult>> AppendAndFanOutAsync(
        string organizationId,
        string userId,
        string componentId,
        string definitionJson,
        string expectedHeadVersionId,
        CancellationToken cancellationToken)
    {
        return AppendAndFanOutAsync(
            organizationId,
            userId,
            componentId,
            definitionJson,
            expectedHeadVersionId,
            acknowledgeSignatureOrphans: false,
            cancellationToken);
    }

    public async Task<ErrorOr<SharedComponentAppendResult>> AppendAndFanOutAsync(
        string organizationId,
        string userId,
        string componentId,
        string definitionJson,
        string expectedHeadVersionId,
        bool acknowledgeSignatureOrphans,
        CancellationToken cancellationToken,
        SharedComponentPendingRefTarget? pendingRefTarget = null,
        bool acknowledgeOverwrittenEntries = false)
    {
        return await _coreDbContext.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                _coreDbContext.ClearChangeTracker();
                return await AppendAndFanOutWithinStrategyAsync(
                    organizationId,
                    userId,
                    componentId,
                    definitionJson,
                    expectedHeadVersionId,
                    acknowledgeSignatureOrphans,
                    ct,
                    pendingRefTarget,
                    acknowledgeOverwrittenEntries);
            },
            cancellationToken);
    }

    internal async Task<ErrorOr<SharedComponentAppendResult>> AppendAndFanOutWithinStrategyAsync(
        string organizationId,
        string userId,
        string componentId,
        string definitionJson,
        string expectedHeadVersionId,
        bool acknowledgeSignatureOrphans,
        CancellationToken cancellationToken,
        SharedComponentPendingRefTarget? pendingRefTarget,
        bool acknowledgeOverwrittenEntries = false)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(componentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(componentId);
        }

        if (component.State != SharedComponentState.Active)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.RETIRED,
                $"Component '{component.Id}' is retired; its definition is frozen until unretired.");
        }

        ErrorOr<NormalizedDefinition> normalizedResult = NormalizeDefinition(component, definitionJson);
        if (normalizedResult.IsError)
        {
            return normalizedResult.Errors;
        }

        NormalizedDefinition normalized = normalizedResult.Value;
        List<Error>? agentGuard = AgentGuardErrors(normalized);
        if (agentGuard is not null)
        {
            return agentGuard;
        }

        Error? refGuard = await ValidateRefTargetsAsync(
            normalized.SectionDefinition, pendingRefTarget?.ComponentId, cancellationToken);
        if (refGuard is not null)
        {
            return refGuard.Value;
        }

        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads =
            await SharedComponentRefHeads.LoadAsync(_coreUnitOfWork, normalized.SectionDefinition, cancellationToken);
        if (pendingRefTarget is not null)
        {
            Dictionary<string, SharedComponentRefHead> withPending = refHeads is null
                ? new Dictionary<string, SharedComponentRefHead>(StringComparer.Ordinal)
                : new Dictionary<string, SharedComponentRefHead>(refHeads, StringComparer.Ordinal);
            withPending[pendingRefTarget.ComponentId] = pendingRefTarget.Head;
            refHeads = withPending;
        }
        SharedComponentVersion? head = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(component.Id), cancellationToken);
        if (head is null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.NO_VERSIONS,
                $"Component '{component.Id}' has no versions to append onto.");
        }

        if (!string.Equals(expectedHeadVersionId, head.Id, StringComparison.Ordinal))
        {
            return Error.Conflict(
                SharedComponentErrorCodes.HEAD_MOVED,
                $"The component head version is '{head.Id}'; the supplied expectedHeadVersionId is stale or missing. "
                + "Re-read the component and re-apply the edit.",
                BuildComponentHeadMetadata(head));
        }

        bool wasDeduped = MatchesHead(head, normalized);
        if (wasDeduped)
        {
            return new SharedComponentAppendResult(component.Id, head.Id, head.ContentHash, true, []);
        }

        List<SharedComponentVersion> chain = await LoadChainAsync(component, cancellationToken);

        await using IDbContextTransaction transaction =
            await _coreDbContext.BeginTransactionAsync(cancellationToken);
        var version = new SharedComponentVersion
        {
            SharedComponentId = component.Id,
            SequenceNumber = head.SequenceNumber + 1,
            ContentHash = normalized.ContentHash,
            ParentVersionId = head.Id,
            CreatedByUserId = userId,
            ContentJson = normalized.DefinitionJson,
        };

        try
        {
            List<SharedComponentConsumer> consumers = await GetConsumersAsync(component.Id, cancellationToken);
            ErrorOr<NormalizedDefinition?> oldDefinition = NormalizeStoredHead(component, head);
            if (oldDefinition.IsError)
            {
                return oldDefinition.Errors;
            }

            SharedComponentUpdateImpact impact = await GetUpdateImpactAsync(
                component,
                oldDefinition.Value,
                normalized,
                consumers,
                cancellationToken,
                includeAffectedBaselines: false);
            if (impact.OrphanedSignatureOverrides.Count > 0 && !acknowledgeSignatureOrphans)
            {
                return OrphansError(component.Id, impact.OrphanedSignatureOverrides);
            }

            _ = _coreDbContext.Add(version);

            List<SharedComponentConsumerSaveResult> consumerSaves = [];
            string narrative = $"Updated from shared component '{component.Name}' ({normalized.ContentHash[..8]})";
            Dictionary<string, Section?> oldDefinitionsByHash = new(StringComparer.Ordinal);
            foreach (SharedComponentConsumer consumer in consumers)
            {
                Workflow? consumerWorkflow = await _coreUnitOfWork.Workflows
                    .GetByTypeIdAsNoTrackingAsync(consumer.WorkflowTypeId);
                if (consumerWorkflow is null)
                {
                    _logger.LogWarning(
                        "Skipping fan-out of component {SharedComponentId} to {WorkflowTypeId}: the template row no longer exists",
                        component.Id,
                        consumer.WorkflowTypeId);
                    continue;
                }

                ErrorOr<List<SectionReconcileResult>> grafting = GraftConsumer(
                    component,
                    consumerWorkflow,
                    normalized,
                    chain,
                    oldDefinitionsByHash,
                    refHeads,
                    await LoadStoredContentHashesAsync(consumerWorkflow, RefHeadVersions(refHeads), cancellationToken),
                    acknowledgeOverwrittenEntries);
                if (grafting.IsError)
                {
                    DetachPendingVersion(version);

                    return grafting.Errors;
                }

                List<SectionReconcileResult> sectionReconciles = grafting.Value;
                PruneOrphanedSignatureDefinitions(
                    consumerWorkflow,
                    consumer.WorkflowTypeId,
                    impact.OrphanedSignatureOverrides);

                ErrorOr<SaveWorkflowResult> save = await _savePipeline.SaveCoreAsync(
                    new SaveWorkflowCommand(
                        organizationId,
                        userId,
                        consumer.WorkflowTypeId,
                        consumerWorkflow,
                        ConfirmSignatureFieldDeletions: true),
                    narrative,
                    cancellationToken);
                if (save.IsError)
                {
                    DetachPendingVersion(version);

                    return save.Errors;
                }

                consumerSaves.Add(BuildWorkflowSaveResult(
                    consumer.WorkflowTypeId,
                    save.Value,
                    consumerWorkflow,
                    sectionReconciles,
                    impact.OrphanedSignatureOverrides));
            }

            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Appended version {VersionId} (seq {SequenceNumber}) to component {SharedComponentId} and fanned out to {ConsumerCount} consumer templates",
                version.Id,
                version.SequenceNumber,
                component.Id,
                consumerSaves.Count);

            return new SharedComponentAppendResult(
                component.Id,
                version.Id,
                normalized.ContentHash,
                false,
                consumerSaves);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachPendingVersion(version);
            _logger.LogWarning(
                ex,
                "Concurrent template save aborted the fan-out of component {SharedComponentId}; nothing was applied",
                component.Id);

            return Error.Conflict(
                WorkflowSavePipeline.CONCURRENT_SAVE_CODE,
                "A consumer template was modified concurrently during the fan-out; nothing was applied. Retry the operation.");
        }
        catch (DbUpdateException ex) when (ex.Entries.Any(e => e.Entity is SharedComponentVersion))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachPendingVersion(version);
            _logger.LogWarning(
                ex,
                "Concurrent append detected for component {SharedComponentId}",
                component.Id);
            SharedComponentVersion? currentHead = await TryReadHeadAsync(component.Id, cancellationToken);

            return Error.Conflict(
                SharedComponentErrorCodes.CONCURRENT_APPEND,
                $"Another version of component '{component.Id}' was appended concurrently; re-read the head and retry.",
                currentHead is null ? null : BuildComponentHeadMetadata(currentHead));
        }
        catch
        {
            DetachPendingVersion(version);

            throw;
        }
    }

    /// <summary>
    /// The write half of the reconcile pair: applies one batch of per-instance decisions against
    /// the anchor template plus every fan-out those decisions trigger, atomically. Local arms
    /// (BreakOut, KeepLocal, Relink, AdoptHead) mutate the anchor graph in memory; each
    /// UpdateShared then appends the instance's reverse-normalized content as the component's new
    /// head and grafts every Linked consumer — decisions and fan-outs land in ONE transaction,
    /// with the anchor saved first under the caller's BaseVersionId and non-anchor consumers
    /// saved tokenless (a racing rival edit trips the delete-count guard and rolls the whole
    /// apply back). Orphan-producing grafts are gated per decision by
    /// <see cref="SharedComponentReconcileDecision.AcknowledgeSignatureOrphans"/>, re-computed
    /// here rather than trusted from any preview.
    /// </summary>
    public async Task<ErrorOr<SharedComponentReconcileApplyResult>> ApplyReconcileAsync(
        ApplySharedComponentReconcileCommand request,
        CancellationToken cancellationToken)
    {
        return await _coreDbContext.CreateExecutionStrategy().ExecuteAsync(
            ct => ApplyReconcileCoreAsync(request, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<SharedComponentReconcileApplyResult>> ApplyReconcileCoreAsync(
        ApplySharedComponentReconcileCommand request,
        CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();

        Workflow? anchor = await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(request.WorkflowTypeId);
        if (anchor is null)
        {
            return Error.NotFound(
                WORKFLOW_TEMPLATE_NOT_FOUND,
                $"Workflow template '{request.WorkflowTypeId}' was not found.");
        }

        ErrorOr<List<ResolvedDecision>> resolvedResult = ResolveDecisions(anchor, request.Decisions);
        if (resolvedResult.IsError)
        {
            return resolvedResult.Errors;
        }

        List<ResolvedDecision> resolved = resolvedResult.Value;
        List<string> componentIds = [.. resolved
            .Select(r => r.Decision.SharedComponentId)
            .Concat(CollectAuxiliaryComponentIds(resolved))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        Dictionary<string, SharedComponent> componentsById = (await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);
        Dictionary<string, SharedComponentVersion?> headsById = new(StringComparer.Ordinal);
        foreach (string componentId in componentIds)
        {
            headsById[componentId] = await _coreUnitOfWork.Repository<SharedComponentVersion>()
                .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(componentId), cancellationToken);
        }

        Error? armGuard = ValidateDecisionArms(resolved, componentsById, headsById);
        if (armGuard is not null)
        {
            return armGuard.Value;
        }

        StoredVersionHashes anchorStoredHashes = await LoadStoredContentHashesAsync(
            anchor,
            [.. headsById.Where(pair => pair.Value is not null).Select(pair => (pair.Key, pair.Value!.ContentHash))],
            cancellationToken);
        ErrorOr<List<ComponentUpdatePlan>> plansResult =
            BuildUpdatePlans(resolved, componentsById, headsById, anchorStoredHashes);
        if (plansResult.IsError)
        {
            return plansResult.Errors;
        }

        List<ComponentUpdatePlan> plans = plansResult.Value;
        foreach (ComponentUpdatePlan plan in plans)
        {
            Error? planRefGuard = await ValidateRefTargetsAsync(
                plan.Normalized.SectionDefinition, pendingRefTargetId: null, cancellationToken);
            if (planRefGuard is not null)
            {
                return planRefGuard.Value;
            }
        }

        Error? relinkGuard = CheckRelinkContent(resolved, headsById, anchorStoredHashes);
        if (relinkGuard is not null)
        {
            return relinkGuard.Value;
        }

        var state = new ReconcileApplyState
        {
            ComponentsById = componentsById,
            HeadsById = headsById,
            Plans = plans,
        };
        await using IDbContextTransaction transaction =
            await _coreDbContext.BeginTransactionAsync(cancellationToken);
        Dictionary<string, SharedComponentVersion> appendedByComponent = new(StringComparer.Ordinal);
        try
        {
            var graphs = new Dictionary<string, Workflow>(StringComparer.Ordinal)
            {
                [request.WorkflowTypeId] = anchor,
            };
            Dictionary<string, List<string>> fanOutLinesByType = new(StringComparer.Ordinal);

            foreach (ResolvedDecision item in resolved)
            {
                ErrorOr<Success> armResult = await ApplyLocalArmAsync(
                    request,
                    anchor,
                    item,
                    state,
                    cancellationToken);
                if (armResult.IsError)
                {
                    DetachPendingVersions(appendedByComponent.Values);

                    return armResult.Errors;
                }
            }

            foreach (ComponentUpdatePlan plan in plans.Where(p => !p.AlreadyCurrent))
            {
                List<SharedComponentConsumer> consumers = await GetConsumersAsync(plan.Component.Id, cancellationToken);
                ErrorOr<NormalizedDefinition?> oldDefinition = NormalizeStoredHead(plan.Component, plan.Head);
                if (oldDefinition.IsError)
                {
                    DetachPendingVersions(appendedByComponent.Values);

                    return oldDefinition.Errors;
                }

                SharedComponentUpdateImpact impact = await GetUpdateImpactAsync(
                    plan.Component,
                    oldDefinition.Value,
                    plan.Normalized,
                    consumers,
                    cancellationToken,
                    includeAffectedBaselines: false);
                if (impact.OrphanedSignatureOverrides.Count > 0 && !plan.OrphansAcknowledged)
                {
                    DetachPendingVersions(appendedByComponent.Values);

                    return OrphansError(plan.Component.Id, impact.OrphanedSignatureOverrides);
                }

                state.AllOrphans.AddRange(impact.OrphanedSignatureOverrides);
                var version = new SharedComponentVersion
                {
                    SharedComponentId = plan.Component.Id,
                    SequenceNumber = plan.Head.SequenceNumber + 1,
                    ContentHash = plan.Normalized.ContentHash,
                    ParentVersionId = plan.Head.Id,
                    CreatedByUserId = request.UserId,
                    ContentJson = plan.Normalized.DefinitionJson,
                };
                _ = _coreDbContext.Add(version);
                appendedByComponent[plan.Component.Id] = version;

                List<SharedComponentVersion> chain = await GetChainAsync(
                    plan.Component, state.ChainsByComponent, cancellationToken);
                List<string> touchedTypeIds = [.. consumers.Select(c => c.WorkflowTypeId)];
                if (!touchedTypeIds.Contains(request.WorkflowTypeId, StringComparer.Ordinal))
                {
                    touchedTypeIds.Add(request.WorkflowTypeId);
                }

                IReadOnlyDictionary<string, SharedComponentRefHead>? planRefHeads =
                    await SharedComponentRefHeads.LoadAsync(_coreUnitOfWork, plan.Normalized.SectionDefinition, cancellationToken);

                foreach (string typeId in touchedTypeIds)
                {
                    Workflow? graph = await GetOrLoadGraphAsync(graphs, typeId, plan.Component.Id);
                    if (graph is null)
                    {
                        continue;
                    }

                    ErrorOr<List<SectionReconcileResult>> grafting = GraftConsumer(
                        plan.Component,
                        graph,
                        plan.Normalized,
                        chain,
                        state.OldDefinitionsByHash,
                        planRefHeads,
                        await LoadStoredContentHashesAsync(graph, RefHeadVersions(planRefHeads), cancellationToken),
                        plan.OverwrittenEntriesAcknowledged);
                    if (grafting.IsError)
                    {
                        DetachPendingVersions(appendedByComponent.Values);

                        return grafting.Errors;
                    }

                    AppendTo(state.ReconcilesByType, typeId, grafting.Value);
                    if (!string.Equals(typeId, request.WorkflowTypeId, StringComparison.Ordinal))
                    {
                        AppendTo(
                            fanOutLinesByType,
                            typeId,
                            [$"Updated from shared component '{plan.Component.Name}' ({plan.Normalized.ContentHash[..8]})"]);
                    }
                }
            }

            Dictionary<string, string> currentHeadHashesById = new(StringComparer.Ordinal);
            foreach ((string componentId, SharedComponentVersion? head) in state.HeadsById)
            {
                if (head is not null)
                {
                    currentHeadHashesById[componentId] = head.ContentHash;
                }
            }

            foreach ((string componentId, SharedComponentVersion appended) in appendedByComponent)
            {
                currentHeadHashesById[componentId] = appended.ContentHash;
            }

            await RestampInSyncLinkedNodesAsync(graphs.Values, currentHeadHashesById, cancellationToken);

            List<SharedComponentConsumerSaveResult> workflowSaves = [];
            PruneOrphanedSignatureDefinitions(anchor, request.WorkflowTypeId, state.AllOrphans);
            ErrorOr<SaveWorkflowResult> anchorSave = await _savePipeline.SaveCoreAsync(
                new SaveWorkflowCommand(
                    request.OrganizationId,
                    request.UserId,
                    request.WorkflowTypeId,
                    anchor,
                    request.BaseVersionId,
                    EnforceOptimisticConcurrency: true,
                    ConfirmSignatureFieldDeletions: true),
                narrativeOverride: null,
                cancellationToken);
            if (anchorSave.IsError)
            {
                DetachPendingVersions(appendedByComponent.Values);

                return anchorSave.Errors;
            }

            workflowSaves.Add(BuildWorkflowSaveResult(
                request.WorkflowTypeId,
                anchorSave.Value,
                anchor,
                state.ReconcilesByType.GetValueOrDefault(request.WorkflowTypeId) ?? [],
                state.AllOrphans));
            foreach ((string typeId, Workflow graph) in graphs
                .Where(g => !string.Equals(g.Key, request.WorkflowTypeId, StringComparison.Ordinal))
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                PruneOrphanedSignatureDefinitions(graph, typeId, state.AllOrphans);
                ErrorOr<SaveWorkflowResult> consumerSave = await _savePipeline.SaveCoreAsync(
                    new SaveWorkflowCommand(
                        request.OrganizationId,
                        request.UserId,
                        typeId,
                        graph,
                        ConfirmSignatureFieldDeletions: true),
                    fanOutLinesByType.GetValueOrDefault(typeId) is { Count: > 0 } fanOutLines
                        ? string.Join("; ", fanOutLines)
                        : null,
                    cancellationToken);
                if (consumerSave.IsError)
                {
                    DetachPendingVersions(appendedByComponent.Values);

                    return consumerSave.Errors;
                }

                workflowSaves.Add(BuildWorkflowSaveResult(
                    typeId,
                    consumerSave.Value,
                    graph,
                    state.ReconcilesByType.GetValueOrDefault(typeId) ?? [],
                    state.AllOrphans));
            }

            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Applied {DecisionCount} reconcile decisions on template {WorkflowTypeId}: {AppendCount} component appends, {WorkflowCount} workflows saved",
                resolved.Count,
                LogSanitizer.ForLog(request.WorkflowTypeId),
                appendedByComponent.Count,
                workflowSaves.Count);

            return new SharedComponentReconcileApplyResult(
                request.WorkflowTypeId,
                BuildOutcomes(resolved, plans),
                workflowSaves,
                BuildComponentHeads(componentIds, componentsById, headsById, appendedByComponent));
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachPendingVersions(appendedByComponent.Values);
            _logger.LogWarning(
                ex,
                "Concurrent template save aborted the reconcile apply on {WorkflowTypeId}; nothing was applied",
                LogSanitizer.ForLog(request.WorkflowTypeId));

            return Error.Conflict(
                WorkflowSavePipeline.CONCURRENT_SAVE_CODE,
                "A consumer template was modified concurrently during the apply; nothing was applied. Retry the operation.");
        }
        catch (DbUpdateException ex) when (ex.Entries.Any(e => e.Entity is SharedComponentVersion))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachPendingVersions(appendedByComponent.Values);
            string racedComponentId = ex.Entries
                .Select(e => e.Entity)
                .OfType<SharedComponentVersion>()
                .First()
                .SharedComponentId;
            _logger.LogWarning(ex, "Concurrent append detected for component {SharedComponentId}", racedComponentId);
            SharedComponentVersion? currentHead = await TryReadHeadAsync(racedComponentId, cancellationToken);

            return Error.Conflict(
                SharedComponentErrorCodes.CONCURRENT_APPEND,
                $"Another version of component '{racedComponentId}' was appended concurrently; re-read the head and retry.",
                currentHead is null ? null : BuildComponentHeadMetadata(currentHead));
        }
        catch
        {
            DetachPendingVersions(appendedByComponent.Values);

            throw;
        }
    }

    /// <summary>
    /// Lands an exported bundle onto the local deployment: upserts the categories the created
    /// components need, creates the bundle's New components (adopting their origin ids when free —
    /// tier-4 ids are free by construction), appends every take-theirs component onto its local
    /// chain (deduping against the head so an idempotent re-import no-ops) with a full fan-out to
    /// every local consumer, then lands the template under the caller's chosen arm — all in ONE
    /// transaction. Take-theirs template = the bundle's structure with each linked node's component
    /// id mapped through the correlation and its version hash restamped to the post-append local
    /// head (the link quartet is hash-suppressed, so the template ContentHash is unaffected);
    /// keep-mine = the local template with the appended components' restamps riding the fan-out.
    /// Concurrency guards mirror the append path: a racing consumer save rolls back to
    /// CONCURRENT_SAVE, a racing component append trips the unique index and rolls back BEFORE the
    /// recovery head re-read.
    /// </summary>
    public async Task<ErrorOr<ImportWorkflowTemplateApplyResult>> ImportApplyAsync(
        ImportWorkflowTemplateApplyCommand request,
        CancellationToken cancellationToken)
    {
        return await _coreDbContext.CreateExecutionStrategy().ExecuteAsync(
            ct => ImportApplyCoreAsync(request, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<ImportWorkflowTemplateApplyResult>> ImportApplyCoreAsync(
        ImportWorkflowTemplateApplyCommand request,
        CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();

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
        bool creatingFirstVersion = head is null;
        if (creatingFirstVersion && string.IsNullOrWhiteSpace(bundle.WorkflowTypeName))
        {
            return Error.Validation(
                SharedComponentErrorCodes.BUNDLE_TYPE_NAME_MISSING,
                $"The bundle does not carry a workflow type name, so it cannot create '{request.WorkflowTypeId}' "
                + "on this deployment. Re-export the bundle from the origin.");
        }

        bool typeRowExists = presence != WorkflowTypePresence.Missing;

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

        ErrorOr<Success> bundleShape = ValidateBundleClosure(bundle);
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

        ErrorOr<ImportApplyPlan> planResult = await BuildImportPlanAsync(request, correlations, cancellationToken);
        if (planResult.IsError)
        {
            return planResult.Errors;
        }

        ImportApplyPlan plan = planResult.Value;

        foreach (ImportComponentPlan landing in plan.Components
            .Where(c => c.Mode is ImportComponentMode.Create or ImportComponentMode.Append))
        {
            List<Error>? agentGuard = AgentGuardErrors(landing.Normalized!);
            if (agentGuard is not null)
            {
                return agentGuard;
            }
        }

        Error? importRefGuard = ValidateImportRefTargets(plan);
        if (importRefGuard is not null)
        {
            return importRefGuard.Value;
        }

        bool takeTheirsTemplate = creatingFirstVersion || request.TemplateArm == SharedComponentTransferArm.TakeTheirs;
        Workflow? targetGraph = takeTheirsTemplate
            ? bundleTemplate
            : await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(request.WorkflowTypeId);
        if (targetGraph is null)
        {
            return Error.NotFound(
                WORKFLOW_TEMPLATE_NOT_FOUND,
                $"Workflow template '{request.WorkflowTypeId}' was not found.");
        }

        Dictionary<string, List<SharedComponentVersion>> chainsByComponent = new(StringComparer.Ordinal);
        Dictionary<string, Section?> oldDefinitionsByHash = new(StringComparer.Ordinal);
        Dictionary<string, SharedComponentVersion> pendingVersions = new(StringComparer.Ordinal);
        List<SharedComponent> pendingComponents = [];
        List<object> pendingTypeRows = [];
        List<SharedComponentOverrideImpact> allOrphans = [];

        await using IDbContextTransaction transaction =
            await _coreDbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!typeRowExists)
            {
                var provisionedType = new WorkflowType(
                    request.WorkflowTypeId,
                    Truncate(bundle.WorkflowTypeName, WorkflowType.NAME_MAX_LENGTH))
                {
                    Description = bundle.WorkflowTypeDescription,
                };
                var provisionedAccess = new WorkflowTypeOrganizationAccess
                {
                    WorkflowTypeId = request.WorkflowTypeId,
                    OrganizationId = request.OrganizationId,
                };
                _ = _coreDbContext.Add(provisionedType);
                _ = _coreDbContext.Add(provisionedAccess);
                pendingTypeRows.Add(provisionedType);
                pendingTypeRows.Add(provisionedAccess);
            }

            HashSet<string> validCategoryIds = await UpsertMissingCategoriesAsync(bundle.Categories, plan, cancellationToken);

            foreach (ImportComponentPlan componentPlan in plan.Components.Where(c => c.Mode == ImportComponentMode.Create))
            {
                var component = new SharedComponent
                {
                    Id = componentPlan.LocalId,
                    Kind = (SharedComponentKind)(int)componentPlan.Bundle.Kind,
                    Name = Truncate(componentPlan.Bundle.Name, SharedComponent.NAME_MAX_LENGTH),
                    Description = componentPlan.Bundle.Description,
                    CategoryId = componentPlan.Bundle.CategoryId is { } categoryId && validCategoryIds.Contains(categoryId)
                        ? categoryId
                        : null,
                    State = SharedComponentState.Active,
                };
                var version = new SharedComponentVersion
                {
                    SharedComponentId = componentPlan.LocalId,
                    SequenceNumber = 1,
                    ContentHash = componentPlan.FinalContentHash,
                    CreatedByUserId = request.UserId,
                    ContentJson = componentPlan.FinalContentJson!,
                };
                _ = _coreDbContext.Add(component);
                _ = _coreDbContext.Add(version);
                pendingComponents.Add(component);
                pendingVersions[componentPlan.LocalId] = version;
            }

            var graphs = new Dictionary<string, Workflow>(StringComparer.Ordinal)
            {
                [request.WorkflowTypeId] = targetGraph,
            };
            Dictionary<string, List<string>> fanOutLinesByType = new(StringComparer.Ordinal);

            foreach (ImportComponentPlan componentPlan in plan.Components.Where(c => c.Mode == ImportComponentMode.Append))
            {
                List<SharedComponentConsumer> consumers = await GetConsumersAsync(componentPlan.LocalId, cancellationToken);
                ErrorOr<NormalizedDefinition?> oldDefinition = NormalizeStoredHead(componentPlan.LocalComponent!, componentPlan.LocalHead!);
                if (oldDefinition.IsError)
                {
                    DetachPendingImportEntities(pendingVersions.Values, pendingComponents, pendingTypeRows);

                    return oldDefinition.Errors;
                }

                SharedComponentUpdateImpact impact = await GetUpdateImpactAsync(
                    componentPlan.LocalComponent!,
                    oldDefinition.Value,
                    componentPlan.Normalized,
                    consumers,
                    cancellationToken,
                    includeAffectedBaselines: false);
                if (impact.OrphanedSignatureOverrides.Count > 0 && !request.AcknowledgeSignatureOrphans)
                {
                    DetachPendingImportEntities(pendingVersions.Values, pendingComponents, pendingTypeRows);

                    return OrphansError(componentPlan.LocalId, impact.OrphanedSignatureOverrides);
                }

                allOrphans.AddRange(impact.OrphanedSignatureOverrides);
                var version = new SharedComponentVersion
                {
                    SharedComponentId = componentPlan.LocalId,
                    SequenceNumber = componentPlan.LocalHead!.SequenceNumber + 1,
                    ContentHash = componentPlan.FinalContentHash,
                    ParentVersionId = componentPlan.LocalHead.Id,
                    CreatedByUserId = request.UserId,
                    ContentJson = componentPlan.FinalContentJson!,
                };
                _ = _coreDbContext.Add(version);
                pendingVersions[componentPlan.LocalId] = version;

                List<SharedComponentVersion> chain = await GetChainAsync(
                    componentPlan.LocalComponent!, chainsByComponent, cancellationToken);
                IReadOnlyDictionary<string, SharedComponentRefHead>? planRefHeads =
                    await SharedComponentRefHeads.LoadAsync(
                        _coreUnitOfWork, componentPlan.Normalized!.SectionDefinition, cancellationToken);

                List<string> touchedTypeIds = [.. consumers.Select(c => c.WorkflowTypeId)];
                if (!takeTheirsTemplate && !touchedTypeIds.Contains(request.WorkflowTypeId, StringComparer.Ordinal))
                {
                    touchedTypeIds.Add(request.WorkflowTypeId);
                }

                foreach (string typeId in touchedTypeIds)
                {
                    if (takeTheirsTemplate && string.Equals(typeId, request.WorkflowTypeId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Workflow? graph = await GetOrLoadGraphAsync(graphs, typeId, componentPlan.LocalId);
                    if (graph is null)
                    {
                        continue;
                    }

                    _ = GraftConsumer(
                        componentPlan.LocalComponent!,
                        graph,
                        componentPlan.Normalized!,
                        chain,
                        oldDefinitionsByHash,
                        planRefHeads,
                        StoredVersionHashes.None,
                        acknowledgeOverwrittenEntries: true);
                    if (!string.Equals(typeId, request.WorkflowTypeId, StringComparison.Ordinal))
                    {
                        AppendTo(
                            fanOutLinesByType,
                            typeId,
                            [$"Updated from shared component '{componentPlan.LocalComponent!.Name}' ({componentPlan.FinalContentHash[..8]})"]);
                    }
                }
            }

            if (takeTheirsTemplate)
            {
                RemapAndRestampTemplateLinks(targetGraph, plan.FinalHeadsByOriginId);
            }

            PruneOrphanedSignatureDefinitions(targetGraph, request.WorkflowTypeId, allOrphans);
            ErrorOr<SaveWorkflowResult> targetSave = await _savePipeline.SaveCoreAsync(
                new SaveWorkflowCommand(
                    request.OrganizationId,
                    request.UserId,
                    request.WorkflowTypeId,
                    targetGraph,
                    request.BaseVersionId,
                    EnforceOptimisticConcurrency: true,
                    ConfirmSignatureFieldDeletions: true),
                narrativeOverride: $"Imported from {bundle.OriginWorkflowTypeId}",
                cancellationToken);
            if (targetSave.IsError)
            {
                DetachPendingImportEntities(pendingVersions.Values, pendingComponents, pendingTypeRows);

                return targetSave.Errors;
            }

            foreach ((string typeId, Workflow graph) in graphs
                .Where(g => !string.Equals(g.Key, request.WorkflowTypeId, StringComparison.Ordinal))
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                PruneOrphanedSignatureDefinitions(graph, typeId, allOrphans);
                ErrorOr<SaveWorkflowResult> consumerSave = await _savePipeline.SaveCoreAsync(
                    new SaveWorkflowCommand(
                        request.OrganizationId,
                        request.UserId,
                        typeId,
                        graph,
                        ConfirmSignatureFieldDeletions: true),
                    fanOutLinesByType.GetValueOrDefault(typeId) is { Count: > 0 } fanOutLines
                        ? string.Join("; ", fanOutLines)
                        : null,
                    cancellationToken);
                if (consumerSave.IsError)
                {
                    DetachPendingImportEntities(pendingVersions.Values, pendingComponents, pendingTypeRows);

                    return consumerSave.Errors;
                }
            }

            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Imported workflow type {WorkflowTypeId} from {OriginWorkflowTypeId}: {CreateCount} components created, {AppendCount} appended, template arm {TemplateArm}",
                LogSanitizer.ForLog(request.WorkflowTypeId),
                LogSanitizer.ForLog(bundle.OriginWorkflowTypeId),
                plan.Components.Count(c => c.Mode == ImportComponentMode.Create),
                plan.Components.Count(c => c.Mode == ImportComponentMode.Append),
                request.TemplateArm);

            return BuildImportApplyResult(request, plan, targetSave.Value, pendingVersions);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachPendingImportEntities(pendingVersions.Values, pendingComponents, pendingTypeRows);
            _logger.LogWarning(
                ex,
                "Concurrent template save aborted the import of {WorkflowTypeId}; nothing was applied",
                LogSanitizer.ForLog(request.WorkflowTypeId));

            return Error.Conflict(
                WorkflowSavePipeline.CONCURRENT_SAVE_CODE,
                "A consumer template was modified concurrently during the import; nothing was applied. Retry the operation.");
        }
        catch (DbUpdateException ex) when (ex.Entries.Any(e => e.Entity is SharedComponentVersion))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachPendingImportEntities(pendingVersions.Values, pendingComponents, pendingTypeRows);
            string racedComponentId = ex.Entries
                .Select(e => e.Entity)
                .OfType<SharedComponentVersion>()
                .First()
                .SharedComponentId;
            _logger.LogWarning(ex, "Concurrent append detected for component {SharedComponentId} during import", racedComponentId);
            SharedComponentVersion? currentHead = await TryReadHeadAsync(racedComponentId, cancellationToken);

            return Error.Conflict(
                SharedComponentErrorCodes.CONCURRENT_APPEND,
                $"Another version of component '{racedComponentId}' was appended concurrently; re-read the head and retry.",
                currentHead is null ? null : BuildComponentHeadMetadata(currentHead));
        }
        catch
        {
            DetachPendingImportEntities(pendingVersions.Values, pendingComponents, pendingTypeRows);

            throw;
        }
    }

    private async Task<ErrorOr<ImportApplyPlan>> BuildImportPlanAsync(
        ImportWorkflowTemplateApplyCommand request,
        IReadOnlyDictionary<string, SharedComponentCorrelator.Correlation> correlations,
        CancellationToken cancellationToken)
    {
        WorkflowTransferBundle bundle = request.Bundle;
        Dictionary<string, ImportComponentDecision> decisionsByComponent = new(StringComparer.Ordinal);
        foreach (ImportComponentDecision decision in request.ComponentDecisions)
        {
            decisionsByComponent[decision.ComponentId] = decision;
        }

        List<string> correlatedIds = [.. correlations.Values
            .Select(correlation => correlation.LocalComponentId)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)];
        Dictionary<string, SharedComponent> localComponentsById =
            await SharedComponentProjections.GetComponentsByIdAsync(_coreUnitOfWork, correlatedIds, cancellationToken);
        Dictionary<string, SharedComponentVersion> localHeadsById =
            await SharedComponentProjections.GetHeadVersionRowsAsync(_coreUnitOfWork, correlatedIds, cancellationToken);
        Dictionary<string, List<string>> localTrailsById =
            await SharedComponentProjections.GetTrailsByComponentAsync(_coreUnitOfWork, correlatedIds, cancellationToken);

        bool IsLiveLocal(string originId)
        {
            return correlations[originId].LocalComponentId is { } localId
                && localComponentsById.ContainsKey(localId)
                && localHeadsById.ContainsKey(localId);
        }

        List<string> createBundleIds = [.. bundle.Components
            .Where(component => !IsLiveLocal(component.ComponentId))
            .Select(component => component.ComponentId)];
        HashSet<string> takenIds = await LoadTakenComponentIdsAsync(createBundleIds, cancellationToken);

        var localIdByOrigin = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TransferComponentDto component in bundle.Components)
        {
            localIdByOrigin[component.ComponentId] = IsLiveLocal(component.ComponentId)
                ? correlations[component.ComponentId].LocalComponentId!
                : takenIds.Contains(component.ComponentId)
                    ? Guid.NewGuid().ToString()
                    : component.ComponentId;
        }

        var augmentedCorrelations = new Dictionary<string, SharedComponentCorrelator.Correlation>(StringComparer.Ordinal);
        foreach ((string originId, SharedComponentCorrelator.Correlation correlation) in correlations)
        {
            augmentedCorrelations[originId] = correlation with { LocalComponentId = localIdByOrigin[originId] };
        }

        var componentPlans = new List<ImportComponentPlan>();
        foreach (TransferComponentDto component in bundle.Components)
        {
            string localId = localIdByOrigin[component.ComponentId];
            if (!IsLiveLocal(component.ComponentId))
            {
                ErrorOr<ImportComponentPlan> created = BuildCreatePlan(component, localId, augmentedCorrelations);
                if (created.IsError)
                {
                    return created.Errors;
                }

                componentPlans.Add(created.Value);
                continue;
            }

            SharedComponent localComponent = localComponentsById[localId];
            SharedComponentVersion localHead = localHeadsById[localId];
            SharedComponentTransferArm arm = decisionsByComponent.TryGetValue(component.ComponentId, out ImportComponentDecision? decision)
                ? decision.Arm
                : SharedComponentTransferArm.KeepMine;

            if (arm == SharedComponentTransferArm.KeepMine)
            {
                componentPlans.Add(ImportComponentPlan.Keep(component, localComponent, localHead));
                continue;
            }

            if (localComponent.State == SharedComponentState.Retired)
            {
                return Error.Conflict(
                    SharedComponentErrorCodes.RETIRED,
                    $"Local component '{localComponent.Id}' is retired; unretire it before importing over it, or keep-mine it.");
            }

            string? expectedHeadVersionId = decisionsByComponent.GetValueOrDefault(component.ComponentId)?.ExpectedLocalHeadVersionId;
            if (!string.Equals(expectedHeadVersionId, localHead.Id, StringComparison.Ordinal))
            {
                return Error.Conflict(
                    SharedComponentErrorCodes.HEAD_MOVED,
                    $"The local head of component '{localComponent.Id}' is '{localHead.Id}'; the supplied "
                    + "expectedLocalHeadVersionId is stale or missing. Re-preview the import and retry.",
                    BuildComponentHeadMetadata(localHead));
            }

            List<string> localTrail = localTrailsById.GetValueOrDefault(localComponent.Id) ?? [];
            var localTrailSet = new HashSet<string>(localTrail, StringComparer.Ordinal);
            if (!component.HashTrail.Any(localTrailSet.Contains) && !request.AcknowledgeUnrelatedHistory)
            {
                return Error.Conflict(
                    SharedComponentErrorCodes.DIVERGED_LINEAGE,
                    $"Component '{localComponent.Id}' shares no version history with the bundle's component; "
                    + "take-theirs would append unrelated content. Re-send with acknowledgeUnrelatedHistory=true to proceed.",
                    new Dictionary<string, object>
                    {
                        ["sharedComponentId"] = localComponent.Id,
                        ["bundleHashTrail"] = component.HashTrail,
                        ["localHashTrail"] = localTrail,
                    });
            }

            string localizedJson = SharedComponentTransferCompare.LocalizeHeadDefinition(component, augmentedCorrelations);
            ErrorOr<NormalizedDefinition> normalized = NormalizeDefinition(localComponent, localizedJson);
            if (normalized.IsError)
            {
                return normalized.Errors;
            }

            if (MatchesHead(localHead, normalized.Value))
            {
                componentPlans.Add(ImportComponentPlan.Dedup(component, localComponent, localHead));
                continue;
            }

            componentPlans.Add(ImportComponentPlan.Append(component, localComponent, localHead, normalized.Value));
        }

        return new ImportApplyPlan(
            componentPlans,
            componentPlans.ToDictionary(
                plan => plan.Bundle.ComponentId,
                plan => (plan.LocalId, plan.FinalContentHash),
                StringComparer.Ordinal));
    }

    private ErrorOr<ImportComponentPlan> BuildCreatePlan(
        TransferComponentDto component,
        string localId,
        IReadOnlyDictionary<string, SharedComponentCorrelator.Correlation> augmentedCorrelations)
    {
        string localizedJson = SharedComponentTransferCompare.LocalizeHeadDefinition(component, augmentedCorrelations);
        var staged = new SharedComponent
        {
            Id = localId,
            Kind = (SharedComponentKind)(int)component.Kind,
            Name = component.Name,
        };

        if (string.Equals(localizedJson, component.HeadDefinitionJson, StringComparison.Ordinal))
        {
            ErrorOr<NormalizedDefinition> passThrough = NormalizeDefinition(staged, component.HeadDefinitionJson);
            if (passThrough.IsError)
            {
                return passThrough.Errors;
            }

            return ImportComponentPlan.Create(
                component, localId, passThrough.Value, component.HeadDefinitionJson, component.HeadContentHash);
        }

        ErrorOr<NormalizedDefinition> normalized = NormalizeDefinition(staged, localizedJson);
        if (normalized.IsError)
        {
            return normalized.Errors;
        }

        return ImportComponentPlan.Create(
            component, localId, normalized.Value, normalized.Value.DefinitionJson, normalized.Value.ContentHash);
    }

    private async Task<HashSet<string>> UpsertMissingCategoriesAsync(
        List<TransferCategoryDto> categories,
        ImportApplyPlan plan,
        CancellationToken cancellationToken)
    {
        HashSet<string> neededCategoryIds = [.. plan.Components
            .Where(component => component.Mode == ImportComponentMode.Create && component.Bundle.CategoryId is not null)
            .Select(component => component.Bundle.CategoryId!)];
        if (neededCategoryIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        List<string> visibleLocalIds = await _coreUnitOfWork.Repository<SharedComponentCategory>()
            .GetAll()
            .AsNoTracking()
            .Where(category => neededCategoryIds.Contains(category.Id))
            .Select(category => category.Id)
            .ToListAsync(cancellationToken);
        var validCategoryIds = new HashSet<string>(visibleLocalIds, StringComparer.Ordinal);

        Dictionary<string, TransferCategoryDto> bundleCategoriesById = categories
            .GroupBy(category => category.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (string categoryId in neededCategoryIds)
        {
            if (validCategoryIds.Contains(categoryId)
                || !bundleCategoriesById.TryGetValue(categoryId, out TransferCategoryDto? bundleCategory))
            {
                continue;
            }

            _ = await SharedComponentCategoryGuard.CreateOrReviveAsync(
                _coreUnitOfWork,
                categoryId,
                Truncate(bundleCategory.Name, SharedComponentCategory.NAME_MAX_LENGTH),
                bundleCategory.Description,
                displayOrder: null,
                overwriteVisible: false,
                cancellationToken);
            _ = validCategoryIds.Add(categoryId);
        }

        return validCategoryIds;
    }

    private static void RemapAndRestampTemplateLinks(
        Workflow template,
        IReadOnlyDictionary<string, (string LocalId, string HeadContentHash)> finalHeadsByOriginId)
    {
        foreach (Section section in template.Sections)
        {
            RestampLinkedNode(section, finalHeadsByOriginId);
        }

        foreach (Block block in template.GetAllBlocks())
        {
            RestampLinkedNode(block, finalHeadsByOriginId);
        }
    }

    private static void RestampLinkedNode(
        ISharedComponentLinkable node,
        IReadOnlyDictionary<string, (string LocalId, string HeadContentHash)> finalHeadsByOriginId)
    {
        if (!node.IsActivelyLinked())
        {
            return;
        }

        if (node.SharedComponentId is { } originId
            && finalHeadsByOriginId.TryGetValue(originId, out (string LocalId, string HeadContentHash) final))
        {
            node.SharedComponentId = final.LocalId;
            node.SharedComponentVersionHash = final.HeadContentHash;
        }
    }

    internal ErrorOr<Success> ValidateBundleClosure(WorkflowTransferBundle bundle)
    {
        var bundleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TransferComponentDto component in bundle.Components)
        {
            if (!bundleIds.Add(component.ComponentId))
            {
                return Error.Validation(
                    SharedComponentErrorCodes.CORRUPT_DEFINITION,
                    $"The bundle carries component id '{component.ComponentId}' more than once; component ids must be unique.");
            }
        }

        foreach (TransferComponentDto component in bundle.Components)
        {
            var staged = new SharedComponent
            {
                Id = component.ComponentId,
                Kind = (SharedComponentKind)(int)component.Kind,
                Name = component.Name,
            };
            ErrorOr<NormalizedDefinition> normalized = NormalizeDefinition(staged, component.HeadDefinitionJson);
            if (normalized.IsError)
            {
                return normalized.Errors;
            }

            if (component.Kind != WireKind.Section)
            {
                continue;
            }

            Section? section = Section.FromJson(component.HeadDefinitionJson);
            if (section is null)
            {
                return Error.Validation(
                    SharedComponentErrorCodes.CORRUPT_DEFINITION,
                    $"Bundle section component '{component.ComponentId}' could not be parsed.");
            }

            foreach (string refTargetId in SharedComponentRefHeads.CollectRefTargetIds(section))
            {
                if (!bundleIds.Contains(refTargetId))
                {
                    return Error.Validation(
                        SharedComponentErrorCodes.CORRUPT_DEFINITION,
                        $"Bundle section component '{component.ComponentId}' references component "
                        + $"'{refTargetId}', which the bundle does not carry. The bundle is incomplete.");
                }
            }
        }

        return Result.Success;
    }

    private static Error? ValidateImportRefTargets(ImportApplyPlan plan)
    {
        Dictionary<string, ImportComponentPlan> plansByLocalId = new(StringComparer.Ordinal);
        foreach (ImportComponentPlan componentPlan in plan.Components)
        {
            _ = plansByLocalId.TryAdd(componentPlan.LocalId, componentPlan);
        }

        foreach (ImportComponentPlan componentPlan in plan.Components)
        {
            bool landsContent = componentPlan.Mode is ImportComponentMode.Create or ImportComponentMode.Append;
            if (!landsContent || (SharedComponentKind)(int)componentPlan.Bundle.Kind != SharedComponentKind.Section)
            {
                continue;
            }

            Section? landed = componentPlan.Normalized?.SectionDefinition;
            if (landed is null)
            {
                return Error.Validation(
                    SharedComponentErrorCodes.CORRUPT_DEFINITION,
                    $"Section component '{componentPlan.Bundle.ComponentId}' could not be parsed to validate its entry references.");
            }

            foreach (string refTargetId in SharedComponentRefHeads.CollectRefTargetIds(landed))
            {
                Error? refError = ValidateImportRefTarget(componentPlan.Bundle.ComponentId, refTargetId, plansByLocalId);
                if (refError is not null)
                {
                    return refError;
                }
            }
        }

        return null;
    }

    private static Error? ValidateImportRefTarget(
        string sectionComponentId,
        string refTargetId,
        IReadOnlyDictionary<string, ImportComponentPlan> plansByLocalId)
    {
        ImportComponentPlan? target = plansByLocalId.GetValueOrDefault(refTargetId);
        SharedComponentKind? targetKind = target is null
            ? null
            : target.LocalComponent?.Kind ?? (SharedComponentKind)(int)target.Bundle.Kind;
        SharedComponentState? targetState = target is null
            ? null
            : target.Mode == ImportComponentMode.Create
                ? SharedComponentState.Active
                : target.LocalComponent!.State;
        bool headPayloadAvailable = target is not null
            && (target.Mode != ImportComponentMode.Keep || target.LocalHead!.PayloadAbsentAt is null);

        string? violation = SharedComponentRefHeads.ClassifyRefTarget(
            target is not null, targetKind, targetState, headPayloadAvailable);

        return violation switch
        {
            null => null,
            SharedComponentErrorCodes.UNKNOWN_COMPONENT => Error.Validation(
                violation,
                $"Section component '{sectionComponentId}' references component '{refTargetId}', "
                + "which this import neither carries nor matches to a local component."),
            SharedComponentErrorCodes.KIND_MISMATCH => Error.Validation(
                violation,
                $"Section component '{sectionComponentId}' references component '{refTargetId}', which is "
                + $"a {targetKind} component; section entries can only reference Block components."),
            SharedComponentErrorCodes.RETIRED => Error.Validation(
                violation,
                $"Section component '{sectionComponentId}' references component '{refTargetId}', which is "
                + "retired locally; unretire it before importing content that references it."),
            _ => Error.Validation(
                violation,
                $"Section component '{sectionComponentId}' references component '{refTargetId}', whose local head "
                + "version has no stored payload; take the bundle's version of it or restore its payload first."),
        };
    }

    private async Task<HashSet<string>> LoadTakenComponentIdsAsync(
        List<string> candidateIds,
        CancellationToken cancellationToken)
    {
        if (candidateIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        List<string> taken = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(component => candidateIds.Contains(component.Id))
            .Select(component => component.Id)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(taken, StringComparer.Ordinal);
    }

    private static ImportWorkflowTemplateApplyResult BuildImportApplyResult(
        ImportWorkflowTemplateApplyCommand request,
        ImportApplyPlan plan,
        SaveWorkflowResult templateSave,
        IReadOnlyDictionary<string, SharedComponentVersion> pendingVersions)
    {
        List<ImportComponentOutcome> outcomes = [.. plan.Components.Select(componentPlan =>
        {
            SharedComponentTransferComponentAction action = componentPlan.Mode switch
            {
                ImportComponentMode.Create => SharedComponentTransferComponentAction.Created,
                ImportComponentMode.Append => SharedComponentTransferComponentAction.Appended,
                ImportComponentMode.Dedup => SharedComponentTransferComponentAction.Deduped,
                _ => SharedComponentTransferComponentAction.Kept,
            };
            string? headVersionId = pendingVersions.TryGetValue(componentPlan.LocalId, out SharedComponentVersion? pending)
                ? pending.Id
                : componentPlan.LocalHead?.Id;

            return new ImportComponentOutcome(
                componentPlan.Bundle.ComponentId,
                componentPlan.LocalId,
                action,
                headVersionId,
                componentPlan.FinalContentHash);
        })];

        return new ImportWorkflowTemplateApplyResult(
            request.WorkflowTypeId,
            new ImportTemplateMutation(templateSave.VersionId, templateSave.ContentHash, templateSave.WasDeduped),
            outcomes,
            SKIPPED_CARRIERS);
    }

    private enum ImportComponentMode
    {
        Create,
        Append,
        Dedup,
        Keep,
    }

    private sealed record ImportComponentPlan(
        TransferComponentDto Bundle,
        string LocalId,
        ImportComponentMode Mode,
        SharedComponent? LocalComponent,
        SharedComponentVersion? LocalHead,
        NormalizedDefinition? Normalized,
        string? FinalContentJson,
        string FinalContentHash)
    {
        public static ImportComponentPlan Create(
            TransferComponentDto bundle,
            string localId,
            NormalizedDefinition normalized,
            string finalContentJson,
            string finalContentHash)
        {
            return new ImportComponentPlan(
                bundle, localId, ImportComponentMode.Create, null, null, normalized, finalContentJson, finalContentHash);
        }

        public static ImportComponentPlan Append(
            TransferComponentDto bundle,
            SharedComponent localComponent,
            SharedComponentVersion localHead,
            NormalizedDefinition normalized)
        {
            return new ImportComponentPlan(
                bundle, localComponent.Id, ImportComponentMode.Append, localComponent, localHead, normalized,
                normalized.DefinitionJson, normalized.ContentHash);
        }

        public static ImportComponentPlan Dedup(
            TransferComponentDto bundle,
            SharedComponent localComponent,
            SharedComponentVersion localHead)
        {
            return new ImportComponentPlan(
                bundle, localComponent.Id, ImportComponentMode.Dedup, localComponent, localHead, null,
                null, localHead.ContentHash);
        }

        public static ImportComponentPlan Keep(
            TransferComponentDto bundle,
            SharedComponent localComponent,
            SharedComponentVersion localHead)
        {
            return new ImportComponentPlan(
                bundle, localComponent.Id, ImportComponentMode.Keep, localComponent, localHead, null,
                null, localHead.ContentHash);
        }
    }

    private sealed record ImportApplyPlan(
        IReadOnlyList<ImportComponentPlan> Components,
        IReadOnlyDictionary<string, (string LocalId, string HeadContentHash)> FinalHeadsByOriginId);

    internal static readonly IReadOnlyList<string> SKIPPED_CARRIERS =
    [
        "Organization access",
        "Signature field values",
        CounterpartyTerms.SINGULAR + " maps",
        "Execution history and regression baselines",
    ];

    private ErrorOr<List<ResolvedDecision>> ResolveDecisions(
        Workflow anchor,
        IReadOnlyList<SharedComponentReconcileDecision> decisions)
    {
        List<ResolvedDecision> resolved = [];
        Dictionary<ISharedComponentLinkable, SharedComponentReconcileAction> wholeNodeActions =
            new(ReferenceEqualityComparer.Instance);
        Dictionary<ISharedComponentLinkable, HashSet<string>> entryClaimsByNode =
            new(ReferenceEqualityComparer.Instance);
        foreach (SharedComponentReconcileDecision decision in decisions)
        {
            ErrorOr<SharedComponentTarget> target = _sharedComponentEditor.ResolveTarget(
                anchor, decision.TargetPath, decision.TargetStableId);
            if (target.IsError)
            {
                return target.Errors;
            }

            ISharedComponentLinkable node = target.Value.Kind == SharedComponentKind.Block
                ? target.Value.Block!
                : target.Value.Section!;
            if (string.IsNullOrEmpty(node.SharedComponentId) || node.SharedComponentLinkState is null)
            {
                return Error.Validation(
                    DECISIONS_ERROR_KEY,
                    $"The node at {target.Value.CanonicalPath} carries no shared-component link. "
                    + $"[{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
            }

            if (!string.Equals(node.SharedComponentId, decision.SharedComponentId, StringComparison.Ordinal))
            {
                return Error.Validation(
                    DECISIONS_ERROR_KEY,
                    $"The node at {target.Value.CanonicalPath} is a copy of component '{node.SharedComponentId}', "
                    + $"not '{decision.SharedComponentId}'. [{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
            }

            Error? entryGuard = TryResolveEntryLocator(decision, target.Value, node, out ResolvedEntry? entry);
            if (entryGuard is not null)
            {
                return entryGuard.Value;
            }

            if (entry is null)
            {
                if (!wholeNodeActions.TryAdd(node, decision.Action))
                {
                    return ConflictingDecisions($"Two decisions address the node at {target.Value.CanonicalPath}.");
                }
            }
            else
            {
                if (!entryClaimsByNode.TryGetValue(node, out HashSet<string>? coordinates))
                {
                    coordinates = new HashSet<string>(StringComparer.Ordinal);
                    entryClaimsByNode[node] = coordinates;
                }

                if (!coordinates.Add(entry.Coordinate))
                {
                    return ConflictingDecisions(
                        $"Two decisions address the entry '{entry.Coordinate}' of the section at "
                        + $"{target.Value.CanonicalPath}.");
                }
            }

            resolved.Add(new ResolvedDecision(decision, target.Value, node, entry));
        }

        foreach (ResolvedDecision item in resolved)
        {
            if (item.Entry is null
                && item.Decision.Action != SharedComponentReconcileAction.KeepLocal
                && entryClaimsByNode.ContainsKey(item.Node))
            {
                return ConflictingDecisions(
                    $"The section at {item.Target.CanonicalPath} has both a whole-section {item.Decision.Action} "
                    + "decision and entry-scoped decisions; decide the section as a whole or per entry, not both.");
            }
        }

        return resolved;
    }

    private static IEnumerable<string> CollectAuxiliaryComponentIds(List<ResolvedDecision> resolved)
    {
        foreach (ResolvedDecision item in resolved)
        {
            if (item.Entry?.LinkedInnerComponentId is { } refId)
            {
                yield return refId;
            }
            else if (item.Entry is null
                && item.Decision.Action == SharedComponentReconcileAction.UpdateShared
                && item.Target.Kind == SharedComponentKind.Section)
            {
                foreach (Block inner in item.Target.Section!.Blocks
                    .Where(b => b is { SharedComponentLinkState: SharedComponentLinkState.Linked, SharedComponentId.Length: > 0 }))
                {
                    yield return inner.SharedComponentId!;
                }
            }
        }
    }

    private static Error? TryResolveEntryLocator(
        SharedComponentReconcileDecision decision,
        SharedComponentTarget target,
        ISharedComponentLinkable node,
        out ResolvedEntry? entry)
    {
        entry = null;
        bool hasKey = !string.IsNullOrEmpty(decision.EntryKey);
        bool hasStableId = !string.IsNullOrEmpty(decision.EntryStableId);
        if (hasKey && hasStableId)
        {
            return EntryLocatorError(
                $"The decision at {target.CanonicalPath} carries both entryKey and entryStableId; supply exactly one.");
        }

        if (!hasKey && !hasStableId)
        {
            return decision.Action is SharedComponentReconcileAction.Hide or SharedComponentReconcileAction.Unhide
                ? EntryLocatorError(
                    $"{decision.Action} is entry-scoped; supply entryKey or entryStableId for the section copy at "
                    + $"{target.CanonicalPath}.")
                : null;
        }

        if (decision.Action == SharedComponentReconcileAction.Relink)
        {
            return EntryLocatorError(
                $"Relink is never entry-scoped; adopt the head for the entry at {target.CanonicalPath} instead.");
        }

        if (target.Kind == SharedComponentKind.Block)
        {
            return EntryLocatorError(
                $"The node at {target.CanonicalPath} is a block copy; entry locators address section copies only.");
        }

        if (node.SharedComponentLinkState != SharedComponentLinkState.Linked)
        {
            return EntryLocatorError(
                $"The section copy at {target.CanonicalPath} is broken out and carries no entry surface.");
        }

        Section section = target.Section!;
        if (hasStableId)
        {
            Block? live = section.Blocks.FirstOrDefault(b =>
                string.Equals(b.StableBlockId, decision.EntryStableId, StringComparison.Ordinal));
            if (live is null)
            {
                return Error.NotFound(
                    SharedComponentErrorCodes.TARGET_NOT_FOUND,
                    $"No block with stable id '{decision.EntryStableId}' exists in the section copy at "
                    + $"{target.CanonicalPath}.");
            }

            entry = new ResolvedEntry(SharedComponentReconciler.SlotFor(section, live), live);

            return null;
        }

        string liveTag = section.SharedComponentBindings?.GetValueOrDefault(decision.EntryKey!) ?? decision.EntryKey!;
        entry = new ResolvedEntry(
            decision.EntryKey!,
            section.Blocks.FirstOrDefault(b => string.Equals(b.ReplacementTag, liveTag, StringComparison.Ordinal)));

        return null;
    }

    private static Error EntryLocatorError(string reason)
    {
        return Error.Validation(
            DECISIONS_ERROR_KEY,
            $"{reason} [{SharedComponentErrorCodes.INVALID_TARGET_LOCATOR}]");
    }

    private static Error? ValidateDecisionArms(
        List<ResolvedDecision> resolved,
        Dictionary<string, SharedComponent> componentsById,
        Dictionary<string, SharedComponentVersion?> headsById)
    {
        foreach (ResolvedDecision item in resolved)
        {
            if (!NeedsLiveHead(item.Decision.Action))
            {
                continue;
            }

            string componentId = item.Decision.SharedComponentId;
            SharedComponent? component = componentsById.GetValueOrDefault(componentId);
            if (component is null)
            {
                return SharedComponentErrorCodes.UnknownComponent(componentId);
            }

            if (component.State != SharedComponentState.Active)
            {
                return Error.Conflict(
                    SharedComponentErrorCodes.RETIRED,
                    $"Component '{componentId}' is retired; only KeepLocal and BreakOut apply to its copies.");
            }

            SharedComponentVersion? head = headsById.GetValueOrDefault(componentId);
            if (head is null)
            {
                return Error.Conflict(
                    SharedComponentErrorCodes.NO_VERSIONS,
                    $"Component '{componentId}' has no versions at all; its chain is corrupt.");
            }

            if (!string.Equals(item.Decision.ExpectedHeadVersionId, head.Id, StringComparison.Ordinal))
            {
                return Error.Conflict(
                    SharedComponentErrorCodes.HEAD_MOVED,
                    $"The component head version is '{head.Id}'; the supplied expectedHeadVersionId is stale or missing. "
                    + "Re-read the reconcile state and re-apply.",
                    BuildComponentHeadMetadata(head));
            }

            if (item.Decision.Action == SharedComponentReconcileAction.AdoptHead && head.PayloadAbsentAt is not null)
            {
                return Error.Conflict(
                    SharedComponentErrorCodes.PAYLOAD_ABSENT,
                    $"The head of component '{componentId}' is a hash-only lineage row with no stored content to adopt.");
            }
        }

        return null;
    }

    private ErrorOr<List<ComponentUpdatePlan>> BuildUpdatePlans(
        List<ResolvedDecision> resolved,
        Dictionary<string, SharedComponent> componentsById,
        Dictionary<string, SharedComponentVersion?> headsById,
        StoredVersionHashes storedHashes)
    {
        Error? entryArmGuard = ValidateEntryPushArms(resolved);
        if (entryArmGuard is not null)
        {
            return entryArmGuard.Value;
        }

        ErrorOr<List<ComponentPushUnit>> unitsResult = CollectPushUnits(resolved, componentsById, headsById, storedHashes);
        if (unitsResult.IsError)
        {
            return unitsResult.Errors;
        }

        List<ComponentUpdatePlan> plans = [];
        foreach (IGrouping<string, ComponentPushUnit> group in unitsResult.Value
            .GroupBy(u => u.ComponentId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (resolved.Any(r => r.Decision.Action == SharedComponentReconcileAction.KeepLocal
                && r.Entry is null
                && string.Equals(r.Decision.SharedComponentId, group.Key, StringComparison.Ordinal)))
            {
                return ConflictingDecisions(
                    $"Component '{group.Key}' has both an UpdateShared and a KeepLocal decision; a pushed head "
                    + "would immediately rewrite the kept-local copy.");
            }

            if (group.Select(u => u.Normalized.DefinitionJson).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                return ConflictingDecisions(
                    $"Component '{group.Key}' has multiple UpdateShared decisions with different instance content; "
                    + "only one definition can become the head.");
            }

            ComponentPushUnit unit = group.First();
            SharedComponent component = componentsById[group.Key];
            SharedComponentVersion head = headsById[group.Key]!;
            foreach (ComponentPushUnit refUnit in group.Where(u => u.IsRefPush))
            {
                Error? tokenGuard = CheckRefPushToken(refUnit, head);
                if (tokenGuard is not null)
                {
                    return tokenGuard.Value;
                }
            }

            plans.Add(new ComponentUpdatePlan(
                component,
                head,
                unit.Normalized,
                group.Any(u => u.AcknowledgeSignatureOrphans),
                group.Any(u => u.AcknowledgeOverwrittenEntries),
                MatchesHead(head, unit.Normalized)));
        }

        return plans;
    }

    private static Error? ValidateEntryPushArms(List<ResolvedDecision> resolved)
    {
        foreach (ResolvedDecision item in resolved
            .Where(r => r.Entry is not null && r.Decision.Action == SharedComponentReconcileAction.UpdateShared))
        {
            string slot = item.Entry!.Coordinate;
            SharedComponentSlotState? state = item.Target.Section!.SharedComponentSlotStates?.GetValueOrDefault(slot);
            if (state == SharedComponentSlotState.Hidden)
            {
                return EntryLocatorError(
                    $"The entry '{slot}' at {item.Target.CanonicalPath} is hidden; unhide it before sharing its content.");
            }

            if (state == SharedComponentSlotState.BrokenOut
                || item.Entry.LiveBlock is { SharedComponentLinkState: SharedComponentLinkState.BrokenOut })
            {
                return EntryLocatorError(
                    $"The entry '{slot}' at {item.Target.CanonicalPath} is broken out; adopt the head before sharing it.");
            }

            if (item.Entry.LiveBlock is null)
            {
                return EntryLocatorError(
                    $"The entry '{slot}' at {item.Target.CanonicalPath} has no live content to share.");
            }
        }

        return null;
    }

    private ErrorOr<List<ComponentPushUnit>> CollectPushUnits(
        List<ResolvedDecision> resolved,
        Dictionary<string, SharedComponent> componentsById,
        Dictionary<string, SharedComponentVersion?> headsById,
        StoredVersionHashes storedHashes)
    {
        List<ComponentPushUnit> units = [];
        foreach (ResolvedDecision item in resolved
            .Where(r => r.Decision.Action == SharedComponentReconcileAction.UpdateShared
                && (r.Entry is null || r.Entry.LinkedInnerComponentId is not null)))
        {
            if (item.Entry is null)
            {
                Error? refConflict = CheckWholeSectionRefEntries(item, componentsById, headsById, storedHashes);
                if (refConflict is not null)
                {
                    return refConflict.Value;
                }

                SharedComponent component = componentsById[item.Decision.SharedComponentId];
                string derivedJson = item.Target.Kind == SharedComponentKind.Block
                    ? _normalizer.ToDefinitionJson(item.Target.Block!)
                    : _normalizer.ToDefinitionJson(item.Target.Section!, item.Target.Section!.SharedComponentBindings);
                ErrorOr<NormalizedDefinition> normalized = NormalizeDefinition(component, derivedJson);
                if (normalized.IsError)
                {
                    return normalized.Errors;
                }

                units.Add(new ComponentPushUnit(
                    item.Decision.SharedComponentId,
                    normalized.Value,
                    item.Decision.AcknowledgeSignatureOrphans,
                    item.Decision.AcknowledgeOverwrittenEntries,
                    RefToken: null,
                    IsRefPush: false));

                continue;
            }

            Block inner = item.Entry.LiveBlock!;
            string refComponentId = item.Entry.LinkedInnerComponentId!;
            SharedComponent refComponent = componentsById[refComponentId];
            string refDerivedJson = _normalizer.ToDefinitionJson(inner, inner.SharedComponentBindings);
            ErrorOr<NormalizedDefinition> refNormalized = NormalizeDefinition(refComponent, refDerivedJson);
            if (refNormalized.IsError)
            {
                return refNormalized.Errors;
            }

            units.Add(new ComponentPushUnit(
                refComponentId,
                refNormalized.Value,
                item.Decision.AcknowledgeSignatureOrphans,
                item.Decision.AcknowledgeOverwrittenEntries,
                item.Decision.ExpectedRefHeadVersionId,
                IsRefPush: true));
        }

        Dictionary<ISharedComponentLinkable, List<ResolvedDecision>> inlineEntryPushesByNode =
            new(ReferenceEqualityComparer.Instance);
        foreach (ResolvedDecision item in resolved
            .Where(r => r.Decision.Action == SharedComponentReconcileAction.UpdateShared
                && r.Entry is not null
                && r.Entry.LinkedInnerComponentId is null))
        {
            if (!inlineEntryPushesByNode.TryGetValue(item.Node, out List<ResolvedDecision>? pushes))
            {
                pushes = [];
                inlineEntryPushesByNode[item.Node] = pushes;
            }

            pushes.Add(item);
        }

        foreach (List<ResolvedDecision> sectionGroup in inlineEntryPushesByNode.Values)
        {
            ResolvedDecision first = sectionGroup[0];
            string sectionComponentId = first.Decision.SharedComponentId;
            SharedComponent component = componentsById[sectionComponentId];
            ErrorOr<NormalizedDefinition> merged = BuildMergedEntryPush(
                component, headsById[sectionComponentId]!, first.Target.Section!, sectionGroup);
            if (merged.IsError)
            {
                return merged.Errors;
            }

            units.Add(new ComponentPushUnit(
                sectionComponentId,
                merged.Value,
                sectionGroup.Any(r => r.Decision.AcknowledgeSignatureOrphans),
                sectionGroup.Any(r => r.Decision.AcknowledgeOverwrittenEntries),
                RefToken: null,
                IsRefPush: false));
        }

        return units;
    }

    private Error? CheckWholeSectionRefEntries(
        ResolvedDecision item,
        Dictionary<string, SharedComponent> componentsById,
        Dictionary<string, SharedComponentVersion?> headsById,
        StoredVersionHashes storedHashes)
    {
        if (item.Target.Kind != SharedComponentKind.Section)
        {
            return null;
        }

        foreach (Block inner in item.Target.Section!.Blocks
            .Where(b => b is { SharedComponentLinkState: SharedComponentLinkState.Linked, SharedComponentId.Length: > 0 }))
        {
            SharedComponent? refComponent = componentsById.GetValueOrDefault(inner.SharedComponentId!);
            SharedComponentVersion? refHead = headsById.GetValueOrDefault(inner.SharedComponentId!);
            string instanceHash = _reconciler.ComputeInstanceContentHash(inner);
            SharedComponentRelationResult relation = _reconciler.Classify(
                refComponent,
                storedHashes.Of(inner.SharedComponentId, refHead?.ContentHash),
                instanceHash,
                storedHashes.Of(inner.SharedComponentId, inner.SharedComponentVersionHash));
            if (relation.Relation is SharedComponentRelation.LocalAhead or SharedComponentRelation.Diverged)
            {
                return ConflictingDecisions(
                    $"The section copy at {item.Target.CanonicalPath} contains a Library-linked block "
                    + $"('{inner.SharedComponentId}') whose local content is ahead of its head; decide the "
                    + "Library-linked block first — a section push stores a reference, not its content.");
            }
        }

        return null;
    }

    private static Error? CheckRefPushToken(ComponentPushUnit unit, SharedComponentVersion head)
    {
        if (!unit.IsRefPush)
        {
            return null;
        }

        if (!string.Equals(unit.RefToken, head.Id, StringComparison.Ordinal))
        {
            return StaleRefHeadError(unit.ComponentId, head);
        }

        return null;
    }

    private static Error StaleRefHeadError(string componentId, SharedComponentVersion head)
    {
        Dictionary<string, object> metadata = BuildComponentHeadMetadata(head);
        metadata["token"] = "expectedRefHeadVersionId";

        return Error.Conflict(
            SharedComponentErrorCodes.HEAD_MOVED,
            $"The head version of referenced component '{componentId}' is '{head.Id}'; the supplied "
            + "expectedRefHeadVersionId is stale or missing. Re-read the reconcile state and re-apply.",
            metadata);
    }

    private ErrorOr<NormalizedDefinition> BuildMergedEntryPush(
        SharedComponent sectionComponent,
        SharedComponentVersion head,
        Section instanceSection,
        List<ResolvedDecision> entryPushes)
    {
        ErrorOr<NormalizedDefinition> headNormalized = NormalizeDefinition(sectionComponent, head.ContentJson);
        if (headNormalized.IsError)
        {
            return CorruptStoredHead(sectionComponent.Id);
        }

        string instanceJson = _normalizer.ToDefinitionJson(instanceSection, instanceSection.SharedComponentBindings);
        Section? instanceDefinition = Section.FromJson(instanceJson);
        Section? merged = Section.FromJson(headNormalized.Value.DefinitionJson);
        if (instanceDefinition is null || merged is null)
        {
            return CorruptStoredHead(sectionComponent.Id);
        }

        foreach (ResolvedDecision push in entryPushes)
        {
            string slot = push.Entry!.Coordinate;
            Block? instanceBlock = instanceDefinition.Blocks
                .FirstOrDefault(b => string.Equals(b.ReplacementTag, slot, StringComparison.Ordinal));
            if (instanceBlock is null)
            {
                return EntryLocatorError(
                    $"The entry '{slot}' at {push.Target.CanonicalPath} could not be derived for a shared push.");
            }

            Block? existing = merged.Blocks
                .FirstOrDefault(b => string.Equals(b.ReplacementTag, slot, StringComparison.Ordinal));
            if (existing is not null)
            {
                existing.ApplySharedDefinition(instanceBlock);
            }
            else
            {
                Block appended = instanceBlock.CloneToSection(merged, merged.Blocks.Count, slot);
                if (merged.Entries is { Count: > 0 })
                {
                    merged.Entries.Add(new SectionEntry { SlotTag = slot, Kind = SectionEntryKind.Inline });
                }
            }
        }

        return NormalizeDefinition(sectionComponent, _normalizer.ToDefinitionJson(merged));
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length > maxLength ? value[..maxLength] : value;
    }

    private Error? CheckRelinkContent(
        List<ResolvedDecision> resolved,
        Dictionary<string, SharedComponentVersion?> headsById,
        StoredVersionHashes storedHashes)
    {
        foreach (ResolvedDecision item in resolved
            .Where(r => r.Decision.Action == SharedComponentReconcileAction.Relink
                && r.Node.SharedComponentLinkState == SharedComponentLinkState.BrokenOut))
        {
            SharedComponentVersion head = headsById[item.Decision.SharedComponentId]!;
            string headHash = storedHashes.Of(item.Decision.SharedComponentId, head.ContentHash);
            string instanceHash;
            if (item.Target.Kind == SharedComponentKind.Block)
            {
                instanceHash = _reconciler.ComputeInstanceContentHash(item.Target.Block!);
            }
            else
            {
                instanceHash = _reconciler
                    .ComputeInstanceContentHash(
                        item.Target.Section!,
                        () => head.PayloadAbsentAt is null ? Section.FromJson(head.ContentJson) : null,
                        headHash)
                    .ContentHash;
            }

            if (!string.Equals(instanceHash, headHash, StringComparison.Ordinal))
            {
                return Error.Conflict(
                    SharedComponentErrorCodes.CONTENT_MISMATCH,
                    $"The copy at {item.Target.CanonicalPath} has drifted from the head of component "
                    + $"'{item.Decision.SharedComponentId}'; adopt the head or push the local content instead.",
                    new Dictionary<string, object>
                    {
                        ["expectedHash"] = head.ContentHash,
                        ["actualHash"] = instanceHash,
                    });
            }
        }

        return null;
    }

    private async Task<ErrorOr<Success>> ApplyLocalArmAsync(
        ApplySharedComponentReconcileCommand request,
        Workflow anchor,
        ResolvedDecision item,
        ReconcileApplyState state,
        CancellationToken cancellationToken)
    {
        string componentId = item.Decision.SharedComponentId;
        if (item.Entry is not null)
        {
            return await ApplyEntryArmAsync(anchor, item, state, cancellationToken);
        }

        switch (item.Decision.Action)
        {
            case SharedComponentReconcileAction.KeepLocal:
                return Result.Success;
            case SharedComponentReconcileAction.BreakOut:
                item.Node.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;

                return Result.Success;
            case SharedComponentReconcileAction.Relink:
            {
                if (item.Node.SharedComponentLinkState == SharedComponentLinkState.Linked)
                {
                    return Result.Success;
                }

                SharedComponentVersion head = state.HeadsById[componentId]!;
                item.Node.SharedComponentLinkState = SharedComponentLinkState.Linked;
                item.Node.SharedComponentVersionHash = head.ContentHash;
                if (item.Target.Section is { SharedComponentSlotStates.Count: > 0 })
                {
                    return await ClearOverridesOnRelinkAsync(anchor, item, state, cancellationToken);
                }

                return Result.Success;
            }

            case SharedComponentReconcileAction.UpdateShared:
            {
                item.Node.SharedComponentLinkState = SharedComponentLinkState.Linked;
                ComponentUpdatePlan plan = state.Plans.Single(p =>
                    string.Equals(p.Component.Id, componentId, StringComparison.Ordinal));
                if (plan.AlreadyCurrent)
                {
                    item.Node.SharedComponentVersionHash = plan.Head.ContentHash;
                }

                return Result.Success;
            }

            case SharedComponentReconcileAction.AdoptHead:
            {
                SharedComponent component = state.ComponentsById[componentId];
                SharedComponentVersion head = state.HeadsById[componentId]!;
                ErrorOr<NormalizedDefinition> normalizedHead = GetNormalizedHead(componentId, state);
                if (normalizedHead.IsError)
                {
                    return normalizedHead.Errors;
                }

                NormalizedDefinition headDefinition = normalizedHead.Value;

                if (component.Kind == SharedComponentKind.Block)
                {
                    Block instanceBlock = item.Target.Block!;
                    List<SharedComponentVersion> chain = await GetChainAsync(
                        component, state.ChainsByComponent, cancellationToken);
                    Block? oldBlockDefinition = ResolveOldBlockDefinition(
                        instanceBlock.SharedComponentVersionHash, chain, state.OldBlockDefinitionsByHash);
                    if (oldBlockDefinition is not null)
                    {
                        List<SharedComponentOverrideImpact> orphans = await CollectBlockAdoptOrphansAsync(
                            request.WorkflowTypeId,
                            instanceBlock,
                            oldBlockDefinition,
                            headDefinition.BlockDefinition!,
                            cancellationToken);
                        if (orphans.Count > 0 && !item.Decision.AcknowledgeSignatureOrphans)
                        {
                            return OrphansError(componentId, orphans);
                        }

                        state.AllOrphans.AddRange(orphans);
                    }

                    instanceBlock.ApplySharedDefinition(headDefinition.BlockDefinition!);
                    if (instanceBlock.SharedComponentBindings is { Count: > 0 })
                    {
                        instanceBlock.ApplyReplacementTagRenames(instanceBlock.SharedComponentBindings);
                    }
                }
                else
                {
                    List<SharedComponentVersion> chain = await GetChainAsync(
                        component, state.ChainsByComponent, cancellationToken);
                    Section instance = item.Target.Section!;
                    Section? oldDefinition = ResolveOldDefinition(
                        instance.SharedComponentVersionHash, chain, state.OldDefinitionsByHash);
                    if (oldDefinition is not null)
                    {
                        List<SharedComponentOverrideImpact> orphans = await CollectAdoptOrphansAsync(
                            request.WorkflowTypeId,
                            instance,
                            oldDefinition,
                            headDefinition.SectionDefinition!,
                            cancellationToken);
                        if (orphans.Count > 0 && !item.Decision.AcknowledgeSignatureOrphans)
                        {
                            return OrphansError(componentId, orphans);
                        }

                        state.AllOrphans.AddRange(orphans);
                    }

                    AppendTo(
                        state.ReconcilesByType,
                        request.WorkflowTypeId,
                        [_reconciler.ApplySectionDefinition(
                            anchor,
                            instance,
                            oldDefinition,
                            headDefinition.SectionDefinition!,
                            await GetRefHeadsAsync(componentId, headDefinition.SectionDefinition!, state, cancellationToken))]);
                }

                item.Node.SharedComponentLinkState = SharedComponentLinkState.Linked;
                item.Node.SharedComponentVersionHash = head.ContentHash;

                return Result.Success;
            }

            default:
                throw new InvalidOperationException(
                    $"Unsupported reconcile action '{item.Decision.Action}'.");
        }
    }

    private async Task<ErrorOr<Success>> ApplyEntryArmAsync(
        Workflow anchor,
        ResolvedDecision item,
        ReconcileApplyState state,
        CancellationToken cancellationToken)
    {
        switch (item.Decision.Action)
        {
            case SharedComponentReconcileAction.KeepLocal:
                return Result.Success;
            case SharedComponentReconcileAction.Hide:
                return ApplyHideArm(item, state);
            case SharedComponentReconcileAction.Unhide:
                return await ApplyUnhideArmAsync(anchor, item, state, cancellationToken);
            case SharedComponentReconcileAction.BreakOut:
                return ApplyEntryBreakOutArm(item, state);
            case SharedComponentReconcileAction.AdoptHead:
                return await ApplyEntryAdoptHeadArmAsync(anchor, item, state, cancellationToken);
            case SharedComponentReconcileAction.UpdateShared:
                return ApplyEntryUpdateSharedArm(item, state);
            default:
                throw new InvalidOperationException(
                    $"Unsupported entry-scoped reconcile action '{item.Decision.Action}'.");
        }
    }

    private ErrorOr<Success> ApplyEntryUpdateSharedArm(ResolvedDecision item, ReconcileApplyState state)
    {
        item.Node.SharedComponentLinkState = SharedComponentLinkState.Linked;
        if (item.Entry!.LinkedInnerComponentId is not null)
        {
            return Result.Success;
        }

        ErrorOr<NormalizedDefinition> headResult = GetNormalizedHead(item.Decision.SharedComponentId, state);
        if (headResult.IsError)
        {
            return headResult.Errors;
        }

        bool isDefSlot = headResult.Value.SectionDefinition!.EffectiveEntries()
            .Any(e => string.Equals(e.SlotTag, item.Entry.Coordinate, StringComparison.Ordinal));
        if (!isDefSlot && item.Entry.LiveBlock is { } localInsert)
        {
            _ = item.Target.Section!.Blocks.Remove(localInsert);
        }

        return Result.Success;
    }

    private ErrorOr<Success> ApplyHideArm(ResolvedDecision item, ReconcileApplyState state)
    {
        Section section = item.Target.Section!;
        string slot = item.Entry!.Coordinate;
        SharedComponentSlotState? current = section.SharedComponentSlotStates?.GetValueOrDefault(slot);
        if (current == SharedComponentSlotState.Hidden)
        {
            return Result.Success;
        }

        if (current == SharedComponentSlotState.BrokenOut)
        {
            return EntryLocatorError(
                $"The entry '{slot}' at {item.Target.CanonicalPath} is broken out; adopt the head or keep the "
                + "local copy instead of hiding it.");
        }

        ErrorOr<NormalizedDefinition> headResult = GetNormalizedHead(item.Decision.SharedComponentId, state);
        if (headResult.IsError)
        {
            return headResult.Errors;
        }

        if (!headResult.Value.SectionDefinition!.EffectiveEntries()
            .Any(e => string.Equals(e.SlotTag, slot, StringComparison.Ordinal)))
        {
            return EntryLocatorError(
                $"Hide applies to definition-provided entries; '{slot}' at {item.Target.CanonicalPath} is a local insert.");
        }

        Dictionary<string, SharedComponentSlotState> slotStates = section.SharedComponentSlotStates
            ?? new Dictionary<string, SharedComponentSlotState>(StringComparer.Ordinal);
        slotStates[slot] = SharedComponentSlotState.Hidden;
        section.SharedComponentSlotStates = slotStates;
        if (item.Entry.LiveBlock is { } live)
        {
            _ = section.Blocks.Remove(live);
        }

        if (section.SharedComponentBindings is { } bindings && bindings.Remove(slot) && bindings.Count == 0)
        {
            section.SharedComponentBindings = null;
        }

        return Result.Success;
    }

    private async Task<ErrorOr<Success>> ApplyUnhideArmAsync(
        Workflow anchor,
        ResolvedDecision item,
        ReconcileApplyState state,
        CancellationToken cancellationToken)
    {
        Section section = item.Target.Section!;
        string slot = item.Entry!.Coordinate;
        if (section.SharedComponentSlotStates is not { } slotStates
            || slotStates.GetValueOrDefault(slot) != SharedComponentSlotState.Hidden)
        {
            return Result.Success;
        }

        ErrorOr<NormalizedDefinition> headResult = GetNormalizedHead(item.Decision.SharedComponentId, state);
        if (headResult.IsError)
        {
            return headResult.Errors;
        }

        Section headDefinition = headResult.Value.SectionDefinition!;
        List<SectionEntry> entries = headDefinition.EffectiveEntries();
        int rank = entries.FindIndex(e => string.Equals(e.SlotTag, slot, StringComparison.Ordinal));
        ClearSlotOverride(section, slot);
        if (rank < 0)
        {
            return Result.Success;
        }

        return await MaterializeDefEntryAsync(anchor, section, item, headDefinition, entries, rank, state, cancellationToken);
    }

    private async Task<ErrorOr<Success>> MaterializeDefEntryAsync(
        Workflow anchor,
        Section section,
        ResolvedDecision item,
        Section headDefinition,
        List<SectionEntry> entries,
        int rank,
        ReconcileApplyState state,
        CancellationToken cancellationToken)
    {
        string slot = entries[rank].SlotTag;
        SectionEntry entry = entries[rank];
        HashSet<string> reservedTags = SharedComponentEditor.ExistingTagNamespace(anchor);
        string landedTag = reservedTags.Add(slot) ? slot : SharedComponentEditor.UniqueSlug(slot, reservedTags);
        Block landed;
        if (entry.Kind == SectionEntryKind.ComponentRef)
        {
            if (string.IsNullOrEmpty(entry.SharedComponentId))
            {
                return CorruptStoredHead(item.Decision.SharedComponentId);
            }

            IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads =
                await GetRefHeadsAsync(item.Decision.SharedComponentId, headDefinition, state, cancellationToken);
            if (refHeads is null || !refHeads.TryGetValue(entry.SharedComponentId, out SharedComponentRefHead? refHead))
            {
                return SharedComponentErrorCodes.UnknownComponent(entry.SharedComponentId);
            }

            landed = SharedComponentRefHeads.MaterializeRefBlock(section, landedTag, entry, refHead);
        }
        else
        {
            Block? defBlock = headDefinition.Blocks.FirstOrDefault(b =>
                string.Equals(b.ReplacementTag, slot, StringComparison.Ordinal));
            if (defBlock is null)
            {
                return CorruptStoredHead(item.Decision.SharedComponentId);
            }

            landed = defBlock.CloneToSection(section, -1, landedTag);
            landed.ReMintStableBlockId();
        }

        if (!string.Equals(landedTag, slot, StringComparison.Ordinal))
        {
            Dictionary<string, string> bindings = section.SharedComponentBindings
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
            bindings[slot] = landedTag;
            section.SharedComponentBindings = bindings;
        }

        if (entry.Kind == SectionEntryKind.ComponentRef)
        {
            SharedComponentRefHeads.BindRefBlock(
                entry, landed, section.SharedComponentBindings ?? new Dictionary<string, string>(StringComparer.Ordinal));
        }
        else if (section.SharedComponentBindings is { Count: > 0 } renames)
        {
            landed.ApplyReplacementTagRenames(renames);
        }

        List<Block> ordered = [.. section.Blocks.Where(b => !ReferenceEquals(b, landed)).OrderBy(b => b.Order)];
        int insertAt = 0;
        for (int precedingRank = rank - 1; precedingRank >= 0; precedingRank--)
        {
            string precedingSlot = entries[precedingRank].SlotTag;
            string precedingLiveTag = section.SharedComponentBindings?.GetValueOrDefault(precedingSlot) ?? precedingSlot;
            Block? predecessor = ordered.FirstOrDefault(b =>
                string.Equals(b.ReplacementTag, precedingLiveTag, StringComparison.Ordinal));
            if (predecessor is not null)
            {
                insertAt = ordered.IndexOf(predecessor) + 1;
                break;
            }
        }

        ordered.Insert(insertAt, landed);
        section.Blocks.Clear();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
            section.Blocks.Add(ordered[i]);
        }

        return Result.Success;
    }

    private static void ClearSlotOverride(Section section, string slot)
    {
        if (section.SharedComponentSlotStates is { } slotStates
            && slotStates.Remove(slot)
            && slotStates.Count == 0)
        {
            section.SharedComponentSlotStates = null;
        }
    }

    private async Task<ErrorOr<Success>> ApplyEntryAdoptHeadArmAsync(
        Workflow anchor,
        ResolvedDecision item,
        ReconcileApplyState state,
        CancellationToken cancellationToken)
    {
        Section section = item.Target.Section!;
        string slot = item.Entry!.Coordinate;
        if (section.SharedComponentSlotStates?.GetValueOrDefault(slot) == SharedComponentSlotState.Hidden)
        {
            return EntryLocatorError(
                $"The entry '{slot}' at {item.Target.CanonicalPath} is hidden; unhide it instead of adopting the head.");
        }

        ErrorOr<NormalizedDefinition> headResult = GetNormalizedHead(item.Decision.SharedComponentId, state);
        if (headResult.IsError)
        {
            return headResult.Errors;
        }

        Section headDefinition = headResult.Value.SectionDefinition!;
        List<SectionEntry> entries = headDefinition.EffectiveEntries();
        int rank = entries.FindIndex(e => string.Equals(e.SlotTag, slot, StringComparison.Ordinal));
        if (rank < 0)
        {
            return EntryLocatorError(
                $"AdoptHead of a section entry applies to definition-provided entries; '{slot}' at "
                + $"{item.Target.CanonicalPath} is not provided by the component head.");
        }

        SectionEntry entry = entries[rank];
        if (entry.Kind == SectionEntryKind.ComponentRef)
        {
            return await AdoptRefEntryAsync(anchor, section, item, headDefinition, entries, rank, state, cancellationToken);
        }

        Block? live = item.Entry.LiveBlock;
        if (live is null)
        {
            ErrorOr<Success> materialized = await MaterializeDefEntryAsync(
                anchor, section, item, headDefinition, entries, rank, state, cancellationToken);
            if (materialized.IsError)
            {
                return materialized.Errors;
            }
        }
        else
        {
            ErrorOr<Success> adopted = AdoptInlineDefBlock(
                section, headDefinition, slot, live, item.Decision.SharedComponentId);
            if (adopted.IsError)
            {
                return adopted.Errors;
            }
        }

        ClearSlotOverride(section, slot);

        return Result.Success;
    }

    private async Task<ErrorOr<Success>> AdoptRefEntryAsync(
        Workflow anchor,
        Section section,
        ResolvedDecision item,
        Section headDefinition,
        List<SectionEntry> entries,
        int rank,
        ReconcileApplyState state,
        CancellationToken cancellationToken)
    {
        SectionEntry entry = entries[rank];
        string slot = entry.SlotTag;
        if (string.IsNullOrEmpty(entry.SharedComponentId))
        {
            return CorruptStoredHead(item.Decision.SharedComponentId);
        }

        string refComponentId = entry.SharedComponentId;
        ErrorOr<(SharedComponentVersion Head, Block Definition)> resolved = await ResolveRefDefinitionAsync(
            refComponentId, item.Decision.ExpectedRefHeadVersionId ?? string.Empty, cancellationToken);
        if (resolved.IsError)
        {
            return resolved.Errors;
        }

        Block? live = item.Entry!.LiveBlock;
        if (live is null)
        {
            ErrorOr<Success> materialized = await MaterializeDefEntryAsync(
                anchor, section, item, headDefinition, entries, rank, state, cancellationToken);
            if (materialized.IsError)
            {
                return materialized.Errors;
            }
        }
        else
        {
            SharedComponentRefHeads.ReapplyRefDefinition(
                live, refComponentId, resolved.Value.Head.ContentHash, resolved.Value.Definition);
            SharedComponentRefHeads.BindRefBlock(
                entry, live, section.SharedComponentBindings ?? new Dictionary<string, string>(StringComparer.Ordinal));
        }

        ClearSlotOverride(section, slot);

        return Result.Success;
    }

    private async Task<ErrorOr<(SharedComponentVersion Head, Block Definition)>> ResolveRefDefinitionAsync(
        string refComponentId,
        string? requiredExpectedHeadVersionId,
        CancellationToken cancellationToken)
    {
        SharedComponentVersion? refHead = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(refComponentId), cancellationToken);
        if (refHead is null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.NO_VERSIONS,
                $"Component '{refComponentId}' has no versions at all; its chain is corrupt.");
        }

        if (requiredExpectedHeadVersionId is not null
            && !string.Equals(requiredExpectedHeadVersionId, refHead.Id, StringComparison.Ordinal))
        {
            return StaleRefHeadError(refComponentId, refHead);
        }

        if (refHead.PayloadAbsentAt is not null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"The head of component '{refComponentId}' is a hash-only lineage row with no stored content to adopt.");
        }

        Block? refDefinition = SharedComponentContentNormalizer.ReadBlockDefinitionForNewCopy(refHead.ContentJson);
        if (refDefinition is null)
        {
            return CorruptStoredHead(refComponentId);
        }

        return (refHead, refDefinition);
    }

    private async Task<ErrorOr<Success>> ClearOverridesOnRelinkAsync(
        Workflow anchor,
        ResolvedDecision item,
        ReconcileApplyState state,
        CancellationToken cancellationToken)
    {
        Section section = item.Target.Section!;
        ErrorOr<NormalizedDefinition> headResult = GetNormalizedHead(item.Decision.SharedComponentId, state);
        if (headResult.IsError)
        {
            return headResult.Errors;
        }

        Section headDefinition = headResult.Value.SectionDefinition!;
        List<SectionEntry> entries = headDefinition.EffectiveEntries();
        foreach ((string slot, SharedComponentSlotState slotState) in section.SharedComponentSlotStates!
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .ToList())
        {
            ClearSlotOverride(section, slot);
            int rank = entries.FindIndex(e => string.Equals(e.SlotTag, slot, StringComparison.Ordinal));
            if (rank < 0)
            {
                continue;
            }

            SectionEntry entry = entries[rank];
            string liveTag = section.SharedComponentBindings?.GetValueOrDefault(slot) ?? slot;
            Block? live = section.Blocks.FirstOrDefault(b =>
                string.Equals(b.ReplacementTag, liveTag, StringComparison.Ordinal));
            if (live is null)
            {
                ErrorOr<Success> materialized = await MaterializeDefEntryAsync(
                    anchor, section, item, headDefinition, entries, rank, state, cancellationToken);
                if (materialized.IsError)
                {
                    return materialized.Errors;
                }

                continue;
            }

            if (slotState == SharedComponentSlotState.Hidden)
            {
                continue;
            }

            if (entry.Kind == SectionEntryKind.ComponentRef)
            {
                ErrorOr<Success> reconnected = await ReconnectRefEntryAsync(
                    section, entry, live, cancellationToken);
                if (reconnected.IsError)
                {
                    return reconnected.Errors;
                }

                continue;
            }

            ErrorOr<Success> adopted = AdoptInlineDefBlock(
                section, headDefinition, slot, live, item.Decision.SharedComponentId);
            if (adopted.IsError)
            {
                return adopted.Errors;
            }
        }

        return Result.Success;
    }

    private ErrorOr<Success> AdoptInlineDefBlock(
        Section section,
        Section headDefinition,
        string slot,
        Block live,
        string componentId)
    {
        Block? defBlock = headDefinition.Blocks.FirstOrDefault(b =>
            string.Equals(b.ReplacementTag, slot, StringComparison.Ordinal));
        if (defBlock is null)
        {
            return CorruptStoredHead(componentId);
        }

        live.ApplySharedDefinition(defBlock);
        if (section.SharedComponentBindings is { Count: > 0 } renames)
        {
            live.ApplyReplacementTagRenames(renames);
        }

        return Result.Success;
    }

    private async Task<ErrorOr<Success>> ReconnectRefEntryAsync(
        Section section,
        SectionEntry entry,
        Block live,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entry.SharedComponentId))
        {
            return CorruptStoredHead(section.SharedComponentId ?? string.Empty);
        }

        string refComponentId = entry.SharedComponentId;
        ErrorOr<(SharedComponentVersion Head, Block Definition)> resolved =
            await ResolveRefDefinitionAsync(refComponentId, null, cancellationToken);
        if (resolved.IsError)
        {
            return resolved.Errors;
        }

        SharedComponentRefHeads.ReapplyRefDefinition(
            live, refComponentId, resolved.Value.Head.ContentHash, resolved.Value.Definition);
        SharedComponentRefHeads.BindRefBlock(
            entry, live, section.SharedComponentBindings ?? new Dictionary<string, string>(StringComparer.Ordinal));

        return Result.Success;
    }

    private ErrorOr<Success> ApplyEntryBreakOutArm(ResolvedDecision item, ReconcileApplyState state)
    {
        Section section = item.Target.Section!;
        string slot = item.Entry!.Coordinate;
        SharedComponentSlotState? current = section.SharedComponentSlotStates?.GetValueOrDefault(slot);
        if (current == SharedComponentSlotState.Hidden)
        {
            return EntryLocatorError(
                $"The entry '{slot}' at {item.Target.CanonicalPath} is hidden; unhide it before breaking it out.");
        }

        Block? live = item.Entry.LiveBlock;
        if (live is null)
        {
            return EntryLocatorError(
                $"The entry '{slot}' at {item.Target.CanonicalPath} has no live block to break out.");
        }

        if (!string.IsNullOrEmpty(live.SharedComponentId) && live.SharedComponentLinkState is not null)
        {
            live.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;

            return Result.Success;
        }

        if (current == SharedComponentSlotState.BrokenOut)
        {
            return Result.Success;
        }

        ErrorOr<NormalizedDefinition> headResult = GetNormalizedHead(item.Decision.SharedComponentId, state);
        if (headResult.IsError)
        {
            return headResult.Errors;
        }

        if (!headResult.Value.SectionDefinition!.EffectiveEntries()
            .Any(e => string.Equals(e.SlotTag, slot, StringComparison.Ordinal)))
        {
            return EntryLocatorError(
                $"BreakOut of a section entry applies to definition-provided entries; '{slot}' at "
                + $"{item.Target.CanonicalPath} is a local insert.");
        }

        Dictionary<string, SharedComponentSlotState> slotStates = section.SharedComponentSlotStates
            ?? new Dictionary<string, SharedComponentSlotState>(StringComparer.Ordinal);
        slotStates[slot] = SharedComponentSlotState.BrokenOut;
        section.SharedComponentSlotStates = slotStates;

        return Result.Success;
    }

    private ErrorOr<NormalizedDefinition> GetNormalizedHead(string componentId, ReconcileApplyState state)
    {
        if (state.NormalizedHeadsByComponent.TryGetValue(componentId, out NormalizedDefinition? cached))
        {
            return cached;
        }

        if (!state.ComponentsById.TryGetValue(componentId, out SharedComponent? component))
        {
            return SharedComponentErrorCodes.UnknownComponent(componentId);
        }

        SharedComponentVersion? head = state.HeadsById.GetValueOrDefault(componentId);
        if (head is null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.NO_VERSIONS,
                $"Component '{componentId}' has no versions at all; its chain is corrupt.");
        }

        if (head.PayloadAbsentAt is not null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"The head of component '{componentId}' is a hash-only lineage row with no stored content.");
        }

        ErrorOr<NormalizedDefinition> normalized = NormalizeDefinition(component, head.ContentJson);
        if (normalized.IsError)
        {
            return CorruptStoredHead(componentId);
        }

        state.NormalizedHeadsByComponent[componentId] = normalized.Value;

        return normalized.Value;
    }

    private async Task<IReadOnlyDictionary<string, SharedComponentRefHead>?> GetRefHeadsAsync(
        string componentId,
        Section headDefinition,
        ReconcileApplyState state,
        CancellationToken cancellationToken)
    {
        if (state.RefHeadsByComponent.TryGetValue(componentId, out IReadOnlyDictionary<string, SharedComponentRefHead>? cached))
        {
            return cached;
        }

        IReadOnlyDictionary<string, SharedComponentRefHead>? loaded =
            await SharedComponentRefHeads.LoadAsync(_coreUnitOfWork, headDefinition, cancellationToken);
        state.RefHeadsByComponent[componentId] = loaded;

        return loaded;
    }

    private sealed class ReconcileApplyState
    {
        public required Dictionary<string, SharedComponent> ComponentsById { get; init; }

        public required Dictionary<string, SharedComponentVersion?> HeadsById { get; init; }

        public required List<ComponentUpdatePlan> Plans { get; init; }

        public Dictionary<string, List<SharedComponentVersion>> ChainsByComponent { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Section?> OldDefinitionsByHash { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Block?> OldBlockDefinitionsByHash { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<SectionReconcileResult>> ReconcilesByType { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, NormalizedDefinition> NormalizedHeadsByComponent { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyDictionary<string, SharedComponentRefHead>?> RefHeadsByComponent { get; } = new(StringComparer.Ordinal);

        public List<SharedComponentOverrideImpact> AllOrphans { get; } = [];
    }

    private async Task<List<SharedComponentOverrideImpact>> CollectBlockAdoptOrphansAsync(
        string workflowTypeId,
        Block instance,
        Block oldDefinition,
        Block headDefinition,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> inertPlaceholders = SharedComponentReconciler.RemovedSignaturePlaceholders(
            oldDefinition, headDefinition);
        if (inertPlaceholders.Count == 0 || string.IsNullOrEmpty(instance.StableBlockId))
        {
            return [];
        }

        List<OverrideProbe> probes =
        [
            new OverrideProbe(
                workflowTypeId,
                instance.StableBlockId,
                instance.ReplacementTag,
                inertPlaceholders,
                SharedComponentOverrideImpactKind.GoingInert),
        ];

        return await ResolveProbesAsync(probes, cancellationToken);
    }

    private async Task<List<SharedComponentOverrideImpact>> CollectAdoptOrphansAsync(
        string workflowTypeId,
        Section instance,
        Section oldDefinition,
        Section headDefinition,
        CancellationToken cancellationToken)
    {
        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDefinition, headDefinition);
        IReadOnlyDictionary<string, IReadOnlyList<string>> removedBySlotTag =
            RemovedSlotPlaceholders(oldDefinition, delta.RemovedSlotTags);
        IReadOnlyDictionary<string, IReadOnlyList<string>> inertBySlotTag =
            SharedComponentReconciler.ComputeInertPlaceholders(
                oldDefinition, headDefinition, delta.MatchedSlotTags, delta.RescuedSlots);
        if (removedBySlotTag.Count == 0 && inertBySlotTag.Count == 0)
        {
            return [];
        }

        List<OverrideProbe> probes = [];
        AddInstanceProbes(probes, workflowTypeId, instance, removedBySlotTag, inertBySlotTag);

        return await ResolveProbesAsync(probes, cancellationToken);
    }


    private async Task<List<SharedComponentVersion>> GetChainAsync(
        SharedComponent component,
        Dictionary<string, List<SharedComponentVersion>> chainsByComponent,
        CancellationToken cancellationToken)
    {
        if (chainsByComponent.TryGetValue(component.Id, out List<SharedComponentVersion>? cached))
        {
            return cached;
        }

        List<SharedComponentVersion> chain = await LoadChainAsync(component, cancellationToken);
        chainsByComponent[component.Id] = chain;

        return chain;
    }

    private async Task<List<SharedComponentVersion>> LoadChainAsync(
        SharedComponent component,
        CancellationToken cancellationToken)
    {
        return await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => v.SharedComponentId == component.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<Workflow?> GetOrLoadGraphAsync(
        Dictionary<string, Workflow> graphs,
        string workflowTypeId,
        string componentId)
    {
        if (graphs.TryGetValue(workflowTypeId, out Workflow? cached))
        {
            return cached;
        }

        Workflow? loaded = await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId);
        if (loaded is null)
        {
            _logger.LogWarning(
                "Skipping fan-out of component {SharedComponentId} to {WorkflowTypeId}: the template row no longer exists",
                componentId,
                LogSanitizer.ForLog(workflowTypeId));

            return null;
        }

        graphs[workflowTypeId] = loaded;

        return loaded;
    }

    private ErrorOr<NormalizedDefinition?> NormalizeStoredHead(SharedComponent component, SharedComponentVersion head)
    {
        if (head.PayloadAbsentAt is not null)
        {
            return (NormalizedDefinition?)null;
        }

        ErrorOr<NormalizedDefinition> normalized = NormalizeDefinition(component, head.ContentJson);

        return normalized.IsError ? CorruptStoredHead(component.Id) : normalized.Value;
    }

    private SharedComponentConsumerSaveResult BuildWorkflowSaveResult(
        string workflowTypeId,
        SaveWorkflowResult save,
        Workflow workflow,
        IReadOnlyList<SectionReconcileResult> sectionReconciles,
        IReadOnlyList<SharedComponentOverrideImpact> allOrphans)
    {
        return new SharedComponentConsumerSaveResult(
            workflowTypeId,
            save.VersionId,
            save.ContentHash,
            save.WasDeduped,
            save.SignatureSync,
            _structuralValidator.Validate(workflow).Warnings,
            sectionReconciles)
        {
            OrphanedOverrides = [.. allOrphans
                .Where(o => string.Equals(o.WorkflowTypeId, workflowTypeId, StringComparison.Ordinal))],
        };
    }

    private static List<SharedComponentReconcileDecisionOutcome> BuildOutcomes(
        List<ResolvedDecision> resolved,
        List<ComponentUpdatePlan> plans)
    {
        return [.. resolved.Select(item => new SharedComponentReconcileDecisionOutcome(
            item.Decision.SharedComponentId,
            item.Target.CanonicalPath,
            item.Target.Kind == SharedComponentKind.Block
                ? item.Target.Block!.StableBlockId
                : item.Target.Section!.StableSectionId,
            item.Decision.Action,
            item.Decision.Action == SharedComponentReconcileAction.UpdateShared
                && plans.SingleOrDefault(p => string.Equals(
                    p.Component.Id, item.Decision.SharedComponentId, StringComparison.Ordinal)) is { AlreadyCurrent: true }))];
    }

    private static List<SharedComponentReconciledHead> BuildComponentHeads(
        List<string> componentIds,
        Dictionary<string, SharedComponent> componentsById,
        Dictionary<string, SharedComponentVersion?> headsById,
        Dictionary<string, SharedComponentVersion> appendedByComponent)
    {
        List<SharedComponentReconciledHead> heads = [];
        foreach (string componentId in componentIds)
        {
            if (appendedByComponent.TryGetValue(componentId, out SharedComponentVersion? appended))
            {
                heads.Add(new SharedComponentReconciledHead(componentId, appended.Id, appended.ContentHash, true));
            }
            else if (componentsById.ContainsKey(componentId)
                && headsById.GetValueOrDefault(componentId) is { } head)
            {
                heads.Add(new SharedComponentReconciledHead(componentId, head.Id, head.ContentHash, false));
            }
        }

        return heads;
    }

    private static void AppendTo<T>(Dictionary<string, List<T>> byType, string typeId, List<T> items)
    {
        if (!byType.TryGetValue(typeId, out List<T>? list))
        {
            list = [];
            byType[typeId] = list;
        }

        list.AddRange(items);
    }

    private void DetachPendingVersions(IEnumerable<SharedComponentVersion> pendingVersions)
    {
        foreach (SharedComponentVersion version in pendingVersions)
        {
            DetachPendingVersion(version);
        }
    }

    private void DetachPendingImportEntities(
        IEnumerable<SharedComponentVersion> versions,
        IEnumerable<SharedComponent> components,
        IEnumerable<object> provisionedTypeRows)
    {
        DetachPendingVersions(versions);
        foreach (SharedComponent component in components)
        {
            _coreDbContext.Entry(component).State = EntityState.Detached;
        }

        foreach (object row in provisionedTypeRows)
        {
            _coreDbContext.Entry(row).State = EntityState.Detached;
        }
    }

    private static bool NeedsLiveHead(SharedComponentReconcileAction action)
    {
        return action is SharedComponentReconcileAction.UpdateShared
            or SharedComponentReconcileAction.AdoptHead
            or SharedComponentReconcileAction.Relink;
    }

    private static Error ConflictingDecisions(string reason)
    {
        return Error.Validation(
            DECISIONS_ERROR_KEY,
            $"{reason} [{SharedComponentErrorCodes.CONFLICTING_DECISIONS}]");
    }

    private static Error OverwrittenEntriesError(
        string componentId,
        string workflowTypeId,
        IReadOnlyList<string> slotTags)
    {
        return Error.Conflict(
            SharedComponentErrorCodes.OVERWRITES_LOCAL_ENTRIES,
            $"Fanning this change out to '{workflowTypeId}' would discard local edits in "
            + $"{slotTags.Count} entr{(slotTags.Count == 1 ? "y" : "ies")} no decision named. "
            + "Reconcile those entries first, or confirm that replacing them is intended.",
            new Dictionary<string, object>
            {
                ["sharedComponentId"] = componentId,
                [SharedComponentOrphanMetadata.WORKFLOW_TYPE_ID_KEY] = workflowTypeId,
                [SharedComponentOrphanMetadata.OVERWRITTEN_ENTRY_SLOTS_KEY] = slotTags,
            });
    }

    private static Error OrphansError(string componentId, IReadOnlyList<SharedComponentOverrideImpact> orphans)
    {
        return Error.Conflict(
            SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT,
            $"Applying this decision set would orphan {orphans.Count} signature override(s); re-send with "
            + "acknowledgeSignatureOrphans=true on the responsible decision to proceed.",
            new Dictionary<string, object>
            {
                ["sharedComponentId"] = componentId,
                [SharedComponentOrphanMetadata.ORPHANED_OVERRIDES_KEY] = orphans,
            });
    }

    private static Error CorruptStoredHead(string componentId)
    {
        return Error.Unexpected(
            SharedComponentErrorCodes.CORRUPT_DEFINITION,
            $"The stored definition of component '{componentId}' could not be read.");
    }

    /// <summary>
    /// Retires a component atomically with the break-out of every Linked copy — the Retired state
    /// and "zero Linked consumers" land in one transaction, so no reader ever sees a frozen
    /// component that still drives fan-outs. Copies keep their id and version-hash provenance;
    /// each touched template gets a link-only save. Retiring a block component also erases every
    /// definition-level reference to it in the same transaction: each section component whose
    /// head carries a ref entry to it gets a new version with that head content baked in as an
    /// inline slot, and the containers' Linked instances are restamped to the rewritten heads —
    /// content-identical, because their inner copies keep BrokenOut provenance and reconstruction
    /// reads BrokenOut as inline. Idempotent on an already-retired component.
    /// </summary>
    public async Task<ErrorOr<SharedComponentRetireResult>> RetireAndBreakOutAsync(
        string organizationId,
        string userId,
        string componentId,
        CancellationToken cancellationToken)
    {
        return await _coreDbContext.CreateExecutionStrategy().ExecuteAsync(
            ct => RetireAndBreakOutCoreAsync(organizationId, userId, componentId, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<SharedComponentRetireResult>> RetireAndBreakOutCoreAsync(
        string organizationId,
        string userId,
        string componentId,
        CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();

        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .FirstOrDefaultAsync(c => c.Id == componentId, cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(componentId);
        }

        if (component.State == SharedComponentState.Retired)
        {
            return new SharedComponentRetireResult(component.Id, component.State, 0, [], []);
        }

        ErrorOr<List<RetireInliningPlan>> plansResult =
            await BuildRetireInliningPlansAsync(component, cancellationToken);
        if (plansResult.IsError)
        {
            return plansResult.Errors;
        }

        List<RetireInliningPlan> plans = plansResult.Value;
        List<string> scannedComponentIds = [component.Id, .. plans.Select(p => p.Container.Id)];
        List<SharedComponentConsumerScan.ComponentConsumerRow> consumerRows =
            (await SharedComponentConsumerScan.ScanAsync(_coreUnitOfWork, scannedComponentIds, cancellationToken)).Consumers;
        List<string> affectedTypeIds = [.. consumerRows
            .Where(r => !string.IsNullOrEmpty(r.Consumer.WorkflowTypeId))
            .Select(r => r.Consumer.WorkflowTypeId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        await using IDbContextTransaction transaction =
            await _coreDbContext.BeginTransactionAsync(cancellationToken);
        List<SharedComponentVersion> pendingVersions = [];
        try
        {
            foreach (RetireInliningPlan plan in plans)
            {
                var inlinedVersion = new SharedComponentVersion
                {
                    SharedComponentId = plan.Container.Id,
                    SequenceNumber = plan.Head.SequenceNumber + 1,
                    ContentHash = plan.Baked.ContentHash,
                    ParentVersionId = plan.Head.Id,
                    CreatedByUserId = userId,
                    ContentJson = plan.Baked.DefinitionJson,
                };
                _ = _coreDbContext.Add(inlinedVersion);
                pendingVersions.Add(inlinedVersion);
            }

            List<SharedComponentConsumerSaveResult> workflowSaves = [];
            int brokenOut = 0;
            foreach (string workflowTypeId in affectedTypeIds)
            {
                Workflow? consumerWorkflow = await _coreUnitOfWork.Workflows
                    .GetByTypeIdAsNoTrackingAsync(workflowTypeId);
                if (consumerWorkflow is null)
                {
                    _logger.LogWarning(
                        "Skipping retire break-out of component {SharedComponentId} in {WorkflowTypeId}: the template row no longer exists",
                        component.Id,
                        workflowTypeId);
                    continue;
                }

                int flippedHere = 0;
                foreach (Section instance in consumerWorkflow.Sections
                    .Where(s => IsLinkedInstance(s, component.Id)))
                {
                    instance.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
                    flippedHere++;
                }

                foreach (Block instance in consumerWorkflow.GetAllBlocks()
                    .Where(b => IsLinkedInstance(b, component.Id)))
                {
                    instance.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
                    flippedHere++;
                }

                foreach (RetireInliningPlan plan in plans)
                {
                    foreach (Section instance in consumerWorkflow.Sections
                        .Where(s => IsLinkedInstance(s, plan.Container.Id)))
                    {
                        instance.SharedComponentVersionHash = plan.Baked.ContentHash;
                    }
                }

                brokenOut += flippedHere;
                string narrative = flippedHere > 0
                    ? $"Broke out: shared component '{component.Name}' was retired"
                    : $"Inlined retired component '{component.Name}'";
                ErrorOr<SaveWorkflowResult> save = await _savePipeline.SaveCoreAsync(
                    new SaveWorkflowCommand(
                        organizationId,
                        userId,
                        workflowTypeId,
                        consumerWorkflow,
                        ConfirmSignatureFieldDeletions: true),
                    narrative,
                    cancellationToken);
                if (save.IsError)
                {
                    DetachPendingVersions(pendingVersions);
                    DetachTrackedComponent(component);

                    return save.Errors;
                }

                workflowSaves.Add(BuildWorkflowSaveResult(
                    workflowTypeId,
                    save.Value,
                    consumerWorkflow,
                    [],
                    []));
            }

            component.State = SharedComponentState.Retired;
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Retired component {SharedComponentId}: {BrokenOutCount} linked copies broken out across {WorkflowCount} templates, {InlinedContainerCount} referencing definitions inlined",
                component.Id,
                brokenOut,
                workflowSaves.Count,
                plans.Count);

            return new SharedComponentRetireResult(
                component.Id,
                component.State,
                brokenOut,
                workflowSaves,
                [.. plans.Select(p => p.Container.Id)]);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachPendingVersions(pendingVersions);
            DetachTrackedComponent(component);
            _logger.LogWarning(
                ex,
                "Concurrent template save aborted the retirement of component {SharedComponentId}; nothing was applied",
                component.Id);

            return Error.Conflict(
                WorkflowSavePipeline.CONCURRENT_SAVE_CODE,
                "A consumer template was modified concurrently during the retirement; nothing was applied. Retry the operation.");
        }
        catch (DbUpdateException ex) when (ex.Entries.Any(e => e.Entity is SharedComponentVersion))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            DetachPendingVersions(pendingVersions);
            DetachTrackedComponent(component);
            _logger.LogWarning(
                ex,
                "Concurrent definition append aborted the retirement of component {SharedComponentId}; nothing was applied",
                component.Id);

            return Error.Conflict(
                SharedComponentErrorCodes.CONCURRENT_APPEND,
                "A referencing container definition moved concurrently during the retirement; nothing was applied. Retry the operation.");
        }
        catch
        {
            DetachPendingVersions(pendingVersions);
            DetachTrackedComponent(component);

            throw;
        }
    }

    private async Task<ErrorOr<List<RetireInliningPlan>>> BuildRetireInliningPlansAsync(
        SharedComponent retiring,
        CancellationToken cancellationToken)
    {
        List<RetireInliningPlan> plans = [];
        if (retiring.Kind != SharedComponentKind.Block)
        {
            return plans;
        }

        List<SharedComponent> sectionComponents = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(c => c.Kind == SharedComponentKind.Section)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
        List<string> containerIds = [.. sectionComponents.Select(c => c.Id)];
        IQueryable<SharedComponentVersion> versions = _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking();
        Dictionary<string, SharedComponentVersion> headsByContainer = (await versions
            .Where(v => containerIds.Contains(v.SharedComponentId)
                && v.PayloadAbsentAt == null
                && !versions.Any(o => o.SharedComponentId == v.SharedComponentId && o.SequenceNumber > v.SequenceNumber))
            .ToListAsync(cancellationToken))
            .ToDictionary(v => v.SharedComponentId, StringComparer.Ordinal);
        Block? retiringDefinition = null;
        foreach (SharedComponent container in sectionComponents)
        {
            if (!headsByContainer.TryGetValue(container.Id, out SharedComponentVersion? head))
            {
                continue;
            }

            Section? definition = Section.FromJson(head.ContentJson);
            if (definition is null)
            {
                return CorruptStoredHead(container.Id);
            }

            bool referencesRetiring = definition.Entries
                ?.Any(e => e.Kind == SectionEntryKind.ComponentRef
                    && string.Equals(e.SharedComponentId, retiring.Id, StringComparison.Ordinal)) == true;
            if (!referencesRetiring)
            {
                continue;
            }

            if (retiringDefinition is null)
            {
                SharedComponentVersion? retiringHead = await _coreUnitOfWork.Repository<SharedComponentVersion>()
                    .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(retiring.Id), cancellationToken);
                if (retiringHead is null || retiringHead.PayloadAbsentAt is not null)
                {
                    return CorruptStoredHead(retiring.Id);
                }

                retiringDefinition = Block.FromJson(retiringHead.ContentJson);
                if (retiringDefinition is null)
                {
                    return CorruptStoredHead(retiring.Id);
                }
            }

            plans.Add(new RetireInliningPlan(
                container,
                head,
                BakeRetiredRefs(definition, retiring.Id, retiringDefinition)));
        }

        return plans;
    }

    private NormalizedDefinition BakeRetiredRefs(Section definition, string retiringId, Block retiringDefinition)
    {
        List<SectionEntry> rewritten = new(definition.Entries!.Count);
        foreach (SectionEntry entry in definition.Entries!)
        {
            bool bakes = entry.Kind == SectionEntryKind.ComponentRef
                && string.Equals(entry.SharedComponentId, retiringId, StringComparison.Ordinal);
            if (!bakes)
            {
                rewritten.Add(entry);
                continue;
            }

            Block baked = retiringDefinition.CloneToSection(definition, -1, entry.SlotTag);
            if (entry.Bindings is { Count: > 0 })
            {
                baked.ApplyReplacementTagRenames(entry.Bindings);
            }

            rewritten.Add(new SectionEntry { SlotTag = entry.SlotTag, Kind = SectionEntryKind.Inline });
        }

        int inlineOrder = 0;
        foreach (SectionEntry entry in rewritten)
        {
            if (entry.Kind != SectionEntryKind.Inline)
            {
                continue;
            }

            Block? slotBlock = definition.FindBlockByReplacementTag(entry.SlotTag);
            if (slotBlock is not null)
            {
                slotBlock.Order = inlineOrder++;
            }
        }

        definition.Entries = rewritten.All(e => e.Kind == SectionEntryKind.Inline) ? null : rewritten;

        string definitionJson = _normalizer.ToDefinitionJson(definition);

        return new NormalizedDefinition(
            definitionJson,
            _normalizer.ComputeContentHashOfDefinitionJson(definitionJson),
            null,
            Section.FromJson(definitionJson));
    }

    private sealed record RetireInliningPlan(
        SharedComponent Container,
        SharedComponentVersion Head,
        NormalizedDefinition Baked);

    private void DetachTrackedComponent(SharedComponent component)
    {
        _coreDbContext.Entry(component).State = EntityState.Detached;
    }

    private sealed record ResolvedDecision(
        SharedComponentReconcileDecision Decision,
        SharedComponentTarget Target,
        ISharedComponentLinkable Node,
        ResolvedEntry? Entry);

    private sealed record ResolvedEntry(string Coordinate, Block? LiveBlock)
    {
        public string? LinkedInnerComponentId => LiveBlock is
        {
            SharedComponentLinkState: SharedComponentLinkState.Linked,
            SharedComponentId.Length: > 0,
        }
            ? LiveBlock.SharedComponentId
            : null;
    }

    private sealed record ComponentPushUnit(
        string ComponentId,
        NormalizedDefinition Normalized,
        bool AcknowledgeSignatureOrphans,
        bool AcknowledgeOverwrittenEntries,
        string? RefToken,
        bool IsRefPush);

    private sealed record ComponentUpdatePlan(
        SharedComponent Component,
        SharedComponentVersion Head,
        NormalizedDefinition Normalized,
        bool OrphansAcknowledged,
        bool OverwrittenEntriesAcknowledged,
        bool AlreadyCurrent);

    public async Task<List<SharedComponentConsumer>> GetConsumersAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
        return await SharedComponentConsumerScan.GetConsumersForComponentAsync(
            _coreUnitOfWork, componentId, cancellationToken);
    }

    public async Task<List<SharedComponentBaselineImpact>> GetAffectedBaselinesAsync(
        IReadOnlyCollection<string> workflowTypeIds,
        CancellationToken cancellationToken)
    {
        if (workflowTypeIds.Count == 0)
        {
            return [];
        }

        IReadOnlyList<WorkflowBaselineRegistration> registrations = await _baselines.ActiveRegistrationsAsync(workflowTypeIds, cancellationToken);

        Dictionary<string, string> headHashesByTypeId = await GetHeadHashesByTypeIdAsync(
            [.. registrations.Select(r => r.WorkflowTypeId).Distinct(StringComparer.Ordinal)],
            cancellationToken);

        return [.. registrations
            .Select(r => new SharedComponentBaselineImpact(
                r.Id,
                r.WorkflowTypeId,
                r.OrganizationId,
                r.CounterpartyId,
                r.CounterpartyName,
                r.BlessedContentHash is not null
                    && string.Equals(
                        r.BlessedContentHash,
                        headHashesByTypeId.GetValueOrDefault(r.WorkflowTypeId),
                        StringComparison.Ordinal)))
            .OrderBy(i => i.WorkflowTypeId, StringComparer.Ordinal)
            .ThenBy(i => i.OrganizationId, StringComparer.Ordinal)
            .ThenBy(i => i.CounterpartyId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The head hash of each workflow type in two batched queries instead of one head election
    /// per type. The election mirrors <see cref="LatestWorkflowVersionByTypeOrdering"/>: newest
    /// CreatedAt wins, hash then id break the (client-minted, hence rare) exact-timestamp ties.
    /// </summary>
    private async Task<Dictionary<string, string>> GetHeadHashesByTypeIdAsync(
        List<string> workflowTypeIds,
        CancellationToken cancellationToken)
    {
        if (workflowTypeIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var latestDates = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => workflowTypeIds.Contains(v.WorkflowTypeId))
            .GroupBy(v => v.WorkflowTypeId)
            .Select(g => new { WorkflowTypeId = g.Key, MaxCreatedAt = g.Max(v => v.CreatedAt) })
            .ToListAsync(cancellationToken);
        if (latestDates.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        Dictionary<string, DateTime> maxByType = latestDates.ToDictionary(
            d => d.WorkflowTypeId, d => d.MaxCreatedAt, StringComparer.Ordinal);
        List<DateTime> maxDates = [.. latestDates.Select(d => d.MaxCreatedAt).Distinct()];
        var headRows = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(v => workflowTypeIds.Contains(v.WorkflowTypeId) && maxDates.Contains(v.CreatedAt))
            .Select(v => new { v.WorkflowTypeId, v.CreatedAt, v.ContentHash, v.Id })
            .ToListAsync(cancellationToken);

        return headRows
            .Where(v => v.CreatedAt == maxByType[v.WorkflowTypeId])
            .GroupBy(v => v.WorkflowTypeId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(v => v.ContentHash, StringComparer.Ordinal)
                    .ThenByDescending(v => v.Id, StringComparer.Ordinal)
                    .First().ContentHash,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// What appending <paramref name="candidate"/> as the new head would do beyond the consumer
    /// rewrites themselves: baseline registrations whose freshness the fan-out saves would stale,
    /// override rows the slot delta would delete (their block drops with a removed slot) or leave
    /// inert (a signature-bearing matched slot loses the placeholder they key on), and the slot
    /// delta itself for section components. Without a candidate the projection degrades to the
    /// blast radius every component mutation shares — baselines only. The reconcile-apply path
    /// re-runs this inside its transaction; previews are advisory and go stale by design.
    /// </summary>
    internal async Task<SharedComponentUpdateImpact> GetUpdateImpactAsync(
        SharedComponent component,
        NormalizedDefinition? oldDefinition,
        NormalizedDefinition? candidate,
        List<SharedComponentConsumer> consumers,
        CancellationToken cancellationToken,
        bool includeAffectedBaselines = true)
    {
        List<SharedComponentBaselineImpact> affectedBaselines = includeAffectedBaselines
            ? await GetAffectedBaselinesAsync([.. consumers.Select(c => c.WorkflowTypeId)], cancellationToken)
            : [];
        if (oldDefinition is null || candidate is null)
        {
            return new SharedComponentUpdateImpact(affectedBaselines, [], null);
        }

        if (component.Kind == SharedComponentKind.Block)
        {
            IReadOnlyList<string> inertPlaceholders = SharedComponentReconciler.RemovedSignaturePlaceholders(
                oldDefinition.BlockDefinition!,
                candidate.BlockDefinition!);
            List<SharedComponentOverrideImpact> blockOrphans = await CollectBlockOverrideImpactsAsync(
                component.Id, consumers, inertPlaceholders, cancellationToken);

            return new SharedComponentUpdateImpact(affectedBaselines, blockOrphans, null);
        }

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(
            oldDefinition.SectionDefinition!,
            candidate.SectionDefinition!);
        IReadOnlyDictionary<string, IReadOnlyList<string>> removedBySlotTag =
            RemovedSlotPlaceholders(oldDefinition.SectionDefinition!, delta.RemovedSlotTags);
        IReadOnlyDictionary<string, IReadOnlyList<string>> inertBySlotTag =
            SharedComponentReconciler.ComputeInertPlaceholders(
                oldDefinition.SectionDefinition!,
                candidate.SectionDefinition!,
                delta.MatchedSlotTags,
                delta.RescuedSlots);
        List<SharedComponentOverrideImpact> orphans = await CollectSectionOverrideImpactsAsync(
            component.Id, consumers, removedBySlotTag, inertBySlotTag, cancellationToken);

        return new SharedComponentUpdateImpact(affectedBaselines, orphans, delta);
    }

    private async Task<List<SharedComponentOverrideImpact>> CollectSectionOverrideImpactsAsync(
        string componentId,
        List<SharedComponentConsumer> consumers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> removedPlaceholdersBySlotTag,
        IReadOnlyDictionary<string, IReadOnlyList<string>> inertPlaceholdersBySlotTag,
        CancellationToken cancellationToken)
    {
        if (removedPlaceholdersBySlotTag.Count == 0 && inertPlaceholdersBySlotTag.Count == 0)
        {
            return [];
        }

        List<OverrideProbe> probes = [];
        foreach (SharedComponentConsumer consumer in consumers)
        {
            Workflow? consumerWorkflow = await _coreUnitOfWork.Workflows
                .GetByTypeIdAsNoTrackingAsync(consumer.WorkflowTypeId);
            if (consumerWorkflow is null)
            {
                continue;
            }

            foreach (Section instance in consumerWorkflow.Sections.Where(s => IsLinkedInstance(s, componentId)))
            {
                AddInstanceProbes(
                    probes,
                    consumer.WorkflowTypeId,
                    instance,
                    removedPlaceholdersBySlotTag,
                    inertPlaceholdersBySlotTag);
            }
        }

        return await ResolveProbesAsync(probes, cancellationToken);
    }

    private async Task<List<SharedComponentOverrideImpact>> CollectBlockOverrideImpactsAsync(
        string componentId,
        List<SharedComponentConsumer> consumers,
        IReadOnlyList<string> inertPlaceholders,
        CancellationToken cancellationToken)
    {
        if (inertPlaceholders.Count == 0)
        {
            return [];
        }

        List<OverrideProbe> probes = [];
        foreach (SharedComponentConsumer consumer in consumers)
        {
            Workflow? consumerWorkflow = await _coreUnitOfWork.Workflows
                .GetByTypeIdAsNoTrackingAsync(consumer.WorkflowTypeId);
            if (consumerWorkflow is null)
            {
                continue;
            }

            foreach (Block instance in consumerWorkflow.GetAllBlocks())
            {
                if (IsLinkedInstance(instance, componentId) && !string.IsNullOrEmpty(instance.StableBlockId))
                {
                    probes.Add(new OverrideProbe(
                        consumer.WorkflowTypeId,
                        instance.StableBlockId,
                        instance.ReplacementTag,
                        inertPlaceholders,
                        SharedComponentOverrideImpactKind.GoingInert));
                }
            }
        }

        return await ResolveProbesAsync(probes, cancellationToken);
    }

    private static void AddInstanceProbes(
        List<OverrideProbe> probes,
        string workflowTypeId,
        Section instance,
        IReadOnlyDictionary<string, IReadOnlyList<string>> removedPlaceholdersBySlotTag,
        IReadOnlyDictionary<string, IReadOnlyList<string>> inertPlaceholdersBySlotTag)
    {
        (Dictionary<string, string> bindings, Dictionary<string, Block> instanceByTag) =
            SharedComponentReconciler.SnapshotInstance(instance);
        foreach ((string removedTag, IReadOnlyList<string> placeholders) in removedPlaceholdersBySlotTag)
        {
            AddProbe(
                probes,
                workflowTypeId,
                bindings,
                instanceByTag,
                removedTag,
                placeholders,
                SharedComponentOverrideImpactKind.Deleted);
        }

        foreach ((string slotTag, IReadOnlyList<string> placeholders) in inertPlaceholdersBySlotTag)
        {
            AddProbe(
                probes,
                workflowTypeId,
                bindings,
                instanceByTag,
                slotTag,
                placeholders,
                SharedComponentOverrideImpactKind.GoingInert);
        }
    }

    private static void AddProbe(
        List<OverrideProbe> probes,
        string workflowTypeId,
        Dictionary<string, string> bindings,
        Dictionary<string, Block> instanceByTag,
        string slotTag,
        IReadOnlyList<string> placeholders,
        SharedComponentOverrideImpactKind kind)
    {
        string expectedTag = bindings.GetValueOrDefault(slotTag, slotTag);
        if (!instanceByTag.TryGetValue(expectedTag, out Block? block) || string.IsNullOrEmpty(block.StableBlockId))
        {
            return;
        }

        probes.Add(new OverrideProbe(workflowTypeId, block.StableBlockId, block.ReplacementTag, placeholders, kind));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> RemovedSlotPlaceholders(
        Section oldDefinition,
        IReadOnlyList<string> removedSlotTags)
    {
        Dictionary<string, IReadOnlyList<string>> placeholdersBySlotTag = new(StringComparer.Ordinal);
        if (removedSlotTags.Count == 0)
        {
            return placeholdersBySlotTag;
        }

        Dictionary<string, Block> definitionByTag = new(StringComparer.Ordinal);
        foreach (Block block in oldDefinition.Blocks)
        {
            if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
            {
                _ = definitionByTag.TryAdd(block.ReplacementTag, block);
            }
        }

        foreach (string removedTag in removedSlotTags)
        {
            if (definitionByTag.TryGetValue(removedTag, out Block? definitionBlock)
                && DeclaredPlaceholders(definitionBlock) is { Count: > 0 } declared)
            {
                placeholdersBySlotTag[removedTag] = declared;
            }
        }

        return placeholdersBySlotTag;
    }

    private static IReadOnlyList<string> DeclaredPlaceholders(Block definitionBlock)
    {
        return definitionBlock.Properties is IHasSignatureFields { SignatureFields: { Count: > 0 } fields }
            ? [.. fields.Keys]
            : [];
    }

    /// <summary>
    /// Removes workflow-level signature field definitions whose acknowledged Deleted-kind orphan
    /// placeholder is no longer referenced by any block after the graft. The save pipeline's
    /// synchronizer then sees the definition disappear and cascade-deletes the override rows for
    /// every organization under the fan-out's confirmed-deletion flag.
    /// </summary>
    private static void PruneOrphanedSignatureDefinitions(
        Workflow workflow,
        string workflowTypeId,
        IReadOnlyList<SharedComponentOverrideImpact> orphans)
    {
        List<string> deletedNames = [.. orphans
            .Where(o => o.Kind == SharedComponentOverrideImpactKind.Deleted
                && string.Equals(o.WorkflowTypeId, workflowTypeId, StringComparison.Ordinal))
            .Select(o => o.PlaceholderName)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (deletedNames.Count == 0)
        {
            return;
        }

        Dictionary<string, List<Block>> references = workflow.GetSignatureFieldReferences();
        foreach (string name in deletedNames)
        {
            if (!references.TryGetValue(name, out List<Block>? referencingBlocks) || referencingBlocks.Count > 0)
            {
                continue;
            }

            List<WorkflowSignatureField> unreferenced = [.. workflow.SignatureFields
                .Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))];
            foreach (WorkflowSignatureField field in unreferenced)
            {
                _ = workflow.SignatureFields.Remove(field);
            }
        }
    }

    private async Task<List<SharedComponentOverrideImpact>> ResolveProbesAsync(
        List<OverrideProbe> probes,
        CancellationToken cancellationToken)
    {
        if (probes.Count == 0)
        {
            return [];
        }

        List<string> typeIds = [.. probes.Select(p => p.WorkflowTypeId).Distinct(StringComparer.Ordinal)];
        var activeRows = await _coreUnitOfWork.Repository<SignatureFieldOverride>()
            .GetAll()
            .AsNoTracking()
            .Where(o => typeIds.Contains(o.WorkflowTypeId))
            .Select(o => new { o.OrganizationId, o.WorkflowTypeId, o.PlaceholderName })
            .ToListAsync(cancellationToken);
        var pendingRows = await _coreUnitOfWork.Repository<PendingSignatureOverride>()
            .GetAll()
            .AsNoTracking()
            .Where(o => typeIds.Contains(o.WorkflowTypeId))
            .Select(o => new { o.OrganizationId, o.WorkflowTypeId, o.PlaceholderName })
            .ToListAsync(cancellationToken);
        List<(string OrganizationId, string WorkflowTypeId, string PlaceholderName, bool IsPending)> rows =
            [.. activeRows.Select(r => (r.OrganizationId, r.WorkflowTypeId, r.PlaceholderName, false)),
                .. pendingRows.Select(r => (r.OrganizationId, r.WorkflowTypeId, r.PlaceholderName, true))];

        HashSet<(string, string, string, bool)> claimedRows = [];
        HashSet<SharedComponentOverrideImpact> impacts = [];
        foreach (OverrideProbe probe in probes)
        {
            foreach ((string organizationId, string workflowTypeId, string placeholderName, bool isPending) in rows
                .Where(r => string.Equals(r.WorkflowTypeId, probe.WorkflowTypeId, StringComparison.Ordinal)
                    && probe.Placeholders.Contains(r.PlaceholderName, StringComparer.Ordinal)))
            {
                if (!claimedRows.Add((organizationId, workflowTypeId, placeholderName, isPending)))
                {
                    continue;
                }

                _ = impacts.Add(new SharedComponentOverrideImpact(
                    organizationId,
                    workflowTypeId,
                    probe.StableBlockId,
                    probe.BlockTag,
                    placeholderName,
                    isPending,
                    probe.Kind));
            }
        }

        return [.. impacts
            .OrderBy(i => i.WorkflowTypeId, StringComparer.Ordinal)
            .ThenBy(i => i.OrganizationId, StringComparer.Ordinal)
            .ThenBy(i => i.BlockTag, StringComparer.Ordinal)
            .ThenBy(i => i.PlaceholderName, StringComparer.Ordinal)
            .ThenBy(i => i.IsPending)];
    }

    private ErrorOr<List<SectionReconcileResult>> GraftConsumer(
        SharedComponent component,
        Workflow consumerWorkflow,
        NormalizedDefinition normalized,
        List<SharedComponentVersion> chain,
        Dictionary<string, Section?> oldDefinitionsByHash,
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads,
        StoredVersionHashes storedHashes,
        bool acknowledgeOverwrittenEntries)
    {
        if (component.Kind == SharedComponentKind.Block)
        {
            List<Block> linkedInstances = [.. consumerWorkflow.GetAllBlocks().Where(b => IsLinkedInstance(b, component.Id))];
            if (!acknowledgeOverwrittenEntries)
            {
                List<string> overwrittenInstanceTags = [.. linkedInstances
                    .Where(instance => _reconciler.LinkedBlockHoldsLocalEdits(
                        instance,
                        component.Id,
                        ChainJsonByHash(chain, instance.SharedComponentVersionHash),
                        normalized.ContentHash,
                        normalized.DefinitionJson))
                    .Select(instance => instance.ReplacementTag)];
                if (overwrittenInstanceTags.Count > 0)
                {
                    return OverwrittenEntriesError(
                        component.Id, consumerWorkflow.WorkflowTypeId ?? string.Empty, overwrittenInstanceTags);
                }
            }

            foreach (Block instance in linkedInstances)
            {
                instance.ApplySharedDefinition(normalized.BlockDefinition!);
                if (instance.SharedComponentBindings is { Count: > 0 })
                {
                    instance.ApplyReplacementTagRenames(instance.SharedComponentBindings);
                }

                instance.SharedComponentVersionHash = normalized.ContentHash;
            }

            return new List<SectionReconcileResult>();
        }

        List<SectionReconcileResult> sectionReconciles = [];
        List<string> overwrittenInlineSlots = [];
        foreach (Section instance in consumerWorkflow.Sections.Where(s => IsLinkedInstance(s, component.Id)).ToList())
        {
            Section? oldDefinition = ResolveOldDefinition(instance.SharedComponentVersionHash, chain, oldDefinitionsByHash);
            if (!acknowledgeOverwrittenEntries)
            {
                overwrittenInlineSlots.AddRange(
                    _reconciler.OverwrittenInlineSlots(instance, oldDefinition, normalized.SectionDefinition!));
            }

            SectionReconcileResult reconcile = _reconciler.ApplySectionDefinition(
                consumerWorkflow,
                instance,
                oldDefinition,
                normalized.SectionDefinition!,
                refHeads,
                storedHashes);
            if (reconcile.StaleRefSlots.Count > 0 || reconcile.DroppedRefSlots.Count > 0)
            {
                _logger.LogWarning(
                    "Grafting component {SharedComponentId} into workflow type {WorkflowTypeId}, section '{SectionTitle}': "
                    + "unresolvable ref heads left stale copies in slots [{StaleRefSlots}] and landed nothing for slots [{DroppedRefSlots}]",
                    component.Id,
                    consumerWorkflow.WorkflowTypeId,
                    instance.Title,
                    string.Join(", ", reconcile.StaleRefSlots),
                    string.Join(", ", reconcile.DroppedRefSlots));
            }

            sectionReconciles.Add(reconcile);
            instance.SharedComponentVersionHash = normalized.ContentHash;
        }

        List<string> overwrittenEntrySlots =
            [.. overwrittenInlineSlots,
                .. sectionReconciles.SelectMany(reconcile => reconcile.OverwrittenLocalRefSlots)];

        return overwrittenEntrySlots.Count > 0 && !acknowledgeOverwrittenEntries
            ? OverwrittenEntriesError(component.Id, consumerWorkflow.WorkflowTypeId ?? string.Empty, overwrittenEntrySlots)
            : sectionReconciles;
    }

    private Task<StoredVersionHashes> LoadStoredContentHashesAsync(
        Workflow graph,
        IReadOnlyCollection<(string ComponentId, string ContentHash)> heads,
        CancellationToken cancellationToken)
    {
        HashSet<string> componentIds = new(heads.Select(head => head.ComponentId), StringComparer.Ordinal);
        IEnumerable<(string, string)> linkStamps = graph.GetAllBlocks()
            .Where(block => block.SharedComponentId is { } componentId
                && componentIds.Contains(componentId)
                && block.SharedComponentVersionHash is not null)
            .Select(block => (block.SharedComponentId!, block.SharedComponentVersionHash!));

        return StoredVersionHashes.LoadContentHashesAsync(
            _coreUnitOfWork, _normalizer, heads.Concat(linkStamps), cancellationToken);
    }

    private static List<(string ComponentId, string ContentHash)> RefHeadVersions(
        IReadOnlyDictionary<string, SharedComponentRefHead>? refHeads)
    {
        return refHeads is null ? [] : [.. refHeads.Select(pair => (pair.Key, pair.Value.ContentHash))];
    }

    private async Task RestampInSyncLinkedNodesAsync(
        IReadOnlyCollection<Workflow> graphs,
        Dictionary<string, string> currentHeadHashesById,
        CancellationToken cancellationToken)
    {
        List<ISharedComponentLinkable> linkedNodes = [];
        foreach (Workflow graph in graphs)
        {
            linkedNodes.AddRange(graph.Sections.Where(s => s.IsActivelyLinked()));
            linkedNodes.AddRange(graph.GetAllBlocks().Where(b => b.IsActivelyLinked()));
        }

        List<string> unknownIds = [.. linkedNodes
            .Select(n => n.SharedComponentId!)
            .Where(id => !currentHeadHashesById.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)];
        if (unknownIds.Count > 0)
        {
            Dictionary<string, SharedComponentProjections.HeadTokens> loadedHeads =
                await SharedComponentProjections.GetHeadsAsync(_coreUnitOfWork, unknownIds, cancellationToken);
            foreach ((string componentId, SharedComponentProjections.HeadTokens tokens) in loadedHeads)
            {
                currentHeadHashesById[componentId] = tokens.ContentHash;
            }
        }

        List<(ISharedComponentLinkable Node, string HeadHash)> staleStamped = [];
        foreach (ISharedComponentLinkable node in linkedNodes)
        {
            if (currentHeadHashesById.TryGetValue(node.SharedComponentId!, out string? headHash)
                && !string.Equals(node.SharedComponentVersionHash, headHash, StringComparison.Ordinal)
                && node is not Section { SharedComponentSlotStates.Count: > 0 })
            {
                staleStamped.Add((node, headHash));
            }
        }

        StoredVersionHashes storedHashes = await StoredVersionHashes.LoadContentHashesAsync(
            _coreUnitOfWork,
            _normalizer,
            staleStamped.Select(stale => (stale.Node.SharedComponentId!, stale.HeadHash)),
            cancellationToken);
        foreach ((ISharedComponentLinkable node, string headHash) in staleStamped)
        {
            string instanceHash = node switch
            {
                Block block => _reconciler.ComputeInstanceContentHash(block),
                Section section => _reconciler.ComputeInstanceContentHash(section),
                _ => string.Empty,
            };
            if (string.Equals(instanceHash, storedHashes.Of(node.SharedComponentId, headHash), StringComparison.Ordinal))
            {
                node.SharedComponentVersionHash = headHash;
            }
        }
    }

    private Block? ResolveOldBlockDefinition(
        string? linkVersionHash,
        List<SharedComponentVersion> chain,
        Dictionary<string, Block?> oldBlockDefinitionsByHash)
    {
        if (string.IsNullOrWhiteSpace(linkVersionHash))
        {
            return null;
        }

        if (oldBlockDefinitionsByHash.TryGetValue(linkVersionHash, out Block? cached))
        {
            return cached;
        }

        SharedComponentVersion? source = _reconciler.ResolveDefinitionVersion(chain, linkVersionHash);
        Block? oldDefinition = source is null ? null : Block.FromJson(source.ContentJson);
        oldBlockDefinitionsByHash[linkVersionHash] = oldDefinition;

        return oldDefinition;
    }

    private Section? ResolveOldDefinition(
        string? linkVersionHash,
        List<SharedComponentVersion> chain,
        Dictionary<string, Section?> oldDefinitionsByHash)
    {
        if (string.IsNullOrWhiteSpace(linkVersionHash))
        {
            return null;
        }

        if (oldDefinitionsByHash.TryGetValue(linkVersionHash, out Section? cached))
        {
            return cached;
        }

        SharedComponentVersion? source = _reconciler.ResolveDefinitionVersion(chain, linkVersionHash);
        Section? oldDefinition = source is null ? null : Section.FromJson(source.ContentJson);
        oldDefinitionsByHash[linkVersionHash] = oldDefinition;

        return oldDefinition;
    }

    private string? ChainJsonByHash(List<SharedComponentVersion> chain, string? linkVersionHash)
    {
        return string.IsNullOrWhiteSpace(linkVersionHash)
            ? null
            : _reconciler.ResolveDefinitionVersion(chain, linkVersionHash)?.ContentJson;
    }

    internal ErrorOr<NormalizedDefinition> NormalizeDefinition(SharedComponent component, string definitionJson)
    {
        if (component.Kind == SharedComponentKind.Block)
        {
            if (SharedComponentContentNormalizer.IsSectionDefinition(definitionJson))
            {
                return DefinitionValidation(
                    "The definition carries a Blocks collection — that is a section shape; this component "
                    + $"is a block. [{SharedComponentErrorCodes.KIND_MISMATCH}]");
            }

            Block? parsedBlock = Block.FromJson(definitionJson);
            if (parsedBlock is null)
            {
                return DefinitionValidation(
                    $"The definition could not be read as a block. [{SharedComponentErrorCodes.CORRUPT_DEFINITION}]");
            }

            string blockJson = _normalizer.ToDefinitionJson(parsedBlock);

            return new NormalizedDefinition(
                blockJson,
                _normalizer.ComputeContentHashOfDefinitionJson(blockJson),
                Block.FromJson(blockJson),
                null);
        }

        Section? parsedSection = Section.FromJson(definitionJson);
        if (parsedSection is null)
        {
            return DefinitionValidation(
                $"The definition could not be read as a section. [{SharedComponentErrorCodes.CORRUPT_DEFINITION}]");
        }

        if (parsedSection.IsSeparator)
        {
            return DefinitionValidation(
                $"Separator sections carry no shareable content. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        if (parsedSection.ExpandedFromParameterizedKey is not null)
        {
            return DefinitionValidation(
                "An expanded parameterized section variant cannot become a component definition; submit the "
                + $"source section instead. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        bool hasRefEntries = parsedSection.Entries
            ?.Any(e => e.Kind == SectionEntryKind.ComponentRef) == true;
        if (parsedSection.Blocks.Count == 0 && !hasRefEntries)
        {
            return DefinitionValidation(
                "The definition has no inner blocks — either a block shape was submitted for a section "
                + "component, or the section is empty; neither carries shareable slots. "
                + $"[{SharedComponentErrorCodes.KIND_MISMATCH}]");
        }

        List<string> innerTags = [.. parsedSection.Blocks.Select(b => b.ReplacementTag)];
        if (innerTags.Any(string.IsNullOrWhiteSpace))
        {
            return DefinitionValidation(
                "Every block inside a section definition needs a non-empty ReplacementTag; the bindings "
                + $"contract keys on them. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        List<string> duplicates = [.. innerTags
            .GroupBy(t => t, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)];
        if (duplicates.Count > 0)
        {
            return DefinitionValidation(
                "Inner ReplacementTags must be unique within a section definition; duplicated: "
                + $"{string.Join(", ", duplicates)}. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        if (parsedSection.Entries is { Count: > 0 })
        {
            Error? entriesError = ValidateEntries(parsedSection);
            if (entriesError is not null)
            {
                return entriesError.Value;
            }
        }

        string sectionJson = _normalizer.ToDefinitionJson(parsedSection);

        return new NormalizedDefinition(
            sectionJson,
            _normalizer.ComputeContentHashOfDefinitionJson(sectionJson),
            null,
            Section.FromJson(sectionJson));
    }

    internal async Task<ErrorOr<(string DefinitionJson, string ContentHash)>> NormalizeAndValidateForCreateAsync(
        SharedComponent component,
        string definitionJson,
        CancellationToken cancellationToken)
    {
        ErrorOr<NormalizedDefinition> normalizedResult = NormalizeDefinition(component, definitionJson);
        if (normalizedResult.IsError)
        {
            return normalizedResult.Errors;
        }

        NormalizedDefinition normalized = normalizedResult.Value;
        List<Error>? agentGuard = AgentGuardErrors(normalized);
        if (agentGuard is not null)
        {
            return agentGuard;
        }

        Error? refGuard = await ValidateRefTargetsAsync(
            normalized.SectionDefinition, pendingRefTargetId: null, cancellationToken);
        if (refGuard is not null)
        {
            return refGuard.Value;
        }

        return (normalized.DefinitionJson, normalized.ContentHash);
    }

    private static Error? ValidateEntries(Section parsedSection)
    {
        List<SectionEntry> entries = parsedSection.Entries!;
        if (entries.Any(e => string.IsNullOrWhiteSpace(e.SlotTag)))
        {
            return DefinitionValidation(
                $"Every entry needs a non-empty SlotTag. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        List<string> duplicated = [.. entries
            .GroupBy(e => e.SlotTag, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)];
        if (duplicated.Count > 0)
        {
            return DefinitionValidation(
                "Entry SlotTags must be unique within a section definition; duplicated: "
                + $"{string.Join(", ", duplicated)}. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        foreach (SectionEntry entry in entries)
        {
            switch (entry.Kind)
            {
                case SectionEntryKind.Inline:
                    if (!string.IsNullOrEmpty(entry.SharedComponentId))
                    {
                        return DefinitionValidation(
                            $"Inline entry '{entry.SlotTag}' must not carry a SharedComponentId; inline "
                            + $"content lives in Blocks. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
                    }

                    if (entry.Bindings is { Count: > 0 })
                    {
                        return DefinitionValidation(
                            $"Inline entry '{entry.SlotTag}' must not carry Bindings; bindings belong to "
                            + $"ComponentRef entries. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
                    }

                    break;
                case SectionEntryKind.ComponentRef:
                    if (string.IsNullOrEmpty(entry.SharedComponentId))
                    {
                        return DefinitionValidation(
                            $"ComponentRef entry '{entry.SlotTag}' needs a SharedComponentId. "
                            + $"[{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
                    }

                    break;
                default:
                    return DefinitionValidation(
                        $"Entry '{entry.SlotTag}' has an unknown Kind. "
                        + $"[{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
            }
        }

        HashSet<string> inlineTags = new(
            entries.Where(e => e.Kind == SectionEntryKind.Inline).Select(e => e.SlotTag),
            StringComparer.Ordinal);
        HashSet<string> blockTags = new(
            parsedSection.Blocks.Select(b => b.ReplacementTag),
            StringComparer.Ordinal);
        if (!inlineTags.SetEquals(blockTags))
        {
            return DefinitionValidation(
                "Inline entries and Blocks must correspond one-to-one by tag; an entry without content "
                + "or a block without an entry breaks the definition shape. "
                + $"[{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        if (parsedSection.Blocks.Any(b => !string.IsNullOrEmpty(b.SharedComponentId)))
        {
            return DefinitionValidation(
                "A definition that records Entries must keep its Blocks plain; link fields on inner "
                + $"blocks double-encode the refs. [{SharedComponentErrorCodes.INVALID_LINK_SHAPE}]");
        }

        return null;
    }

    private async Task<Error?> ValidateRefTargetsAsync(
        Section? sectionDefinition,
        string? pendingRefTargetId,
        CancellationToken cancellationToken)
    {
        List<string> targetIds = [.. SharedComponentRefHeads.CollectRefTargetIds(sectionDefinition)
            .Where(id => !string.Equals(id, pendingRefTargetId, StringComparison.Ordinal))];
        if (targetIds.Count == 0)
        {
            return null;
        }

        Dictionary<string, SharedComponent> targets = (await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(c => targetIds.Contains(c.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);
        IQueryable<SharedComponentVersion> versions = _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking();
        Dictionary<string, DateTime?> headPayloadAbsence = (await versions
            .Where(v => targetIds.Contains(v.SharedComponentId)
                && !versions.Any(o => o.SharedComponentId == v.SharedComponentId && o.SequenceNumber > v.SequenceNumber))
            .Select(v => new { v.SharedComponentId, v.PayloadAbsentAt })
            .ToListAsync(cancellationToken))
            .ToDictionary(v => v.SharedComponentId, v => v.PayloadAbsentAt, StringComparer.Ordinal);

        foreach (string targetId in targetIds)
        {
            if (!targets.TryGetValue(targetId, out SharedComponent? target))
            {
                return DefinitionValidation(
                    $"Entry references component '{targetId}', which does not exist. "
                    + $"[{SharedComponentErrorCodes.UNKNOWN_COMPONENT}]");
            }

            if (target.Kind != SharedComponentKind.Block)
            {
                return DefinitionValidation(
                    $"Entry references component '{targetId}', which is a {target.Kind} component; "
                    + $"section entries can only reference Block components. "
                    + $"[{SharedComponentErrorCodes.KIND_MISMATCH}]");
            }

            if (target.State != SharedComponentState.Active)
            {
                return DefinitionValidation(
                    $"Entry references component '{targetId}', which is retired; unretire it before "
                    + $"referencing it. [{SharedComponentErrorCodes.RETIRED}]");
            }

            if (!headPayloadAbsence.TryGetValue(targetId, out DateTime? payloadAbsentAt) || payloadAbsentAt is not null)
            {
                return DefinitionValidation(
                    $"Entry references component '{targetId}', whose head version has no stored payload; "
                    + $"a ref entry needs materializable content. [{SharedComponentErrorCodes.PAYLOAD_ABSENT}]");
            }
        }

        return null;
    }

    private async Task<SharedComponentVersion?> TryReadHeadAsync(string componentId, CancellationToken cancellationToken)
    {
        try
        {
            return await _coreUnitOfWork.Repository<SharedComponentVersion>()
                .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(componentId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not re-read the head of component {SharedComponentId} after a concurrent append",
                componentId);

            return null;
        }
    }

    private void DetachPendingVersion(SharedComponentVersion version)
    {
        _coreDbContext.Entry(version).State = EntityState.Detached;
    }

    private static bool IsLinkedInstance(ISharedComponentLinkable node, string componentId)
    {
        return string.Equals(node.SharedComponentId, componentId, StringComparison.Ordinal)
            && node.SharedComponentLinkState == SharedComponentLinkState.Linked;
    }

    private static Error DefinitionValidation(string description)
    {
        return Error.Validation(DEFINITION_JSON_ERROR_KEY, description);
    }

    private static Dictionary<string, object> BuildComponentHeadMetadata(SharedComponentVersion head)
    {
        return WorkflowSavePipeline.BuildHeadMetadata(head.Id, head.ContentHash, head.CreatedAt);
    }

    private static bool MatchesHead(SharedComponentVersion head, NormalizedDefinition candidate)
    {
        return string.Equals(head.ContentHash, candidate.ContentHash, StringComparison.Ordinal)
            && head.PayloadAbsentAt is null
            && string.Equals(head.ContentJson, candidate.DefinitionJson, StringComparison.Ordinal);
    }

    internal sealed record NormalizedDefinition(
        string DefinitionJson,
        string ContentHash,
        Block? BlockDefinition,
        Section? SectionDefinition);

    private sealed record OverrideProbe(
        string WorkflowTypeId,
        string StableBlockId,
        string BlockTag,
        IReadOnlyList<string> Placeholders,
        SharedComponentOverrideImpactKind Kind);
}

public enum SharedComponentConsumerChannel
{
    Direct = 1,
    ViaSection = 2,
}

public sealed record SharedComponentConsumer(
    string WorkflowTypeId,
    string WorkflowTypeName,
    int LinkedInstanceCount,
    string? CategoryId = null,
    string? CategoryName = null,
    SharedComponentConsumerChannel Channel = SharedComponentConsumerChannel.Direct,
    string? ViaSharedComponentId = null,
    string? ViaSharedComponentName = null);

public sealed record SharedComponentBaselineImpact(
    string RegistrationId,
    string WorkflowTypeId,
    string OrganizationId,
    string CounterpartyId,
    string? CounterpartyName,
    bool IsCurrentlyFresh);

public sealed record SharedComponentPendingRefTarget(
    string ComponentId,
    SharedComponentRefHead Head);

public sealed record SharedComponentAppendResult(
    string SharedComponentId,
    string HeadVersionId,
    string HeadContentHash,
    bool WasDeduped,
    IReadOnlyList<SharedComponentConsumerSaveResult> ConsumerSaves);

public sealed record SharedComponentConsumerSaveResult(
    string WorkflowTypeId,
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    SignatureSyncResult SignatureSync,
    IReadOnlyList<WorkflowStructuralValidationWarning> Warnings,
    IReadOnlyList<SectionReconcileResult> SectionReconciles)
{
    public IReadOnlyList<SharedComponentOverrideImpact> OrphanedOverrides { get; init; } = [];
}

public enum SharedComponentOverrideImpactKind
{
    Deleted = 1,
    GoingInert = 2,
}

public sealed record SharedComponentOverrideImpact(
    string OrganizationId,
    string WorkflowTypeId,
    string StableBlockId,
    string BlockTag,
    string PlaceholderName,
    bool IsPending,
    SharedComponentOverrideImpactKind Kind);

public sealed record SharedComponentUpdateImpact(
    IReadOnlyList<SharedComponentBaselineImpact> AffectedBaselines,
    IReadOnlyList<SharedComponentOverrideImpact> OrphanedSignatureOverrides,
    SharedComponentSlotDelta? SlotDelta);
