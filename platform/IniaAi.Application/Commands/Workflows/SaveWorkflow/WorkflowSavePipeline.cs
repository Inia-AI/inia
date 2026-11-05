using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Signatures;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Text;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Signatures;
using IniaAi.Domain.Specifications.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Commands.Workflows.SaveWorkflow;

/// <summary>
/// The transaction-free core of a Core template save: agent-block guard, optimistic-concurrency
/// check, agent-block model-id normalization, whole-graph replace, stable-id backfill, canonicalization, dedup, version append, and
/// signature-field sync. It never begins, commits, or rolls back a transaction — the caller owns one:
/// <see cref="SaveWorkflowCommandHandler"/> wraps a single save, and the shared-component
/// fan-out wraps one transaction around many consumer saves. A
/// <c>DbUpdateConcurrencyException</c> (the lost-race 0-row DELETE) escapes to that owner, which
/// must roll back before reading recovery state. A <c>narrativeOverride</c> replaces the
/// diff-derived version narrative — fan-out saves stamp what happened ("Updated from shared
/// component …") instead of paying a diff per consumer.
/// </summary>
public class WorkflowSavePipeline
{
    public const string VERSION_CONFLICT_CODE = "WorkflowTemplate.VersionConflict";
    public const string CONCURRENT_SAVE_CODE = "WorkflowTemplate.ConcurrentSave";
    public const string CURRENT_HEAD_VERSION_ID_METADATA_KEY = "currentHeadVersionId";
    public const string CURRENT_CONTENT_HASH_METADATA_KEY = "currentContentHash";
    public const string HEAD_CREATED_AT_UTC_METADATA_KEY = "headCreatedAtUtc";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ICoreDbContext _coreDbContext;
    private readonly WorkflowCanonicalizer _canonicalizer;
    private readonly WorkflowDiffService _diffService;
    private readonly WorkflowNarrativeGenerator _narrativeGenerator;
    private readonly AgentRuntimeSettings _agentRuntime;
    private readonly ILogger<WorkflowSavePipeline> _logger;
    private readonly WorkflowSaveGates _gates;
    private readonly ISuperAdminLookup _superAdmins;

    public WorkflowSavePipeline(
        ICoreUnitOfWork coreUnitOfWork,
        ICoreDbContext coreDbContext,
        WorkflowCanonicalizer canonicalizer,
        WorkflowDiffService diffService,
        WorkflowNarrativeGenerator narrativeGenerator,
        IOptions<AgentRuntimeSettings> agentRuntimeSettings,
        ILogger<WorkflowSavePipeline> logger,
        ISuperAdminLookup superAdmins,
        IAgentRunLimitsProvider? runLimitsProvider = null,
        IOptions<WorkflowExecutionJanitorSettings>? janitorSettings = null,
        IOptions<PlatformSettings>? platformSettings = null)
    {
        _superAdmins = superAdmins;
        _coreUnitOfWork = coreUnitOfWork;
        _coreDbContext = coreDbContext;
        _canonicalizer = canonicalizer;
        _diffService = diffService;
        _narrativeGenerator = narrativeGenerator;
        _agentRuntime = agentRuntimeSettings.Value;
        _logger = logger;
        _gates = new WorkflowSaveGates(
            _agentRuntime,
            runLimitsProvider,
            janitorSettings?.Value,
            platformSettings?.Value.MaxBlockParallelism,
            platformSettings?.Value.ModelRouting);
    }

