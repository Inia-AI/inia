using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.AgentRuntime;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The shared agent-integration layer's executor: start → gated poll to settled → collect
/// the trace pack → persist it under the consumer's anchor → acknowledge-or-withhold, in that
/// order, for every consumer — the runtime's copy of a pack is released only once the platform
/// durably holds its own. The one
/// polling implementation in the platform: it honours each monitor's Retry-After, falls back
/// to a jittered interval when one is missing (one malformed monitor must not kill a long
/// run mid-flight), surfaces Unknown and AwaitingInput immediately instead of polling them
/// to the deadline, and acquires <see cref="AgentRunPollGate"/> per poll — wrapping a whole
/// wait would hold a slot for the run's entire lifetime.
/// </summary>
public class AgentRunExecutor : IAgentRunExecutor
{
    private const int FALLBACK_POLL_SECONDS = 2;
    private const int FALLBACK_ADMISSION_RETRY_SECONDS = 5;
    private const int LIVE_TRACE_PUBLISH_INTERVAL_SECONDS = 5;
    private const int LIVE_TRACE_FETCH_TIMEOUT_SECONDS = 10;
    private const string TRACE_PACK_PATH = "trace/trace.ndjson";
    private const int COLLECT_TIMEOUT_SECONDS = 30;
    private const int PERSIST_TIMEOUT_SECONDS = 30;
    private const int CUSTODY_TIMEOUT_SECONDS = 30;
    private const int COMPLETE_TIMEOUT_SECONDS = 10;
    private const decimal UPSTREAM_RETRY_MAX_SPEND_SHARE = 0.25m;
    private const int CANCEL_TIMEOUT_SECONDS = 10;
    private const int SETTLE_POLL_TIMEOUT_SECONDS = 5;

