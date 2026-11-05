using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The shared agent-integration layer's run lifecycle: start → gated poll to settled →
/// collect the trace pack → persist it under the consumer's anchor →
/// acknowledge-or-withhold. Every platform consumer runs agents through this seam so no
/// consumer re-implements the ordering, the poll gate, or the withhold rules — above all the
/// persist-then-acknowledge order: acknowledging releases the runtime's only copy of
/// a run's evidence, so it happens only once the platform durably holds its own. Registered
/// beside <see cref="IAgentRuntimeClient"/> exactly when the runtime is enabled; the client
/// keeps single-shot verbs only — loop-shaped needs belong here.
/// </summary>
public interface IAgentRunExecutor
{
    /// <summary>
    /// The whole lifecycle for one run. Returns a conclusion when the run settled — however
    /// it settled: a failed run is a successful observation, read
    /// <see cref="AgentRunMonitor.TryGetError"/> and apply consumer policy. Returns an error
    /// when the machinery failed — start rejected, poll failed, deadline exhausted, unknown
    /// status, or awaiting input under these options (see <see cref="AgentRunExecutorErrors"/>
    /// for the codes). Acknowledgement is decided inside, never by the caller: on every path
    /// after a successful start the run is quietly completed — a no-op
    /// <c>AgentRun.NotCompletable</c> on live runs — except where the withhold rules forbid it
    /// (unknown status; failed or missing trace collection; for an anchored consumer, a failed
    /// or missing persistence of the collected rows). One exit cancels quietly and skips
    /// collection and acknowledgement entirely — a thrown
    /// <see cref="AgentRunExecutionOptions.OnRunStarted"/>, where nothing has been spent and
    /// the consumer's own record machinery is already broken — leaving the run's pack
    /// collectable for the consumer's cleanup path; the runs already bought always reach the
    /// caller through <see cref="AgentRunExecutionOptions.OnAttempt"/>.
    /// <para>
    /// Every path that abandons a run — an exhausted deadline, polling given up on, an
    /// unreadable status, a pause these options cannot answer, and caller cancellation (which
    /// tears down first, then propagates the <see cref="OperationCanceledException"/>) —
    /// cancels it, waits briefly for its settled verdict (the monitor carries cost only once
    /// settled, so the cancelled attempt's ledger row carries real spend), collects its pack,
    /// persists it, and acknowledges only when the evidence is secured — for an unanchored
    /// consumer the cancellation exit always withholds, because its collected files reach
    /// nobody on a thrown path and the runtime's retention window is the only copy left. Nobody is waiting for
    /// an abandoned run, so leaving it to its own server-side deadline is minutes of spend
    /// bought for nothing — and acknowledging before the platform holds the evidence would
    /// release the only copy.
    /// </para>
    /// </summary>
    Task<ErrorOr<AgentRunConclusion>> ExecuteAsync(
        AgentRunStartRequest request,
        AgentRunExecutionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The acknowledge tail alone, for consumers that hold only a run id (terminal-transition
    /// cleanup, the janitor path). Quiet discipline: no caller token is accepted — the verb
    /// runs on its own fresh short-lived token, because a caller's token is typically already
    /// dying when teardown runs and an aborted complete would leave data for the runtime's
    /// retention reaper — and warns instead of throwing. Returns whether the runtime accepted
    /// the complete: false — a rejected, failed, or timed-out call — means the run was not
    /// acknowledged, however the caller meant it to be.
    /// </summary>
    Task<bool> AcknowledgeQuietlyAsync(string executionId);

    /// <summary>
    /// Best-effort cancel for abandoning a live run, same quiet discipline as
    /// <see cref="AcknowledgeQuietlyAsync"/>: a fresh short-lived token (the
    /// <see cref="IAgentRuntimeClient.CancelRunAsync"/> cancellation-token trap — an
    /// already-cancelled caller token would abort the HTTP call before the wire and leave the
    /// sidecar running and billing), warn-never-fail. <see cref="ExecuteAsync"/> calls this
    /// itself on every path that abandons a run; this overload is for consumers holding only a
    /// run id.
    /// </summary>
    Task CancelQuietlyAsync(string executionId);

    /// <summary>
    /// The whole abandonment teardown from just a run id, for consumers whose watcher died —
    /// terminal-transition cleanup, the janitor: cancel, wait briefly for the settled verdict,
    /// collect the pack, persist it under <paramref name="anchor"/>, and acknowledge only when
    /// the evidence is secured. Quiet discipline throughout — fresh short-lived tokens, bounded
    /// probes and timeouts, never throws. Returns whether the runtime accepted the complete;
    /// false leaves the pack collectable for the runtime's retention window. A null
    /// <paramref name="anchor"/> is the ephemeral mode: acknowledged on successful collection
    /// alone, nothing persisted.
    /// </summary>
    Task<bool> SettleAbandonedQuietlyAsync(string executionId, AgentRunTraceAnchor? anchor);

    /// <summary>
    /// <see cref="SettleAbandonedQuietlyAsync"/> for a consumer that bills the run: the same
    /// teardown, returning the settled monitor so its <see cref="AgentRunMonitor.EstimatedCostUsd"/>
    /// can be recorded. A run the runtime never knew settles at no cost.
    /// </summary>
    Task<AgentRunMonitor> AbandonQuietlyAsync(string executionId, AgentRunTraceAnchor? anchor);
}

/// <summary>
/// The settled outcome of <see cref="IAgentRunExecutor.ExecuteAsync"/>.
/// </summary>
/// <param name="Monitor">The settled monitor, result or error payload included.</param>
/// <param name="TraceFiles">Verified trace rows, unanchored — null block id, empty
/// organization — the caller stamps its own identity, same contract as
/// <see cref="IAgentRunTraceCollector"/>. Null exactly when collection never produced an
/// outcome (no collector wired, or the collector broke its never-throw contract): the caller
/// then leaves whatever prior evidence it holds untouched instead of replacing it.</param>
/// <param name="CompleteWithheld">True when no <c>/complete</c> was accepted for at least one
/// pass — the withhold rules forbade sending it (trace collection failed or never ran, its
/// rows could not be durably persisted, or the pass ended in an unknowable state), or the call
/// itself failed or was rejected — so at least
/// one pack stays collectable on the runtime for its retention window. Per-attempt
/// <see cref="AgentRunAttempt.Unacknowledged"/> is the strictly broader signal (it also covers
/// cancellation and stale aborted registrations): cleanup paths enumerate by the attempt
/// marker, not this flag.</param>
public sealed record AgentRunConclusion(
    AgentRunMonitor Monitor,
    IReadOnlyList<AgentRunTraceFile>? TraceFiles,
    bool CompleteWithheld,
    IReadOnlyList<AgentRunAttempt> Attempts);

/// <summary>
/// One run the executor bought on the caller's behalf. The ledger of these is what a consumer
/// records as spend and what makes a failure resumable: it names which run to look at, which
/// pass it was, how it ended, and what it cost.
/// </summary>
/// <param name="Pass">The pass this run was, counting from one.</param>
/// <param name="ExecutionId">The run id the runtime knows it by. Always a run whose start
/// returned success — a start whose response was lost in flight may still have registered on
/// the runtime, so an empty ledger means no run was ever watched, not proof nothing was
/// bought.</param>
/// <param name="Status">Its last observed status.</param>
/// <param name="FailureSubtype">The runtime's failure subtype, null unless it settled failed.</param>
/// <param name="EstimatedCostUsd">The runtime's own measure of what this run spent.</param>
/// <param name="Unacknowledged">True when no complete was accepted for this run — the
/// withhold rules forbade sending it (failed collection or persistence, an unknowable state),
/// the call failed or was rejected (a still-live run answers <c>AgentRun.NotCompletable</c>),
/// or an admission shed aborted the registration — so the run (or its retained pack) may still
/// be live on the runtime and belongs to a cleanup path's enumeration.</param>
/// <param name="RanContinuationFallback">True when this run was bought under
/// <see cref="AgentRunExecutionOptions.ContinuationFallbackPrompt"/> — the executor degraded a
/// continuation that failed to restore at no spend, so this run and every later one of the
/// call ran without the continuation. The consumer's mode bookkeeping reads it to record the
/// override in its own vocabulary.</param>
public sealed record AgentRunAttempt(
    int Pass,
    string ExecutionId,
    AgentRunStatus Status,
    string? FailureSubtype,
    decimal EstimatedCostUsd,
    bool Unacknowledged,
    bool RanContinuationFallback = false);

/// <summary>
/// A run the executor just started on the caller's behalf — raised before the first poll, so
/// the caller can durably record the run while nothing has had a chance to die yet.
/// </summary>
/// <param name="Pass">The pass this run is, counting from one.</param>
/// <param name="ExecutionId">The run id the runtime registered it under.</param>
/// <param name="RanContinuationFallback">Same contract as
/// <see cref="AgentRunAttempt.RanContinuationFallback"/>, known at start time because the
/// degrade swap happens between passes.</param>
public sealed record AgentRunStarted(int Pass, string ExecutionId, bool RanContinuationFallback = false);

/// <summary>
/// Reading a run ledger, wherever it arrived from. A machinery error carries one just as a
/// conclusion does, so every consumer that records spend or reports a failure reads it the same
/// way instead of re-deriving the metadata cast.
/// </summary>
public static class AgentRunAttempts
{
    public static IReadOnlyList<AgentRunAttempt> Of(Error error)
    {
        return error.Metadata?.GetValueOrDefault(AgentRunExecutorErrors.ATTEMPTS_METADATA_KEY)
            as IReadOnlyList<AgentRunAttempt> ?? [];
    }

    /// <summary>
    /// Trace evidence riding a machinery error. Earlier passes may have collected packs whose
    /// server-side copies their acknowledgements already released, so the error is the only
    /// place that evidence still exists; null when no collection ever produced an outcome.
    /// </summary>
    public static IReadOnlyList<AgentRunTraceFile>? TraceFilesOf(Error error)
    {
        return error.Metadata?.GetValueOrDefault(AgentRunExecutorErrors.TRACE_FILES_METADATA_KEY)
            as IReadOnlyList<AgentRunTraceFile>;
    }

    public static decimal TotalCostUsd(this IReadOnlyList<AgentRunAttempt> attempts)
    {
        return attempts.Sum(static attempt => attempt.EstimatedCostUsd);
    }
}

/// <summary>
/// Per-call execution policy. The deadline bounds one run's polling; the rest is what the
/// caller is willing to spend protecting the work that led here. Under these options a run that
/// parks on awaiting_input is a machinery error; AwaitingInput-as-conclusion lands as a
/// new member so the seam keeps its shape. <paramref name="Activity"/> is the run's optional
/// live-feed registration: when present and a feed is wired, the executor announces the run,
/// publishes mid-run digest projections while polling, and concludes the entry when the run
/// settles.
/// </summary>
public sealed record AgentRunExecutionOptions(
    TimeSpan Deadline,
    AgentRunActivityRegistration? Activity = null)
{
    /// <summary>
    /// How long to wait out capacity refusals before giving up on being admitted. Spent before
    /// a run exists, so it never eats <see cref="Deadline"/>. Zero returns the refusal verbatim
    /// on the first shed, for callers whose durable layer re-dispatches instead of waiting.
    /// </summary>
    public TimeSpan AdmissionPatience { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// How many runs this call may buy when each failure is retryable infrastructure trouble
    /// (see <see cref="AgentRunSubtypes.IsRetryable"/>). Capped at one unless
    /// <see cref="ExecutionIdForPass"/> can mint the next pass an id of its own — reusing the
    /// failed run's id would replay its verdict rather than start a run.
    /// </summary>
    public int MaxPasses { get; init; } = 1;

    /// <summary>
    /// How long to wait before the one fresh pass an <see cref="AgentRunSubtypes.UPSTREAM_ERROR"/>
    /// failure buys, inside <see cref="MaxPasses"/>. The pass is bought only when the failed run
    /// spent at most a quarter of the budget the runtime echoed for it, and it runs on the full
    /// budget again. Zero buys none.
    /// </summary>
    public TimeSpan UpstreamErrorRetryDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// How many consecutive failed polls a live run survives. A poll failure says nothing about
    /// the run, so voiding one on the first errored poll throws away spend the run may be
    /// seconds from earning. A run the runtime no longer knows is terminal regardless.
    /// </summary>
    public int PollErrorTolerance { get; init; } = 0;

    /// <summary>
    /// Mints the run id for a given pass. The caller owns run identity — the components that
    /// compose an id are its vocabulary, not this layer's — so retries beyond the first pass
    /// need this. Null keeps the call single-pass. Only the id is re-minted:
    /// <see cref="AgentRunStartRequest"/>.<c>RunToken</c> rides every pass verbatim, so a
    /// caller that mints per-run tokens must stay single-pass.
    /// </summary>
    public Func<int, string>? ExecutionIdForPass { get; init; }

    /// <summary>
    /// Where this consumer durably stores collected trace evidence: rows are stamped with the
    /// anchor's organization and block before the shared layer commits them through
    /// <see cref="IAgentRunTracePersister"/>, and a run is acknowledged only after that commit
    /// succeeds — persist-then-acknowledge. Null is the ephemeral mode for consumers
    /// with no durable home (editor block/section tests, whose evidence lives in the block
    /// instance for the session): collection alone gates the acknowledgement, exactly because
    /// withholding it would leak runtime retention capacity on every test click. Any consumer
    /// whose evidence must survive the process sets an anchor.
    /// </summary>
    public AgentRunTraceAnchor? TraceAnchor { get; init; }

    /// <summary>
    /// The prompt this call re-runs with — continuation stripped, next pass id — if its
    /// continuation fails to restore at no spend. A prompt rather than a whole request, so the
    /// fallback is the same leg by construction and cannot silently differ in model, inputs,
    /// or fuses; the executor derives the fallback request itself. Consumed at most once per
    /// call, regardless of <see cref="MaxPasses"/>: the fallback is the leg running without
    /// its continuation inside the same round, not a retry of the same run. Null — or a
    /// request whose continuation never fails — leaves the ordinary pass rules alone.
    /// Meaningful only when the primary request carries a continuation and
    /// <see cref="ExecutionIdForPass"/> can mint the fallback its own id.
    /// </summary>
    public string? ContinuationFallbackPrompt { get; init; }

    /// <summary>
    /// Where this consumer files the session bundles of the runs it buys, or null to buy none.
    /// Null is the default and the common case: a bundle is only worth its bytes to a consumer
    /// that can send this leg back to redo its work, so a plain Agent block outside any
    /// verification group declares no anchor, fetches nothing and stores nothing. Custody runs
    /// before the acknowledgement releases the runtime's copy, and never gates it — see
    /// <see cref="IAgentRunSessionBundleCustodian"/> for why the rule differs from trace
    /// persistence.
    /// </summary>
    public AgentRunSessionBundleAnchor? SessionBundleAnchor { get; init; }

    /// <summary>
    /// Observes each attempt the moment it is recorded, so a consumer's ledger survives the
    /// exits that never hand a return value back — above all caller cancellation, where the
    /// runs already bought would otherwise vanish from the record.
    /// </summary>
    public Action<AgentRunAttempt>? OnAttempt { get; init; }

    /// <summary>
    /// Awaited after every successful start and before that pass's first poll — the consumer's
    /// seam for durably recording the run while it is provably alive and nothing has died yet.
    /// A throw here abandons the fresh run: the executor quietly cancels it, records its
    /// attempt as unacknowledged, and rethrows — a run nobody could record must not keep
    /// burning unwatched.
    /// </summary>
    public Func<AgentRunStarted, CancellationToken, Task>? OnRunStarted { get; init; }

    /// <summary>
    /// How the live feed of <see cref="Activity"/> is projected and how often. Null keeps the
    /// shared defaults: the feed is refreshed on the status poll, at most every few seconds.
    /// </summary>
    public AgentRunLiveActivityOptions? LiveActivity { get; init; }
}

/// <summary>
/// A consumer's live-feed preferences. A chat surface wants its steps near-live and named in its
/// own words; a workflow run is content with the shared defaults.
/// </summary>
public sealed record AgentRunLiveActivityOptions
{
    /// <summary>
    /// How often the trace is read and the feed refreshed while the run works, independent of
    /// the status poll. Null refreshes on the status poll, at most every few seconds.
    /// </summary>
    public TimeSpan? PublishInterval { get; init; }

    /// <summary>The projection's options; the executor adds the narration it tidied.</summary>
    public Activity.AgentActivityProjectionOptions Projection { get; init; } = Activity.AgentActivityProjectionOptions.Default;
}

/// <summary>
/// The identity the consumer stamps onto persisted trace evidence — the direct tenant anchor
/// and the optional block the rows belong to (block-less consumers persist org-anchored rows;
/// see <see cref="AgentRunTraceFile"/>).
/// </summary>
public sealed record AgentRunTraceAnchor(string OrganizationId, string? BlockId);

/// <summary>
/// Error codes <see cref="IAgentRunExecutor.ExecuteAsync"/> returns when the machinery
/// failed — as opposed to a run that settled with a failure subtype, which is a conclusion.
/// Wrapped errors carry the underlying client error as "<c>Code</c>: <c>Description</c>" in
/// the description; the deadline error carries the last observed status under
/// <see cref="STATUS_METADATA_KEY"/>.
/// </summary>
public static class AgentRunExecutorErrors
{
    public const string START_FAILED = "AgentRunExecutor.StartFailed";
    public const string POLL_FAILED = "AgentRunExecutor.PollFailed";
    public const string DEADLINE_EXCEEDED = "AgentRunExecutor.DeadlineExceeded";
    public const string UNKNOWN_STATUS = "AgentRunExecutor.UnknownStatus";
    public const string AWAITING_INPUT = "AgentRunExecutor.AwaitingInput";
    public const string ADMISSION_EXHAUSTED = "AgentRunExecutor.AdmissionExhausted";
    public const string STATUS_METADATA_KEY = "status";

    /// <summary>
    /// Key under which every machinery error carries its
    /// <see cref="IReadOnlyList{T}"/> of <see cref="AgentRunAttempt"/>, so a consumer records
    /// what was spent and which runs to look at even when no conclusion came back.
    /// </summary>
    public const string ATTEMPTS_METADATA_KEY = "attempts";

    /// <summary>
    /// Key under which a machinery error carries the trace files collected across its passes
    /// (read via <see cref="AgentRunAttempts.TraceFilesOf"/>); absent when collection never
    /// produced an outcome.
    /// </summary>
    public const string TRACE_FILES_METADATA_KEY = "traceFiles";
}