    public virtual async Task<ErrorOr<SaveWorkflowResult>> SaveCoreAsync(
        SaveWorkflowCommand request,
        string? narrativeOverride,
        CancellationToken cancellationToken)
    {
        Workflow workflow = request.Workflow;

        WorkflowTypePresence presence = await WorkflowTypePresenceQuery.ReadAsync(
            _coreDbContext.WorkflowTypes,
            request.WorkflowTypeId,
            cancellationToken);
        if (presence == WorkflowTypePresence.Retired)
        {
            return WorkflowTypeErrorFactory.Deleted(request.WorkflowTypeId);
        }

        // The WebApp save path passes a live Workflow object that never went through FromJson,
        // so hoist any residual per-block signature dicts here before canonicalization.
        SignatureFieldsLegacyMigrator.Hoist(workflow);

        // Without the runtime's limits the save carries a warning instead of a ceiling check; the
        // runtime's own 400 at run start still refuses an over-ceiling fuse before any spend.
        AgentRunLimits? runLimits = _gates.RunLimits;
        List<WorkflowCheckFailure> gateFailures = request.SkipAuthorGates ? [] : _gates.Check(workflow, runLimits, request.OrganizationId);
        List<WorkflowCheckFailure> agentViolations = gateFailures.FindAll(static failure => failure.Check == WorkflowCheck.AgentGates);
        if (agentViolations.Count > 0)
        {
            _logger.LogWarning(
                "Rejected save of workflow {WorkflowTypeId}: {ViolationCount} agent-block violation(s)",
                LogSanitizer.ForLog(request.WorkflowTypeId),
                agentViolations.Count);
            return agentViolations.ConvertAll(violation => TemplateRefusal(request.WorkflowTypeId, violation.Message));
        }

        if (gateFailures.Find(static failure => failure.Check == WorkflowCheck.Screening) is WorkflowCheckFailure screeningRefusal)
        {
            _logger.LogWarning("Rejected save of workflow {WorkflowTypeId}: {Reason}", LogSanitizer.ForLog(request.WorkflowTypeId), screeningRefusal.Message);
            return TemplateRefusal(request.WorkflowTypeId, screeningRefusal.Message);
        }

        WorkflowVersion? priorHead = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(
                new LatestWorkflowVersionByTypeSpec(request.WorkflowTypeId),
                cancellationToken);

        if (request.EnforceOptimisticConcurrency)
        {
            Error? conflict = CheckBaseVersion(request.BaseVersionId, priorHead);
            if (conflict is not null)
            {
                _logger.LogWarning(
                    "Rejected save of workflow {WorkflowTypeId}: {ConflictCode}",
                    LogSanitizer.ForLog(request.WorkflowTypeId),
                    conflict.Value.Code);
                return conflict.Value;
            }
        }

        Workflow? oldWorkflow = await _coreUnitOfWork.Workflows
            .GetByTypeIdAsync(request.WorkflowTypeId);

        // Only past every refusal, so a refused editor save leaves the live editor object as it
        // was; and only on author saves, whose narrative is the diff and so names the change.
        if (narrativeOverride is null)
        {
            int normalizedModels = AgentBlockModelNormalizer.Normalize(workflow, _agentRuntime);
            if (normalizedModels > 0)
            {
                _logger.LogInformation(
                    "Normalized {Count} agent-family block model id(s) to provider-neutral ids on save of workflow {WorkflowTypeId}",
                    normalizedModels,
                    LogSanitizer.ForLog(request.WorkflowTypeId));
            }
        }

        if (oldWorkflow is not null)
        {
            _coreUnitOfWork.Workflows.Remove(oldWorkflow);
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
        }

        workflow.WorkflowTypeId = request.WorkflowTypeId;
        workflow.WorkflowType = null;
        workflow.UserId = request.UserId;
        workflow.OrganizationId = request.OrganizationId;
        foreach (DocumentClass documentClass in workflow.DocumentClasses)
        {
            documentClass.Workflow = null;
        }

        if (oldWorkflow is not null)
        {
            workflow.AdoptStableIdsFrom(oldWorkflow);
        }

        BackfillAndDedupeStableIds(workflow.GetAllBlocks(), b => b.StableBlockId, b => b.ReMintStableBlockId());
        BackfillAndDedupeStableIds(workflow.Sections, s => s.StableSectionId, s => s.ReMintStableSectionId());

        await _coreUnitOfWork.Workflows.AddAsync(workflow);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        string canonicalJson = _canonicalizer.ToCanonicalJson(workflow);
        string contentHash = _canonicalizer.ComputeContentHash(workflow);

        string? priorHeadJson = priorHead is { PayloadTrimmedAt: null } ? priorHead.WorkflowJson : null;

        bool wasDeduped = priorHead is not null
            && string.Equals(priorHead.ContentHash, contentHash, StringComparison.Ordinal)
            && string.Equals(priorHeadJson, canonicalJson, StringComparison.Ordinal);
        string versionId;
        if (wasDeduped)
        {
            versionId = priorHead!.Id;
        }
        else
        {
            string narrative = narrativeOverride
                ?? (string.IsNullOrWhiteSpace(priorHeadJson)
                    ? WorkflowNarrativeGenerator.FirstVersionSummary
                    : _narrativeGenerator.Generate(_diffService.Compare(priorHeadJson, canonicalJson)));

            var version = new WorkflowVersion
            {
                WorkflowTypeId = request.WorkflowTypeId,
                ContentHash = contentHash,
                WorkflowJson = canonicalJson,
                ParentVersionId = priorHead?.Id,
                AutoNarrativeSummary = narrative,
                CreatedByUserId = request.UserId,
                HumanNote = request.VersionNote,
            };

            await _coreUnitOfWork.Repository<WorkflowVersion>().AddAsync(version);
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
            versionId = version.Id;
        }

        ErrorOr<SignatureSyncResult> sync = await SignatureFieldSaveSynchronizer.SyncAsync(
            _coreDbContext,
            request.WorkflowTypeId,
            request.UserId,
            oldWorkflow,
            workflow,
            request.SignatureFieldRenames,
            request.ConfirmSignatureFieldDeletions,
            _logger,
            cancellationToken);
        if (sync.IsError)
        {
            return sync.Errors;
        }

        _ = await _coreDbContext.SaveChangesAsync(cancellationToken);

        var warnings = new List<string>();
        if (runLimits is null && _agentRuntime.Enabled && AgentBlockGuard.DeclaresAnyFuse(workflow))
        {
            warnings.Add(AgentBlockGuard.FUSES_UNCHECKED_WARNING);
            _logger.LogWarning(
                "Saved workflow {WorkflowTypeId} with unchecked agent fuse values: the runtime's limits were unknown",
                LogSanitizer.ForLog(request.WorkflowTypeId));
        }

        if (!request.SkipAuthorGates)
        {
            warnings.AddRange(AgentTemplateGrammar.SaveWarnings(workflow));
        }

        return new SaveWorkflowResult(versionId, contentHash, wasDeduped, sync.Value, warnings.Count == 0 ? null : warnings);
    }

