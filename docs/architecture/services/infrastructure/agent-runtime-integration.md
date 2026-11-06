---
type: explanation
status: current
description: "The shared agent-integration layer: one executor owning start → gated poll → collect → persist → acknowledge-or-withhold, session-bundle custody, the failure-absorbing policies and their cost, billing from trace tokens, run fuses, the poll gate, 503 handling, the request-repair helpers, agent conversations, registration, and the consumers. Read when an agent-family block or an editor agent turn fails, stalls, retries or costs more than expected, or when you build a new consumer of the agent runtime."
---

# Agent Runtime Integration

**On this page:** The executor owns the run lifecycle · Persist, then acknowledge — the order is the contract · Session-bundle custody · Protecting spend the workflow already made · What an agent run reports to usage · Which model a run calls · Declaring run fuses · One poller, one gate · A 503 is a refusal, not a transient fault · Request-repair helpers · The task setup section · Agent conversations · Registration and the disabled default · Consumers

Agent integration is a **platform-wide capability, not workflow machinery**: only logic
specific to the workflow engine or the Agent block's product rules lives in a block strategy.
Everything else — polling with its process-wide gate, trace collection, run acknowledgement,
run identity, request repair — lives in one shared layer in
`platform/IniaAi.Common.Interfaces/Services/AgentRuntime/` (contracts) and
`platform/IniaAi.Infrastructure/Services/` (implementations), reusable by every consumer of the
agent-runtime sidecar.


## The executor owns the run lifecycle

