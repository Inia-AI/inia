using ErrorOr;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Common.Helpers;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Constants;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Replay;
using IniaAi.Domain.Reruns;
using IniaAi.Domain.StoredKeys;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Core.Workflows;

/// <summary>
/// Base class for workflow handlers.
/// </summary>
public class WorkflowHandler : IWorkflowHandler, IBlockGroupHost
{
    #region Constants

    public const string DOCUMENT_INTAKE_SECTION_NAME = "Document intake";

    private const string REPLAY_REASON_NOT_FOUND = "no matching block for captured external output";

    private const string REPLAY_REASON_UNCOVERED =
        "freeze-eligible block has no captured output; live fetch suppressed";
    private const string REPLAY_REASON_UNCOVERED_RAN_LIVE =
        "freeze-eligible block has no captured output; ran live";

    private const string REPLAY_REASON_AMBIGUOUS_FORMAT = "ambiguous: {0} candidate blocks";

    private const string PARAMETERIZED_CONFIG_UNPARSEABLE_SKIP_REASON =
        "Parameterized section config could not be parsed; the section was not expanded";

    private const string PARAMETERIZED_CONFIG_NO_VARIANTS_SKIP_REASON =
        "Parameterized section config has no variants; the section was not expanded";

    private const int PERSISTENCE_BREAKER_THRESHOLD = 3;

    #endregion

    #region Private fields

    protected readonly ILogger Logger;
    protected readonly IOptions<PlatformSettings> PlatformSettings;
    protected readonly IServiceScopeFactory ServiceScopeFactory;
    protected readonly IWorkflowOutputService WorkflowOutputService;
    protected WorkflowExecution? RootOutput = null;

    /// <summary>A validation or regression rerun (<see cref="RerunWrites"/>): it saves no change to live data.</summary>
    protected static bool IsRerun => RerunWrites.AreSuppressed;

    private readonly WorkflowAnnotationsContext _workflowAnnotationsContext;
    private readonly ConditionalWeakTable<Workflow, CitationSources> _citationSources = new();
    private readonly IDocumentProcessingService _documentProcessingService;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly IEntityPlaceholderResolver _entityPlaceholderResolver;

    private readonly IWorkflowGraphBuilder _graphBuilder;
    private readonly LevelScheduler _scheduler;

    // Raw writer. Mid-run block results go through PersistBlockResultGuardedAsync (persist-failure
    // taint tracking); mid-run checkpoints go through PersistBlockCheckpointGuardedAsync (breaker
    // only); the end-of-run replay is the only other sanctioned direct PersistBlockResultAsync call.
    private readonly IExecutedBlockWriter _blockWriter;

    private readonly IReadOnlyDictionary<BlockTypes, IBlockStrategy> _blockStrategies;
    private readonly Dictionary<string, IStringReplacerStrategy> _stringReplacerStrategies = [];
    private readonly ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>> _ragResultsCache = [];
    private readonly CookieContainer _webApiCookies = new();

    // A group member's report output waits for its group: its unit stashes the block annotations
    // here (keyed by block id) instead of materializing, a later pass of the member overwrites the
    // stash, and the group's strategy releases it once the group settles.
    private readonly ConcurrentDictionary<string, BlockAnnotations?> _withheldMemberAnnotations = new(StringComparer.Ordinal);

    // Write-once guard for OrganizationId/UserId against the concurrent block path.
    private readonly Lock _identityLock = new();

    // Blocks whose failure handling itself failed (escaped the runner into the scheduler).
    // Their persisted state cannot be trusted, so a non-zero count fails the run.
    private int _schedulerEscapedBlockFailures;

    // Block results whose persist failed mid-run. Each entry captures the exact write for an
    // end-of-run retry; any entry at all (even one later recovered) fails the run — a persisted
    // execution with mid-run persistence anomalies must never conclude as finished.
    private readonly ConcurrentQueue<PendingBlockPersist> _unpersistedResults = new();

    // Persistence circuit breaker: PERSISTENCE_BREAKER_THRESHOLD consecutive block-persist
    // failures (reset on any success) indicate the store is systemically down, so the run aborts
    // instead of executing further blocks whose results could not be saved anyway.
    private int _consecutivePersistFailures;
    private volatile bool _persistenceBreakerTripped;

    // Set before the abort token is cancelled by a block whose failure must not be absorbed, so the
    // unwind can tell that abort apart from the caller's cancellation and from the breaker's.
    private volatile CriticalBlockFailure? _criticalBlockFailure;
    private readonly object _criticalAbortLock = new();

    // Set at the end of a successful run when a block group asks for a warning, and written onto
    // the execution row with the Draft transition.
    private (string Kind, string Detail)? _completionWarning;

    // A handler's own warning, given after the blocks finish; a block group's warning outranks it.
    private (string Kind, string Detail)? _handlerWarning;

    // The terminal state another writer published while this run was still going. Outranks every
    // other abort: the row belongs to whoever published it, so this run stops without writing.
    private volatile ExecutionFence? _executionFence;

    private sealed record ExecutionFence(WorkflowState ObservedState);

    /// <summary>
    /// True once any of the run's own abort sources has fired. The sources stay separate because
    /// their outcomes differ — the breaker reports lost results, a critical block reports its
    /// failure, a fence reports nothing at all — but every one of them cancels the run token, so
    /// an in-flight write killed by that cancellation is queued rather than dropped.
    /// </summary>
    private bool InternalAbortRequested =>
        _persistenceBreakerTripped
        || _criticalBlockFailure is not null
        || _executionFence is not null;

    private CancellationTokenSource? _executionAbortCts;

    private sealed record PendingBlockPersist(
        Block Block,
        ReportParagraph? Paragraph,
        WorkflowExecutionLog? Log);

    /// <summary>
    /// Active client signature overrides loaded once per workflow execution, keyed by field
    /// name (case-insensitive). <see cref="InjectSignatureFieldValues"/> reads from this dict;
    /// it's populated by <see cref="LoadActiveSignatureOverridesAsync"/> before the section
    /// loop starts. Null until loaded; empty dict means no overrides exist.
    /// </summary>
    private IReadOnlyDictionary<string, string>? _activeSignatureOverrides;

    #endregion

    #region Properties

    public List<Page> AllPages { get; } = [];

    public ConcurrentDictionary<string, string> SharedInputs { get; } = [];

    // Replay state: blocks execute in parallel, so unmatched records use a thread-safe bag and the
    // coverage counters are interlocked.
    private bool _isReplayRun;
    private ReplayUncoveredBlockPolicy _replayUncoveredBlockPolicy;
    private int _freezeEligibleCount;
    private int _freezeReplayedCount;
    private readonly ConcurrentBag<ReplayUnmatched> _replayUnmatched = [];

    // Captures that matched no block at initial injection. Loop bodies expand per section at
    // execution time, so their per-iteration clones (with suffixed stable ids) do not exist yet
    // when InjectReplayOutputs runs; these captures are retried after each loop expansion and
    // only recorded as NotFound if still unmatched when the run ends.
    private readonly List<ReplayOutput> _replayPending = [];
    private readonly object _replayPendingLock = new();

    public string? CounterpartyId { get; set; }

    public List<string>? PeerCompanyIds { get; set; }

    /// <inheritdoc/>
    public string? OrganizationId { get; set; }

    /// <inheritdoc/>
    public string? UserId { get; set; }

    /// <inheritdoc/>
    public TokenExecutionContext ExecutionContext { get; set; } = TokenExecutionContext.WorkflowExecution;

    /// <inheritdoc/>
    public string? WorkflowExecutionId => RootOutput?.Id;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowHandler"/> class.
    /// </summary>
    /// <param name="chatConnector">The chat connector.</param>
    public WorkflowHandler(
        ILogger<WorkflowHandler> logger,
        IOptions<PlatformSettings> platformSettings,
        IServiceScopeFactory serviceScopeFactory,
        IWorkflowOutputService workflowOutputService,
        IDocumentProcessingService documentProcessingService,
        IFileStorageManager fileStorageManager,
        IEntityPlaceholderResolver entityPlaceholderResolver,
        IWorkflowGraphBuilder graphBuilder,
        LevelScheduler scheduler,
        IExecutedBlockWriter blockWriter)
    {
        Logger = logger;
        PlatformSettings = platformSettings;
        ServiceScopeFactory = serviceScopeFactory;
        WorkflowOutputService = workflowOutputService;
        _documentProcessingService = documentProcessingService;
        _fileStorageManager = fileStorageManager;
        _entityPlaceholderResolver = entityPlaceholderResolver;
        _graphBuilder = graphBuilder;
        _scheduler = scheduler;
        _blockWriter = blockWriter;

        // doing this here to ensure REPORT_DATE is set for workflow, section and block executions
        SharedInputs[SharedInputKeys.REPORT_DATE] = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Read through a scope: a constructor parameter would fan into the monitoring handlers and
        // the test helpers that construct this one by hand.
        using (IServiceScope scope = serviceScopeFactory.CreateScope())
        {
            _blockStrategies = new BlockStrategies(scope.ServiceProvider.GetServices<BlockStrategyRegistration>()).ByType;
        }

        _stringReplacerStrategies.Add("String", new StringReplacerStrategy());
        _stringReplacerStrategies.Add("Json", new JsonStringReplacerStrategy());

        _workflowAnnotationsContext = new WorkflowAnnotationsContext();
    }

    #endregion

    #region Public methods

    public virtual async Task<Workflow> ExecuteWorkflowAsync(
        Workflow workflow,
        ConcurrentDictionary<string, string> anonymisationMap,
        string organizationId,
        string jobId,
        IUserDataUnitOfWork userDataUnitOfWork,
        string? userId = null,
        Dictionary<string, string>? inputs = null,
        IReadOnlyList<ReplayOutput>? replayOutputs = null,
        ReplayUncoveredBlockPolicy replayUncoveredBlockPolicy = ReplayUncoveredBlockPolicy.SkipAsInconclusive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Store the organizationId and userId for use throughout the execution (token tracking)
        OrganizationId = organizationId;
        UserId = userId;

        // Store in SharedInputs for block execution context
        SharedInputs[SharedInputKeys.ORGANIZATION_ID] = organizationId;
        SharedInputs[SharedInputKeys.JOB_ID] = jobId;

        Logger.LogInformation("ExecuteWorkflowAsync: BEFORE setting - workflow.OrganizationId was '{ExistingOrganizationId}', parameter organizationId is '{ParameterOrganizationId}'",
            workflow.OrganizationId,
            organizationId);

        workflow.OrganizationId = organizationId;

        Logger.LogInformation("ExecuteWorkflowAsync: AFTER setting - workflow.OrganizationId to '{OrganizationId}' for workflow '{WorkflowId}'",
            organizationId,
            workflow.Id);

        _ = Interlocked.Exchange(ref _schedulerEscapedBlockFailures, 0);
        _ = Interlocked.Exchange(ref _consecutivePersistFailures, 0);
        _unpersistedResults.Clear();
        _persistenceBreakerTripped = false;
        _criticalBlockFailure = null;
        _executionFence = null;
        _handlerWarning = null;

        // Linked so an internal abort (the persistence breaker, a critical block failure) can stop
        // the run without owning the caller's token; external cancellation flows through unchanged.
        _executionAbortCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken runToken = _executionAbortCts.Token;

        Exception? snapshotFailure = null;

        try
        {
            await LogSkippedProtectedFilesAsync(inputs, userDataUnitOfWork);

            Workflow? workflowCloned = await BuildExecutedWorkflowAsync(
                workflow,
                userDataUnitOfWork);

            if (workflowCloned is not null)
            {
                workflow = workflowCloned;
            }

            ProcessWorkflowVariables(workflow, inputs);

            WorkflowOutputContext? workflowOutputContext = RootOutput is null
                ? null
                : InitializeContext(RootOutput);

            runToken.ThrowIfCancellationRequested();

            await ProcessParameterizedSectionsAsync(workflow, userDataUnitOfWork);

            // A non-null replayOutputs marks this as a replay run (both producers always supply a list,
            // empty when the source captured nothing). Freezing is then derived per block from
            // _isReplayRun + the freeze policy: a matched block is served its captured output and never
            // fetches live. What an uncovered freeze-eligible block does is the policy's call — the
            // dormant regression path skips it and marks the run inconclusive, the freeze submission
            // option runs it live and discloses coverage.
            _isReplayRun = replayOutputs is not null;
            _replayUncoveredBlockPolicy = replayUncoveredBlockPolicy;
            _ = Interlocked.Exchange(ref _freezeEligibleCount, 0);
            _ = Interlocked.Exchange(ref _freezeReplayedCount, 0);
            _replayUnmatched.Clear();
            if (replayOutputs is { Count: > 0 })
            {
                InjectReplayOutputs(workflow, replayOutputs);
            }

            await LoadActiveSignatureOverridesAsync(workflow, runToken);

            // Detach the executed graph now that the skeleton + variant rows are on disk. Each block
            // persists its own result through a pooled context during the run, so the shared context
            // must track nothing (no DetectChanges-vs-mutation race under parallel execution).
            userDataUnitOfWork.ClearChangeTracker();

            Logger.LogInformation("Executing sections");

            await ExecuteSectionSegmentsAsync(
                workflow,
                anonymisationMap,
                userDataUnitOfWork,
                workflowOutputContext,
                runToken);

            cancellationToken.ThrowIfCancellationRequested();

            // A breaker-only trip deliberately falls through: the scheduler finished on its own,
            // so the end-of-run replay in ThrowIfResultsLostAsync is the queued results' one
            // remaining chance to land. A fence or critical abort still stops the run here.
            if (_executionFence is not null || _criticalBlockFailure is not null)
            {
                runToken.ThrowIfCancellationRequested();
            }

            await PersistReplayOutcomeAsync(userDataUnitOfWork);

            await ThrowIfResultsLostAsync(workflow);

            _completionWarning = GroupStrategies()
                .Select(group => group.CompletionWarningFor(workflow))
                .FirstOrDefault(warning => warning is not null);

            Logger.LogInformation(
                "Workflow {WorkflowId} executed successfully.",
                workflow.Id);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _executionFence is not null)
        {
            // Outranks the breaker and a critical block failure: whatever else went wrong, this
            // row already belongs to another writer, so nothing about this run may be recorded on
            // it — not a state, not a failure, not even an error log row.
            throw new WorkflowExecutionFencedException(RootOutput!.Id, _executionFence.ObservedState);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _persistenceBreakerTripped)
        {
            // The abort came from the persistence breaker, not the caller: surfacing it as a
            // cancellation would mislabel the run Cancelled. Outranks a critical block failure that
            // tripped alongside it — a dead store loses results, which is the worse outcome to
            // report. No replay attempt — the breaker just proved the store dead three times in a row.
            throw await RecordExecutionFailureAsync(BuildBreakerAbortException(workflow));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _criticalBlockFailure is not null)
        {
            throw await RecordExecutionFailureAsync(await BuildCriticalAbortExceptionAsync(workflow));
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Workflow execution was canceled.");
            throw;
        }
        catch (WorkflowDispatchRefusedException refusal)
        {
            // Structural, and refused before the group could spend: the run has no block failure to
            // report, so the reason only reaches the app if it is logged onto the execution here.
            Logger.LogError(
                refusal,
                "Workflow {WorkflowId} refused: its shape cannot be dispatched.",
                workflow.Id);

            throw await RecordExecutionFailureAsync(refusal);
        }
        catch (Exception e)
        {
            Logger.LogError(
                e,
                "An error occurred while processing workflow {WorkflowId}.",
                workflow.Id);
            throw;
        }
        finally
        {
            if (_executionFence is null)
            {
                snapshotFailure = await SaveExecutedWorkflowResultsAsync(workflow, userDataUnitOfWork);
            }

            CancellationTokenSource abortCts = _executionAbortCts;
            _executionAbortCts = null;
            abortCts.Dispose();
        }

        // Only the success path reaches here (every catch rethrows). A failed final snapshot means
        // the stored workflow/section states are stale, so the run must not conclude as finished.
        if (snapshotFailure is not null)
        {
            var snapshotLost = new WorkflowResultsLostException(
                permanentlyLostBlocks: [],
                recoveredBlocks: [],
                unconcludedBlocks: [],
                schedulerEscapedCount: 0,
                snapshotFailure: snapshotFailure.Message,
                innerException: snapshotFailure);

            throw await RecordExecutionFailureAsync(snapshotLost);
        }

        return workflow;
    }