    internal async Task<WorkflowVersion?> TryReadCurrentHeadAsync(
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _coreUnitOfWork.Repository<WorkflowVersion>()
                .GetFirstOrDefaultAsync(
                    new LatestWorkflowVersionByTypeSpec(workflowTypeId),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not re-read head after concurrent save of {WorkflowTypeId}",
                LogSanitizer.ForLog(workflowTypeId));
            return null;
        }
    }

    internal static Dictionary<string, object> BuildHeadMetadata(WorkflowVersion head)
    {
        return BuildHeadMetadata(head.Id, head.ContentHash, head.CreatedAt);
    }

    internal static Dictionary<string, object> BuildHeadMetadata(
        string headVersionId,
        string contentHash,
        DateTime createdAtUtc)
    {
        return new Dictionary<string, object>
        {
            [CURRENT_HEAD_VERSION_ID_METADATA_KEY] = headVersionId,
            [CURRENT_CONTENT_HASH_METADATA_KEY] = contentHash,
            [HEAD_CREATED_AT_UTC_METADATA_KEY] = createdAtUtc,
        };
    }

    private static Error TemplateRefusal(string workflowTypeId, string message)
    {
        return Error.Validation(nameof(SaveWorkflowCommand.Workflow), $"Template '{workflowTypeId}': {message}");
    }

    private static void BackfillAndDedupeStableIds<T>(
        IEnumerable<T> items,
        Func<T, string?> getStableId,
        Action<T> reMintStableId)
    {
        var seenStableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (T item in items)
        {
            string? stableId = getStableId(item);
            if (string.IsNullOrEmpty(stableId) || !seenStableIds.Add(stableId))
            {
                reMintStableId(item);
                _ = seenStableIds.Add(getStableId(item)!);
            }
        }
    }

    internal static Error? CheckBaseVersion(string? baseVersionId, WorkflowVersion? priorHead)
    {
        if (priorHead is null)
        {
            return baseVersionId is null
                ? null
                : Error.Conflict(
                    VERSION_CONFLICT_CODE,
                    "No head version exists for this workflow template; omit baseVersionId for a first save.");
        }

        if (string.Equals(baseVersionId, priorHead.Id, StringComparison.Ordinal))
        {
            return null;
        }

        return Error.Conflict(
            VERSION_CONFLICT_CODE,
            $"The workflow template head version is '{priorHead.Id}'; the supplied baseVersionId is stale or missing. Re-read the template and re-apply the edit.",
            BuildHeadMetadata(priorHead));
    }
}