    private static readonly TimeSpan[] _settleProbeDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1500),
    ];

    private readonly IAgentRuntimeClient _client;
    private readonly IAgentRunTraceCollector? _traceCollector;
    private readonly IAgentRunTracePersister? _tracePersister;
    private readonly IAgentRunSessionBundleCustodian? _bundleCustodian;
    private readonly AgentRunPollGate _pollGate;
    private readonly ILogger<AgentRunExecutor> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IAgentRunActivityFeed? _activityFeed;
    private readonly IAgentNarrationTidier? _narrationTidier;

    public AgentRunExecutor(
        IAgentRuntimeClient client,
        AgentRunPollGate pollGate,
        ILogger<AgentRunExecutor> logger,
        IAgentRunTraceCollector? traceCollector = null,
        IAgentRunTracePersister? tracePersister = null,
        TimeProvider? timeProvider = null,
        IAgentRunActivityFeed? activityFeed = null,
        IAgentNarrationTidier? narrationTidier = null,
        IAgentRunSessionBundleCustodian? bundleCustodian = null)
    {
        _client = client;
        _traceCollector = traceCollector;
        _tracePersister = tracePersister;
        _pollGate = pollGate;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _activityFeed = activityFeed;
        _narrationTidier = narrationTidier;
        _bundleCustodian = bundleCustodian;
    }

    public async Task<ErrorOr<AgentRunConclusion>> ExecuteAsync(
        AgentRunStartRequest request,
        AgentRunExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int maxPasses = ResolveMaxPasses(options);
        var attempts = new List<AgentRunAttempt>(maxPasses);
        var traceFiles = new List<AgentRunTraceFile>();
        bool anyCollectionOutcome = false;
        bool anyCompleteWithheld = false;
        bool publishActivity = _activityFeed is not null && options.Activity is not null;

        void Record(AgentRunAttempt attempt)
        {
            attempts.Add(attempt);
            options.OnAttempt?.Invoke(attempt);
        }

        AgentRunStartRequest baseRequest = request;
        bool continuationFallbackUsed = false;
        bool upstreamRetryUsed = false;
        // Waited at the top of the next pass, not before `continue`: inside the pass's try the
        // finally would conclude the failed run on the activity feed only after the wait, showing
        // it live for the whole outage pause.
        TimeSpan? waitBeforePass = null;

        for (int pass = 1; ; pass++)
        {
            if (waitBeforePass is TimeSpan wait)
            {
                waitBeforePass = null;
                await Task.Delay(wait, _timeProvider, cancellationToken);
            }

            AgentRunStartRequest passRequest = RequestForPass(baseRequest, options, pass);
            if (publishActivity)
            {
                _activityFeed!.BeginRun(options.Activity!.ForPass(passRequest.ExecutionId));
            }

            try
            {
                StartOutcome start = await StartPatientlyAsync(passRequest, options, cancellationToken);
                if (start.Started.IsError)
                {
                    return WithLedger(
                        WrapMachineryError(start.Started.FirstError, AgentRunExecutorErrors.START_FAILED),
                        attempts,
                        anyCollectionOutcome ? traceFiles : null);
                }

                if (options.OnRunStarted is not null)
                {
                    try
                    {
                        await options.OnRunStarted(
                            new AgentRunStarted(pass, passRequest.ExecutionId, continuationFallbackUsed),
                            cancellationToken);
                    }
                    catch
                    {
                        _logger.LogWarning(
                            "Agent run {ExecutionId} could not be recorded by its consumer; cancelling it rather than letting it burn unwatched",
                            passRequest.ExecutionId);
                        Record(AttemptOf(
                            pass, passRequest.ExecutionId, start.Started.Value,
                            unacknowledged: true, continuationFallbackUsed));
                        await CancelQuietlyAsync(passRequest.ExecutionId);
                        throw;
                    }
                }

                if (start.StaleRefusedRegistration && pass < maxPasses)
                {
                    Record(AttemptOf(
                        pass, passRequest.ExecutionId, start.Started.Value,
                        unacknowledged: true, continuationFallbackUsed));
                    _logger.LogWarning(
                        "Agent run {ExecutionId} came back as a registration an admission shed had already aborted; buying pass {NextPass} of {MaxPasses} under a fresh id",
                        passRequest.ExecutionId, pass + 1, maxPasses);

                    continue;
                }

                PassOutcome outcome;
                try
                {
                    outcome = await RunPassAsync(
                        passRequest.ExecutionId, pass, start.Started.Value, options, publishActivity,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Teardown teardown = await TearDownAbandonedAsync(
                        passRequest.ExecutionId,
                        pass,
                        start.Started.Value,
                        forceWithhold: options.TraceAnchor is null,
                        options.TraceAnchor);
                    Record(AttemptOf(
                        pass, passRequest.ExecutionId, teardown.Monitor, teardown.Withheld,
                        continuationFallbackUsed));
                    throw;
                }

                Record(AttemptOf(
                    pass, passRequest.ExecutionId, outcome.Monitor, outcome.CompleteWithheld,
                    continuationFallbackUsed));
                anyCompleteWithheld |= outcome.CompleteWithheld;
                if (outcome.TraceFiles is not null)
                {
                    traceFiles.AddRange(outcome.TraceFiles);
                    anyCollectionOutcome = true;
                }

                // The spend-carrying restore failure has a stated rule, because the record
                // conflicts: D32/D36e classify every restore subtype as degrade-to-scratch,
                // §9.1/D37l(4) say only a session-id mismatch can carry spend, and §12 forbids
                // replaying a failed leg on fresh spend (runtime_shutdown excepted). The rule
                // that honours all three: a restore failure that carried NO spend re-runs the
                // leg under the caller's fallback prompt inside the same round — nothing was
                // bought, so the fallback is the leg running without its continuation, not a
                // replay. A restore failure that DID carry spend (only the session-id mismatch
                // can) takes the ordinary failed-conclusion exit: degrading it here would be
                // exactly the fresh-spend replay §12 forbids, so the leg fails, the consumer
                // absorbs it, and the prior attempt stands — the failure consumed its round.
                if (!continuationFallbackUsed
                    && options.ContinuationFallbackPrompt is string fallbackPrompt
                    && passRequest.Continuation is not null
                    && options.ExecutionIdForPass is not null
                    && outcome.Error is null
                    && RestoreFailureSubtypeAtNoSpend(outcome.Monitor) is string restoreSubtype)
                {
                    continuationFallbackUsed = true;
                    baseRequest = baseRequest with { Prompt = fallbackPrompt, Continuation = null };
                    _logger.LogWarning(
                        "Agent run {ExecutionId} could not restore its continuation ({Subtype}) and spent nothing; "
                        + "re-running the leg without it as pass {NextPass} — a recorded override, not a failure",
                        passRequest.ExecutionId,
                        restoreSubtype,
                        pass + 1);

                    continue;
                }

                bool upstreamRetry = !upstreamRetryUsed
                    && outcome.Error is null
                    && IsWorthAnUpstreamRetry(outcome.Monitor, options);
                if (pass >= maxPasses || !(outcome.WorthAnotherPass || upstreamRetry))
                {
                    if (outcome.Error is Error machineryError)
                    {
                        return WithLedger(machineryError, attempts, anyCollectionOutcome ? traceFiles : null);
                    }

                    return new AgentRunConclusion(
                        outcome.Monitor,
                        anyCollectionOutcome ? traceFiles : null,
                        anyCompleteWithheld,
                        attempts);
                }

                if (upstreamRetry)
                {
                    upstreamRetryUsed = true;
                    waitBeforePass = options.UpstreamErrorRetryDelay;
                    _logger.LogWarning(
                        "Agent run {ExecutionId} failed with {Subtype}: the model API failed on its side; waiting {Delay} before pass {NextPass} of {MaxPasses}",
                        passRequest.ExecutionId,
                        AgentRunSubtypes.UPSTREAM_ERROR,
                        waitBeforePass,
                        pass + 1,
                        maxPasses);

                    continue;
                }

                _logger.LogWarning(
                    "Agent run {ExecutionId} is worth another pass ({Reason}); re-registering as pass {NextPass} of {MaxPasses}",
                    passRequest.ExecutionId,
                    attempts[^1].FailureSubtype ?? "the runtime no longer knows the run",
                    pass + 1,
                    maxPasses);
            }
            finally
            {
                if (publishActivity)
                {
                    _activityFeed!.ConcludeRun(passRequest.ExecutionId);
                }
            }
        }
    }

    /// <summary>
    /// One run, start to teardown. Every path that walks away from a run tears it down the
    /// same way — cancel, wait briefly for the settled verdict, collect the pack, persist it,
    /// acknowledge only when the evidence is secured — because the monitor carries a run's cost
    /// only once it settles, and acknowledging releases evidence nobody durably holds yet.
    /// Caller cancellation takes the same teardown from ExecuteAsync; the one cancel-only exit
    /// left is a thrown OnRunStarted, where nothing has been spent and the consumer's own
    /// record machinery is already broken.
    /// </summary>
    private async Task<PassOutcome> RunPassAsync(
        string executionId,
        int pass,
        AgentRunMonitor startMonitor,
        AgentRunExecutionOptions options,
        bool publishActivity,
        CancellationToken cancellationToken)
    {
        bool handled = false;
        var tidyState = new LiveNarrationTidyState();
        try
        {
            PollOutcome polled = await PollToSettledAsync(
                executionId, startMonitor, options, publishActivity, tidyState, cancellationToken);

            AgentRunMonitor monitor = polled.Monitor;
            if (polled.Error is Error pollError)
            {
                Teardown teardown = await TearDownAbandonedAsync(
                    executionId, pass, monitor, forceWithhold: false, options.TraceAnchor);
                handled = true;

                return new PassOutcome(
                    teardown.Monitor, pollError, teardown.Files, teardown.Withheld, polled.RuntimeLostTheRun);
            }

            if (publishActivity)
            {
                await PublishLiveDigestQuietlyAsync(executionId, tidyState, options.LiveActivity, cancellationToken);
            }

            if (monitor.Status == AgentRunStatus.Unknown)
            {
                Teardown teardown = await TearDownAbandonedAsync(
                    executionId, pass, monitor, forceWithhold: true, options.TraceAnchor);
                handled = true;

                return new PassOutcome(
                    teardown.Monitor,
                    Error.Failure(
                        AgentRunExecutorErrors.UNKNOWN_STATUS,
                        $"Agent run '{executionId}' reported an unknown status; its acknowledgement is withheld because its true state is unknowable"),
                    teardown.Files,
                    CompleteWithheld: true,
                    WorthAnotherPass: false);
            }

            if (monitor.Status == AgentRunStatus.AwaitingInput)
            {
                Teardown teardown = await TearDownAbandonedAsync(
                    executionId, pass, monitor, forceWithhold: false, options.TraceAnchor);
                handled = true;

                return new PassOutcome(
                    teardown.Monitor,
                    Error.Failure(
                        AgentRunExecutorErrors.AWAITING_INPUT,
                        $"Agent run '{executionId}' paused for input, which these execution options cannot answer"),
                    teardown.Files,
                    teardown.Withheld,
                    WorthAnotherPass: false);
            }

            SecuredEvidence evidence = await SecureEvidenceQuietlyAsync(
                executionId,
                monitor.Attempt > 0 ? monitor.Attempt : pass,
                options.TraceAnchor,
                options.SessionBundleAnchor);
            bool acknowledged = evidence.Secured && await AcknowledgeQuietlyAsync(executionId);

            handled = true;
            return new PassOutcome(monitor, Error: null, evidence.Files, !acknowledged, IsWorthAnotherPass(monitor));
        }
        catch (OperationCanceledException)
        {
            handled = true;
            throw;
        }
        finally
        {
            if (!handled)
            {
                await CancelQuietlyAsync(executionId);
            }
        }
    }

    /// <summary>
    /// Shared teardown for every path that abandons a run — an exhausted deadline, polling
    /// given up on, an unreadable status, a pause the options cannot answer, caller
    /// cancellation, and the settle-by-id verb: cancel it, wait briefly for its settled
    /// monitor, collect its pack, persist it, and acknowledge only when the evidence is
    /// secured — otherwise the pack stays collectable on the runtime for its retention window.
    /// Unknown status forces the withhold regardless: an acknowledgement finalizes a state
    /// nobody could read — and so does caller cancellation for an unanchored consumer, whose
    /// collected files exit through no return value on that path, so acknowledging would purge
    /// the runtime's only copy of evidence nobody received. Deliberately blind to the caller's
    /// cancellation token — every step is bounded by its own timeout, so a cancellation
    /// arriving mid-teardown cannot abandon the evidence half-secured.
    /// </summary>
    private async Task<Teardown> TearDownAbandonedAsync(
        string executionId,
        int pass,
        AgentRunMonitor lastMonitor,
        bool forceWithhold,
        AgentRunTraceAnchor? anchor)
    {
        await CancelQuietlyAsync(executionId);
        AgentRunMonitor monitor = await SettleQuietlyAsync(executionId, lastMonitor);
        // Custody is deliberately absent here. Every teardown path abandons its run, and the
        // execution that owned it is on its way to a terminal that releases bundles anyway, so
        // storing one would buy bytes nothing can ever resume from.
        SecuredEvidence evidence = await SecureEvidenceQuietlyAsync(
            executionId, monitor.Attempt > 0 ? monitor.Attempt : pass, anchor, bundleAnchor: null);
        bool acknowledged = !forceWithhold
            && evidence.Secured
            && await AcknowledgeQuietlyAsync(executionId);

        return new Teardown(monitor, evidence.Files, !acknowledged);
    }

    public async Task<bool> SettleAbandonedQuietlyAsync(string executionId, AgentRunTraceAnchor? anchor)
    {
        Teardown teardown = await TearDownAbandonedAsync(
            executionId, pass: 1, Unsettled(executionId), forceWithhold: false, anchor);

        return !teardown.Withheld;
    }

    public async Task<AgentRunMonitor> AbandonQuietlyAsync(string executionId, AgentRunTraceAnchor? anchor)
    {
        Teardown teardown = await TearDownAbandonedAsync(
            executionId, pass: 1, Unsettled(executionId), forceWithhold: false, anchor);

        return teardown.Monitor;
    }

    private static AgentRunMonitor Unsettled(string executionId)
    {
        return new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = AgentRunStatus.Running,
        };
    }

    /// <summary>
    /// Collect, then commit under the consumer's anchor — the evidence half every teardown and
    /// every settled pass shares. Secured means the acknowledgement may release the runtime's
    /// copy: the collection was complete and, for an anchored consumer, its rows are durably
    /// committed. Partial rows are persisted regardless — durable partial evidence beats none —
    /// but never count as secured. A null anchor is the ephemeral mode: successful collection
    /// alone secures, because the consumer declared it has no durable home (and withholding
    /// would leak runtime retention capacity on every such run).
    /// <para>
    /// A consumer that declared a bundle anchor also has its session bundle taken into custody
    /// here — after the evidence that gates the acknowledgement, and before the acknowledgement
    /// itself, because the work that can withhold runs first. Custody requires the evidence to
    /// be secured: the persisted <c>out/</c> rows are the payload a continuation re-carries, so
    /// a bundle whose rows are missing, unenumerable (a failed listing), or not durably stored
    /// is a resume trap, and skipping custody converts every such case into the planner's
    /// no-bundle scratch. The one recorded exception is the size cap — a cap skip keeps the
    /// evidence secured and rides the custody row as <c>DroppedOutFileCount</c> instead. In the
    /// other direction custody's own outcome never reaches <see cref="SecuredEvidence.Secured"/>:
    /// a lost bundle costs a redo leg its head start, while a withheld acknowledgement holds
    /// runtime capacity for the whole window.
    /// </para>
    /// </summary>
    private async Task<SecuredEvidence> SecureEvidenceQuietlyAsync(
        string executionId,
        int attempt,
        AgentRunTraceAnchor? anchor,
        AgentRunSessionBundleAnchor? bundleAnchor)
    {
        (SecuredEvidence evidence, int droppedOutFileCount) =
            await CollectAndPersistQuietlyAsync(executionId, attempt, anchor);

        if (bundleAnchor is not null)
        {
            if (evidence.Secured)
            {
                await TakeBundleCustodyQuietlyAsync(executionId, attempt, droppedOutFileCount, bundleAnchor);
            }
            else
            {
                _logger.LogWarning(
                    "The evidence of run {ExecutionId} was not fully secured, so its session bundle stays with "
                        + "the runtime; a later leg of this member starts from scratch",
                    executionId);
            }
        }

        return evidence;
    }

    private async Task<(SecuredEvidence Evidence, int DroppedOutFileCount)> CollectAndPersistQuietlyAsync(
        string executionId,
        int attempt,
        AgentRunTraceAnchor? anchor)
    {
        AgentRunTraceCollectionResult? collection = await CollectQuietlyAsync(executionId, attempt);
        if (collection is null)
        {
            return (new SecuredEvidence(Files: null, Secured: false), 0);
        }

        int droppedOutFileCount = collection.DroppedArtifactPaths.Count;
        if (anchor is null || collection.Files.Count == 0)
        {
            return (new SecuredEvidence(collection.Files, collection.Succeeded), droppedOutFileCount);
        }

        foreach (AgentRunTraceFile file in collection.Files)
        {
            file.OrganizationId = anchor.OrganizationId;
            file.BlockId = anchor.BlockId;
        }

        bool persisted = await PersistQuietlyAsync(executionId, collection.Files);

        return (new SecuredEvidence(collection.Files, collection.Succeeded && persisted), droppedOutFileCount);
    }

    /// <summary>
    /// Takes the run's session bundle into platform custody under its own timeout. Quiet by
    /// contract — the custodian never throws and never withholds — so this only turns a broken
    /// contract or an exhausted timeout into a log line.
    /// </summary>
    private async Task TakeBundleCustodyQuietlyAsync(
        string executionId,
        int attempt,
        int droppedOutFileCount,
        AgentRunSessionBundleAnchor bundleAnchor)
    {
        if (_bundleCustodian is null)
        {
            _logger.LogWarning(
                "No session bundle custodian is wired beside the agent runtime client; run {ExecutionId} keeps its "
                    + "acknowledgement and a later leg starts from scratch",
                executionId);

            return;
        }

        try
        {
            using var custodyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(CUSTODY_TIMEOUT_SECONDS));
            _ = await _bundleCustodian.TakeCustodyAsync(
                bundleAnchor, executionId, attempt, droppedOutFileCount, custodyTimeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Taking custody of the session bundle of run {ExecutionId} threw despite the custodian's never-throw "
                    + "contract; the run keeps its acknowledgement",
                executionId);
        }
    }

    /// <summary>
    /// Commits collected rows through the persister and never lets a persistence failure fail
    /// the run outcome: false withholds the acknowledgement, so the runtime keeps the pack for
    /// its retention window and nothing is lost — the exact reverse of the 2026-08-12 incident,
    /// where an early acknowledge made a writer failure permanent.
    /// </summary>
    private async Task<bool> PersistQuietlyAsync(string executionId, IReadOnlyList<AgentRunTraceFile> files)
    {
        if (_tracePersister is null)
        {
            _logger.LogWarning(
                "No trace persister is wired beside the agent runtime client; withholding complete for run {ExecutionId} so the pack stays collectable",
                executionId);

            return false;
        }

        try
        {
            using var persistTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(PERSIST_TIMEOUT_SECONDS));
            bool persisted = await _tracePersister.PersistAsync(files, persistTimeout.Token);
            if (!persisted)
            {
                _logger.LogError(
                    "Persisting {FileCount} trace row(s) for run {ExecutionId} failed; withholding complete so the pack stays collectable on the runtime",
                    files.Count, executionId);
            }

            return persisted;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Persisting trace rows for run {ExecutionId} threw despite the persister's never-throw contract; withholding complete so the pack stays collectable",
                executionId);

            return false;
        }
    }

    /// <summary>
    /// Brief best-effort wait for a cancelled run's settled monitor. The runtime fills in a
    /// run's cost only when it settles, so reading the last live monitor would record a real
    /// spend as zero — the settled verdict is worth a moment, not a vigil.
    /// </summary>
    private async Task<AgentRunMonitor> SettleQuietlyAsync(string executionId, AgentRunMonitor lastMonitor)
    {
        AgentRunMonitor monitor = lastMonitor;
        for (int probe = 0; probe < _settleProbeDelays.Length && !monitor.Status.IsTerminal(); probe++)
        {
            try
            {
                await Task.Delay(_settleProbeDelays[probe], _timeProvider, CancellationToken.None);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(SETTLE_POLL_TIMEOUT_SECONDS));
                await _pollGate.WaitAsync(timeout.Token);
                ErrorOr<AgentRunMonitor> polled;
                try
                {
                    polled = await _client.GetRunAsync(executionId, timeout.Token);
                }
                finally
                {
                    _pollGate.Release();
                }

                if (polled.IsError)
                {
                    break;
                }

                monitor = polled.Value;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Settle probe for cancelled agent run {ExecutionId} threw; keeping the last observed monitor",
                    executionId);

                break;
            }
        }

        return monitor;
    }

    /// <summary>
    /// Waits out capacity refusals within the caller's patience. Patience is spent here rather
    /// than against the run deadline: queuing for a slot is not execution time. A refusal does
    /// not prove the run stayed unregistered — the runtime can shed after registering — so a
    /// re-send that comes back already-registered and terminal is flagged as a stale aborted
    /// registration instead of being read as this pass's verdict.
    /// </summary>
    private async Task<StartOutcome> StartPatientlyAsync(
        AgentRunStartRequest request,
        AgentRunExecutionOptions options,
        CancellationToken cancellationToken)
    {
        long waitingSince = _timeProvider.GetTimestamp();
        bool refusalSeen = false;

        while (true)
        {
            ErrorOr<AgentRunMonitor> started = await _client.StartRunAsync(request, cancellationToken);
            if (!started.IsError)
            {
                bool stale = refusalSeen
                    && started.Value.AlreadyRegistered
                    && started.Value.Status.IsTerminal();

                return new StartOutcome(started, stale);
            }

            if (!IsAdmissionRefusal(started.FirstError))
            {
                return new StartOutcome(started, StaleRefusedRegistration: false);
            }

            refusalSeen = true;
            TimeSpan retryAfter = AdvisedRetryAfter(started.FirstError);
            TimeSpan waited = _timeProvider.GetElapsedTime(waitingSince);
            if (waited + retryAfter > options.AdmissionPatience)
            {
                return new StartOutcome(
                    options.AdmissionPatience > TimeSpan.Zero
                        ? Error.Failure(
                            AgentRunExecutorErrors.ADMISSION_EXHAUSTED,
                            $"Agent run '{request.ExecutionId}' was refused for {started.FirstError.Code} throughout "
                            + $"{options.AdmissionPatience} of admission patience; no run was ever started, so nothing was spent")
                        : started.FirstError,
                    StaleRefusedRegistration: false);
            }

            _logger.LogInformation(
                "Agent runtime refused run {ExecutionId} ({Code}); waiting {RetryAfter} of {Patience} admission patience",
                request.ExecutionId, started.FirstError.Code, retryAfter, options.AdmissionPatience);

            await Task.Delay(retryAfter, _timeProvider, cancellationToken);
        }
    }

    public Task<bool> AcknowledgeQuietlyAsync(string executionId)
    {
        return CallQuietlyAsync(
            executionId, COMPLETE_TIMEOUT_SECONDS, _client.CompleteRunAsync,
            "Completing", "the runtime's retention reaper is the backstop");
    }

    public Task CancelQuietlyAsync(string executionId)
    {
        return CallQuietlyAsync(
            executionId, CANCEL_TIMEOUT_SECONDS, _client.CancelRunAsync,
            "Cancelling", "the run keeps going to its own deadline");
    }

    private async Task<bool> CallQuietlyAsync(
        string executionId,
        int timeoutSeconds,
        Func<string, CancellationToken, Task<ErrorOr<AgentRunMonitor>>> call,
        string verb,
        string backstop)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            ErrorOr<AgentRunMonitor> result = await call(executionId, timeout.Token);
            if (result.IsError)
            {
                _logger.LogWarning(
                    "{Verb} agent run {ExecutionId} failed ({Code}); {Backstop}",
                    verb, executionId, result.FirstError.Code, backstop);

                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "{Verb} agent run {ExecutionId} threw; {Backstop}",
                verb, executionId, backstop);

            return false;
        }
    }

    private async Task<PollOutcome> PollToSettledAsync(
        string executionId,
        AgentRunMonitor startMonitor,
        AgentRunExecutionOptions options,
        bool publishActivity,
        LiveNarrationTidyState tidyState,
        CancellationToken cancellationToken)
    {
        TimeSpan? liveInterval = publishActivity ? options.LiveActivity?.PublishInterval : null;
        if (liveInterval is TimeSpan interval && interval > TimeSpan.Zero)
        {
            using var liveStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task live = PublishLiveLoopAsync(executionId, tidyState, options.LiveActivity!, interval, liveStop.Token);
            try
            {
                return await PollLoopAsync(executionId, startMonitor, options, publishOnPoll: false, tidyState, cancellationToken);
            }
            finally
            {
                await liveStop.CancelAsync();
                await live;
            }
        }

        return await PollLoopAsync(executionId, startMonitor, options, publishActivity, tidyState, cancellationToken);
    }

    /// <summary>
    /// Refreshes the live feed on the consumer's own interval while the status poll runs on the
    /// runtime's. Ends quietly when stopped; the settle path publishes the final digest itself.
    /// </summary>
    private async Task PublishLiveLoopAsync(
        string executionId,
        LiveNarrationTidyState tidyState,
        AgentRunLiveActivityOptions live,
        TimeSpan interval,
        CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await PublishLiveDigestQuietlyAsync(executionId, tidyState, live, stop);
                await Task.Delay(interval, _timeProvider, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return;
        }
    }

    private async Task<PollOutcome> PollLoopAsync(
        string executionId,
        AgentRunMonitor startMonitor,
        AgentRunExecutionOptions options,
        bool publishOnPoll,
        LiveNarrationTidyState tidyState,
        CancellationToken cancellationToken)
    {
        long startTimestamp = _timeProvider.GetTimestamp();
        long lastPublishTimestamp = 0;
        AgentRunMonitor monitor = startMonitor;
        int consecutiveErrors = 0;
        bool finalProbeTaken = false;

        while (!monitor.Status.IsTerminal())
        {
            if (monitor.Status is AgentRunStatus.Unknown or AgentRunStatus.AwaitingInput)
            {
                break;
            }

            TimeSpan interval = ResolvePollInterval(monitor);

            if (_timeProvider.GetElapsedTime(startTimestamp) + interval > options.Deadline)
            {
                if (finalProbeTaken)
                {
                    return new PollOutcome(
                        monitor,
                        Error.Failure(
                            AgentRunExecutorErrors.DEADLINE_EXCEEDED,
                            $"Agent run '{executionId}' did not finish within the caller's deadline of {options.Deadline}; last status {monitor.Status}",
                            new Dictionary<string, object> { [AgentRunExecutorErrors.STATUS_METADATA_KEY] = monitor.Status }),
                        RuntimeLostTheRun: false);
                }

                finalProbeTaken = true;
            }
            else
            {
                await Task.Delay(interval, _timeProvider, cancellationToken);
            }

            await _pollGate.WaitAsync(cancellationToken);
            ErrorOr<AgentRunMonitor> polled;
            try
            {
                polled = await _client.GetRunAsync(executionId, cancellationToken);
            }
            finally
            {
                _pollGate.Release();
            }

            if (polled.IsError)
            {
                consecutiveErrors++;
                if (IsAboutTheRunItself(polled.FirstError) || consecutiveErrors > options.PollErrorTolerance)
                {
                    return new PollOutcome(
                        monitor,
                        WrapMachineryError(polled.FirstError, AgentRunExecutorErrors.POLL_FAILED),
                        // Only NOT_FOUND is a lost run worth re-buying. COMPLETED means the run
                        // finished and its evidence was already collected and released — a fresh
                        // pass would re-spend on work that verifiably succeeded.
                        polled.FirstError.Code == AgentRunLifecycleErrors.NOT_FOUND);
                }

                _logger.LogWarning(
                    "Poll {ErrorCount} of {Tolerance} for agent run {ExecutionId} failed ({Code}); the run is still live, so polling continues",
                    consecutiveErrors, options.PollErrorTolerance, executionId, polled.FirstError.Code);

                continue;
            }

            consecutiveErrors = 0;
            monitor = polled.Value;

            bool publishDue = lastPublishTimestamp == 0
                || _timeProvider.GetElapsedTime(lastPublishTimestamp).TotalSeconds
                    >= LIVE_TRACE_PUBLISH_INTERVAL_SECONDS;
            if (publishOnPoll && !monitor.Status.IsTerminal() && publishDue)
            {
                lastPublishTimestamp = _timeProvider.GetTimestamp();
                await PublishLiveDigestQuietlyAsync(executionId, tidyState, options.LiveActivity, cancellationToken);
            }
        }

        return new PollOutcome(monitor, Error: null, RuntimeLostTheRun: false);
    }

    /// <summary>
    /// Fetches the run's trace pack as written so far, projects it, and publishes the digest
    /// to the live feed. Quiet discipline throughout: activity is garnish — no failure here
    /// may disturb the run, and a mid-run read that ends mid-line projects fine because the
    /// projector is truncation-tolerant. Quiet about its own fetch only: the timeout is linked
    /// to the caller's token, so a cancelled caller stops the fetch immediately and the
    /// cancellation propagates instead of being swallowed into the remainder of the timeout.
    /// </summary>
    private async Task PublishLiveDigestQuietlyAsync(
        string executionId,
        LiveNarrationTidyState tidyState,
        AgentRunLiveActivityOptions? live,
        CancellationToken cancellationToken)
    {
        AgentActivityProjectionOptions projection = live?.Projection ?? AgentActivityProjectionOptions.Default;
        try
        {
            using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fetchTimeout.CancelAfter(TimeSpan.FromSeconds(LIVE_TRACE_FETCH_TIMEOUT_SECONDS));
            ErrorOr<byte[]> traceBytes = await _client.GetRunFileAsync(
                executionId, TRACE_PACK_PATH, fetchTimeout.Token);
            if (traceBytes.IsError)
            {
                return;
            }

            await tidyState.ApplyAsync(
                AgentActivityProjector.ExtractNarration(traceBytes.Value, projection), _narrationTidier, fetchTimeout.Token);
            AgentActivityProjectionOptions options = projection with
            {
                BeatSource = AgentActivityBeatSource.Narrated,
                TidiedNarration = tidyState.SettledNarration,
            };
            _activityFeed!.PublishDigest(executionId, AgentActivityProjector.Project(traceBytes.Value, options));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Live activity publish for run {ExecutionId} failed; the feed keeps its last digest",
                executionId);
        }
    }

    /// <summary>
    /// The advised interval when it is a real wait; the jittered fallback otherwise. A stale
    /// date-form Retry-After clamps to zero on the client, and a zero delay would turn the
    /// poll loop into a tight spin against the shared gate.
    /// </summary>
    internal static TimeSpan ResolvePollInterval(AgentRunMonitor monitor)
    {
        return monitor.PollInterval is TimeSpan advised && advised > TimeSpan.Zero
            ? advised
            : TimeSpan.FromSeconds(FALLBACK_POLL_SECONDS * (0.5 + (Random.Shared.NextDouble() / 2)));
    }

    /// <summary>
    /// Whether the failure is the runtime answering about the run rather than the call failing
    /// to reach it. A run it no longer knows, or whose data it has released, will not come back
    /// however patiently it is asked.
    /// </summary>
    private static bool IsAboutTheRunItself(Error clientError)
    {
        return clientError.Code is AgentRunLifecycleErrors.NOT_FOUND or AgentRunLifecycleErrors.COMPLETED;
    }

    private static bool IsAdmissionRefusal(Error clientError)
    {
        return AgentRunAdmissionErrors.IsAdmissionRefusal(clientError.Code);
    }

    private static TimeSpan AdvisedRetryAfter(Error refusal)
    {
        return refusal.Metadata?.GetValueOrDefault(AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY) is TimeSpan advised
            && advised > TimeSpan.Zero
            ? advised
            : TimeSpan.FromSeconds(FALLBACK_ADMISSION_RETRY_SECONDS);
    }

    private static int ResolveMaxPasses(AgentRunExecutionOptions options)
    {
        return options.ExecutionIdForPass is null ? 1 : Math.Max(1, options.MaxPasses);
    }

    private static AgentRunStartRequest RequestForPass(
        AgentRunStartRequest request,
        AgentRunExecutionOptions options,
        int pass)
    {
        return pass == 1 ? request : request with { ExecutionId = options.ExecutionIdForPass!(pass) };
    }

    private static bool IsWorthAnUpstreamRetry(AgentRunMonitor monitor, AgentRunExecutionOptions options)
    {
        return options.UpstreamErrorRetryDelay > TimeSpan.Zero
            && monitor.TryGetError(out AgentRunError? error)
            && error.Subtype is string subtype
            && AgentRunSubtypes.IsUpstreamOutage(subtype)
            && monitor.Fuses?.MaxBudgetUsd is decimal budget
            && monitor.EstimatedCostUsd <= budget * UPSTREAM_RETRY_MAX_SPEND_SHARE;
    }

    private static bool IsWorthAnotherPass(AgentRunMonitor monitor)
    {
        return monitor.TryGetError(out AgentRunError? error)
            && error.Subtype is string subtype
            && AgentRunSubtypes.IsRetryable(subtype);
    }

    /// <summary>
    /// The degrade signal, and its log token in one read: the subtype when the run settled on
    /// a continuation restore failure at zero spend, else null. The runtime puts the wrong
    /// session's spend on the failure's <c>estimatedCostUsd</c> precisely so this branch can
    /// be taken on it.
    /// </summary>
    private static string? RestoreFailureSubtypeAtNoSpend(AgentRunMonitor monitor)
    {
        return monitor.TryGetError(out AgentRunError? error)
            && error.Subtype is string subtype
            && AgentRunSubtypes.IsContinuationRestoreFailure(subtype)
            && monitor.EstimatedCostUsd == 0m
                ? subtype
                : null;
    }

    private static AgentRunAttempt AttemptOf(
        int pass,
        string executionId,
        AgentRunMonitor monitor,
        bool unacknowledged,
        bool ranContinuationFallback = false)
    {
        return new AgentRunAttempt(
            pass,
            executionId,
            monitor.Status,
            monitor.TryGetError(out AgentRunError? error) ? error.Subtype : null,
            monitor.EstimatedCostUsd,
            unacknowledged,
            ranContinuationFallback);
    }

    /// <summary>
    /// Rebuilds the error with the attempt ledger — and any trace evidence the passes
    /// collected — folded into its metadata, keeping whatever it already carried: an admission
    /// refusal's honoured Retry-After and the deadline error's last status are the signals
    /// their consumers act on, and earlier passes' packs may exist nowhere else once their
    /// acknowledgements released the server-side copies.
    /// </summary>
    private static Error WithLedger(
        Error error,
        IReadOnlyList<AgentRunAttempt> attempts,
        IReadOnlyList<AgentRunTraceFile>? traceFiles)
    {
        var metadata = new Dictionary<string, object>(error.Metadata ?? [])
        {
            [AgentRunExecutorErrors.ATTEMPTS_METADATA_KEY] = attempts,
        };

        if (traceFiles is { Count: > 0 })
        {
            metadata[AgentRunExecutorErrors.TRACE_FILES_METADATA_KEY] = traceFiles;
        }

        return Error.Custom((int)error.Type, error.Code, error.Description, metadata);
    }

    private sealed record StartOutcome(ErrorOr<AgentRunMonitor> Started, bool StaleRefusedRegistration);

    private sealed record Teardown(
        AgentRunMonitor Monitor, IReadOnlyList<AgentRunTraceFile>? Files, bool Withheld);

    private sealed record SecuredEvidence(IReadOnlyList<AgentRunTraceFile>? Files, bool Secured);

    private sealed record PollOutcome(AgentRunMonitor Monitor, Error? Error, bool RuntimeLostTheRun);

    private sealed record PassOutcome(
        AgentRunMonitor Monitor,
        Error? Error,
        IReadOnlyList<AgentRunTraceFile>? TraceFiles,
        bool CompleteWithheld,
        bool WorthAnotherPass);

    /// <summary>
    /// Machinery failures are re-coded so consumers switch on one vocabulary — except a 503
    /// admission refusal, whose code and honoured Retry-After metadata are the whole signal a
    /// durable re-dispatcher acts on, chat admission switched off on the runtime, which a chat
    /// consumer reports as unavailable rather than broken, and an exhausted admission patience,
    /// which already speaks this layer's vocabulary. Flattening any of them into a description
    /// string would leave that caller substring-matching English prose.
    /// </summary>
    private static Error WrapMachineryError(Error clientError, string machineryCode)
    {
        if (clientError.Code is AgentRunAdmissionErrors.CAPACITY
            or AgentRunAdmissionErrors.SHUTTING_DOWN
            or AgentRunAdmissionErrors.CHAT_DISABLED
            or AgentRunExecutorErrors.ADMISSION_EXHAUSTED)
        {
            return clientError;
        }

        return Error.Failure(machineryCode, $"{clientError.Code}: {clientError.Description}");
    }

    /// <summary>
    /// Collects the settled run's trace pack — the evidence for engineers and the editor's
    /// trace panel, on failed runs as much as successful ones. Runs on a fresh short-lived
    /// token for the same reason the complete call does, and never fails the run outcome: an
    /// incomplete collection only withholds complete, so the pack stays collectable on the
    /// runtime for its retention window. A null return means collection never produced an
    /// outcome (no collector, or a thrown contract violation) — distinct from a successful
    /// collection of zero rows.
    /// </summary>
    private async Task<AgentRunTraceCollectionResult?> CollectQuietlyAsync(
        string executionId,
        int attempt)
    {
        if (_traceCollector is null)
        {
            _logger.LogWarning(
                "No trace collector is wired beside the agent runtime client; withholding complete for run {ExecutionId} so the pack stays collectable",
                executionId);

            return null;
        }

        try
        {
            using var collectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(COLLECT_TIMEOUT_SECONDS));
            AgentRunTraceCollectionResult collection = await _traceCollector.CollectAsync(
                executionId, attempt, collectTimeout.Token);

            if (!collection.Succeeded)
            {
                _logger.LogWarning(
                    "Trace collection for run {ExecutionId} was incomplete ({Diagnostics}); withholding complete so the pack stays collectable on the runtime",
                    executionId, string.Join(" | ", collection.Diagnostics));
            }

            return collection;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Trace collection for run {ExecutionId} threw despite the collector's never-throw contract; withholding complete so the pack stays collectable",
                executionId);

            return null;
        }
    }
}