    public virtual async Task ExecuteSectionAsync(
        Section section,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        Dictionary<string, string>? inputs = null,
        WorkflowOutputContext? workflowOutputContext = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (section.IsSeparator)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(section.ParameterizedConfig))
        {
            List<Section> expandedSections = await ExpandParameterizedSectionAsync(
                section, userDataUnitOfWork);

            foreach (Section variantSection in expandedSections)
            {
                await ExecuteSectionAsync(
                    variantSection,
                    anonymisationMap,
                    userDataUnitOfWork,
                    inputs,
                    workflowOutputContext,
                    cancellationToken);
            }

            return;
        }

        Workflow root = section.Workflow;
        lock (_identityLock)
        {
            OrganizationId ??= root.OrganizationId;
            UserId ??= root.UserId;
        }

        if (_activeSignatureOverrides is null)
        {
            await LoadActiveSignatureOverridesAsync(root, cancellationToken);
        }

        ProcessWorkflowVariables(root, inputs);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            workflowOutputContext = await SetupWorkflowOutputContextAsync(
                section,
                workflowOutputContext,
                userDataUnitOfWork);

            Dictionary<string, string?> precedingSectionInputs = CollectAllInputs(
                root,
                null);

            GetOutputsAndProperties(section, precedingSectionInputs);

            section.SetExecutionState(asPlaceHolder: true, precedingSectionInputs ?? []);

            string? sectionSkipReason = GetSectionSkipReason(section);

            if (sectionSkipReason is not null)
            {
                await SkipSectionAsync(
                    section,
                    sectionSkipReason,
                    userDataUnitOfWork,
                    cancellationToken);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            await ProcessLoopsAndUpdateDatabaseAsync(section, userDataUnitOfWork);

            await DispatchSectionBlocksAsync(
                section,
                anonymisationMap,
                userDataUnitOfWork,
                workflowOutputContext,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Logger.LogInformation(
                "Section {SectionId} executed successfully.",
                section.Id);
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Execution was canceled.");
            throw;
        }
        catch (Exception e)
        {
            Logger.LogError(
                e,
                "An error occurred while processing section {SectionId}.",
                section.Id);
            throw;
        }
    }

    public async Task<string?> ExecuteBlockAsync(
        Section section,
        int index,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        WorkflowOutputContext? workflowOutputContext = null,
        Dictionary<string, string>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(section.ParameterizedConfig))
        {
            List<Section> expandedSections = await ExpandParameterizedSectionAsync(
                section, userDataUnitOfWork);

            string? lastSkipReason = null;
            foreach (Section variantSection in expandedSections)
            {
                lastSkipReason = await ExecuteBlockAsync(
                    variantSection,
                    index,
                    anonymisationMap,
                    userDataUnitOfWork,
                    workflowOutputContext,
                    inputs,
                    cancellationToken);
            }

            return lastSkipReason;
        }

        // Resolved by Order, which is the key every caller passes. A count-based guard would refuse
        // a real block whose Order sits past the block count. The scheduler never presents one,
        // because Section.ProcessLoops renumbers in the dispatch prelude, but the editor's
        // single-block run reaches here without that prelude, and several shared-component paths
        // remove blocks through Blocks.Remove, which skips Section.Remove's renumber.
        Block? block = section.Blocks.FirstOrDefault(b => b.Order == index);
        if (block is null)
        {
            Logger.LogError("Invalid block index: {Index}", index);

            return "Invalid block index";
        }

        cancellationToken.ThrowIfCancellationRequested();

        string? prepareSkipReason = await PrepareBlockRunAsync(section, block, inputs, cancellationToken);
        if (prepareSkipReason is not null)
        {
            return prepareSkipReason;
        }

        Logger.LogDebug("{block} Executing block", block);

        BlockAnnotations? blockAnnotations = await ExecuteBlockInOwnScopeAsync(
            section,
            block,
            anonymisationMap,
            userDataUnitOfWork,
            round: AgentRunIds.FIRST_ROUND,
            redoLeg: null,
            runPrelude: true,
            belongsToVerificationGroup: false,
            cancellationToken: cancellationToken);