`IAgentRunExecutor` is the seam consumers use. Its `ExecuteAsync` owns the whole lifecycle for
one run — **start → awaited `OnRunStarted` → gated poll to settled → collect the trace pack →
persist it → take custody of the session bundle → acknowledge-or-withhold** — so no consumer
re-implements the ordering or the withhold rules. Custody runs second-to-last deliberately: the
evidence that can withhold the acknowledgement is secured first, and the bundle — which never
withholds — still lands before the acknowledgement releases the runtime's copy. It runs only for
a consumer that declared an `AgentRunSessionBundleAnchor`; see
[session-bundle custody](#session-bundle-custody).
`OnRunStarted` is the consumer's chance to durably record the run before the first
poll; the agent-family strategies use it (through their shared execution-options wiring) to
flush the run id, pass, and an unacknowledged marker into the block's persisted execution
state, so a process death mid-poll leaves a row cleanup can enumerate. A hook that throws abandons the fresh run: the executor records its attempt as
unacknowledged, quietly cancels it, and rethrows — a run nobody could record must not keep
burning unwatched.

The load-bearing line is **conclusion vs error**:

- A run that **settled** — succeeded, failed, cancelled, expired — returns an
  `AgentRunConclusion` (the settled monitor, the collected trace rows, and whether `/complete`
  was withheld). A failed run is a successful observation; the consumer reads
  `Monitor.TryGetError` and applies its own policy.
- A run whose state the platform **could not establish** returns an error
  (`AgentRunExecutorErrors`): start rejected, poll failed, the caller's deadline exhausted, an
  unknown status, or a pause for input under v1 options.

Acknowledgement is decided inside the executor, never by the caller. After a successful start,
every path quietly completes the run — except where the withhold rules forbid it: an unknown
status (an unparseable status may be terminal, and acknowledging it would purge an uncollected
pack), a failed/missing trace collection, or — for an anchored consumer — a failed/missing
persistence of the collected rows (either way the pack must stay collectable on the runtime for
its retention window). A complete that is sent but fails or is rejected — a still-live run
answers `AgentRun.NotCompletable` — counts as not acknowledged too: the attempt's
`Unacknowledged` marker records whether a complete was actually accepted, not whether one was
attempted.

`SettleAbandonedQuietlyAsync(executionId, anchor)` exposes the whole abandonment teardown for
consumers that hold only a run id — terminal-transition cleanup and the janitor path — so even
a run whose watcher died is re-collected and committed before any complete is sent.
`AcknowledgeQuietlyAsync` and `CancelQuietlyAsync` ship beside it for narrower needs — all
three encode the fresh-short-lived-token discipline: the caller's token is typically already
dying during teardown, and an aborted complete or cancel would leave the sidecar running or its
data waiting for the retention reaper.

## Persist, then acknowledge — the order is the contract

Acknowledging a run makes the runtime purge its only copy of the run's trace pack, so the
executor releases it **only after the platform durably holds its own copy**. The order on every
path is: collect the pack → commit its rows through `IAgentRunTracePersister` → send
`/complete`. The persister (`AgentRunTracePersister`) writes through a short-lived
`UserDataDbContext` scope of its own, idempotently by the row's natural key (run execution id,
attempt, path), so a replayed teardown or a cleanup re-collection after a crash inserts nothing
twice — and a unique index on that key converges even *concurrent* persists of one run (a
watcher's cancellation teardown racing terminal cleanup happens by construction on a user
cancel): the losing insert fails and one retry finds the winner's rows already committed. A failed persist is logged with the execution id, withholds the acknowledgement, and
leaves the pack collectable on the runtime for its retention window — nothing is lost, and the
next settle attempt (terminal cleanup, or the runtime's retention reaper as last resort)
re-collects. Persistence happens **per pass**, not at conclusion: a multi-pass block's earlier
packs are committed before their acknowledgements release the runtime copies, so a crash
between passes loses nothing.

This ordering exists because the reverse order loses evidence: when a writer drops the rows after
the executor has acknowledged on collection alone, the runtime has already purged its only copy of
the pack. The engine's block-persist insert of `block.AgentRunTraceFiles` still runs and
skips already-persisted ids — it is the backstop now, not the primary writer.


Where the rows land is the consumer's declaration: `AgentRunExecutionOptions.TraceAnchor`
names the organization and (optionally) the block the executor stamps onto the collected rows
before committing them. A **null anchor is the ephemeral mode** — acknowledgement is gated on
successful collection alone and nothing is persisted — declared by consumers with no durable
home for the rows: editor block/section tests run against the template's block, which has no
UserData row to satisfy the trace table's foreign key, and their evidence lives on the
in-memory block instance for the session. Withholding completes there would pile abandoned
packs onto the runtime with every editor test click. One exception: an ephemeral run torn down
by **caller cancellation** withholds, because its collected files exit through no return value
on a thrown path — acknowledging would purge the runtime's only copy of evidence nobody
received, so the pack waits for the retention reaper instead. Every consumer whose evidence
must survive the process sets an anchor.

**Every path that abandons a run cancels it first — and then secures the evidence.** Caller
cancellation, an exhausted deadline, polling given up on, an unreadable status, a pause these
options cannot answer — each runs the same teardown: cancel, wait briefly for the settled
monitor (bounded probes — the monitor carries a run's cost only once it settles, so the
cancelled attempt's ledger row carries real spend instead of the start monitor's zero),
collect, persist, acknowledge-or-withhold. On caller cancellation the teardown runs before the
`OperationCanceledException` propagates and is deliberately blind to the caller's token, every
step bounded by its own timeout — a cancellation arriving mid-teardown cannot abandon the
evidence half-secured. The one cancel-only exit is a thrown `OnRunStarted`, where nothing has
been spent and the consumer's own record machinery is already broken. A user cancelling a
workflow therefore still stops the sidecar within seconds, and the run's settled cost lands in
the block's ledger and usage record like any other run's.

## Session-bundle custody

A finished run's **session bundle** is the state a later leg would need to continue that session
instead of starting over. The runtime writes it to a container tmpfs that any redeploy takes with
it, so the platform takes its own copy while the run still holds one.

`IAgentRunSessionBundleCustodian` fetches the bundle over the client's `checkpoint` verb, re-hashes
it against the digest the runtime declared, refuses anything past the runtime's own 64 MiB cap,
stores the bytes through `IFileStorageManager` under the `agent-session-bundles` category, and
records an `AgentRunSessionBundles` pointer row keyed on its owner and round: a block and its
verification round, or a [conversation](#agent-conversations) and its turn number (see
[UserData reference](../../../reference/database/user-data-database.md#agentrunsessionbundles)). A row names
exactly one owner; the custodian refuses an anchor that names both or neither, and a database CHECK
constraint refuses such a row. The bytes are
stored before the row, and a failed commit deletes them again: a row therefore never names bytes
the platform does not hold, and the only failure direction is bytes no row names.

Two rules make this different from trace persistence, and both are deliberate:

- **Custody fails open.** A failed fetch or store logs, records no row, and never withholds the
  acknowledgement. A lost bundle costs one redo leg its head start; a withheld acknowledgement
  holds runtime retention capacity for the whole window. A failed trace persist still withholds,
  unchanged — that evidence has no second source.
- **Only a consumer that resumes sessions pays for it.** A bundle is worth its bytes only to a
  later leg that continues the session, so only a consumer that declares an anchor stores one, such
  as an [agent conversation](#agent-conversations) that resumes sessions. A plain Agent block
  declares none: it fetches nothing and stores nothing — the runtime still publishes its bundle,
  and the platform simply declines to collect it.


A bundle stops being worth its bytes the moment its execution can no longer redo anything, so the
execution's terminal releases it through `IAgentRunSessionBundleReaper`, and the
[`AgentRunSessionBundleReaper` sweep](../../../operations/application-configuration.md#agentrunsessionbundlereaper)
collects what a crashed or stranded execution never released. The retention rules themselves — what
each terminal deletes, what `Failed` keeps, the seven-day backstop, and why the row always goes
before the bytes — have one home: [Soft-Delete Cleanup](../../../operations/soft-delete-cleanup.md).

## Protecting spend the workflow already made

An agent run is usually the last and least replaceable thing a workflow buys, so the executor
treats failures that say nothing about the work itself as things to absorb rather than surface.
All four behaviours are per-call policy on `AgentRunExecutionOptions`, defaulted from
`AgentRuntimeSettings` by the consumer, and all four are off when the options say so — the
shape of the seam does not change for a caller that wants none of them.

- **Admission patience.** A capacity or `shutting down` refusal is waited out within
  `AdmissionPatience`, honouring the runtime's `Retry-After` each time. A shed leaves the run
  unregistered, so re-sending the identical request is safe. Patience is spent *before* a run
  exists and never draws on the run deadline: queuing for a slot is not execution time. When
  patience runs out the error is `ADMISSION_EXHAUSTED` and nothing was spent, because nothing
  ever started. With patience at zero the refusal passes through verbatim, code and
  `Retry-After` intact, for a caller whose durable layer re-dispatches instead of waiting.
- **Admission class.** Agent-conversation turns set `AgentRunStartRequest.AdmissionClass` to
  `Chat` when `PlatformSettings:AgentRuntime:SendAdmissionClass` is on, and the client sends it as
  the wire's `admissionClass`. The runtime then keeps a small reserve of slots for chat that
  workflow runs cannot take, and caps how many chat runs it holds (agent-runtime ADR 0142). Workflow
  block runs never set it and are admitted as workflow runs. The setting is off
  by default because a runtime older than the release that added the admission class refuses
  the unknown field with 400. A runtime with chat admission switched off (a chat cap of 0) refuses the
  start `503` with reason `chat_disabled` and no `Retry-After`; the client maps it to
  `AgentRun.ChatDisabled`, which is not an admission refusal, so no caller waits it out, the
  executor passes it through unwrapped, and the conversation turn fails with "The agent is not
  available on this deployment."
- **Retryable passes.** A pass that settles with a subtype `AgentRunSubtypes.IsRetryable`
  accepts — `uid_pool_exhausted`, `internal_error`, `runtime_shutdown` — is re-registered as
  the next pass within `MaxPasses` (the caller's `AgentRunExecutionOptions.MaxPasses`; the
  workflow consumer wires it from `PlatformSettings:AgentRuntime:MaxRunPasses`). So is a run
  **the runtime no longer knows**: a `404` while polling a run the platform itself started
  means the runtime lost it, which is the same class of failure as a drain and is exactly what
  a single-instance restart produces, since the run registry is in memory. `run_child_error` is
  **not** in the set: the runtime uses it as its non-retryable, spend-carrying failure — a
  child killed after cost frames, a finished run whose deliverables could not be published, a
  supervisor-side failure after spend; its one $0 use is the child's catch-all for unknown
  errors — so a fresh pass would re-buy a run the runtime told the platform not to re-run.
  `runtime_shutdown` is the deliberate exception: the drained run may carry spend, but a deploy
  is the platform's own infrastructure at work, and a routine deploy must not permanently fail
  the block. Each pass gets its own execution id from the caller's `ExecutionIdForPass` —
  reusing the failed run's id would replay its verdict rather than start a run. A retry never
  re-buys the failed run's evidence: each pass's trace pack is collected before that pass is
  torn down, and the packs accumulate across passes, keyed by execution id and pass on the rows.
- **An upstream outage buys one delayed pass.** `upstream_error` (the model API failed on its
  side, ADR 0130 in the runtime) is not in `IsRetryable`: buying the next pass at once would land
  inside the same outage. `AgentRunSubtypes.IsUpstreamOutage` names it, and when the caller sets
  `AgentRunExecutionOptions.UpstreamErrorRetryDelay` the executor waits that long and then buys
  **one** fresh pass, inside `MaxPasses`. The agent-family blocks set it from
  `PlatformSettings:AgentRuntime:UpstreamErrorRetryDelaySeconds` (600; 0 turns it off); editor
  conversations leave it unset. The pass is bought only when the failed run spent at most a
  quarter of its own budget, read from the `fuses` the runtime echoes on the monitor (a runtime
  that echoes none gets no retry), and it runs with the full budget again, so the outage pass adds at
  most 1.25 times the budget for those two passes; the spend bound below still holds for the block. A second `upstream_error`, or a first one over the bound, fails the
  block as before, and the ledger holds every pass. The wait is logged as a warning naming
  `upstream_error`, the wait and the next pass, so an operator can see why the block took longer.
  The ten minutes come from a real Bedrock outage: each failed run had already spent 5 to
  8 minutes in the SDK's own retries, and the errors lasted about 40 minutes. The wait
  holds the block's slot, so at a block parallelism of 1 the whole execution pauses with it.
- **Poll-error tolerance.** A failed poll says nothing about the run, so up to
  `PollErrorTolerance` **consecutive** failures are tolerated rather than voiding a live run
  that may be seconds from succeeding. The counter resets on any successful poll. Errors about
  the run itself — `AgentRun.NotFound`, `AgentRun.Completed` — are never tolerated: no amount
  of patience brings back a run the runtime has released.
- **A ledger on every outcome.** Both a conclusion and a machinery error carry
  `AgentRunAttempt` rows — pass, execution id, last observed status, failure subtype, the
  runtime's own `estimatedCostUsd`, and an `Unacknowledged` marker (true when no complete was
  accepted for the run, so it may still be live on the runtime) — the conclusion in
  `Attempts`, the error under `ATTEMPTS_METADATA_KEY`. The marker is what cleanup paths
  enumerate on; the rest is what lets a consumer record real spend and turn a failure into a
  resumable record instead of a dead end.

Continuation — resuming a finished member's own session rather than starting from scratch — is
built on both sides. The runtime accepts a `continuation` on `POST /v1/runs` (agent-runtime ADR
0124): a platform-held session bundle plus the resumed run's own re-carried `out/` files, every
declared SHA-256 digest verified before any spend, and the resumed session id compared against
what the stream reports — a mismatch fails the run with a non-retryable restore subtype instead
of silently starting a fresh conversation. On the platform side `AgentRunStartRequest` carries
the continuation, and the client pre-flights the runtime's ceilings before any HTTP. A plain
`ExecuteAsync` retry remains a cold start on purpose.


### What absorbing failures costs

These four behaviours are bought, not free, and the price is paid in two currencies that no run
fuse bounds. Both are accepted deliberately: the alternative is failing a block outright and
discarding everything the workflow spent upstream, which is the larger loss.

- **Spend multiplies by `MaxRunPasses`.** A fuse is a per-*run* limit the runtime enforces, and a
  retry is a new run with the same declared fuses. Worst-case block spend is therefore the
  declared budget × `MaxRunPasses` — $7.50 at the shipped $2.50 and 3 passes. Nothing composes a
  block-level budget across passes; the `AgentRunAttempt` ledger reports what was actually spent
  after the fact rather than capping it in advance.
- **Wall time compounds the same way.** Each pass may spend `AdmissionPatienceSeconds` queuing
  and then its whole poll deadline running, and patience is deliberately *not* drawn from the run
  deadline. At the shipped defaults the ceiling is 3 × (10 min + 30 min) ≈ **2 hours** before the
  block gives up, plus the ten-minute wait when one of the passes failed `upstream_error`. A tighter bound comes from lowering `MaxRunPasses` or `AdmissionPatienceSeconds`,
  not from a run deadline, which bounds one run rather than the block.

## What an agent run reports to usage

An agent run is billed from its **tokens at list price**, the same way every other model call is
billed: the tokens come out of the collected trace pack and each request is priced from the
`ModelPricing` row of the model it called (`AgentRunBilling`, shared by the agent-family blocks
and editor agent turns). The runtime's own cost figure is never the bill when
a pricing row covers the tokens (the runtime passed on the Claude Agent SDK's figure, and
SDK 0.3.281 priced Claude Haiku 5.5 and Claude Sonnet 5.5 at Claude Opus 5.5 rates).

**Tokens come out of the trace pack.** The monitor payload carries a cost and no token field, so
the split exists only in the pack the executor collects. `AgentRunTraceTokens` reads two sources
from `trace/trace.ndjson`:

- one `usage` record per settled API request, main loop and sub-agents, with that request's model
  and its `input_tokens`, `output_tokens`, `cache_creation_input_tokens` (split into 5-minute and
  1-hour writes from `cache_creation`) and `cache_read_input_tokens`;
- the model SDK's per-model totals: the runtime's `model_usage` record per settled segment, or, from
  a runtime that predates it, the `modelUsage` of the run's last result message. The totals count
  requests the message stream never shows: context compaction and the CLI's own Claude Haiku 4.5
  calls. What they hold beyond the per-request records is added as one summed request per model
  (this was the 7% to 8% that agent spend ran above its token rows). A result message's
  totals include every earlier turn of a resumed session, so they are read only for a run that did
  not start from a restored session (`stage` `run.started` with `resumed: false`).

Packs are deduplicated on their natural key (run, attempt, path), so a re-collected pack counts
once while separate passes still add up.

**The records.** One usage record per model the run called, with `Input`/`Output`/`CacheWrite5m`/
`CacheWrite1h`/`CacheRead` components carrying the tokens and their cost, and one spend record whose
`AgentRunSpend` component has `Units` = the number of passes and carries only the cost no pricing
row covered:

| Case | Token records | `AgentRunSpend` cost | `costSource` |
|---|---|---|---|
| Every model has a pricing row | priced | 0 | `tokens` |
| A model has no pricing row | priced where a row exists, else 0 | the runtime's figure minus the priced tokens | `runtime` |
| A pass's trace was not collected | priced for the passes that were | that pass's runtime figure, plus any of the above | `runtime` |
| No trace was collected | none | the runtime's figure | `runtime` |

The spend record's `Metadata` also keeps the runtime's own figure as `runtimeCostUsd`, and stamps
the token provenance: `{"tokenSource":"trace"}` when the pack supplied tokens,
`{"tokenSource":"unknown"}` when no pack was collected or it carried no usage records. A withheld
trace therefore costs the report its token counts, not its honesty. The pricing provider is the one
the configured model id names (`ModelIdNormalizer.GetProvider`: a `us.anthropic.*` id prices under
`aws-bedrock`, a Foundry deployment name under `azure-foundry`); a block that names no model uses
the trace envelope's `modelPin`. Rows written before the platform billed agent runs from their
tokens carry the runtime's whole figure on the spend record and zero on the token records.

**A price that depends on prompt length** applies request by request: Claude Haiku 5.5 charges a
request whose prompt is over its `ModelPricing.LongPromptThresholdTokens` the `LongPrompt*`
components for every token of that request ([Model pricing](../../../reference/database/core-database.md#modelpricings--modelpricingcomponents)).
A summed request is priced at the standard rates, because a sum over the threshold says nothing
about any one request.

**The block awaits its billing.** The token usage service writes through the block's service scope,
which ends when the block returns, so `RecordRunSpendAsync` completes before the block does.

## Which model a run calls

An agent-family block names a provider-neutral model id (`claude-sonnet-5-5`), or none. A block that names none runs its tier's model: `PlatformSettings:AgentRuntime:AdvancedModel` (default `claude-opus-5-5`) or `BasicModel` (default `claude-sonnet-5-5`), or the organization's `ModelRouting:TenantModelOverrides` `AgentAdvancedModel` / `AgentBasicModel` (`AgentModelTiers`). When the run starts, `AgentRunBlockSupport.RuntimeModelOf` resolves that id to the form `AgentRuntime:AllowedModels` lists for this deployment — a Bedrock profile id on AWS and behind a model gateway, a Foundry deployment name on Azure — through `AgentRuntimeModels`, and the start request and the spend record carry it. A model the allowlist has no form of is refused at save and at submit (`WorkflowExecution.AgentModelNotAllowed`). The allowlist's shape check, the normalisation of stored ids and the editor's model list are on [Azure AI Foundry → The agent path in Azure mode](../../ai-integration/azure-foundry.md#the-agent-path-in-azure-mode); the block's settings on the [block reference](../../../guides/blocks/block-reference.md#agent).

## Declaring run fuses

A run fuse is a limit the **caller** may declare, and the platform declares one only when a block
author asked for it. There is no platform-side default for any fuse: `AgentBlockStrategy` maps the
four authored properties straight onto the start request, converting minutes to `deadlineMs` and
MiB to `maxRetainedOutputBytes`, and a property the author left blank stays null. Nulls are omitted
from the serialized body, so a block that declared nothing sends no fuse fields at all and the
runtime's own deployed default governs the run.

This split of ownership is deliberate, and each half belongs to whoever can be right about it:

- **The ceiling and the default are the runtime's.** The runtime validates a request against its
  *deployed* configuration, which ops can tighten per environment. A platform-side default would be
  a second copy of a number the platform does not own, and the day ops tightened the runtime below
  it every block that had merely accepted the default would start failing admission — fleet-wide
  and pre-spend, on the very fuse meant to protect spend.
- **The tightening is the author's.** A declared fuse may only tighten the runtime's ceiling. A
  value above it is refused with a `400` **before any spend**, never clamped, so a misconfigured
  block is loud rather than quietly running under different limits than it asked for. The platform
  deliberately does **not** mirror the runtime's ceilings to pre-check this; it checks shape only
  (a value at or below zero is refused at save time) and leaves the verdict to the runtime.
- **Silence is a real state, not a missing value.** Blank means "no opinion", which is exactly what
  omitting the field expresses. There is no separate on/off control per fuse, because an "off"
  state that still leaves a runtime bound in force would not turn anything off.

The one thing the platform loses by staying silent is knowing the deadline the run is actually
under, which it needs to size its own polling (below). Until the runtime echoes the effective
values back on the start response, the consumer falls back to its own patience bound.

## One poller, one gate

The executor contains the platform's only polling loop. Its discipline: honour each monitor's
`Retry-After`; fall back to a jittered interval when one is missing (one malformed monitor
must not kill a long run mid-flight); surface `Unknown` and `AwaitingInput` immediately
instead of polling them to the deadline; bound the wait by the caller's deadline
(`AgentRunExecutionOptions`, which the Agent block derives from the run deadline it declared
plus `RunDeadlineMarginSeconds`, or — when it declared none and the governing deadline is the
runtime's own — from `MaxPollDeadlineMinutes`, its own patience bound, with no margin); and
acquire `AgentRunPollGate` per poll — a process-wide DI
singleton capping concurrent polls at 8 so concurrent workflows cannot stampede the one
shared sidecar. The gate is held per poll, never across a run's lifetime.

`IAgentRuntimeClient` beneath the executor keeps **single-shot verbs only** (start, get,
cancel, submit-input, files, checkpoint, complete) — its old wait loops were deleted with zero
production callers, so the gate holds by construction: there is no second provided loop to
copy.

## A 503 is a refusal, not a transient fault

The sidecar answers `503` when it refuses a request outright. It refuses for four reasons: a
capacity shed when the run pool is full, an exhausted staged-input byte budget, an exhausted
in-flight upload budget, or a shutdown drain. Runs occupy the pool for minutes, so a
transport-level retry budget measured in seconds can never outlast one. `AgentRuntimeClient`
therefore types the refusal instead of retrying it away, into `AgentRunAdmissionErrors`:
`AgentRun.ShuttingDown` for the drain (the body says so), `AgentRun.Capacity` for everything
else, unknown bodies included — and whatever the runtime actually said rides into the error
description, so a reason the platform has never heard of still reaches an operator.

`AgentRuntimeResilienceHandler` gives `503` **one** retry rather than its exponential transient
budget, because the runtime sheds from two unrelated places: an upload-budget shed taken before
the request body is read, which clears in the time one body read takes and advertises a constant
5 s, and a capacity shed on a pool of minutes-long runs. One attempt rescues the first; more
would not help either, since every attempt re-uploads the whole request.

That retry treats `Retry-After` as a **floor**, not a suggestion: it waits the greater of the
advertised delay and its own base delay, jittered **upward** so a fleet refused together does not
return in lockstep. When the advertised wait is longer than the request can afford to be held
open (over 5 s), it does not retry at all — the refusal surfaces immediately for the client to
type and for the caller's admission patience to wait out, which holds no body and cannot outlast
the client timeout.

**Start requests declare their length.** The runtime charges its in-flight upload budget from the
request's `Content-Length` *before* it reads a byte, so a chunked request — which declares
nothing — is charged the entire 384 MiB budget and lets exactly one start through at a time
across the whole service. The client therefore serializes each JSON body to bytes and posts it
with a declared length rather than streaming it, at the cost of one extra in-memory copy. Parallel agent blocks are then bounded by the run pool, as intended, instead of by an
upload budget that nobody was actually filling.

When the refusal carries a usable `Retry-After`, the honoured delay rides in the error's
metadata under `AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY` as a `TimeSpan`. It is the
server's value capped at the runtime's own 300-second ceiling, then jittered **upward only**:
`Retry-After` is a floor ([RFC 9110 §10.2.3](https://www.rfc-editor.org/rfc/rfc9110#section-10.2.3)),
the runtime derives it from when a pool slot is expected to free, and returning early just
earns another refusal. A missing key means the runtime advised nothing usable — the caller
falls back to its own backoff.

Two things callers must not assume. A refusal **does not prove the run is unregistered**: the
runtime reserves its output and input budgets after `register` succeeds, so a refused start
can still leave a failed record, and a re-post with a changed payload answers
`AgentRun.PayloadConflict`. And a `503` reaches **every verb**, not only start. The executor
passes admission refusals through with their code and metadata intact rather than re-coding
them to `START_FAILED`/`POLL_FAILED`, because those two fields are the whole signal a durable
re-dispatcher acts on. Nothing waits inside the **client**: an in-process wait would park a
request holding its payload and lose the work on restart. Waiting is the executor's job, under
the caller's admission patience (above).

## Request-repair helpers

Beside the contracts in `platform/IniaAi.Common.Interfaces/Services/AgentRuntime/` sit pure static
helpers that make real-world requests admissible before any HTTP is attempted:

- `AgentRunIds` — mints and validates the sidecar's execution-id shape.
- `AgentStagedNames` — maps user-uploaded document names onto the runtime's staged-input rules
  (admitted alphabet, reserved names, extension-preserving truncation, dedup suffixing).
  It covers document names only.
- `AgentSkillNames` — mirrors the runtime's `normalizeSkillName`, because a skill's name is the
  SDK identity the model invokes it by rather than a file name. The platform **refuses** a name
  that is not already an identity instead of rewriting one, so the name an author typed is the
  name the model sees; the normalizer serves that refusal by naming the identity the author
  probably meant. It also owns the description rules, so `AgentBlockStrategy` can refuse before
  it spends anything and `AgentRuntimeClient` can refuse before it uploads anything.
- `AgentOutputSchemas` — parses the author-typed output schema to a JSON object so a malformed
  one fails before a billed run.

The client validates the same rules pre-flight; helper and validation reference the same
contract from the same assembly.

## The task setup section

Every agent-family block run's system prompt is three XML-delimited sections: the runtime's
own `<environment>`, then the platform's `<task_setup>`, then the
authored system prompt inside `<instructions>`. The sections are tags rather than markdown
headings because authored prompts use `#` headings of their own. `AgentRunFacts` in
`platform/IniaAi.Core/Services/AgentRuntime/` composes the platform's part from the `AgentRunStartRequest`
itself, so it states exactly what the request carries. `<task_setup>` opens with the line "Added
automatically by the system." and then one bullet per item that applies: the skills as
`skills:<name>`, comma separated (names only, since the SDK lists skills with their
descriptions); the skill files as `$SKILL_ASSETS_DIR/<skill>/<file>`, comma separated, or one
per sub-bullet when there are more than three; and a line when an output schema is enforced.
With no bullets the whole `<task_setup>` is left out. The authored system prompt goes inside
`<instructions>` unchanged, and only when it is not blank; a request with neither keeps a null
system prompt, because the runtime refuses a blank one. Staged documents are left out: the
runtime states each one with its document class in the first user turn, and a per-run list in
the system prompt would break prompt caching across runs. The block strategies call
`AgentRunFacts.WithRunFacts` on the request they build; the strategies hold no copy of the
text. The section is always on, with no opt-out. For an Agent block with two skills, one skill
file and an output schema, the platform sends:

```text
<task_setup>
Added automatically by the system.
- Skills: skills:balance-sheet, skills:verify-bs
- Skill files: $SKILL_ASSETS_DIR/balance-sheet/check.py
- The final answer is checked against an enforced output schema.
</task_setup>

<instructions>
...the block's authored system prompt, unchanged...
</instructions>
```


The editor previews the same `<task_setup>` through `AgentRunFacts.Preview`, which renders
through the same code as the run. It shows it beside the runtime's text, which
`IAgentRuntimeClient.GetEnvironmentAsync` reads from `GET /v1/environment` (JSON
`{ "runtimeVersion", "text" }`). The platform learns the runtime version only from that
response, so the cache cannot key on it: `AgentRuntimeEnvironmentCache` holds the text for 10 minutes,
shares one fetch among concurrent callers, and holds a missing text for 1 minute, so a runtime
upgraded to serve the endpoint shows up quickly. A 404 (a runtime older than the endpoint), any
other failure, and a host with agents disabled all read as not available.


## Agent conversations

A conversation is a series of messages one user holds with an agent, each message a **turn** and
each turn a run. The shared layer owns everything a turn needs; a consumer supplies only what the
run reads. Today's one consumer is the workflow editor's agent (purpose `edchat`).

| Piece | Owns |
|---|---|
| `IAgentConversationTurns` (`AgentConversationTurns`) | Turn numbering, the per-user running cap, the order of turns within a conversation, starting waiting turns, cancel, and ending a conversation. A singleton: a process-wide lock serializes numbering, cap checks and dispatch. |
| `IAgentConversationConsumer` | One per purpose: the purpose's policy (retention days, running cap, turn deadline), its billing, and `StageTurnAsync`, which builds the run's prompt, documents and skills. |
| `IAgentConversationRunner` (`AgentConversationRunner`) | One turn's run: run identity, the continuation from the previous turn's session bundle or a fresh start with the transcript in the prompt, custody, live activity, the spend record. |
| `AgentConversationLedger` | Every write that settles a turn, from any path. |
| `IAgentConversationReaper` (`AgentConversationReaper`) | Retention, the overdue sweep, and startup recovery. |

**Waiting turns.** A message sent while an earlier turn of its conversation is in flight is not
refused: it is stored as a waiting turn (status `Queued`, `DispatchedAt` null) and starts on its
own, in turn order, when the turns before it have settled. When a turn settles, its job starts the
user's next waiting turns. Waiting turns live in the database, so they survive a reload and a
restart. The per-user cap counts dispatched turns only; a message that would start at once while
the user already has the cap running is refused (`AgentConversation.TooManyRunning`).

**Ownership.** Dispatch claims a waiting turn with a conditional update that stamps the job id,
`DispatchedAt` and `HostInstanceId` (`AgentConversationSettings:HostInstanceId`, or the machine
name and application name). A turn is the current host's while its job service still lists the
job. Cancel on such a turn cancels the job, which cancels the run and settles and bills the turn.
Cancel on another host's turn cancels the run on the runtime by its run id and leaves the settle to
that host; one with no run id yet is refused (`AgentConversation.StartingElsewhere`) rather than
marked Cancelled while it may still run.

**Settling.** The ledger writes a verdict only while the turn is in flight. A turn another party
settled first keeps its verdict and only takes cost it was missing, and each settler bills only the
cost its write added, so a turn is never billed twice. The turn's status is an optimistic
concurrency token. The runner settles every fault of its own (loading, bundle reads, writes) as
Failed with the spend its attempts reported, and Cancelled when its token is cancelled; the turn's
job settles anything that escapes, so no fault leaves a turn in flight.

**Recovery and sweeps.** At startup `AgentConversationHostService` (WebApp, the host that runs
turns) settles the turns this host dispatched and the database still shows in flight: each run is
cancelled by its run id, or by the first-pass run id when the run had not reported one yet, and the
teardown's spend is billed. It then starts waiting turns whose predecessors settled, and every
`TurnSweepIntervalMinutes` fails turns still in flight `OverdueMarginMinutes` past their purpose's
deadline (whatever host ran them) and starts waiting turns again. A host that returns under a new
name (a new container) never recovers its old turns; the overdue sweep settles those.

**Retention.** `AgentConversationRetentionService` (WebApi) runs whether or not the runtime is
enabled. It ends conversations idle past their purpose's retention window and with no turn in
flight, then deletes ended conversations: each session bundle's bytes first, its row only once the
bytes are gone, then the runs' evidence rows by run id prefix, then the conversation, whose turns
and the consumer's rows go by cascade. A bundle whose bytes cannot be deleted keeps its row and its
conversation for the next pass. Each end and delete re-checks its conditions in the statement, so a
conversation that has just taken a message is not touched. A conversation that stays in use keeps
its rows, but each turn settled longer ago than the window loses its runs' evidence rows, session
bundles and step labels in the same pass, and its consumer's `PruneTurnsAsync` drops what the
consumer keeps of it: they hold what the agent read, such as a past report's outputs, and must not
outlive the window because the conversation is still active. The turn's answer stays. A user's "New conversation" ends
the old one at once, cancelling its waiting turns.

**Live steps.** A consumer can ask for its own live pace and name its own steps
(`AgentConversationTurnRequest.LiveActivity`, an `AgentRunLiveActivityOptions`). With a
`PublishInterval` the executor reads the trace and refreshes the feed on that interval, beside the
status poll and independent of its `Retry-After`; without one it refreshes on the poll, at most every
5 seconds, which is what workflow runs keep. The projection's `StepLabeller`
(`IAgentActivityStepLabeller`) names tool calls and the agent's remarks in the consumer's words, with
an icon key the consumer's surface draws; a line it names is never sent to the narration tidier, so
naming adds no model call and no wait. `RemarksBetweenToolCallsOnly` keeps the text after the last
tool call, the answer itself, out of the steps. The settled steps stored on the turn are projected
with the same options. The editor agent refreshes every second
(`EditorAgent:LiveStepsIntervalMilliseconds`) and names its `wf.py` lookups by what they looked up:
"Searched the prompts for "word limit"", "Opened Revenue breakdown", "Looked at the 3 Oct report".

**The editor agent's past reports.** Each editor turn stages `reports.md`, the organization's latest
runs of the open workflow. A report the user picked, or one the agent asks for in
`out/open-reports.json` (up to three per round, GUID ids only, at most 4 KB; ids that are not the
organization's runs of the workflow are counted back, never echoed), is staged whole: a short
`report-<id>.md` header (workflow type, counterparty, date, status, failure, document names) and
`report-<id>.workflow.json`, the executed workflow serialized like the template with every block's
output, status, error and prompt as sent, period and loop copies included. The staging marks each
copied block with `TemplateStableBlockId` (the block it was copied from) and a variant copy with
`Variant` and `TemplateTag`, so `wf.py report <id> <block>` returns the block with all its copies.
A run over 4 MB travels gzipped (`.workflow.json.gz`); a run over `EditorAgent:MaxDraftMegabytes`
is not staged and its header says so. A confidential variable's value is masked in the staged run and its header with the run page's own
mask. A report's document is staged as its stored page text, each page opened by a
`=== <marker> page N ===` line whose marker is drawn at random per document and named in the
report's manifest, so a document's own text cannot pass for a page break; its original is staged
beside it only when the platform holds its text (a plain-text original is its own text), and the
run file and every original are charged to the message's report budget. Otherwise nothing is
trimmed: a run is the organization's own data and can quote its documents. Because the workflow is
a template other organizations share, the proposal gate refuses a change whose new text holds a
40-character passage of report text, a staged report's client or document name, or a confidential
variable's value, that the workflow's authored fields did not already hold (a test run's output
left on the draft does not count as the workflow's own), and a proposal that brings in three or
more figures (1,234 / 5.6% / 12.3m; years and short whole numbers do not count) that a report holds
and the authored workflow does not ("This change contains text from a client report"), and sends
the agent back to describe the pattern in general terms. Report text is everything a staged run
holds outside the template's own fields (outputs, inputs and prompts as sent, variable values,
errors, traces, title) and its documents' text. Every report any turn of the conversation picked
or opened (`EditorAgentTurnReports`) is read again for its text on every later turn, since a
resumed session still holds it. The agent's earlier answers are not report text: a passage an
answer quoted from a report is caught by that report's own text. Copies of a report live only in the turns that read it: its evidence rows and
session bundle go with that turn's retention above, where the turn also loses its step labels and
the text of every change refused as report text; a line its answer quoted stays until the
conversation is deleted. A report deleted later is not chased into the turns that read it; the
retention window bounds how long a copy outlives it.

**Rounds within a turn.** A consumer can review each completed round of a turn
(`AgentConversationTurnRequest.ReviewRound`) and send the agent on with a message of its own, up to
`MaxRounds`. A further round continues the round before it when the request resumes sessions, and
otherwise starts fresh with the turn's message, the consumer's message after it and any documents
the review staged (`AgentConversationRoundReview.Documents`). Every round runs within what is left
of the turn's budget and deadline; the clock starts when the first round starts, so the first round
gets the whole declared deadline. A round that fails leaves the turn with the previous round's
answer, and the turn's answer is the last completed round's.

**The editor agent's rounds: report requests and the stop gate.** The editor agent's review
(`EditorAgentConversationConsumer`) allows five rounds: two for report requests
(`EditorAgentReportFetch.MAX_FETCH_ROUNDS`) and three for the stop gate
(`EditorAgentProposalGate.MAX_ROUNDS`), so a report request never takes a round the gate needs. A
round that left `out/open-reports.json` is answered first, by staging the reports. Otherwise the
stop gate reads `out/workflow.json`; a round without one ends the turn as a plain answer. The gate
builds the change list against the draft the turn read (`WorkflowProposalBuilder`), reads the
agent's `out/summary.json` (one entry per block or section it meant to change), and runs every
check a template save runs on the result (`WorkflowSaveChecks`: the JSON Schema, the structural
validator, Library references, unknown agent template tags, the agent and screening gates, and
signature-field deletions), reporting every failure rather than the first. It then applies the changes to a copy of the draft the way the editor's preview and accept will (`WorkflowProposalApplier`), so a change the editor cannot apply, such as a deleted list setting, fails as an `Apply` check error that names the block, the setting and how to fix it, instead of reaching the user. When the changes fail only together, each is tried alone to name the one at fault; a failure no single change explains is laid on every block the changes touch. A failure the draft
already had does not count against the proposal, and an unknown agent template tag is kept as a
warning (the save asks for confirmation of it) rather than an error. It records the proposal
on the turn (`EditorAgentProposals`) each round. While checks fail and a gate round is left, it
sends the agent the errors to fix in the same turn (`editor-agent-gate.md`), with the previous
attempt staged as `previous-attempt.json`, `previous-attempt-summary.json` and
`previous-attempt-answer.md` for a round that starts fresh. After the last round, or when a round
brings back exactly the same errors as the round before (the agent is not fixing them), the remaining
errors stay on the proposal and the changes they trace to cannot be accepted, so the editor's
preview never applies them. The gate also refuses
a change to a Library link, a file over `EditorAgent:MaxDraftMegabytes`, more than 1,000 changed
fields or more than 500 summary entries, and a change that carries report text (below). A file the
agent carried over unchanged from a resumed session is recognized by its digest and not proposed
twice.

**The outbound check.** `EditorAgentOutboundFlow` marks every change that can alter what the
workflow sends to an outside service: a change to a block that calls out (a web call, a search, an
exchange rate or peer financials lookup) or to anything that feeds one, directly or through other
blocks, a variable, a signature field or a document class. It walks the dependency map of both the
draft and the proposed workflow, so a change that adds a path and one that removes one are both
caught; a change to a block's name, order, description, output title or log display is not. Agent
and AI blocks do not count as outbound. A marked change shows **Outside address** and is never
taken by **Accept all**; it applies only when accepted on its own.

**Shutdown.** `JobQueueShutdownService` cancels running delegated jobs (conversation turns) and
waits for them to leave the job list, up to the host's shutdown timeout, so their runs are
cancelled and billed. No waiting turn starts while the host is stopping.

**Single process.** The cap and the numbering are checked under the process lock against the
database. A second host racing the same conversation is caught by the unique turn number and
answered with `AgentConversation.Busy`; a cap can be exceeded by one turn per extra host.

**Resuming sessions.** A turn resumes the previous turn's session only when the consumer allows it
(`AgentConversationTurnRequest.ResumeSession`). The editor agent's `EditorAgent:ResumeSessions` is off
by default until the runtime bills a continuation by its own cost; off, every turn starts
fresh with the transcript and no bundle is kept.

## Registration and the disabled default

`AddAgentRuntimeClient` in `InfrastructureServiceRegistration` binds and validates
`AgentRuntimeSettings` on every host, and registers the client, the trace collector, the trace
persister, the session-bundle custodian and reaper, the poll gate, and the executor **only when
`AgentRuntimeSettings:Enabled` is true** — the shipped default is disabled. `BlockExecutionContext.AgentRunExecutor` is therefore null exactly when
the runtime is disabled, and a strategy that needs it fails its own block descriptively (see
[Block Strategy Pattern](../../workflow-engine/block-strategy-pattern.md)). The conversation
runner is registered with the executor. The rest of the [conversation](#agent-conversations) layer
(`AddAgentConversations`: turns, ledger, turn job, reaper, events, host identity) is registered on
every host, so retention keeps running after the runtime is turned off and a turn sent while it is
off settles Failed instead of hanging.

## Consumers

- **The Agent block** (`AgentBlockStrategy`) — builds the start request from block state
  including whichever fuses its author declared (converting authored minutes and MiB into the
  wire's milliseconds and bytes, and omitting the ones left blank), sets the execution options
  from `AgentRuntimeSettings`, declares the trace anchor (its block row and organization on the
  job path; none for editor tests), records the run's spend, pass count and per-model token
  split through `ITokenUsageService`
  (see [What an agent run reports to usage](#what-an-agent-run-reports-to-usage)),
  and maps the conclusion onto the block or a block failure that names the run. A settled
  leg's output lands first as an append-only per-attempt row
  ([`AgentRunOutputs`](../../../reference/database/user-data-database.md#agentrunoutputs), keyed block +
  verification round, so a redo leg never overwrites the attempt before it), and the
  `BlockResponse` is then settled from the selected attempt — recording an attempt and
  settling the response are separate acts, so the verification loop can put best-attempt
  selection between the two.
- **Terminal-transition cleanup** (`AgentRunTerminalCleanup`, registered beside the executor
  when the runtime is enabled) — on every path that takes an execution to a terminal (a
  critical abort, a user cancel, the janitor's mark, and the successful finish), it releases
  the execution's [session bundles](#session-bundle-custody) and then reads only the
  agent-family blocks (through the family's `RunLedger` surface) of the execution's
  snapshot in a fresh scope — a projected, execution-scoped query, never a jsonb scan across
  the table — and settles the `RunPasses` rows still marked `Unacknowledged` that may still be
  spending: a null status (a run nobody watched settle) or a non-terminal one. Per run it calls
  `SettleAbandonedQuietlyAsync` with the run anchored to its own block, so the pack is
  collected and committed before any complete is sent — never a blind acknowledge, which would
  purge evidence nobody persisted. A settled run whose complete was withheld is deliberately
  left alone — completing it would purge the evidence pack the withhold rules preserved — and
  waits for the runtime's 24-hour retention reaper. Three call sites share the one service: the
  platform's terminal-write funnel (which the WebApi stuck-execution janitor,
  `StuckWorkflowExecutionJanitor` with config section
  `PlatformSettings:WorkflowExecutionJanitor`, marks through), the user-cancel API
  handler, which needs the win/lose result for its own error semantics, and the workflow
  engine's own completed-terminal writer, which reached no cleanup at all before.
- **The workflow editor's agent** (`EditorAgentConversationConsumer`, purpose `edchat`) — an
  [agent conversation](#agent-conversations) consumer. Each turn stages the editor's workflow as
  `workflow.json` (the export with test-run outputs, unsaved edits included), `outline.txt`,
  `dependency-map.json`, `versions.md` (the saved versions with their summaries) and
  `reports.md` (the organization's latest 30 runs of the workflow), the picked reports, and the
  `workflow-guide` skill with the workflow JSON Schema, a checker (`check_workflow.py`) and a
  lookup helper (`wf.py`). It bills under `LlmEditorAgentChat` with the execution context
  `WorkflowEditorAgent`. What users see: [Editor Agent](../../../features/editor-agent.md).