        return await MaterializeBlockOutputAsync(
            section, block, blockAnnotations, workflowOutputContext, userDataUnitOfWork, cancellationToken);
    }

    /// <summary>
    /// The block prelude shared by the single-block path and the verification loop: pins the run
    /// identity, loads the signature overrides once, refuses a block whose required documents are
    /// missing (persisting the skip when an execution row exists), and resolves the workflow
    /// variables. Returns the skip reason, or null when the block may run.
    /// </summary>
    private async Task<string?> PrepareBlockRunAsync(
        Section section,
        Block block,
        Dictionary<string, string>? inputs,
        CancellationToken cancellationToken)
    {
        Workflow rootForBlock = section.Workflow;
        lock (_identityLock)
        {
            OrganizationId ??= rootForBlock.OrganizationId;
            UserId ??= rootForBlock.UserId;
        }

        if (_activeSignatureOverrides is null)
        {
            await LoadActiveSignatureOverridesAsync(rootForBlock, cancellationToken);
        }

        if (block.RequiredDocumentsNotProvided())
        {
            const string skipReason = "Required documents not provided";
            Logger.LogWarning("{Block} skipped: {Reason}", block, skipReason);

            block.ExecutionState ??= new BlockExecutionState(block);
            block.ExecutionState.MarkAsSkipped(skipReason);

            if (RootOutput?.Id is not null)
            {
                await PersistBlockResultGuardedAsync(block, null, null, cancellationToken);
            }

            return skipReason;
        }

        ProcessWorkflowVariables(rootForBlock, inputs);

        return null;
    }

    /// <summary>
    /// One strategy execution in a fresh service scope, with the flush checkpoint wired to
    /// <paramref name="block"/> when an execution row exists — the wiring that lets an
    /// agent-family strategy land its start-time checkpoint (and with it a prior round's
    /// criterion rows) on the right block. The verification loop's passes ride their round and
    /// redo directive in on the context.
    /// </summary>
    private async Task<BlockAnnotations?> ExecuteBlockInOwnScopeAsync(
        Section section,
        Block block,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        int round,
        VerificationRedoLeg? redoLeg,
        bool runPrelude,
        bool belongsToVerificationGroup,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = ServiceScopeFactory.CreateScope();

        BlockExecutionContext blockExecutionContext = CreateBlockExecutionContext(
            section,
            anonymisationMap,
            scope.ServiceProvider);
        blockExecutionContext.VerificationRound = round;
        blockExecutionContext.VerificationRedoLeg = redoLeg;
        blockExecutionContext.BelongsToVerificationGroup = belongsToVerificationGroup;
        blockExecutionContext.IsCitationSource = CitationSourcesOf(section.Workflow).Includes(block);

        if (blockExecutionContext.WorkflowExecutionId is not null)
        {
            blockExecutionContext.FlushBlockStateAsync = flushCancellationToken =>
                PersistBlockCheckpointGuardedAsync(block, flushCancellationToken);
        }

        return await ExecuteBlockAsync(
            scope,
            section,
            block,
            blockExecutionContext,
            userDataUnitOfWork,
            runPrelude,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The block epilogue shared by the single-block path and the verification loop: persists a
    /// skip the strategy produced, honors the development exit when no output context exists,
    /// and materializes the block's report output. Returns the skip reason to surface, or null.
    /// </summary>
    private async Task<string?> MaterializeBlockOutputAsync(
        Section section,
        Block block,
        BlockAnnotations? blockAnnotations,
        WorkflowOutputContext? workflowOutputContext,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        if (block.ExecutionState?.Status is ExecutionStatus.Skipped)
        {
            if (RootOutput?.Id is not null)
            {
                await PersistBlockResultGuardedAsync(block, null, null, cancellationToken);
            }

            return block.ExecutionState.Error;
        }

        if (workflowOutputContext is null)
        {
            Logger.LogInformation(
                "Development: Skipping output element creation for block {block}",
                block.Name);

            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await BuildBlockOutputAsync(
            section,
            workflowOutputContext,
            block,
            blockAnnotations,
            userDataUnitOfWork,
            cancellationToken);

        return null;
    }

    /// <summary>
    /// Dispatches sections in order, treating each loop-bearing section as a barrier. Maximal runs
    /// of loop-free sections execute together as one cross-section parallel DAG; a loop section runs
    /// alone via <see cref="ExecuteSectionAsync"/>, which expands it at runtime and fans the
    /// iterations out under the cap. Parameterized sections are pre-expanded, so the only runtime
    /// barrier is a loop.
    /// </summary>
    private async Task ExecuteSectionSegmentsAsync(
        Workflow rootWorkflow,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        WorkflowOutputContext? workflowOutputContext,
        CancellationToken cancellationToken)
    {
        List<DispatchSegment> segments = DispatchSegments.Split(rootWorkflow, IsExecutionOrderFence);

        foreach (IBlockGroupStrategy group in GroupStrategies())
        {
            group.ThrowIfSegmentsCannotHoldGroups(segments);
        }

        foreach (DispatchSegment segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (segment.IsLoop)
            {
                await ExecuteSectionAsync(
                    segment.Sections[0],
                    anonymisationMap,
                    userDataUnitOfWork,
                    inputs: null,
                    workflowOutputContext,
                    cancellationToken);

                continue;
            }

            await ExecuteSectionsInParallelAsync(
                segment.Sections,
                rootWorkflow,
                anonymisationMap,
                userDataUnitOfWork,
                workflowOutputContext,
                cancellationToken);
        }
    }

    /// <summary>
    /// Runs each loop-free section's prelude single-threaded (capturing its per-section output
    /// context), then builds one cross-section <see cref="SchedulingView"/> over every block and
    /// dispatches under the cap. Cross-section dependencies are respected through placeholder edges.
    /// </summary>
    private async Task ExecuteSectionsInParallelAsync(
        IReadOnlyList<Section> orderedSections,
        Workflow rootWorkflow,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        WorkflowOutputContext? workflowOutputContext,
        CancellationToken cancellationToken)
    {
        Dictionary<int, List<Block>> blocksBySectionOrder = [];
        Dictionary<string, (Section Section, Block Block, WorkflowOutputContext? Context)> blockMap =
            new(StringComparer.Ordinal);

        foreach (Section section in orderedSections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (section.IsSeparator)
            {
                continue;
            }

            Workflow root = section.Workflow;
            lock (_identityLock)
            {
                OrganizationId ??= root.OrganizationId;
                UserId ??= root.UserId;
            }

            ProcessWorkflowVariables(root, inputs: null);

            // SetupWorkflowOutputContextAsync mutates the shared base context to point at THIS section's
            // report section. Snapshot it per section: in the parallel path every section's setup runs
            // before any block executes, so without a copy every block would read the last section's
            // report section and all paragraphs would land in the wrong section.
            WorkflowOutputContext? sharedContext = await SetupWorkflowOutputContextAsync(
                section,
                workflowOutputContext,
                userDataUnitOfWork);

            WorkflowOutputContext? perSectionContext = sharedContext is null
                ? null
                : new WorkflowOutputContext(sharedContext);

            Dictionary<string, string?> precedingSectionInputs = CollectAllInputs(root, null);

            GetOutputsAndProperties(section, precedingSectionInputs);

            section.SetExecutionState(asPlaceHolder: true, precedingSectionInputs ?? []);

            string? sectionSkipReason = GetSectionSkipReason(section);

            if (sectionSkipReason is not null)
            {
                await SkipSectionAsync(section, sectionSkipReason, userDataUnitOfWork, cancellationToken);
                continue;
            }

            await ProcessLoopsAndUpdateDatabaseAsync(section, userDataUnitOfWork);

            blocksBySectionOrder[section.Order] = [.. section.Blocks];

            foreach (Block block in section.Blocks)
            {
                blockMap[block.Id] = (section, block, perSectionContext);
            }
        }

        if (blockMap.Count == 0)
        {
            return;
        }

        WorkflowGraph graph = _graphBuilder.Build(
            blocksBySectionOrder,
            [.. (rootWorkflow.Variables ?? [])],
            CollectFenceBlockIds(blocksBySectionOrder));

        SchedulingView view = BuildSchedulingView(graph);

        List<(Section Section, Block Block, WorkflowOutputContext? Context)> dispatchedBlocks =
            [.. blockMap.Values];

        await RunSchedulerAsync(
            view,
            (blockId, blockCt) => blockMap.TryGetValue(blockId, out (Section Section, Block Block, WorkflowOutputContext? Context) entry)
                ? RunResolvedBlockAsync(
                    entry.Section,
                    entry.Block,
                    anonymisationMap,
                    userDataUnitOfWork,
                    entry.Context,
                    dispatchedBlocks,
                    blockCt)
                : Task.CompletedTask,
            cancellationToken);
    }

    /// <summary>
    /// True when the block carries a tag that a dispatched group block declares as a member. Such a
    /// block's report output waits for its group: its unit runs withheld and the group's strategy
    /// releases it once the group settles.
    /// </summary>
    private bool IsGroupMember(
        Block block,
        IReadOnlyList<(Section Section, Block Block, WorkflowOutputContext? Context)> dispatchedBlocks)
    {
        if (string.IsNullOrWhiteSpace(block.ReplacementTag))
        {
            return false;
        }

        return dispatchedBlocks.Any(entry =>
            GroupStrategyOf(entry.Block) is { } group
            && group.MemberTagsOf(entry.Block).Contains(block.ReplacementTag, StringComparer.Ordinal));
    }

    private IBlockGroupStrategy? GroupStrategyOf(Block block)
    {
        return _blockStrategies.GetValueOrDefault(block.Type) as IBlockGroupStrategy;
    }

    private IEnumerable<IBlockGroupStrategy> GroupStrategies()
    {
        return _blockStrategies.Values.OfType<IBlockGroupStrategy>();
    }

    /// <summary>
    /// True when the block's handler side effects (SharedInputs writes, CounterpartyId,
    /// AllPagesTopics) are invisible to placeholder analysis, so the block must stay pinned at its
    /// authored position in the schedule. Override in handlers whose
    /// <see cref="OnBlockExecutedAsync"/> writes shared state.
    /// </summary>
    protected virtual bool IsExecutionOrderFence(Block block)
    {
        return DispatchSegments.IsBaseExecutionOrderFence(block);
    }

    private IReadOnlySet<string> CollectFenceBlockIds(Dictionary<int, List<Block>> blocksBySectionOrder)
    {
        HashSet<string> fenceIds = new(StringComparer.Ordinal);

        foreach (Block block in blocksBySectionOrder.Values.SelectMany(blocks => blocks))
        {
            if (IsExecutionOrderFence(block))
            {
                _ = fenceIds.Add(block.Id);
            }
        }

        return fenceIds;
    }

    private SchedulingView BuildSchedulingView(WorkflowGraph graph)
    {
        SchedulingView view = SchedulingViewBuilder.Build(graph);

        if (view.ForcedCycleNodes.Count > 0)
        {
            foreach (IBlockGroupStrategy group in GroupStrategies())
            {
                group.ThrowIfCycleBreakTouchesGroup(graph, view.ForcedCycleNodes);
            }

            Logger.LogWarning(
                "Workflow graph contains cyclic references; broke the cycle at: {Nodes}. The forward placeholders resolve to no-data.",
                string.Join(", ", view.ForcedCycleNodes));
        }

        return view;
    }

    /// <summary>
    /// Every dispatch path runs the scheduler through this wrapper: it folds the scheduler's
    /// escaped-failure count into the end-of-run integrity gate, which a raw
    /// <see cref="LevelScheduler.RunAsync"/> call would silently drop.
    /// </summary>
    private async Task RunSchedulerAsync(
        SchedulingView view,
        Func<string, CancellationToken, Task> runBlock,
        CancellationToken cancellationToken)
    {
        int escapedFailures = await _scheduler.RunAsync(view, runBlock, cancellationToken);
        _ = Interlocked.Add(ref _schedulerEscapedBlockFailures, escapedFailures);
    }

    /// <summary>
    /// Single-section dispatch (used for loop sections post-expansion): builds a graph over the
    /// section's blocks and runs them under the cap.
    /// </summary>
    private async Task DispatchSectionBlocksAsync(
        Section section,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        WorkflowOutputContext? workflowOutputContext,
        CancellationToken cancellationToken)
    {
        Dictionary<int, List<Block>> sectionBlocks = new()
        {
            [section.Order] = [.. section.Blocks],
        };

        WorkflowGraph graph = _graphBuilder.Build(
            sectionBlocks,
            [.. (section.Workflow.Variables ?? [])],
            CollectFenceBlockIds(sectionBlocks));

        SchedulingView view = BuildSchedulingView(graph);

        Dictionary<string, Block> blocksByKey = new(StringComparer.Ordinal);
        foreach (Block b in section.Blocks)
        {
            blocksByKey[b.Id] = b;
        }

        List<(Section Section, Block Block, WorkflowOutputContext? Context)> dispatchedBlocks =
            [.. section.Blocks.Select(b => (section, b, workflowOutputContext))];

        await RunSchedulerAsync(
            view,
            (blockId, blockCt) => blocksByKey.TryGetValue(blockId, out Block? block)
                ? RunResolvedBlockAsync(
                    section,
                    block,
                    anonymisationMap,
                    userDataUnitOfWork,
                    workflowOutputContext,
                    dispatchedBlocks,
                    blockCt)
                : Task.CompletedTask,
            cancellationToken);
    }

    /// <summary>
    /// Per-block runner used by the scheduler. Brackets the block's own unit of work in a
    /// concurrency permit (<see cref="IBlockUnitPermit"/>), which is where the engine's cap now
    /// binds - the scheduler no longer guards its nodes. A block whose strategy runs units of its
    /// own is dispatched with no permit here; each of its units takes one.
    /// </summary>
    private async Task RunResolvedBlockAsync(
        Section section,
        Block block,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        WorkflowOutputContext? workflowOutputContext,
        IReadOnlyList<(Section Section, Block Block, WorkflowOutputContext? Context)> dispatchedBlocks,
        CancellationToken cancellationToken)
    {
        bool dispatchesNestedUnits = DispatchesNestedUnits(block);

        Task RunShellAsync(CancellationToken shellCancellationToken)
        {
            return RunResolvedBlockUnitAsync(
                section,
                block,
                anonymisationMap,
                userDataUnitOfWork,
                workflowOutputContext,
                dispatchedBlocks,
                dispatchesNestedUnits,
                shellCancellationToken);
        }

        await (dispatchesNestedUnits
            ? _scheduler.UnitPermits.RunNestedDispatchAsync(RunShellAsync, cancellationToken)
            : _scheduler.UnitPermits.RunUnitAsync(RunShellAsync, cancellationToken));
    }

    private bool DispatchesNestedUnits(Block block)
    {
        return _blockStrategies.TryGetValue(block.Type, out IBlockStrategy? strategy)
            && strategy.DispatchesNestedUnits(block);
    }

    /// <summary>
    /// One block's unit of work. <see cref="ExecuteBlockAsync"/> persists the block's state on its
    /// success and skip paths; this runner adds the catch-log-continue + OCE-rethrow shell and
    /// persists the failed block (with its error log) on the error path. Every write goes through
    /// the per-block pooled writer, so nothing touches the shared change tracker. Runs under one
    /// permit taken by <see cref="RunResolvedBlockAsync"/>.
    /// </summary>
    private async Task RunResolvedBlockUnitAsync(
        Section section,
        Block block,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        WorkflowOutputContext? workflowOutputContext,
        IReadOnlyList<(Section Section, Block Block, WorkflowOutputContext? Context)> dispatchedBlocks,
        bool dispatchesNestedUnits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await FenceIfExecutionWentTerminalAsync(cancellationToken);

        try
        {
            // The permit shape (DispatchesNestedUnits) is general — any strategy may dispatch its
            // own units through the context's permit surface. A group's orchestration belongs to
            // its group strategy, so only a group block routes into it.
            string? blockSkipReason;
            if (dispatchesNestedUnits && GroupStrategyOf(block) is { } group)
            {
                blockSkipReason = await group.RunGroupAsync(
                    new BlockGroupRun(
                        this,
                        section,
                        block,
                        group.MemberTagsOf(block),
                        anonymisationMap,
                        userDataUnitOfWork,
                        workflowOutputContext,
                        dispatchedBlocks),
                    cancellationToken);
            }
            else if (IsGroupMember(block, dispatchedBlocks))
            {
                blockSkipReason = await ExecuteMemberWithheldAsync(
                    section,
                    block,
                    anonymisationMap,
                    userDataUnitOfWork,
                    cancellationToken);
            }
            else
            {
                blockSkipReason = await ExecuteBlockAsync(
                    section,
                    block.Order,
                    anonymisationMap,
                    userDataUnitOfWork,
                    workflowOutputContext,
                    cancellationToken: cancellationToken);
            }

            if (blockSkipReason is not null)
            {
                Logger.LogInformation("{Block} skipped: {Reason}", block.Name, blockSkipReason);

                block.ExecutionState ??= new BlockExecutionState(block);
                if (block.ExecutionState.Status is not ExecutionStatus.Skipped)
                {
                    block.ExecutionState.MarkAsSkipped(blockSkipReason);
                }

                if (RootOutput?.Id is not null)
                {
                    await PersistBlockResultGuardedAsync(
                        block, null, BuildSkippedLog(section, blockSkipReason, block), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only a real abort (external cancel or the persistence breaker) propagates as a
            // cancellation. A block-internal TaskCanceledException with no token fired — e.g. an
            // HttpClient timeout — is a block failure, handled below; letting it propagate would
            // mislabel the whole run Cancelled while leaving this block's row in skeleton state.
            throw;
        }
        catch (Exception ex)
        {
            block.ExecutionState ??= new BlockExecutionState(block);

            if (block.ExecutionState.Status is not ExecutionStatus.Failed)
            {
                block.ExecutionState.MarkAsFailed(ex.Message);
            }

            ModelRefusal? refusal = (ex as ModelRefusalException)?.Refusal;

            if (refusal is not null)
            {
                Logger.LogWarning(
                    "The model refused block {BlockName} ({BlockId}) in section {SectionName}. Category: {RefusalCategory}, organization: {OrganizationId}, workflow type: {WorkflowTypeId}, execution: {WorkflowExecutionId}, model: {ModelId}, message: {MessageId}",
                    block.Name,
                    block.Id,
                    section.Title,
                    refusal.Category,
                    OrganizationId,
                    RootOutput?.WorkflowTypeId,
                    RootOutput?.Id,
                    refusal.ModelId,
                    refusal.RequestId);
            }
            else
            {
                Logger.LogError(
                    ex,
                    "Block execution for Type: {blockType} failed: {blockName}",
                    block.Type,
                    block.Name);
            }

            bool abortsExecution = ex is IAbortsWorkflowRun || block.AbortsExecutionOnFailure();

            if (RootOutput?.Id is not null && _executionFence is null)
            {
                WorkflowExecutionLog? errorLog = BuildErrorLog(section, block, ex.Message);
                if (errorLog is not null)
                {
                    errorLog.RefusalCategory = refusal?.Category;
                }

                // An aborting block persists on an uncancellable token: the trip that follows is
                // about to cancel the run token, which would kill the very record the abort exists
                // to leave behind. A tripped fence outranks that — the row belongs to another
                // writer, so nothing may land on it.
                await PersistBlockResultGuardedAsync(
                    block,
                    null,
                    errorLog,
                    abortsExecution ? CancellationToken.None : cancellationToken);
            }

            if (abortsExecution)
            {
                TripCriticalBlockAbort(block, ex);
            }
        }
    }

    /// <summary>
    /// A group member's unit of work: the block runs and its row persists (the group's own pass
    /// stages a durable response), but its report paragraph and log are withheld — a member's
    /// report output waits for its group, so the group's strategy materializes it from the stash
    /// once the group settles. A skip surfaces as usual; the shell writes its log row.
    /// </summary>
    private async Task<string?> ExecuteMemberWithheldAsync(
        Section section,
        Block block,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        string? prepareSkipReason = await PrepareBlockRunAsync(section, block, inputs: null, cancellationToken);
        if (prepareSkipReason is not null)
        {
            return prepareSkipReason;
        }

        Logger.LogDebug("{block} Executing group member with report output withheld", block);

        BlockAnnotations? blockAnnotations = await ExecuteBlockInOwnScopeAsync(
            section,
            block,
            anonymisationMap,
            userDataUnitOfWork,
            round: AgentRunIds.FIRST_ROUND,
            redoLeg: null,
            runPrelude: true,
            belongsToVerificationGroup: true,
            cancellationToken: cancellationToken);

        if (block.ExecutionState?.Status is ExecutionStatus.Skipped)
        {
            if (RootOutput?.Id is not null)
            {
                await PersistBlockResultGuardedAsync(block, null, null, cancellationToken);
            }

            return block.ExecutionState.Error;
        }

        _withheldMemberAnnotations[block.Id] = blockAnnotations;

        if (RootOutput?.Id is not null)
        {
            await PersistBlockResultGuardedAsync(block, null, null, cancellationToken);
        }

        return null;
    }

    #region Block group host

    ILogger IBlockGroupHost.Logger => Logger;

    PlatformSettings IBlockGroupHost.PlatformSettings => PlatformSettings.Value;

    IServiceScopeFactory IBlockGroupHost.ServiceScopeFactory => ServiceScopeFactory;

    WorkflowExecution? IBlockGroupHost.RootOutput => RootOutput;

    bool IBlockGroupHost.IsFenced => _executionFence is not null;

    IBlockUnitPermit IBlockGroupHost.UnitPermits => _scheduler.UnitPermits;

    Task<string?> IBlockGroupHost.PrepareBlockRunAsync(Section section, Block block, CancellationToken cancellationToken)
    {
        return PrepareBlockRunAsync(section, block, inputs: null, cancellationToken);
    }

    Task<BlockAnnotations?> IBlockGroupHost.ExecuteGroupPassAsync(
        BlockGroupRun run,
        Section section,
        Block block,
        int round,
        VerificationRedoLeg? redoLeg,
        bool runPrelude,
        CancellationToken cancellationToken)
    {
        return ExecuteBlockInOwnScopeAsync(
            section,
            block,
            run.AnonymisationMap,
            run.UserDataUnitOfWork,
            round,
            redoLeg,
            runPrelude,
            belongsToVerificationGroup: true,
            cancellationToken);
    }

    Task<string?> IBlockGroupHost.MaterializeGroupBlockOutputAsync(
        BlockGroupRun run,
        BlockAnnotations? blockAnnotations,
        CancellationToken cancellationToken)
    {
        return MaterializeBlockOutputAsync(
            run.Section, run.Block, blockAnnotations, run.WorkflowOutputContext, run.UserDataUnitOfWork, cancellationToken);
    }

    /// <summary>
    /// A member whose state is not Completed is excluded: its shell already spoke for it (error or
    /// skip log).
    /// </summary>
    async Task IBlockGroupHost.ReleaseWithheldMembersAsync(BlockGroupRun run, CancellationToken cancellationToken)
    {
        foreach (string memberTag in run.MemberTags)
        {
            foreach ((Section memberSection, Block memberBlock, WorkflowOutputContext? memberContext)
                in run.MembersCarrying(memberTag))
            {
                if (memberBlock.ExecutionState?.Status is not ExecutionStatus.Completed)
                {
                    continue;
                }

                _ = await MaterializeBlockOutputAsync(
                    memberSection,
                    memberBlock,
                    _withheldMemberAnnotations.GetValueOrDefault(memberBlock.Id),
                    memberContext,
                    run.UserDataUnitOfWork,
                    cancellationToken);
            }
        }
    }

    void IBlockGroupHost.Withhold(Block member, BlockAnnotations? blockAnnotations)
    {
        _withheldMemberAnnotations[member.Id] = blockAnnotations;
    }

    Task IBlockGroupHost.PersistBlockStateAsync(Block block, CancellationToken cancellationToken)
    {
        return PersistBlockResultGuardedAsync(block, null, null, cancellationToken);
    }

    Task IBlockGroupHost.FenceIfExecutionWentTerminalAsync(CancellationToken cancellationToken)
    {
        return FenceIfExecutionWentTerminalAsync(cancellationToken);
    }

    BlockAnnotations IBlockGroupHost.RebuildAnnotations(Section section, Block block, string? citationFactsJson)
    {
        var annotations = new BlockAnnotations();
        Dictionary<string, string?> textInputs = CollectAllInputs(section, block);
        Dictionary<string, Block> referencedBlocks = GetOutputsAndProperties(section, block, textInputs);
        bool isCitationSource = CitationSourcesOf(section.Workflow).Includes(block);
        ProcessAnnotations(block, annotations, referencedBlocks.Values, isCitationSource);
        if (isCitationSource && citationFactsJson is { Length: > 0 })
        {
            // ProcessAnnotations covers only text-derived annotations; an agent's citations are
            // not in the text, so re-apply its stored facts.
            annotations.AddFacts(AgentCitationFactStore.Deserialize(citationFactsJson));
            CitationTraces.Record(block, annotations, new PageIndex(AllPages), _workflowAnnotationsContext, OrganizationId);
        }

        return annotations;
    }

    #endregion

    /// <summary>
    /// Marks every block in a skipped section as Skipped, persists each through the per-block writer
    /// with its own skip log row (the activity feed resolves progress from per-block rows, so a
    /// section skipped without them would hold its step for the rest of the run), and logs the
    /// section skip. Runs in the single-threaded section prelude.
    /// </summary>
    private async Task SkipSectionAsync(
        Section section,
        string reason,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("{Section} skipped: {Reason}", section.Title, reason);

        foreach (Block block in section.Blocks)
        {
            block.ExecutionState ??= new BlockExecutionState(block);
            block.ExecutionState.MarkAsSkipped(reason);

            if (RootOutput?.Id is not null)
            {
                await PersistBlockResultGuardedAsync(
                    block, null, BuildSkippedLog(section, reason, block), cancellationToken);
            }
        }

        await LogSkippedSectionAsync(section, reason, userDataUnitOfWork);
    }

    private async Task LogSkippedSectionAsync(
        Section section,
        string? reason,
        IUserDataUnitOfWork userDataUnitOfWork,
        Block? block = null)
    {
        WorkflowExecutionLog? log = BuildSkippedLog(section, reason, block);

        if (log is null)
        {
            Logger.LogWarning("Root output is null, skipping skipped section log");
            return;
        }

        await userDataUnitOfWork.Repository<WorkflowExecutionLog>().AddAsync(log);
        _ = await userDataUnitOfWork.CompleteAsync();
    }

    /// <summary>
    /// Builds the skipped-section / skipped-block log row, or null when there is no execution
    /// snapshot. Per-block parallel paths hand the row to the pooled writer; the single-threaded
    /// section path writes it via <see cref="LogSkippedSectionAsync"/>.
    /// </summary>
    private WorkflowExecutionLog? BuildSkippedLog(Section section, string? reason, Block? block)
    {
        if (RootOutput?.Id is null)
        {
            return null;
        }

        return new WorkflowExecutionLog
        {
            SectionName = section.Title,
            BlockName = block?.Name
                ?? section.Title,
            BlockType = block?.Type.ToString(),
            ReplacementTag = null,
            Inputs = [],
            Prompt = null,
            Output = null,
            ConfidenceScore = null,
            Sources = [],
            Status = string.IsNullOrEmpty(reason)
                ? BlockExecutionStatus.Completed
                : BlockExecutionStatus.Skipped,
            Error = reason,
            Duration = TimeSpan.Zero,
            ModelUsed = null,
            WorkflowExecutionId = RootOutput.Id,
            ShouldDisplayInLogs = block?.ShouldDisplayInLogs
                ?? true,
            WorkflowId = RootOutput.Id,
            BlockId = block?.Id
        };
    }

    /// <summary>Builds the failed-block log row, or null when there is no execution snapshot.</summary>
    private WorkflowExecutionLog? BuildErrorLog(Section section, Block block, string error)
    {
        if (RootOutput?.Id is null)
        {
            return null;
        }

        var aiProps = block.Properties as AiQueryProperties;

        return new WorkflowExecutionLog
        {
            SectionName = section.Title,
            BlockName = block.Name,
            BlockType = block.Type.ToString(),
            ReplacementTag = block.ReplacementTag,
            Inputs = [],
            Prompt = aiProps?.UserPromptTemplatePreview
                ?? string.Empty,
            Output = block.BlockResponse?.RawResponse,
            ConfidenceScore = block.ExecutionState?.ConfidenceScore,
            Sources = block.ExecutionState?.Sources
                ?? [],
            Status = BlockExecutionStatus.Failed,
            Error = error,
            Duration = block.ExecutionState?.CompletedAt - block.ExecutionState?.StartedAt,
            ModelUsed = ModelUsedOf(block, aiProps),
            WorkflowExecutionId = RootOutput.Id,
            ShouldDisplayInLogs = block.ShouldDisplayInLogs,
            WorkflowId = RootOutput.Id,
            BlockId = block.Id
        };
    }

    /// <summary>
    /// Turns the intake channel's skipped-protected-files marker (a job input) into persisted
    /// Skipped log rows on the execution, then removes the marker so it never reaches
    /// SharedInputs — it is not a replacement tag, and <see cref="ProcessWorkflowVariables"/>
    /// would mangle its JSON ('|' becomes a newline).
    /// </summary>
    private async Task LogSkippedProtectedFilesAsync(
        Dictionary<string, string>? inputs,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        if (inputs is null
            || !inputs.TryGetValue(IntakeInputKeys.SKIPPED_PROTECTED_FILES, out string? marker))
        {
            return;
        }

        _ = inputs.Remove(IntakeInputKeys.SKIPPED_PROTECTED_FILES);

        if (RootOutput?.Id is null)
        {
            Logger.LogWarning("Root output is null, skipping protected-file intake logs");
            return;
        }

        List<SkippedProtectedFile> skippedFiles;

        try
        {
            skippedFiles = SkippedProtectedFile.Deserialize(marker);
        }
        catch (JsonException e)
        {
            Logger.LogWarning(e, "Unreadable skipped-protected-files marker; no intake log rows written");
            return;
        }

        foreach (SkippedProtectedFile skippedFile in skippedFiles)
        {
            // No WorkflowId: these rows are flushed before BuildExecutedWorkflowAsync persists the
            // executed clone, and a non-null value would violate the Workflows FK on relational
            // providers. Intake rows attach to the execution only, like AppendDocumentIntakeErrorLogAsync.
            await userDataUnitOfWork.Repository<WorkflowExecutionLog>().AddAsync(new WorkflowExecutionLog
            {
                SectionName = DOCUMENT_INTAKE_SECTION_NAME,
                BlockName = skippedFile.FileName,
                Status = BlockExecutionStatus.Skipped,
                Error = skippedFile.Reason,
                Duration = TimeSpan.Zero,
                WorkflowExecutionId = RootOutput.Id,
            });
        }

        _ = await userDataUnitOfWork.CompleteAsync();
    }

    private static string? GetSectionSkipReason(Section section)
    {
        return section.RequiredDocumentsNotProvided()
            ? "Required documents not provided"
            : StopMarker.IsIn(section.ExecutionState?.TextInput)
                ? StopMarker.SKIP_REASON
                : null;
    }

    private BlockExecutionContext CreateBlockExecutionContext(
        Section section,
        ConcurrentDictionary<string, string> anonymisationMap,
        IServiceProvider serviceProvider)
    {
        BlockExecutionContext blockExecutionContext = serviceProvider
            .GetRequiredService<BlockExecutionContext>();

        string? workflowTypeId = section.Workflow?.WorkflowTypeId;

        if (workflowTypeId is null)
        {
            Logger.LogError("Workflow type ID is null when setting up block execution context, setting to generic");
            workflowTypeId = WorkflowType.GENERIC;
        }

        _ = SharedInputs.TryGetValue(SharedInputKeys.ORGANIZATION_ID, out string? organizationId);

        blockExecutionContext.WorkflowTypeId = workflowTypeId;
        blockExecutionContext.AnonymisationMap = anonymisationMap;

        blockExecutionContext.OrganizationId = OrganizationId ?? organizationId;
        blockExecutionContext.UserId = UserId;
        blockExecutionContext.WorkflowExecutionId = RootOutput?.Id;
        blockExecutionContext.TemplateVersionId = RootOutput?.TemplateVersionId;
        blockExecutionContext.TemplateContentHash = RootOutput?.TemplateContentHash;
        blockExecutionContext.SectionId = section.Id;

        Logger.LogDebug("CreateBlockExecutionContext: Set BlockExecutionContext.OrganizationId to '{OrganizationId}', UserId to '{UserId}', WorkflowExecutionId to '{WorkflowExecutionId}', SectionId to '{SectionId}'",
            OrganizationId, UserId, RootOutput?.Id, section.Id);
        blockExecutionContext.ExecutionContext = ExecutionContext;
        blockExecutionContext.AllPages = AllPages;
        blockExecutionContext.Counterparty = CounterpartyId is not null
            ? _entityPlaceholderResolver.GetCounterparty(CounterpartyId)
            : null;
        blockExecutionContext.PeerCompanies = RetrievePeerCompanies();
        blockExecutionContext.RagResultsCache = _ragResultsCache;
        blockExecutionContext.WebApiCookies = _webApiCookies;
        blockExecutionContext.UnitPermits = _scheduler.UnitPermits;

        return blockExecutionContext;
    }

    private List<Counterparty> RetrievePeerCompanies()
    {
        List<Counterparty> peerCompanies = [];

        foreach (string? peerCompanyId in PeerCompanyIds ?? [])
        {
            Counterparty? peerCompany = _entityPlaceholderResolver.GetCounterparty(
                peerCompanyId);

            if (peerCompany is null)
            {
                continue;
            }

            peerCompanies.Add(peerCompany);
        }

        return peerCompanies;
    }

    private async Task BuildBlockOutputAsync(
        Section section,
        WorkflowOutputContext workflowOutputContext,
        Block block,
        BlockAnnotations? blockAnnotations,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        BuildDataOutput(block, workflowOutputContext, blockAnnotations);

        // No execution snapshot to persist into (editor / single-block dev run).
        if (RootOutput?.Id is null)
        {
            return;
        }

        (ReportParagraph? paragraph, string? reason, bool isValidForOutput) = await WorkflowOutputService.BuildReportParagraphFromBlock(
            block,
            workflowOutputContext,
            blockAnnotations,
            _workflowAnnotationsContext);

        CitationTraces.RecordReport(block, paragraph, blockAnnotations, OrganizationId);

        if (!isValidForOutput)
        {
            // A block with no report output that ran is not skipped work — it ran and may have spent
            // money — so its log row states its real work (a group's, the group's) instead of a
            // zero-duration skip with no model.
            if (GroupStrategyOf(block) is { } group && reason is null)
            {
                await PersistBlockResultGuardedAsync(
                    block, null, group.BuildLog(section, block, RootOutput.Id, AgentModelTiersForRun()), cancellationToken);
                return;
            }

            if (reason is null && block.ExecutionState is { } ranState)
            {
                await PersistBlockResultGuardedAsync(
                    block, null, BuildBlockLog(section, block, ranState), cancellationToken);
                return;
            }

            Logger.LogWarning(
                "No output produced for block {BlockName}, skipping output creation",
                block.Name);

            // Persist the block's state plus its skipped log through the per-block pooled context.
            await PersistBlockResultGuardedAsync(block, null, BuildSkippedLog(section, reason, block), cancellationToken);
            return;
        }

        BlockExecutionState? blockExecutionState = block.ExecutionState;

        if (blockExecutionState is null)
        {
            Logger.LogWarning("ExecutionState is null for block {BlockName}", block.Name);
            await PersistBlockResultGuardedAsync(block, null, null, cancellationToken);
            return;
        }

        WorkflowExecutionLog? executionLog = GroupStrategyOf(block) is { } groupStrategy
            ? groupStrategy.BuildLog(section, block, RootOutput.Id, AgentModelTiersForRun())
            : BuildBlockLog(section, block, blockExecutionState);

        // One pooled context per block writes the block's state plus its report row together. When a
        // paragraph exists the log rides on its WorkflowExecutionLogs navigation (so it is passed as
        // null to avoid a double insert); otherwise the standalone log is written directly.
        if (paragraph is not null && executionLog is not null)
        {
            paragraph.WorkflowExecutionLogs = [executionLog];
            await PersistBlockResultGuardedAsync(block, paragraph, null, cancellationToken);
        }
        else
        {
            await PersistBlockResultGuardedAsync(block, paragraph, executionLog, cancellationToken);
        }
    }

    /// <summary>
    /// Puts the block's data output on the block for the per-block writer. A block marked as a data
    /// output whose result is not JSON has failed: its log row says why, and the run goes on. An
    /// editor run persists nothing, so it only checks the JSON, and the author sees the failure there
    /// too.
    /// </summary>
    private void BuildDataOutput(
        Block block,
        WorkflowOutputContext workflowOutputContext,
        BlockAnnotations? blockAnnotations)
    {
        bool persists = RootOutput?.Id is not null;
        ErrorOr<ReportDataOutput>? build = WorkflowOutputService.BuildDataOutputFromBlock(
            block,
            workflowOutputContext,
            persists ? blockAnnotations : null,
            _workflowAnnotationsContext);

        block.DataOutput = build is { IsError: false } built && persists ? built.Value : null;

        if (build is not { IsError: true } refused)
        {
            return;
        }

        string error = refused.FirstError.Description;
        Logger.LogWarning("{Block} has no data output: {Error}", block, error);
        block.ExecutionState?.MarkAsFailed(error);
    }

    private AgentModelTiers AgentModelTiersForRun()
    {
        return AgentRunBlockSupport.ModelTiersOf(PlatformSettings.Value, OrganizationId);
    }

    /// <summary>
    /// The model column of a block's log row: an agent-family block's configured model, for an
    /// AiQuery block with a model override the id its call was sent to when the connector reports
    /// it, else the override, an AiQuery block's tier label, and nothing for a block that calls no
    /// model.
    /// </summary>
    private string? ModelUsedOf(Block block, AiQueryProperties? aiProps)
    {
        if (block.Properties is IAgentRunProperties agentProps)
        {
            return AgentModelTiersForRun().ConfiguredModelOf(agentProps);
        }

        if (aiProps is null)
        {
            return null;
        }

        if (aiProps.ModelIdOverride is not null)
        {
            return aiProps.ExecutionState?.ModelCalled
                ?? aiProps.ModelIdOverride;
        }

        return aiProps.ShouldUseAdvancedModel
            ? "Advanced Model"
            : "Simple Model";
    }

    private WorkflowExecutionLog? BuildBlockLog(
        Section section,
        Block block,
        BlockExecutionState blockExecutionState)
    {
        if (RootOutput?.Id is null)
        {
            return null;
        }

        var aiProps = block.Properties as AiQueryProperties;

        return new WorkflowExecutionLog
        {
            SectionName = section.Title,
            BlockName = block.Name,
            BlockType = block.Type.ToString(),
            ReplacementTag = block.ReplacementTag,
            Inputs = [],
            Prompt = string.Empty,
            Output = block.BlockResponse?.RawResponse,
            ConfidenceScore = blockExecutionState.ConfidenceScore,
            Sources = blockExecutionState.Sources
                ?? [],
            Status = blockExecutionState.Error is not null
                ? BlockExecutionStatus.Failed
                : BlockExecutionStatus.Completed,
            Error = blockExecutionState.Error,
            Duration = blockExecutionState.CompletedAt - blockExecutionState.StartedAt,
            ModelUsed = ModelUsedOf(block, aiProps),
            WorkflowExecutionId = RootOutput.Id,
            ShouldDisplayInLogs = block.ShouldDisplayInLogs,
            WorkflowId = RootOutput.Id,
            BlockId = block.Id,
        };
    }

    public async Task CreateRootOutputAsync(
        IUserDataUnitOfWork userDataUnitOfWork,
        string? jobId,
        Workflow rootWorkflow,
        string organizationId,
        string? userId,
        Dictionary<string, object>? variables = null,
        string? workflowExecutionId = null)
    {
        DateTime? reviewDateOutput = SharedInputs.TryGetValue(SharedInputKeys.REPORT_DATE, out string? reviewDate)
            ? DateTime.Parse(reviewDate).ToUniversalTime()
            : null;

        List<Counterparty>? currentPeerCompanies = null;

        if (PeerCompanyIds is not null && PeerCompanyIds.Count != 0)
        {
            currentPeerCompanies = [];

            PeerCompanyIds.ForEach(x =>
            {
                currentPeerCompanies.Add(new Counterparty { Id = x });
            });
        }

        WorkflowExecution? root = workflowExecutionId is not null
            ? await WorkflowOutputService.GetWorkflowExecution(
                workflowExecutionId,
                userDataUnitOfWork)
            : await WorkflowOutputService.CreateExecutionFromWorkflowAsync(
                rootWorkflow,
                new WorkflowOutputContext()
                {
                    CurrentUserId = userId,
                    CurrentCounterparty = CounterpartyId is not null
                        ? new Counterparty { Id = CounterpartyId }
                        : null,
                    CurrentOrganizationId = organizationId,
                    CurrentPeerCompanies = currentPeerCompanies,
                },
                userDataUnitOfWork,
                reviewDate: reviewDateOutput,
                workflowState: WorkflowState.Running,
                jobId: jobId,
                variables: variables);

        RootOutput = root;
    }

    public async Task UpdateWorkflowStateAsync(
        WorkflowState state,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        if (RootOutput is null)
        {
            Logger.LogWarning("Root output is null, skipping state update");
            return;
        }

        Logger.LogInformation("Updating workflow state to {state}, root output: {rootOutput}", state, RootOutput.Id);

        (string Kind, string Detail)? warning = state is WorkflowState.Draft ? CompletionWarning : null;

        bool won = await userDataUnitOfWork.WorkflowExecutions.TryTransitionStateIfActiveAsync(
            RootOutput,
            state,
            warningKind: warning?.Kind,
            warningDetail: warning?.Detail);

        if (!won)
        {
            throw new WorkflowExecutionFencedException(RootOutput.Id);
        }

        if (!WorkflowExecution.IsActiveState(state))
        {
            await RunFinalStateHooksAsync(RootOutput.Id, RootOutput.OrganizationId, state);
        }
    }

    /// <summary>
    /// The final state's hooks, from one fresh scope. The agent-run hook is the completed
    /// terminal's — every state in <see cref="WorkflowExecution.CompletedStates"/>, not Draft
    /// alone, because a caller may materialize an execution straight into Confirmed and would
    /// otherwise win the transition and clean up nothing. The failed and cancelled terminals reach cleanup through the
    /// guarded terminal funnel and the cancel command; a completion reached nothing at all, so
    /// an execution that finished with a run still unacknowledged left its evidence for the
    /// runtime's own reaper, and an execution that finished with session bundles in custody kept
    /// paying for bundles nothing can ever redo from. Resolved from a fresh scope and optional,
    /// like every other holder of this seam: the service exists only where the runtime is
    /// enabled. Settlement runs for every final state this writer reaches; it is safe to repeat.
    /// </summary>
    private async Task RunFinalStateHooksAsync(
        string workflowExecutionId,
        string? rowOrganizationId,
        WorkflowState terminalState)
    {
        // The execution row's own stamp comes first: this writer runs on paths that never went
        // through ExecuteWorkflowAsync, which is what fills the handler's copy.
        string? organizationId = rowOrganizationId ?? OrganizationId;
        if (string.IsNullOrEmpty(organizationId))
        {
            return;
        }

        using IServiceScope scope = ServiceScopeFactory.CreateScope();
        if (WorkflowExecution.CompletedStates.Contains(terminalState)
            && scope.ServiceProvider.GetService<IAgentRunTerminalCleanup>() is IAgentRunTerminalCleanup cleanup)
        {
            try
            {
                await cleanup.CleanUpAsync(workflowExecutionId, organizationId, terminalState);
            }
            catch (Exception exception)
            {
                Logger.LogError(exception,
                    "Agent-run cleanup broke its never-throw contract for execution {ExecutionId}",
                    workflowExecutionId);
            }
        }

        IRunSettlement settlement = scope.ServiceProvider.GetService<IRunSettlement>() ?? new NoRunSettlement();
        try
        {
            await settlement.SettleAsync(workflowExecutionId, organizationId);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception,
                "Run settlement broke its never-throw contract for execution {ExecutionId}",
                workflowExecutionId);
        }
    }

    #endregion

    #region Private methods

    protected virtual Task OnBlockExecutedAsync(
        Block block,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gives a run that completes normally a warning of the handler's own, written with the Draft
    /// transition. A block group's warning outranks it.
    /// </summary>
    protected void ConcludeWithWarning(string kind, string detail)
    {
        _handlerWarning = (kind, detail);
    }

    internal (string Kind, string Detail)? CompletionWarning => _completionWarning ?? _handlerWarning;

    private void ProcessWorkflowVariables(Workflow workflow, Dictionary<string, string>? inputs)
    {
        foreach (WorkflowVariable variable in workflow.Variables)
        {
            if (SharedInputs.ContainsKey(variable.ReplacementTag)
                || variable.DefaultValue is null)
            {
                continue;
            }

            SharedInputs[variable.ReplacementTag] = ToSharedInput(variable, variable.DefaultValue);
        }

        if (inputs is null)
        {
            return;
        }

        foreach ((string replacementTag, string value) in inputs)
        {
            WorkflowVariable? variable = workflow.Variables.FirstOrDefault(
                v => string.Equals(v.ReplacementTag, replacementTag, ReplacementTags.Comparison));
            SharedInputs[replacementTag] = ToSharedInput(variable, value);
        }
    }

    private static string ToSharedInput(WorkflowVariable? variable, string value)
    {
        return (variable?.ToPlaceholderText(value) ?? value).Replace('|', '\n');
    }

    private readonly ConcurrentDictionary<string, List<Document>> _documentsCache = new();

    private async Task<Workflow?> BuildExecutedWorkflowAsync(
        Workflow sourceWorkflow,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        if (RootOutput is null)
        {
            return null;
        }

        foreach (Section section in sourceWorkflow.Sections.OrderBy(s => s.Order))
        {
            foreach (Block block in section.Blocks)
            {
                bool added = _documentsCache.TryAdd(SectionBlockCacheKey(section.Order, block.Order), block.Documents);
                if (!added)
                {
                    Logger.LogWarning("Block {BlockId} already exists in cache, skipping document copy", $"{block.Name}_{block.Order}");
                }
            }
        }

        string? workflowTypeId = sourceWorkflow.WorkflowTypeId;
        string? userId = sourceWorkflow.UserId;
        string? organizationId = sourceWorkflow.OrganizationId;

        Workflow clone = new();
        sourceWorkflow.CloneTo(clone);

        // The cloned workflow is persisted as an execution snapshot in UserData.Workflows,
        // so its entity IDs must be fresh — preserving them would collide with prior
        // snapshots. Signature-override resolution is unaffected: rows are keyed by
        // StableBlockId, which CloneTo preserves.
        clone.Id = RootOutput?.Id ?? clone.Id;
        clone.User = null;
        clone.UserId = userId;
        clone.Organization = null;
        clone.OrganizationId = organizationId;

        clone.WorkflowType = null;
        clone.WorkflowTypeId = workflowTypeId;

        await userDataUnitOfWork.Workflows.AddAsync(clone);
        _ = await userDataUnitOfWork.CompleteAsync();

        foreach (Section section in clone.Sections.OrderBy(s => s.Order))
        {
            foreach (Block block in section.Blocks)
            {
                List<Document> doc = _documentsCache.GetValueOrDefault(SectionBlockCacheKey(section.Order, block.Order), []);
                block.Documents.AddRange(doc);
            }
        }

        return clone;
    }

    private static string SectionBlockCacheKey(int sectionOrder, int blockOrder)
    {
        return $"s{sectionOrder}_b{blockOrder}";
    }

    /// <summary>
    /// Blocks of a finished run that never reached a terminal execution state (no state at all,
    /// NotExecuted, or still Running). On a persisted run these are lost results: their rows in
    /// UserData still show the skeleton. Walks the live section tree, which matches dispatch only
    /// while nothing adds sections mid-run — loop expansion mutates blocks in place within already
    /// dispatched sections, and parameterized re-expansion is neutralized by the Skipped guard.
    /// Exposed as <c>internal static</c> for direct unit testing.
    /// </summary>
    internal static List<Block> CollectUnconcludedBlocks(Workflow workflow)
    {
        return [.. workflow.Sections
            .Where(s => !s.IsSeparator)
            .SelectMany(s => s.Blocks)
            .Where(b => b.ExecutionState is null
                || b.ExecutionState.Status is ExecutionStatus.NotExecuted or ExecutionStatus.Running)];
    }

    /// <summary>
    /// Sole persist path for block results during a run. A failure is never rethrown (except
    /// cancellation): the exact write is queued for one end-of-run retry, and the run is failed at
    /// the end by <see cref="ThrowIfResultsLostAsync"/> — never silently dropped, never allowed to
    /// escape into the scheduler where it would be swallowed.
    /// </summary>
    private async Task PersistBlockResultGuardedAsync(
        Block block,
        ReportParagraph? paragraph,
        WorkflowExecutionLog? executionLog,
        CancellationToken cancellationToken)
    {
        try
        {
            await _blockWriter.PersistBlockResultAsync(block, paragraph, executionLog, cancellationToken);
            _ = Interlocked.Exchange(ref _consecutivePersistFailures, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // An internal abort cancels the run token, which can kill an in-flight write whose
            // result exists nowhere but memory. Queue it so the abort names that block instead of
            // dropping it; an external cancel discards the queue unread.
            if (InternalAbortRequested)
            {
                _unpersistedResults.Enqueue(new PendingBlockPersist(block, paragraph, executionLog));
            }

            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to persist result for block {BlockName}; queued for end-of-run retry.",
                block.Name);

            _unpersistedResults.Enqueue(new PendingBlockPersist(block, paragraph, executionLog));

            if (Interlocked.Increment(ref _consecutivePersistFailures) >= PERSISTENCE_BREAKER_THRESHOLD)
            {
                TripPersistenceBreaker();
            }
        }
    }

    /// <summary>
    /// Mid-run checkpoint writes (block-state bookkeeping the post-block persist supersedes).
    /// Shares the breaker with <see cref="PersistBlockResultGuardedAsync"/> but never the taint
    /// queue: a checkpoint carries no result, so a failed one must not read as lost results at
    /// the end of the run. It rethrows instead — the caller decides pre-spend whether to proceed
    /// unrecorded, and for an agent run the answer is no.
    /// </summary>
    private async Task PersistBlockCheckpointGuardedAsync(Block block, CancellationToken cancellationToken)
    {
        try
        {
            await _blockWriter.PersistBlockResultAsync(block, null, null, cancellationToken);
            _ = Interlocked.Exchange(ref _consecutivePersistFailures, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to persist mid-run checkpoint for block {BlockName}.", block.Name);

            if (Interlocked.Increment(ref _consecutivePersistFailures) >= PERSISTENCE_BREAKER_THRESHOLD)
            {
                TripPersistenceBreaker();
            }

            throw;
        }
    }

    /// <summary>
    /// Aborts the run via the linked token: every result since the last successful persist is
    /// already queued in memory, and executing further blocks would spend model tokens on results
    /// that cannot be saved.
    /// </summary>
    private void TripPersistenceBreaker()
    {
        if (_persistenceBreakerTripped)
        {
            return;
        }

        _persistenceBreakerTripped = true;

        Logger.LogError(
            "{Threshold} consecutive block-persist failures; aborting the run instead of executing further blocks.",
            PERSISTENCE_BREAKER_THRESHOLD);

        CancelRunToken();
    }

    /// <summary>
    /// Block boundary: re-reads the execution's stored state so a cancel or janitor fence from
    /// another process is noticed between blocks instead of after the run has burned through every
    /// remaining one. A terminal read stops this run exactly like an external cancellation.
    /// </summary>
    private async Task FenceIfExecutionWentTerminalAsync(CancellationToken cancellationToken)
    {
        if (RootOutput?.Id is null || OrganizationId is null || _executionFence is not null)
        {
            return;
        }

        WorkflowState? storedState;
        try
        {
            using IServiceScope scope = ServiceScopeFactory.CreateScope();
            IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

            storedState = await unitOfWork.WorkflowExecutions.GetStoredStateAsync(
                RootOutput.Id,
                OrganizationId,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The probe is protection, not a prerequisite: a transient read failure must not fail
            // the block it was about to guard. The next boundary probes again.
            Logger.LogWarning(
                ex,
                "Fence probe failed for execution {ExecutionId}; continuing with the block.",
                RootOutput.Id);
            return;
        }

        if (storedState is null || WorkflowExecution.IsActiveState(storedState.Value))
        {
            return;
        }

        _executionFence = new ExecutionFence(storedState.Value);

        Logger.LogWarning(
            "Execution {ExecutionId} is already {State}; stopping this run at the block boundary instead of executing further blocks.",
            RootOutput.Id,
            storedState);

        CancelRunToken();
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Cancels the run-scoped abort token for an internal abort. Cancel can race the end-of-run
    /// dispose — that race is benign, the run is ending either way.
    /// </summary>
    private void CancelRunToken()
    {
        try
        {
            _executionAbortCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Aborts the run via the linked token after a block whose failure must not be absorbed: the
    /// deliverable that justified the upstream spend is gone, so executing the remaining blocks
    /// only spends more. The failing block's own state is already persisted by the caller on an
    /// uncancellable token; siblings this cancels are caught by the taint queue.
    /// </summary>
    private void TripCriticalBlockAbort(Block block, Exception blockFailure)
    {
        string blockName = DisplayName(block);
        CriticalBlockFailure failure = blockFailure is IAbortsWorkflowRun classified
            ? new CriticalBlockFailure(
                blockName,
                $"Block '{blockName}': {classified.FailureDetail}",
                classified.FailureKind)
            : new CriticalBlockFailure(
                blockName,
                $"Block '{blockName}' is critical and failed, so the execution was aborted: {blockFailure.Message}");

        lock (_criticalAbortLock)
        {
            if (_criticalBlockFailure is not null)
            {
                return;
            }

            _criticalBlockFailure = failure;
        }

        Logger.LogError(
            "{Block} failure aborts the run instead of executing further blocks: {Reason}",
            blockName,
            failure.Explanation);

        CancelRunToken();
    }

    /// <summary>
    /// The critical-abort outcome. Unlike the breaker, the store is healthy here — only a block
    /// failed — so every write the abort cancelled is replayed once, and whatever still fails is
    /// named alongside the block that caused the abort.
    /// </summary>
    private async Task<CriticalBlockFailureException> BuildCriticalAbortExceptionAsync(Workflow workflow)
    {
        CriticalBlockFailure failure = _criticalBlockFailure!;

        (List<string> recovered, List<string> lost, Exception? lastReplayFailure) =
            await ReplayUnpersistedResultsAsync();

        var abort = new CriticalBlockFailureException(failure, recovered, lost, lastReplayFailure);

        Logger.LogError(
            abort,
            "Workflow {WorkflowId} aborted by the critical failure of block {BlockName}.",
            workflow.Id,
            failure.BlockName);

        return abort;
    }

    /// <summary>
    /// The breaker-abort outcome: everything in the taint queue is reported lost (no retry — the
    /// store just failed three consecutive writes) alongside the blocks the abort prevented from
    /// concluding.
    /// </summary>
    private WorkflowResultsLostException BuildBreakerAbortException(Workflow workflow)
    {
        var resultsLost = new WorkflowResultsLostException(
            permanentlyLostBlocks: [.. DrainUnpersistedResults().Select(e => DisplayName(e.Block))],
            recoveredBlocks: [],
            unconcludedBlocks: [.. CollectUnconcludedBlocks(workflow).Select(DisplayName)],
            schedulerEscapedCount: Volatile.Read(ref _schedulerEscapedBlockFailures),
            retryAttempted: false);

        Logger.LogError(
            resultsLost,
            "Workflow {WorkflowId} aborted by the persistence breaker with lost results.",
            workflow.Id);

        return resultsLost;
    }

    /// <summary>
    /// End-of-run integrity check for persisted runs: retries every queued block persist once, then
    /// throws <see cref="WorkflowResultsLostException"/> if anything about the stored execution is
    /// untrusted — a persist failed mid-run (even if the retry just recovered it), a block never
    /// concluded, or a failure escaped the block runner. The throw is what turns silent result loss
    /// into a Failed execution at the job layer.
    /// </summary>
    private async Task ThrowIfResultsLostAsync(Workflow workflow)
    {
        if (RootOutput?.Id is null)
        {
            return;
        }

        List<Block> unconcludedBlocks = CollectUnconcludedBlocks(workflow);
        int escapedFailures = Volatile.Read(ref _schedulerEscapedBlockFailures);

        if (_unpersistedResults.IsEmpty && unconcludedBlocks.Count == 0 && escapedFailures == 0)
        {
            return;
        }

        (List<string> recovered, List<string> lost, Exception? lastReplayFailure) =
            await ReplayUnpersistedResultsAsync();

        var resultsLost = new WorkflowResultsLostException(
            lost,
            recovered,
            [.. unconcludedBlocks.Select(DisplayName)],
            escapedFailures,
            snapshotFailure: null,
            innerException: lastReplayFailure);

        Logger.LogError(resultsLost, "Workflow {WorkflowId} finished with lost or untrusted results.", workflow.Id);

        throw await RecordExecutionFailureAsync(resultsLost);
    }

    private static string DisplayName(Block block)
    {
        return string.IsNullOrEmpty(block.Name)
            ? block.Id
            : block.Name;
    }

    /// <summary>
    /// Records the failure on the execution's own log — the only place a run-level failure is
    /// visible to the user — and hands the exception back, so every such path appends before it
    /// throws.
    /// </summary>
    private async Task<TFailure> RecordExecutionFailureAsync<TFailure>(TFailure failure)
        where TFailure : Exception
    {
        await WorkflowOutputService.AppendExecutionErrorLogAsync(RootOutput?.Id, failure.Message);
        return failure;
    }

    /// <summary>
    /// Drains the taint queue deduped by block id — the writer persists current state, so the last
    /// entry per block wins.
    /// </summary>
    private List<PendingBlockPersist> DrainUnpersistedResults()
    {
        Dictionary<string, PendingBlockPersist> byBlockId = [];
        while (_unpersistedResults.TryDequeue(out PendingBlockPersist? entry))
        {
            byBlockId[entry.Block.Id] = entry;
        }

        return [.. byBlockId.Values];
    }

    /// <summary>
    /// Retries each queued persist once through a fresh pooled context. Runs on
    /// <see cref="CancellationToken.None"/>: by this point the run's token may already be canceled,
    /// and this retry is the last chance to land results that exist only in memory.
    /// </summary>
    private async Task<(List<string> Recovered, List<string> Lost, Exception? LastFailure)> ReplayUnpersistedResultsAsync()
    {
        List<string> recovered = [];
        List<string> lost = [];
        Exception? lastFailure = null;

        foreach (PendingBlockPersist entry in DrainUnpersistedResults())
        {
            string blockName = DisplayName(entry.Block);

            try
            {
                await _blockWriter.PersistBlockResultAsync(entry.Block, entry.Paragraph, entry.Log, CancellationToken.None);
                recovered.Add(blockName);

                Logger.LogWarning(
                    "End-of-run retry recovered the result of block {BlockName}; the run is still failed because persistence misbehaved mid-run.",
                    blockName);
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                lost.Add(blockName);
                Logger.LogError(ex, "End-of-run retry failed for block {BlockName}; its result is lost.", blockName);
            }
        }

        return (recovered, lost, lastFailure);
    }

    /// <summary>
    /// Flushes the shared unit of work and persists the workflow/section execution-state snapshot.
    /// Never throws: it runs in the execute path's finally, where a throw would replace the
    /// in-flight exception (e.g. turning a user cancellation into a DB failure). Instead the
    /// failure is returned, and <see cref="ExecuteWorkflowAsync"/> surfaces it on the success path
    /// only — a run whose final snapshot could not be written must not conclude as finished.
    /// </summary>
    private async Task<Exception?> SaveExecutedWorkflowResultsAsync(
        Workflow workflow,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        if (RootOutput is null)
        {
            return null;
        }

        try
        {
            _ = await userDataUnitOfWork.CompleteAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to flush the shared unit of work at the end of workflow {WorkflowId}.",
                workflow.Id);

            return ex;
        }

        // Only the executed clone (whose Id is RootOutput.Id) has rows in UserData to update.
        if (workflow.Id != RootOutput.Id)
        {
            return null;
        }

        try
        {
            await _blockWriter.PersistWorkflowExecutionStateAsync(workflow, CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to persist the workflow/section execution-state snapshot for workflow {WorkflowId}.",
                workflow.Id);

            return ex;
        }
    }

    private async Task<WorkflowOutputContext?> SetupWorkflowOutputContextAsync(
        Section section,
        WorkflowOutputContext? workflowOutputContext,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        if (RootOutput is null)
        {
            return null;
        }

        if (workflowOutputContext is null)
        {
            return null;
        }

        ReportSection? reportSection = await WorkflowOutputService
            .CreateReportSectionAsync(
                section,
                workflowOutputContext,
                userDataUnitOfWork);

        if (reportSection is not null)
        {
            workflowOutputContext.CurrentSectionId = reportSection.Id;
        }

        return workflowOutputContext;
    }

    private WorkflowOutputContext InitializeContext(WorkflowExecution? workflowOutput)
    {
        ArgumentNullException.ThrowIfNull(workflowOutput, nameof(workflowOutput));

        var workflowOutputContext = new WorkflowOutputContext
        {
            CurrentWorkflowOutputRoot = workflowOutput,
            RootElementId = workflowOutput.Id
        };

        if (CounterpartyId is not null)
        {
            Counterparty? counterparty = _entityPlaceholderResolver.GetCounterparty(
                CounterpartyId);
            if (counterparty is not null)
            {
                workflowOutputContext.CurrentCounterparty = counterparty;
            }
        }

        PeerCompanyIds?.ForEach(peerCompanyId =>
            {
                Counterparty? peerCompany = _entityPlaceholderResolver.GetCounterparty(
                    peerCompanyId);

                if (peerCompany is not null)
                {
                    workflowOutputContext.CurrentPeerCompanies ??= [];
                    workflowOutputContext.CurrentPeerCompanies.Add(peerCompany);
                }
            });

        return workflowOutputContext;
    }

    /// <summary>
    /// The once-per-block-per-run prelude — the stop-term scan and the replay-freeze /
    /// uncovered-block bookkeeping — is gated by <paramref name="runPrelude"/>: the verification
    /// loop turns it off for re-verify passes and redo legs, whose block already ran it on the
    /// first pass. Re-running it would double-count the replay counters, and a redo leg
    /// re-skipping on a stop term would silently swallow a leg the verdict demanded.
    /// </summary>
    private async Task<BlockAnnotations?> ExecuteBlockAsync(
        IServiceScope scope,
        Section section,
        Block block,
        BlockExecutionContext context,
        IUserDataUnitOfWork userDataUnitOfWork,
        bool runPrelude,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, string?> textInputs = CollectAllInputs(section, block);
        Dictionary<string, Block> referencedBlocks = GetOutputsAndProperties(section, block, textInputs);

        block.SetExecutionState(asPlaceHolder: true, textInputs);

        if (runPrelude && StopMarker.IsInInputsOf(block))
        {
            Logger.LogInformation("Block {block} contains stop term, skipping execution", block);

            block.ExecutionState ??= new BlockExecutionState(block);
            block.ExecutionState.MarkAsSkipped(StopMarker.SKIP_REASON);

            return null;
        }

        // Replay run: a matched freeze-eligible block carries its seeded response and never calls live.
        // An uncovered one (including loop clones) is recorded either way; whether it then skips or
        // runs live is the uncovered-block policy's call.
        if (runPrelude && _isReplayRun && FreezePolicy.IsFreezeEligible(block))
        {
            _ = Interlocked.Increment(ref _freezeEligibleCount);

            if (block.BlockResponse is not null)
            {
                _ = Interlocked.Increment(ref _freezeReplayedCount);
                Logger.LogDebug("{block} frozen replay: using captured output (no live call)", block);
                return CompleteWithoutLiveCall(block);
            }

            if (_replayUncoveredBlockPolicy == ReplayUncoveredBlockPolicy.SkipAsInconclusive)
            {
                Logger.LogDebug("{block} frozen replay: no captured output, recording uncovered (no live call)", block);
                _replayUnmatched.Add(new ReplayUnmatched(
                    section.Title ?? string.Empty,
                    block.ReplacementTag ?? string.Empty,
                    block.Type.ToString(),
                    REPLAY_REASON_UNCOVERED));
                return CompleteWithoutLiveCall(block);
            }

            Logger.LogInformation("{block} frozen replay: no captured output, running live (coverage disclosed)", block);
            _replayUnmatched.Add(new ReplayUnmatched(
                section.Title ?? string.Empty,
                block.ReplacementTag ?? string.Empty,
                block.Type.ToString(),
                REPLAY_REASON_UNCOVERED_RAN_LIVE));
        }

        context.TextInputs = textInputs;
        context.ReferencedBlocks = referencedBlocks;

        cancellationToken.ThrowIfCancellationRequested();

        BlockExecutionState? execution = block.ExecutionState;

        if (!_blockStrategies.TryGetValue(block.Type, out IBlockStrategy? strategy))
        {
            Logger.LogError("No strategy found for block type: {BlockType}", block.Type);

            string errorMessage = UnavailableBlockTypes.IsOutsideThePlatform(block.Type)
                ? UnavailableBlockTypes.RunMessage(block.Type)
                : $"No strategy found for block type: {block.Type}";
            execution?.MarkAsFailed(errorMessage);

            throw new InvalidOperationException(errorMessage);
        }

        if (execution?.Status is not ExecutionStatus.Running)
        {
            execution?.MarkAsStarted();
        }

        try
        {
            BlockStrategyOutput output = await strategy.ExecuteBlockAsync(block, context, cancellationToken);

            if (execution?.Status is ExecutionStatus.Skipped)
            {
                return null;
            }

            // A strategy that caught its own failure reports it on the execution state; the block
            // fails rather than completing over it.
            if (execution?.Error is string strategyError)
            {
                throw new InvalidOperationException(strategyError);
            }

            await ProcessBlockStrategyOutput(
                block,
                output,
                userDataUnitOfWork,
                cancellationToken);

            execution?.MarkAsCompleted();
        }
        catch (Exception ex)
        {
            execution?.MarkAsFailed(ex.Message);

            // A refusal is not a fault: the unit shell logs it once, with the run's context.
            if (ex is not ModelRefusalException)
            {
                Logger.LogError(ex, "Block execution for Type: {blockType} failed: {blockName}", block.Type, block.Name);
            }

            throw;
        }

        ProcessAnnotations(block, context.BlockAnnotations, referencedBlocks.Values, context.IsCitationSource);

        await UpdateCounterpartyAsync(
            scope.ServiceProvider,
            block,
            context,
            cancellationToken);

        await OnBlockExecutedAsync(block, userDataUnitOfWork, cancellationToken);

        return context.BlockAnnotations;
    }

    private async Task ProcessBlockStrategyOutput(
        Block block,
        BlockStrategyOutput output,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (output.Documents.Count == 0)
        {
            Logger.LogInformation(
                "No documents to process for block {BlockId}, skipping document processing",
                block.Id);

            return;
        }

        Workflow? workflow = block.Section?.Workflow;

        if (workflow is null)
        {
            Logger.LogWarning(
                "Root workflow is null for block {BlockId}, skipping document processing",
                block.Id);

            return;
        }

        _ = SharedInputs.TryGetValue(SharedInputKeys.ORGANIZATION_ID, out string? organizationId);
        _ = SharedInputs.TryGetValue(SharedInputKeys.JOB_ID, out string? jobId);

        await StoreDocumentsForJob(
            output,
            userDataUnitOfWork,
            organizationId,
            jobId,
            cancellationToken);

        await _documentProcessingService.ProcessDocumentsAsync(
            output.Documents,
            userDataUnitOfWork,
            workflow,
            workflow.WorkflowTypeId,
            cancellationToken: cancellationToken);

        await _documentProcessingService.LoadContextToWorkflowAsync(
            output.Documents,
            workflow,
            AllPages,
            onDocumentProcessed: async _ => await userDataUnitOfWork.CompleteAsync(cancellationToken),
            cancellationToken: cancellationToken);

        _ = await userDataUnitOfWork.CompleteAsync(cancellationToken);
    }

    private async Task StoreDocumentsForJob(
        BlockStrategyOutput output,
        IUserDataUnitOfWork userDataUnitOfWork,
        string? organizationId,
        string? jobId,
        CancellationToken cancellationToken)
    {
        if (jobId is null)
        {
            // Only save document to db if associated with a job. So from legacy
            // editor's block/section execution we skip this.
            return;
        }

        if (string.IsNullOrEmpty(organizationId))
        {
            throw new InvalidOperationException("OrganizationId cannot be null when saving document info with a job");
        }

        foreach (DocumentInfo documentInfo in output.Documents)
        {
            if (documentInfo.Document is null)
            {
                throw new InvalidOperationException("Document cannot be null when saving document info with a job");
            }

            if (documentInfo.Document.Name is not null
                && documentInfo.Document.Name.StartsWith(ExaSearchStrategy.FILE_TAG_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogInformation(
                    "Skipping storage of document {DocumentId} as it is produced by special block strategy.",
                    documentInfo.Document.Id);

                continue;
            }

            if (documentInfo.Document.FileMetadata is null)
            {
                throw new InvalidOperationException("Document FileMetadata cannot be null when saving document info with a job");
            }

            if (documentInfo.Document.FileMetadata.Bytes is null)
            {
                throw new InvalidOperationException("Document FileMetadata Bytes cannot be null when saving document info with a job");
            }

            StorageProvider storageProvider = _fileStorageManager.GetProviderForTenant(organizationId);

            documentInfo.JobId = jobId;
            documentInfo.Document.OrganizationId = organizationId;
            documentInfo.Document.Name += $"-{jobId}";
            documentInfo.Document.FileMetadata.OrganizationId = organizationId;
            documentInfo.Document.FileMetadata.StorageProvider = storageProvider;
            documentInfo.Document.FileMetadata.Name += $"-{jobId}";

            try
            {
                string storagePath = await _fileStorageManager.StoreFileAsync(
                    storageProvider.ToString(),
                    organizationId,
                    StorageCategories.Documents,
                    documentInfo.Document.FileMetadata.Id,
                    new MemoryStream(documentInfo.Document.FileMetadata.Bytes),
                    documentInfo.Document.FileMetadata.ContentType);

                documentInfo.Document.FileMetadata.ExternalStoragePath = storagePath;

                await userDataUnitOfWork.Repository<DocumentInfo>().AddAsync(documentInfo);
                _ = await userDataUnitOfWork.CompleteAsync(cancellationToken);

                Logger.LogInformation(
                    "File stored successfully. FileId: {FileId}, Provider: {Provider}, StoragePath: {StoragePath}.",
                    documentInfo.Document.FileMetadata.Id,
                    storageProvider,
                    storagePath);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to store file content for {FileId}", documentInfo.Document.FileMetadata.Id);

                throw;
            }
        }
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _counterpartyWriteGates = new();

    private async Task UpdateCounterpartyAsync(
        IServiceProvider serviceProvider,
        Block block,
        BlockExecutionContext blockExecutionContext,
        CancellationToken cancellationToken)
    {
        if (!block.ShouldUpdateCounterpartyWithResult
            || string.IsNullOrWhiteSpace(block.BlockResponse?.RawResponse))
        {
            return;
        }

        if (string.IsNullOrEmpty(CounterpartyId))
        {
            return;
        }

        string rawResponse = block.BlockResponse!.RawResponse!;
        bool writesToProperty = block.CounterpartyResultType == CounterpartyResultType.Property;
        string? propertyKey = block.UpdateCounterpartyPropertyKey?.Trim();

        if (writesToProperty && string.IsNullOrEmpty(propertyKey))
        {
            Logger.LogWarning(
                "Block {BlockId} writes a counterparty property but has no property key configured; skipping.",
                block.Id);

            return;
        }

        // A property write-back is a whole-bag read-modify-write, so concurrent blocks writing
        // different keys to the same counterparty would otherwise lose each other's updates. Serialize
        // per counterparty and re-read the current bag under the gate so each write sees the previous.
        SemaphoreSlim writeGate = _counterpartyWriteGates.GetOrAdd(
            CounterpartyId,
            static _ => new SemaphoreSlim(1, 1));
        await writeGate.WaitAsync(cancellationToken);

        try
        {
            if (IsRerun)
            {
                KeepCounterpartyWriteInRun(block, blockExecutionContext, rawResponse, writesToProperty, propertyKey);

                return;
            }

            _entityPlaceholderResolver.InvalidateCounterparty(CounterpartyId);
            Counterparty? counterparty = _entityPlaceholderResolver.GetCounterparty(CounterpartyId);

            if (counterparty is null)
            {
                return;
            }

            IUserDataDbContextAccessor userDataDbContextFactory = serviceProvider
                .GetRequiredService<IUserDataDbContextAccessor>();
            IUserDataDbContext db = await userDataDbContextFactory.CreateDbContextAsync();

            var updateEntity = new Counterparty { Id = counterparty.Id };
            _ = db.Attach(updateEntity);

            if (writesToProperty)
            {
                // Case-insensitive copy so the write upserts in place: a configured "ReportingStyle"
                // updates an existing "reportingStyle" rather than adding a case-variant duplicate.
                Dictionary<string, string> mergedProperties =
                    new(counterparty.Properties ?? [], StringComparer.OrdinalIgnoreCase);
                mergedProperties[propertyKey!] = rawResponse;

                updateEntity.Properties = mergedProperties;
                db.Entry(updateEntity).Property(c => c.Properties).IsModified = true;

                _ = blockExecutionContext.Counterparty?.Properties = mergedProperties;
            }
            else
            {
                updateEntity.LastFinancialAnalysisResult = rawResponse;
                db.Entry(updateEntity).Property(c => c.LastFinancialAnalysisResult).IsModified = true;

                _ = blockExecutionContext.Counterparty?.LastFinancialAnalysisResult = rawResponse;
            }

            _ = await db.SaveChangesAsync(cancellationToken);

            _entityPlaceholderResolver.InvalidateCounterparty(CounterpartyId);

            Logger.LogInformation(
                "Counterparty {CounterpartyId} updated from block {BlockId} ({Target}).",
                CounterpartyId,
                block.Id,
                writesToProperty ? $"property '{propertyKey}'" : nameof(Counterparty.LastFinancialAnalysisResult));
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to update counterparty from block {BlockId}.",
                block.Id);
        }
        finally
        {
            _ = writeGate.Release();
        }
    }

    /// <summary>
    /// A rerun saves no counterparty change. The value goes on the run's cached copy of the
    /// counterparty instead, so a later block of the same run reads it as it would in a report.
    /// </summary>
    private void KeepCounterpartyWriteInRun(
        Block block,
        BlockExecutionContext blockExecutionContext,
        string rawResponse,
        bool writesToProperty,
        string? propertyKey)
    {
        Counterparty? cached = _entityPlaceholderResolver.GetCounterparty(CounterpartyId);
        ApplyCounterpartyWrite(cached, rawResponse, writesToProperty, propertyKey);

        if (!ReferenceEquals(blockExecutionContext.Counterparty, cached))
        {
            ApplyCounterpartyWrite(blockExecutionContext.Counterparty, rawResponse, writesToProperty, propertyKey);
        }

        Logger.LogInformation(
            "Rerun: counterparty {CounterpartyId} not updated from block {BlockId} ({Target}); the value stays inside this run.",
            CounterpartyId,
            block.Id,
            writesToProperty ? $"property '{propertyKey}'" : nameof(Counterparty.LastFinancialAnalysisResult));
    }

    private static void ApplyCounterpartyWrite(Counterparty? counterparty, string rawResponse, bool writesToProperty, string? propertyKey)
    {
        if (counterparty is null)
        {
            return;
        }

        if (writesToProperty)
        {
            Dictionary<string, string> mergedProperties = new(counterparty.Properties ?? [], StringComparer.OrdinalIgnoreCase);
            mergedProperties[propertyKey!] = rawResponse;
            counterparty.Properties = mergedProperties;
        }
        else
        {
            counterparty.LastFinancialAnalysisResult = rawResponse;
        }
    }

    private void ProcessAnnotations(
        Block block,
        BlockAnnotations blockAnnotations,
        IEnumerable<Block> referencedBlocks,
        bool isCitationSource)
    {
        BlockAnnotationProcessor annotationProcessor = new(
            new FactExtractor(),
            new ConfidenceScoreExtractor(),
            new SourceExtractor());

        string? rawResponse = block.BlockResponse?.RawResponse;
        blockAnnotations.RestartTrace();

        annotationProcessor.ProcessConfidenceScore(
            rawResponse ?? string.Empty,
            blockAnnotations);

        annotationProcessor.ProcessSources(
            rawResponse ?? string.Empty,
            blockAnnotations);

        List<BlockAnnotations> referencedAnnotations = _workflowAnnotationsContext.GetReferencedAnnotations(referencedBlocks);
        InheritConfidenceAndSources(block, blockAnnotations, referencedAnnotations);

        if (isCitationSource)
        {
            var pages = new PageIndex(AllPages);

            if (referencedAnnotations.Count > 0 && block.ExecutionState is not null)
            {
                BlockAnnotationProcessor.InheritReferencedFacts(
                    blockAnnotations,
                    referencedAnnotations.SelectMany(annotations => annotations.Facts),
                    rawResponse);
            }

            BlockAnnotationProcessor.SplitNumericalFacts(blockAnnotations, pages);

            ProducingBlockCitations.CiteProducingBlocks(
                blockAnnotations,
                rawResponse,
                CitationSources.ReadInContent(block, referencedBlocks));

            CitationTraces.Record(block, blockAnnotations, pages, _workflowAnnotationsContext, OrganizationId);
        }
        else
        {
            block.CitationTrace = null;
        }

        _workflowAnnotationsContext.AddBlockAnnotations(block, blockAnnotations);

        if (block.BlockResponse?.RawResponse is not null
            && block.ExecutionState is not null)
        {
            block.BlockResponse.RawResponse = ClearAnnotationsFromText(
                block.ExecutionState,
                blockAnnotations,
                block.BlockResponse.RawResponse);
        }
    }

    /// <summary>
    /// An execution runs a frozen copy of its template, so its map is built once. The editor runs
    /// blocks of a template its author keeps editing between runs, so its map is built per run.
    /// </summary>
    private CitationSources CitationSourcesOf(Workflow workflow)
    {
        return RootOutput?.Id is null
            ? CitationSources.Of(workflow)
            : _citationSources.GetValue(workflow, CitationSources.Of);
    }

    private static void InheritConfidenceAndSources(
        Block block,
        BlockAnnotations blockAnnotations,
        List<BlockAnnotations> referencedBlockAnnotations)
    {
        if (referencedBlockAnnotations.Count <= 0
            || block.ExecutionState is null)
        {
            return;
        }

        IEnumerable<double> referencedConfidenceScores = referencedBlockAnnotations
            .SelectMany(annotations => annotations.ConfidenceScores)
            .Distinct();

        if (referencedConfidenceScores.Any())
        {
            block.ExecutionState.ConfidenceScore = referencedConfidenceScores.Average();
        }

        // Propagate sources (string-based, not fact sources)
        IEnumerable<string> referencedSources = referencedBlockAnnotations
            .SelectMany(annotations => annotations.Sources)
            .Distinct();

        if (referencedSources.Any())
        {
            foreach (string source in referencedSources)
            {
                if (block.ExecutionState.Sources.Contains(source))
                {
                    continue;
                }

                block.ExecutionState.Sources.Add(source);
            }

            blockAnnotations.AddSources([.. referencedSources]);
        }
    }

    private static string ClearAnnotationsFromText(
        BlockExecutionState executionState,
        BlockAnnotations blockAnnotations,
        string text)
    {
        if (blockAnnotations.ConfidenceScores.Count != 0)
        {
            executionState.ConfidenceScore = blockAnnotations.ConfidenceScores.First();
            text = BlockAnnotationProcessor.ClearConfidenceTags(text);
        }

        if (blockAnnotations.Sources.Count != 0)
        {
            foreach (string source in blockAnnotations.Sources)
            {
                if (executionState.Sources.Contains(source))
                {
                    continue;
                }

                executionState.Sources.Add(source);
            }

            text = BlockAnnotationProcessor.ClearSourceTags(text);
        }

        return text;
    }

    private Dictionary<string, Block> GetOutputsAndProperties(
        Section section,
        Block block,
        Dictionary<string, string?> textInputs)
    {
        Dictionary<string, Block> referencedBlocks = [];
        List<string> outputProperties = block.GetAllPlaceholders();

        foreach (string outputProperty in outputProperties)
        {
            if (textInputs.ContainsKey(outputProperty) || StoredKeyPlaceholders.KeyName(outputProperty) is not null)
            {
                continue;
            }

            (string? result, Block? referencedBlock) = PopulatePlaceholderStringFromSection(
                section,
                outputProperty);

            result = GetDefaultEmptyPlaceholderValue(outputProperty, result);

            if (result is null)
            {
                continue;
            }

            textInputs[outputProperty] = result;

            if (referencedBlock is null)
            {
                continue;
            }

            referencedBlocks[outputProperty] = referencedBlock;
        }

        return referencedBlocks;
    }

    private void GetOutputsAndProperties(
        Section section,
        Dictionary<string, string?> textInputs)
    {
        List<string> outputProperties = section.GetAllPlaceholders();

        foreach (string outputProperty in outputProperties)
        {
            if (textInputs.ContainsKey(outputProperty) || StoredKeyPlaceholders.KeyName(outputProperty) is not null)
            {
                continue;
            }

            (string? result, _) = PopulatePlaceholderStringFromSection(
                section,
                outputProperty);

            result = GetDefaultEmptyPlaceholderValue(outputProperty, result);

            if (result is null)
            {
                continue;
            }

            textInputs[outputProperty] = result;
        }
    }

    private string? GetDefaultEmptyPlaceholderValue(string key, string? value)
    {
        return SharedInputKeys.ReservedReplacementTags.Contains(key)
            ? value
            : string.IsNullOrWhiteSpace(value)
            || value.StartsWith(IChatConnector.ERROR_PREFIX)
                ? PlatformSettings.Value.IniaAi.NoDataForPlaceHolderText
                : value;
    }

    /// <summary>
    /// A loop-item block's whole output is the list item, so a dotted placeholder on it
    /// (<c>{Funds.reportingStyle}</c>) asks for data about the counterparty that item names, not for
    /// a tag inside the response text.
    /// </summary>
    private static bool IsLoopItemBlock(Block block)
    {
        return block.StableBlockId?.StartsWith(
            Block.LOOP_ITEM_STABLE_ID_PREFIX,
            StringComparison.Ordinal) == true;
    }

    private (string? result, Block? referencedBlock) PopulatePlaceholderStringFromSection(
        Section section,
        string placeholder)
    {
        string[] parts = placeholder.Split('.');

        if (parts.Length == 0)
        {
            return (placeholder, null);
        }

        string key = parts[0];

        if (key.StartsWith(SharedInputKeys.PEER_COMPANIES))
        {
            return (_entityPlaceholderResolver.ResolvePeerCompanies(PeerCompanyIds, parts), null);
        }

        if (key is SharedInputKeys.COUNTERPARTY)
        {
            string? counterpartyValue = _entityPlaceholderResolver.ResolveCounterparty(
                CounterpartyId,
                parts.Length > 1 ? parts[1] : null);

            return (ApplyRemainingPath(counterpartyValue, parts.Length > 2 ? parts[2..] : []), null);
        }

        if (key is SharedInputKeys.ORGANIZATION)
        {
            string property = parts.Length > 1 ? parts[1] : "Name";
            return (_entityPlaceholderResolver.ResolveOrganization(OrganizationId, property), null);
        }

        IEnumerable<Block> blocks = section.Workflow.FindBlocksByReplacementTag(parts[0]);

        Block? block = blocks.FirstOrDefault(b => b.BlockResponse?.RawResponse is not null
                && b.BlockResponse.RawResponse.Length > 0
                && !b.BlockResponse.RawResponse.StartsWith(IChatConnector.ERROR_PREFIX))
            ?? blocks.FirstOrDefault(b => b.BlockResponse?.RawResponse is not null);

        if (block is null)
        {
            return (null, null);
        }

        if (parts.Length == 1)
        {
            return (block.BlockResponse?.RawResponse, block);
        }

        if (IsLoopItemBlock(block))
        {
            string? loopItemValue = _entityPlaceholderResolver.ResolveCounterpartyByName(
                OrganizationId,
                block.BlockResponse?.RawResponse,
                parts.Length > 1 ? parts[1] : null);

            return (ApplyRemainingPath(loopItemValue, parts.Length > 2 ? parts[2..] : []), block);
        }

        if (parts.Length == 2)
        {
            switch (parts[1])
            {
                case BlockResponse.JSON_RESPONSE:
                    return (block.BlockResponse?.JsonResponse, block);

                case BlockResponse.HTML_RESPONSE:
                    return (block.BlockResponse?.HtmlResponse, block);

                case BlockResponse.MARKDOWN_RESPONSE:
                    return (block.BlockResponse?.MarkdownResponse, block);

                default:
                    break;
            }
        }

        string? blockResponse = parts[1] switch
        {
            BlockResponse.JSON_RESPONSE => block.BlockResponse?.JsonResponse,
            BlockResponse.HTML_RESPONSE => block.BlockResponse?.HtmlResponse,
            BlockResponse.MARKDOWN_RESPONSE => block.BlockResponse?.MarkdownResponse,
            _ => block.BlockResponse?.RawResponse,
        };

        int propertiesStartIndex = parts[1] switch
        {
            BlockResponse.JSON_RESPONSE => 2,
            BlockResponse.HTML_RESPONSE => 2,
            BlockResponse.MARKDOWN_RESPONSE => 2,
            _ => 1,
        };

        string[] remainingPath = parts[propertiesStartIndex..];
        string? resolved = ApplyRemainingPath(blockResponse, remainingPath);

        if (resolved is null
            && propertiesStartIndex == 1
            && block.Properties is AiQueryProperties aiQuery
            && aiQuery.ProducesValidatedJson())
        {
            resolved = ApplyRemainingPath(block.BlockResponse?.JsonResponse, remainingPath);
        }

        return (resolved, block);
    }

    /// <summary>
    /// Applies a dotted placeholder tail to an already-resolved raw value the same way a block response
    /// is traversed: a JSON value is walked by path, anything else has its leading tag extracted, and an
    /// empty tail returns the value verbatim. Shared by block-output and counterparty placeholders, so a
    /// counterparty bag value that happens to be JSON traverses exactly like <c>{block.answer.field}</c>.
    /// </summary>
    private string? ApplyRemainingPath(string? rawValue, string[] remainingPath)
    {
        if (remainingPath.Length == 0)
        {
            return rawValue;
        }

        return rawValue?.IsValidJson() == true
            ? _stringReplacerStrategies["Json"].ReplacePlaceholders(
                string.Join('.', remainingPath),
                rawValue,
                embeddedMode: false)
            : _stringReplacerStrategies["String"].ReplacePlaceholders(
                remainingPath[0],
                rawValue,
                embeddedMode: false);
    }

    private Dictionary<string, string?> CollectAllInputs(
        object workflowOrSection,
        Block? block)
    {
        Dictionary<string, string?> inputs = block?.TextInputs.Select(ti => (ti.ReplacementTag, ti.Value))
            .ToDictionary() ?? [];

        CollectInputsFromSharedInputs(inputs);

        InjectSignatureFieldValues(block, inputs);

        Logger.LogTrace("{Block} Collected text inputs:\n", block?.ToString() ?? workflowOrSection.ToString());

        string inputsTraceString = "";
        foreach (string? key in inputs.Keys)
        {
            inputsTraceString += $"{key}\n";
        }

        Logger.LogTrace("{inputsTraceString}", inputsTraceString);

        return inputs;
    }

    /// <summary>
    /// For blocks whose properties implement <see cref="IHasSignatureFields"/>, resolves each
    /// workflow-level signature field the block's templates reference to its client override
    /// (if any) or default value and inserts it into <paramref name="inputs"/> under the field
    /// name. Definitions live on <see cref="Workflow.SignatureFields"/>; overrides live in the
    /// <see cref="SignatureFieldOverride"/> table keyed by field name and are pre-loaded by
    /// <see cref="LoadActiveSignatureOverridesAsync"/> once per workflow execution — no DB round
    /// trip here. Expansion variants resolve the same names as their template blocks.
    /// </summary>
    private void InjectSignatureFieldValues(Block? block, Dictionary<string, string?> inputs)
    {
        if (block?.Properties is not IHasSignatureFields)
        {
            return;
        }

        ICollection<WorkflowSignatureField>? definitions = block.Section?.Workflow?.SignatureFields;
        if (definitions is null || definitions.Count == 0)
        {
            if (block.Properties is AiQueryProperties aiQueryProperties)
            {
                aiQueryProperties.CandidateSignatureFieldNames = null;
            }

            return;
        }

        int matchedOverrides = InjectSignatureFieldValuesForBlock(
            block.Properties, definitions, _activeSignatureOverrides, inputs);

        Logger.LogDebug(
            "InjectSignatureFieldValues: block={BlockId} defs={Defs} cacheSize={Cache} matched={Matched}",
            block.Id,
            definitions.Count,
            _activeSignatureOverrides?.Count ?? -1,
            matchedOverrides);
    }

    /// <summary>
    /// Pure injection step: for every workflow-level definition whose <c>{name}</c> token appears
    /// in the block's templates, writes the resolved value (client override when present, staff
    /// default otherwise) into <paramref name="inputs"/>, and records the referenced names on
    /// <see cref="AiQueryProperties.CandidateSignatureFieldNames"/> so replay capture knows which
    /// inputs were signature values. Returns the count of fields resolved to a client override so
    /// callers can log observability metrics. Exposed as <c>internal static</c> so the injection
    /// can be unit-tested without standing up a WorkflowHandler.
    /// </summary>
    internal static int InjectSignatureFieldValuesForBlock(
        BlockProperties properties,
        IEnumerable<WorkflowSignatureField> definitions,
        IReadOnlyDictionary<string, string>? activeOverrides,
        Dictionary<string, string?> inputs)
    {
        List<string>? referencedNames = null;
        int matchedOverrides = 0;

        foreach (WorkflowSignatureField definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Name)
                || !properties.HasPlaceholder(
                    Workflow.SignatureFieldToken(definition.Name), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            referencedNames ??= [];
            referencedNames.Add(definition.Name);

            if (activeOverrides is not null
                && activeOverrides.TryGetValue(definition.Name, out string? overrideValue))
            {
                inputs[definition.Name] = overrideValue;
                matchedOverrides++;
            }
            else
            {
                inputs[definition.Name] = definition.DefaultValue;
            }
        }

        if (properties is AiQueryProperties aiQueryProperties)
        {
            aiQueryProperties.CandidateSignatureFieldNames = referencedNames;
        }

        return matchedOverrides;
    }

    /// <summary>
    /// Batches a single DB round trip before execution to load every
    /// <see cref="SignatureFieldOverride"/> row for this tenant + workflow type, keyed by field
    /// name. Populates <see cref="_activeSignatureOverrides"/> for
    /// <see cref="InjectSignatureFieldValues"/> to read from without further queries. Null (not
    /// loaded — e.g. no tenant identity yet) still injects staff defaults; the lazy re-load paths
    /// retry once the identity is known.
    /// </summary>
    private async Task LoadActiveSignatureOverridesAsync(Workflow workflow, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(OrganizationId))
        {
            _activeSignatureOverrides = null;
            return;
        }

        // Override rows are keyed by (Org, WorkflowTypeId, PlaceholderName); a workflow that was
        // never saved as a type, or defines no fields, cannot have rows that apply to it.
        if (string.IsNullOrEmpty(workflow.WorkflowTypeId) || workflow.SignatureFields.Count == 0)
        {
            _activeSignatureOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        string orgId = OrganizationId;
        string workflowTypeId = workflow.WorkflowTypeId;
        using IServiceScope scope = ServiceScopeFactory.CreateScope();
        ICoreUnitOfWork coreUnitOfWork = scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();

        List<SignatureFieldOverride> rows = await coreUnitOfWork.Repository<SignatureFieldOverride>()
            .GetAll()
            .AsNoTracking()
            .Where(o => o.OrganizationId == orgId && o.WorkflowTypeId == workflowTypeId)
            .ToListAsync(cancellationToken);

        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (SignatureFieldOverride row in rows)
        {
            map[row.PlaceholderName] = row.Value;
        }

        _activeSignatureOverrides = map;

        Logger.LogDebug(
            "LoadActiveSignatureOverridesAsync: org={Org} definitions={Defs} rowsFound={Rows}",
            orgId,
            workflow.SignatureFields.Count,
            rows.Count);
    }

    private async Task ProcessLoopsAndUpdateDatabaseAsync(
        Section section,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        if (!HasUnprocessedLoops(section))
        {
            return;
        }

        List<string> blocksToRemoveFromDb = GetLoopTemplateBlockIds(section);
        var existingBlockIds = section.Blocks.Select(b => b.Id).ToHashSet();
        int blockCountBefore = section.Blocks.Count;
        // Snapshot for ProcessLoops, which takes a plain Dictionary and only reads it.
        section.ProcessLoops(new Dictionary<string, string>(SharedInputs));
        bool loopWasProcessed = section.Blocks.Count != blockCountBefore;

        if (loopWasProcessed)
        {
            ThrowIfLoopExceedsAgentCopyCap(section, existingBlockIds);
        }

        if (loopWasProcessed && _isReplayRun)
        {
            InjectPendingReplayOutputsForSection(section);
        }

        if (loopWasProcessed && RootOutput?.Id is not null)
        {
            await UpdateWorkflowWithNewLoopBlocksAsync(
                section,
                existingBlockIds,
                blocksToRemoveFromDb,
                userDataUnitOfWork);
        }
    }

    private void ThrowIfLoopExceedsAgentCopyCap(Section section, HashSet<string> blockIdsBeforeExpansion)
    {
        int cap = PlatformSettings.Value.AgentRuntime.MaxLoopAgentCopies;
        int agentCopies = section.Blocks.Count(block =>
            block.Type.IsAgentFamily() && !blockIdsBeforeExpansion.Contains(block.Id));

        if (agentCopies > cap)
        {
            throw new InvalidOperationException(
                $"The loop in section '{section.Title}' would start {agentCopies} agent runs, more than the "
                + $"{cap} one loop may start ({AgentRuntimeSettings.SectionName}:{nameof(AgentRuntimeSettings.MaxLoopAgentCopies)}). "
                + "No agent run was started. Shorten the list the loop runs over, or raise the setting.");
        }
    }

    private static List<string> GetLoopTemplateBlockIds(Section section)
    {
        Block? loopStart = section.Blocks.FirstOrDefault(b => b.Type is BlockTypes.LoopStart);
        Block? loopEnd = section.Blocks.FirstOrDefault(b => b.Type is BlockTypes.LoopEnd);

        if (loopStart is null || loopEnd is null)
        {
            return [];
        }

        var blocksToRemove = section.Blocks
            .Where(b => b.Order >= loopStart.Order && b.Order <= loopEnd.Order)
            .Select(b => b.Id)
            .ToList();

        return blocksToRemove;
    }

    private static bool HasUnprocessedLoops(Section section)
    {
        bool hasLoopStart = section.Blocks.Any(b => b.Type is BlockTypes.LoopStart);
        bool hasLoopEnd = section.Blocks.Any(b => b.Type is BlockTypes.LoopEnd);

        return hasLoopStart && hasLoopEnd;
    }

    /// <summary>
    /// A parameterized section whose config cannot be expanded (unparseable or without variants)
    /// never dispatches its blocks. Mark them Skipped — visible in the execution instead of
    /// silently vanishing, and terminal so the end-of-run completeness gate does not read a config
    /// bug as lost results. Idempotent: the full-run path marks first and the dispatch-time
    /// re-expansion attempt finds the blocks already Skipped.
    /// </summary>
    private async Task SkipUnexpandableParameterizedSectionAsync(Section section, string reason)
    {
        foreach (Block block in section.Blocks)
        {
            if (block.ExecutionState?.Status is ExecutionStatus.Skipped)
            {
                continue;
            }

            block.ExecutionState ??= new BlockExecutionState(block);
            block.ExecutionState.MarkAsSkipped(reason);

            if (RootOutput?.Id is not null)
            {
                await PersistBlockResultGuardedAsync(
                    block, null, BuildSkippedLog(section, reason, block), CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Expands a single parameterized section into its variant sections, replaces it in the
    /// parent workflow tree, and distributes documents to the expanded blocks.
    /// Used by ExecuteSectionAsync and ExecuteBlockAsync when running from the UI
    /// (where ProcessParameterizedSectionsAsync hasn't been called by the full workflow path).
    /// </summary>
    public async Task<List<Section>> ExpandParameterizedSectionAsync(
        Section section,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(section);
        if (config is null)
        {
            Logger.LogError(
                "Failed to deserialize ParameterizedConfig for section '{SectionTitle}' — skipping expansion",
                section.Title);
            await SkipUnexpandableParameterizedSectionAsync(section, PARAMETERIZED_CONFIG_UNPARSEABLE_SKIP_REASON);
            return [];
        }

        if (config.Variants.Count == 0)
        {
            Logger.LogWarning(
                "ParameterizedConfig for section '{SectionTitle}' has no variants — skipping expansion",
                section.Title);
            await SkipUnexpandableParameterizedSectionAsync(section, PARAMETERIZED_CONFIG_NO_VARIANTS_SKIP_REASON);
            return [];
        }

        Logger.LogInformation(
            "Expanding parameterized section '{SectionTitle}' into {VariantCount} variants (on-demand)",
            section.Title,
            config.Variants.Count);

        List<Section> expandedSections = ParameterizedSectionExpander.Expand(section, config);

        Workflow topLevel = section.Workflow;
        IReadOnlyList<Document> allDocs = ParameterizedSectionExpander.CollectDocumentsFromTree(topLevel);

        topLevel.ReplaceSectionWithExpanded(section, expandedSections);

        ParameterizedSectionExpander.DistributeDocumentsToVariants(expandedSections, allDocs);
        LogVariantDocumentDistribution(expandedSections);

        if (RootOutput?.Id is not null)
        {
            _ = await userDataUnitOfWork.CompleteAsync();
        }

        return expandedSections;
    }

    private void LogVariantDocumentDistribution(IReadOnlyList<Section> expandedSections)
    {
        foreach (Section expandedSection in expandedSections)
        {
            foreach (Block block in expandedSection.Blocks)
            {
                string filter = block.SupportedDocumentClasses.Count == 0
                    ? "(empty=Any)"
                    : string.Join(",", block.SupportedDocumentClasses);

                Logger.LogInformation(
                    "Parameterized variant '{Variant}' block '{Block}' attached {DocCount} docs (filter: [{Filter}])",
                    expandedSection.Title,
                    block.Name,
                    block.Documents.Count,
                    filter);
            }
        }
    }

    /// <summary>
    /// Seeds each captured external output onto its matching block (so the frozen block's response is
    /// the captured one). A capture that matches zero or more than one block is recorded as unmatched;
    /// uncovered freeze-eligible blocks are detected later at execution time. The accumulated unmatched
    /// set is written to the execution by <see cref="PersistReplayOutcomeAsync"/> once the run ends.
    /// </summary>
    private void InjectReplayOutputs(
        Workflow executedWorkflow,
        IReadOnlyList<ReplayOutput> replayOutputs)
    {
        IReadOnlyDictionary<string, List<Block>> stableIndex =
            BlockIdentityMatcher.BuildStableIndex(executedWorkflow);

        foreach (ReplayOutput replay in replayOutputs)
        {
            BlockMatchResult match = BlockIdentityMatcher.Resolve(replay.StableBlockId, stableIndex);

            switch (match.Kind)
            {
                case BlockMatchKind.Matched:
                    SeedFrozenBlock(match.Block!, replay.RawResponse, replay.JsonResponse);
                    break;

                case BlockMatchKind.Ambiguous:
                    _replayUnmatched.Add(new ReplayUnmatched(
                        replay.SectionName,
                        replay.ReplacementTag,
                        replay.BlockType,
                        string.Format(REPLAY_REASON_AMBIGUOUS_FORMAT, match.CandidateCount)));
                    break;

                default:
                    // The target block may not exist yet: loop bodies expand per section during
                    // execution. Defer instead of recording NotFound now.
                    lock (_replayPendingLock)
                    {
                        _replayPending.Add(replay);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Retries pending captures against a section whose runtime loop expansion just created its
    /// per-iteration clones. Captures still unmatched stay pending (another section's loop may
    /// cover them) and are recorded as NotFound by <see cref="FlushPendingReplayOutputs"/> when
    /// the run ends.
    /// </summary>
    private void InjectPendingReplayOutputsForSection(Section section)
    {
        List<ReplayOutput> pendingSnapshot;
        lock (_replayPendingLock)
        {
            if (_replayPending.Count == 0)
            {
                return;
            }

            pendingSnapshot = [.. _replayPending];
        }

        IReadOnlyDictionary<string, List<Block>> stableIndex =
            BlockIdentityMatcher.BuildStableIndex(section);

        foreach (ReplayOutput replay in pendingSnapshot)
        {
            BlockMatchResult match = BlockIdentityMatcher.Resolve(replay.StableBlockId, stableIndex);

            switch (match.Kind)
            {
                case BlockMatchKind.Matched:
                    SeedFrozenBlock(match.Block!, replay.RawResponse, replay.JsonResponse);
                    RemovePendingReplayOutput(replay);
                    break;

                case BlockMatchKind.Ambiguous:
                    _replayUnmatched.Add(new ReplayUnmatched(
                        replay.SectionName,
                        replay.ReplacementTag,
                        replay.BlockType,
                        string.Format(REPLAY_REASON_AMBIGUOUS_FORMAT, match.CandidateCount)));
                    RemovePendingReplayOutput(replay);
                    break;

                default:
                    break;
            }
        }
    }

    private void RemovePendingReplayOutput(ReplayOutput replay)
    {
        lock (_replayPendingLock)
        {
            _ = _replayPending.Remove(replay);
        }
    }

    private void FlushPendingReplayOutputs()
    {
        lock (_replayPendingLock)
        {
            foreach (ReplayOutput replay in _replayPending)
            {
                _replayUnmatched.Add(new ReplayUnmatched(
                    replay.SectionName,
                    replay.ReplacementTag,
                    replay.BlockType,
                    REPLAY_REASON_NOT_FOUND));
            }

            _replayPending.Clear();
        }
    }

    private static void SeedFrozenBlock(Block block, string? rawResponse, string? jsonResponse)
    {
        block.BlockResponse = new BlockResponse(block)
        {
            RawResponse = rawResponse,
            JsonResponse = jsonResponse,
        };
    }

    private static BlockAnnotations? CompleteWithoutLiveCall(Block block)
    {
        block.ExecutionState ??= new BlockExecutionState(block);
        block.ExecutionState.MarkAsStarted();
        block.ExecutionState.MarkAsCompleted();

        return null;
    }

    private async Task PersistReplayOutcomeAsync(IUserDataUnitOfWork userDataUnitOfWork)
    {
        if (!_isReplayRun || RootOutput is null)
        {
            return;
        }

        FlushPendingReplayOutputs();

        List<ReplayUnmatched> deduped = [.. _replayUnmatched
            .DistinctBy(u => (u.SectionName, u.ReplacementTag, u.BlockType))];

        if (deduped.Count > 0)
        {
            string reasons = string.Join("; ", deduped.Select(u => $"{u.SectionName}/{u.ReplacementTag} [{u.Reason}]"));

            if (_replayUncoveredBlockPolicy == ReplayUncoveredBlockPolicy.SkipAsInconclusive)
            {
                Logger.LogWarning(
                    "Regression rerun {ExecutionId}: {Count} block(s) could not be hermetically replayed; run is inconclusive. {Reasons}",
                    RootOutput.Id,
                    deduped.Count,
                    reasons);
            }
            else
            {
                Logger.LogInformation(
                    "Frozen execution {ExecutionId}: {Count} block(s) had no captured output; coverage {Replayed}/{Eligible}. {Reasons}",
                    RootOutput.Id,
                    deduped.Count,
                    _freezeReplayedCount,
                    _freezeEligibleCount,
                    reasons);
            }
        }

        RootOutput.ReplayUnmatchedJson = deduped.Count > 0 ? JsonSerializer.Serialize(deduped) : null;
        RootOutput.FreezeReplayedCount = _freezeReplayedCount;
        RootOutput.FreezeEligibleCount = _freezeEligibleCount;
        await userDataUnitOfWork.WorkflowExecutions.UpdateReplayOutcomeAsync(
            RootOutput.Id,
            RootOutput.OrganizationId,
            RootOutput.ReplayUnmatchedJson,
            _freezeReplayedCount,
            _freezeEligibleCount);
    }

    private async Task ProcessParameterizedSectionsAsync(
        Workflow workflow,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        List<Section> sectionsToExpand = [.. workflow.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.ParameterizedConfig))
            .OrderByDescending(s => s.Order)];

        if (sectionsToExpand.Count == 0)
        {
            return;
        }

        foreach (Section templateSection in sectionsToExpand)
        {
            ParameterizedSectionConfig? config = ParameterizedSectionNormalizer.Normalize(templateSection);
            if (config is null)
            {
                Logger.LogError(
                    "Failed to deserialize ParameterizedConfig for section '{SectionTitle}' — skipping expansion",
                    templateSection.Title);
                await SkipUnexpandableParameterizedSectionAsync(templateSection, PARAMETERIZED_CONFIG_UNPARSEABLE_SKIP_REASON);
                continue;
            }

            if (config.Variants.Count == 0)
            {
                await SkipUnexpandableParameterizedSectionAsync(templateSection, PARAMETERIZED_CONFIG_NO_VARIANTS_SKIP_REASON);
                continue;
            }

            Logger.LogInformation(
                "Expanding parameterized section '{SectionTitle}' into {VariantCount} variants",
                templateSection.Title,
                config.Variants.Count);

            List<Section> expandedSections = ParameterizedSectionExpander.Expand(templateSection, config);

            IReadOnlyList<Document> allDocs = ParameterizedSectionExpander.CollectDocumentsFromTree(workflow);

            workflow.ReplaceSectionWithExpanded(templateSection, expandedSections);

            ParameterizedSectionExpander.DistributeDocumentsToVariants(expandedSections, allDocs);
            LogVariantDocumentDistribution(expandedSections);
        }

        if (RootOutput?.Id is not null)
        {
            _ = await userDataUnitOfWork.CompleteAsync();

            Logger.LogInformation(
                "Persisted parameterized section expansion for workflow {WorkflowId}",
                workflow.Id);
        }
    }

    private async Task UpdateWorkflowWithNewLoopBlocksAsync(
        Section section,
        HashSet<string> existingBlockIds,
        List<string> blocksToRemoveFromDb,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        if (RootOutput?.Id is null)
        {
            return;
        }

        try
        {
            foreach (string blockIdToRemove in blocksToRemoveFromDb)
            {
                Block? blockToRemove = await userDataUnitOfWork
                    .Repository<Block>()
                    .GetByIdAsync(blockIdToRemove);

                if (blockToRemove is null)
                {
                    continue;
                }

                userDataUnitOfWork.Repository<Block>().Remove(blockToRemove);
                _ = await userDataUnitOfWork.CompleteAsync();

                Logger.LogInformation(
                    "Removed loop template block {BlockId} ({Name}) from section {SectionId}",
                    blockIdToRemove,
                    blockToRemove.Name,
                    section.Id);
            }

            var newBlocks = section.Blocks.Where(b => !existingBlockIds.Contains(b.Id)).ToList();

            // Inserted entity-by-entity (no graph walk): a cloned child's back-reference can reach
            // the template block and from there the already-persisted section/workflow, which a
            // graph Add would try to re-insert (duplicate PK_Workflows).
            foreach (Block newBlock in newBlocks)
            {
                await _blockWriter.InsertLoopBlockAsync(newBlock, CancellationToken.None);
            }

            if (blocksToRemoveFromDb.Count > 0 || newBlocks.Count > 0)
            {
                Logger.LogInformation(
                    "Updated section {SectionId}: removed {RemovedCount} template blocks, added {AddedCount} new loop blocks",
                    section.Id,
                    blocksToRemoveFromDb.Count,
                    newBlocks.Count);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to update section {SectionId} with loop block changes",
                section.Id);

            // The per-block writer assumes these rows exist; swallowing here would surface later
            // as a 0-row update on every loop block.
            throw;
        }
        finally
        {
            userDataUnitOfWork.ClearChangeTracker();
        }
    }

    private void CollectInputsFromSharedInputs(Dictionary<string, string?> inputs)
    {
        foreach ((string placeholder, string inputText) in SharedInputs)
        {
            inputs[placeholder] = inputText;
        }
    }

    #endregion
}
