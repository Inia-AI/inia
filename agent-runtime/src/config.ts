import { ConfigError, nonEmpty } from "./config-error.js";
import type { LogLevel } from "./logger.js";
import {
  assertModelIdForProvider,
  buildProviderSubprocessEnv,
  type ModelProvider,
  type ModelProviderConfig,
  parseModelProviderConfig,
  pinnedModelOf,
} from "./model-provider.js";

export type AuthConfig = { token: string } | { disabled: true };

export type BuildIdentity = { version: string; revision: string };

export type FuseBounds = { defaultValue: number; ceiling: number };

export type RunFuseConfig = {
  deadlineMs: FuseBounds;
  maxBudgetUsd: FuseBounds;
  maxTurns: FuseBounds;
  maxRetainedOutputBytes: FuseBounds;
};

export type ServerConfig = {
  port: number;
  auth: AuthConfig;
  modelProvider: ModelProviderConfig;
  bodyLimitBytes: number;
  inflightUploadBudgetBytes: number;
  retainedInputBudgetBytes: number;
  retainedOutputBudgetBytes: number;
  documentBudgetBytes: number;
  requestPhaseTimeoutMs: number;
  fuses: RunFuseConfig;
  pauseGraceMinutes: number;
  admissionClasses: AdmissionClassConfig;
  checkpointRoot: string;
  documentRoot: string;
  subprocessEnv: Record<string, string>;
  allowedModels: string[];
  pinnedModel: string;
  buildIdentity: BuildIdentity;
  logLevel: LogLevel;
};

export type AdmissionClassConfig = {
  chatReservedSlots: number;
  chatMaxSlots: number;
  chatBorrowHeadroomSlots: number;
};

export const MS_PER_MINUTE = 60_000;
export const MS_PER_SECOND = 1_000;

const DEFAULT_PORT = 8130;
// 200 MB document budget × 4/3 base64 inflation, plus prompt/JSON overhead.
export const DEFAULT_BODY_LIMIT_BYTES = 384 * 1024 * 1024;
const DEFAULT_REQUEST_PHASE_TIMEOUT_MS = 30_000;
export const RUN_DEADLINE_CEILING_MS = 30 * 60 * 1000;
export const RUN_BUDGET_CEILING_USD = 5;
export const RUN_TURNS_CEILING = 300;
// Deadline and budget hold their defaults below their ceilings (ADR 0117): the
// outlier workload that moved the ceilings is one block, and a run that declares
// nothing keeps the fuse sized to the population rather than to its tail.
export const DEFAULT_RUN_DEADLINE_MS = 15 * 60 * 1000;
export const DEFAULT_MAX_BUDGET_USD = 2.5;
export const DEFAULT_MAX_TURNS = RUN_TURNS_CEILING;
// A cross-module timing pair (ADR 0125): the child's post-deadline exit first waits out the
// SDK stream's bounded close, then publishes deliverables and serializes the terminal
// bundle; the supervisor's SIGKILL grace must contain all of it, plus however long
// workspace prep delayed the child's deadline arm relative to the supervisor's timer.
// config.test.ts proves the containment ordering.
export const DEADLINE_ITERATOR_CLOSE_TIMEOUT_MS = 2_000;
export const CHILD_GRACE_MS = 15_000;
export const WORKSPACE_ROOT = "/workspaces";
export const SHARED_TMP_ROOT = "/tmp";
export const DEFAULT_PAUSE_GRACE_MINUTES = 5;
// Zero reserve by default, so deploying the runtime changes nothing for workflow runs until an
// operator turns chat admission on together with the platform setting (ADR 0142 gives the
// recommended production values).
export const DEFAULT_CHAT_RESERVED_SLOTS = 0;
export const DEFAULT_CHAT_MAX_SLOTS = 8;
// Past its reserve, chat never borrows the last free slots, so a workflow caller coming back after
// its Retry-After finds at least this many slots chat did not take (ADR 0142). Workflow runs can
// still drop to the pool minus the chat cap while borrowed chat turns run.
export const DEFAULT_CHAT_BORROW_HEADROOM_SLOTS = 4;
export const DEFAULT_CHECKPOINT_ROOT = "/workspaces/checkpoints";
export const DEFAULT_DOCUMENT_ROOT = "/workspaces/documents";
const MAX_PAUSE_GRACE_MINUTES = 24 * 60;
// One supervisor hosts every concurrent run, so an uncapped frame lets one run OOM the rest.
// No frame carries document bytes, and the supervisor→child direction carries no bundle bytes
// either, so it is sized for prompts, answers and staged-input metadata alone. The
// child→supervisor direction keeps checkpoint scale: a pause or terminal bundle has no channel
// back but stdout and rides its frame as base64.
export const MAX_CHECKPOINT_BUNDLE_BYTES = 64 * 1024 * 1024;
// A supervisor restart takes the in-memory bundle ledger with it, so the files it described
// belong to nobody and no code would ever delete them. A live run is protected by its registry
// record, not by this age; the age is the second guard, sized off the deadline ceiling so a run
// admitted just before a restart is not reaped out from under a supervisor that has not yet
// heard of it.
export const CHECKPOINT_ORPHAN_MAX_AGE_MS = 2 * RUN_DEADLINE_CEILING_MS;
export const CHECKPOINT_REAP_INTERVAL_MS = 15 * 60 * 1000;
export const MAX_WIRE_FRAME_LENGTH = 32 * 1024 * 1024;
export const MAX_CHILD_FRAME_LENGTH = 96 * 1024 * 1024;
// Matches the platform's own upload ceiling; the entry cap stops a request
// amortizing the budget over a flood of files.
export const MAX_TOTAL_DOCUMENT_BYTES = 200 * 1024 * 1024;
export const MAX_DOCUMENTS_PER_RUN = 100;
// A skill's runnable auxiliary files. Counted per skill (the entry cap above bounds the skill
// count), while the bytes ride the one shared document budget, so assets widen no byte ceiling.
export const MAX_SKILL_ASSETS_PER_SKILL = 16;
// A continuation re-carries a member's approved out/ files. The ceiling sits
// deliberately between MAX_DOCUMENTS_PER_RUN (what the documents array admits; out/ paths
// are not documents, so that cap does not bind) and MAX_PUBLISHED_OUT_FILES (what a member
// may publish, sized for near-empty-file floods whose listing only ever crosses HTTP,
// while every re-carried path rides the supervisor-to-child frame). A member whose out/
// exceeds this cannot be re-carried; the platform degrades that leg to scratch and records
// the override, so the gap costs a redo mode, never a failure. Bytes are not capped here:
// they charge the shared document budget like every staged input.
export const MAX_RECARRIED_OUT_FILES = 1_000;
// Bounds each re-carried path at every parse boundary for the same reason
// MAX_DOCUMENT_META_FIELD_CHARS exists: the frame-cap proof's re-carry allowance is
// arithmetic over this bound, and an unbounded path would overflow the child's line
// splitter instead of 400ing at the HTTP boundary.
export const MAX_RECARRIED_OUT_PATH_CHARS = 512;
// Bounds a document's mediaType and documentClass at every parse boundary. Without it
// the frame-cap proof's metadata allowance would be enforced by nothing — an unbounded
// label could push a frame past MAX_WIRE_FRAME_LENGTH and fail the run at the child's
// line splitter instead of 400ing at the HTTP boundary.
export const MAX_DOCUMENT_META_FIELD_CHARS = 256;
// Sized to exactly one maximal decode pipeline (~1 GB resident: buffered body +
// parsed base64 + decoded bytes), not the sum of concurrent starts — a
// crossing start is refused before a byte is read. Files listings charge here
// too (FILES_LISTING_RESIDENT_BYTES).
export const MAX_INFLIGHT_UPLOAD_BYTES = DEFAULT_BODY_LIMIT_BYTES;
export const UNDECLARED_BODY_PROVISIONAL_BYTES = 64 * 1024;
// Three maximal runs' worth of `/workspaces` tmpfs; the refusal must land
// before the bytes do, or a rejected run's staged inputs hold the tmpfs for
// the whole 24h retention window.
export const MAX_RETAINED_INPUT_BYTES = 3 * MAX_TOTAL_DOCUMENT_BYTES;
export const MAX_PUBLISHED_OUT_BYTES = MAX_TOTAL_DOCUMENT_BYTES;
// The byte cap bounds bytes, not entries: hundreds of thousands of near-empty
// files sit comfortably under it while their listing alone reaches tens of MB
// serialized inside the shared supervisor.
export const MAX_PUBLISHED_OUT_FILES = 10_000;
// Measured on the **html-inert rendering** the manifest is served as, never the
// raw JSON: `<`, `>` and `&` are legal in a filename and each leaves this
// process as a six-byte `\uXXXX` escape, so a raw measure under-counts up to
// six-fold. Re-checked when the supervisor reads a manifest back, since the
// attempt dir belongs to the run's uid.
export const MAX_ARTIFACTS_MANIFEST_BYTES = 8 * 1024 * 1024;
// A listing entry is a manifest entry plus the kind the manifest does not
// carry; `artifact` is the longer of the two kinds.
export const LISTING_KIND_FIELD_BYTES = ',"kind":"artifact"'.length;
// Trace paths are runtime-generated (`trace.ndjson`, one `sub/<uuid>.jsonl` per
// sub-agent, none escapable), so a whole pack's entries fit here many times over.
export const TRACE_LISTING_ALLOWANCE_BYTES = 4 * 1024 * 1024;
// Derived from the same rendering publish's manifest cap is measured on: the
// only growth from a sanctioned manifest to this listing is one kind field per
// entry plus the trace pack.
export const MAX_FILES_LISTING_BYTES =
  MAX_ARTIFACTS_MANIFEST_BYTES +
  MAX_PUBLISHED_OUT_FILES * LISTING_KIND_FIELD_BYTES +
  TRACE_LISTING_ALLOWANCE_BYTES;
// Charges the peak resident copies of one listing (manifest bytes, ~2x parsed
// entries, serialized + escaped renderings) rather than the final response
// size — sizing to output would admit listings the supervisor can't hold.
export const FILES_LISTING_RESIDENT_BYTES = 3 * MAX_ARTIFACTS_MANIFEST_BYTES + 2 * MAX_FILES_LISTING_BYTES;
export const TRACE_PACK_ALLOWANCE_BYTES = 64 * 1024 * 1024;
export const RETAINED_OUTPUT_WORST_CASE_BYTES = MAX_PUBLISHED_OUT_BYTES + TRACE_PACK_ALLOWANCE_BYTES;
export const MAX_RETAINED_OUTPUT_BYTES = 3 * RETAINED_OUTPUT_WORST_CASE_BYTES;
// Sized so the whole uid pool fits inside the output budget (budget / default
// >= pool size, pinned by test) — the pool is meant to bind first, not this.
// Terminal session bundles sit outside this arithmetic: each finished run may
// hold up to MAX_CHECKPOINT_BUNDLE_BYTES more until complete/reap, charged as
// its own reservation and shed — never the run failed — when the budget cannot
// afford it (ADR 0113).
export const DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES = 24 * 1024 * 1024;
// An answer becomes the next prompt verbatim; capped so the resume frame's
// arithmetic (checkpoint + staged-input metadata + answer) provably fits one frame.
export const MAX_ANSWER_CHARS = 1024 * 1024;
// Shares the answer's cap: a prompt that passed the route's 413 could still
// overflow the child's frame splitter without its own bound.
export const MAX_PROMPT_CHARS = MAX_ANSWER_CHARS;
// Enforced on the serialized value, so the block-array variant's envelope is
// bounded too, not just its text.
export const MAX_SYSTEM_PROMPT_CHARS = MAX_PROMPT_CHARS;
// Under the SDK's per-skill listing truncation (skillListingMaxDescChars,
// default 1536) with margin for accounting we don't control (escaping, an
// ellipsis, the name line), so an accepted description is never silently cut
// out of the model's view. The SDK also squeezes the whole listing to 1% of
// the context window (skillListingBudgetFraction), which no per-description
// cap can guard against.
export const MAX_SKILL_DESCRIPTION_CHARS = 1400;
// Caps counted in JS string length need a byte body cap carrying the worst-case
// UTF-8 expansion, or the cap is unreachable in a non-Latin script: a BMP
// CJK/Cyrillic/Greek character is one JS char and three bytes.
export const MAX_UTF8_BYTES_PER_JS_CHAR = 3;
export const RUN_INPUT_BODY_LIMIT_BYTES = 4 * 1024 * 1024;
export const MAX_OUTPUT_FORMAT_CHARS = 256 * 1024;
// Pinned to header-safe printable ASCII at a JWT-scale length so a malformed
// runToken 400s at the boundary instead of surfacing as mcp_connect_failed
// after a spawn.
export const MAX_RUN_TOKEN_CHARS = 4096;
// Only the tail ever matters (it rides the exit report's message); unbounded
// accumulation would let a crash-looping child grow supervisor heap for the
// run's whole deadline.
export const MAX_CHILD_STDERR_TAIL_BYTES = 16 * 1024;
// Measured floor: a bare runtime process is 6 tasks and aborts below that.
// Deployed headroom assumes thread pools sizing from host CPU count rather than
// cgroup quota (numpy/OpenBLAS, this runtime's own pool) — a measured base plus
// two host-sized pools at the deployed vCPU count.
export const RUN_CHILD_TASK_LIMIT = 128;
export const RUN_CHILD_OOM_SCORE_ADJ = 500;
export const UID_POOL_BASE = 20001;
// Mirrors the deployed mem_limit, which the deployment sets (outside
// this repo's reach), so the pairing test can refuse to let the pool and the
// memory budget drift apart.
export const PRODUCTION_MEM_LIMIT_MIB = 12288;
// Production at 4 GiB fed ~23 children before the cgroup OOM killer fired,
// ≈178 MiB effective each within the observed 150–250 MB band; 256 MiB budgets
// the top of that band with headroom.
export const RUN_CHILD_MEMORY_BUDGET_MIB = 256;
// The supervisor's ~1 GiB decode transient plus the budgeted retained tmpfs
// bytes (inputs, outputs, checkpoints) and baseline RSS.
export const SUPERVISOR_MEMORY_RESERVE_MIB = 4096;
export const UID_POOL_SIZE = Math.floor(
  (PRODUCTION_MEM_LIMIT_MIB - SUPERVISOR_MEMORY_RESERVE_MIB) / RUN_CHILD_MEMORY_BUDGET_MIB,
);
// Tasks the supervisor keeps for itself — including the kill and sweep spawns
// that must still run when every pool uid sits at its cap.
export const SUPERVISOR_TASK_RESERVE = 256;
// Below this, runs at their task cap starve the supervisor that kills them —
// the outcome the per-uid cap exists to prevent.
export const REQUIRED_PIDS_LIMIT = SUPERVISOR_TASK_RESERVE + UID_POOL_SIZE * RUN_CHILD_TASK_LIMIT;

export const UNKNOWN_BUILD_FIELD = "unknown";
const MAX_BUILD_FIELD_CHARS = 64;
const BUILD_FIELD_PATTERN = /^[0-9A-Za-z._+-]+$/;

const REJECTED_VALUE_PREVIEW_CHARS = 48;

function previewRejectedValue(value: string): string {
  const clipped =
    value.length > REJECTED_VALUE_PREVIEW_CHARS ? `${value.slice(0, REJECTED_VALUE_PREVIEW_CHARS)}…` : value;
  return JSON.stringify(clipped);
}

function parseBuildField(raw: string | undefined, name: string): string {
  const value = raw?.trim() ?? "";
  if (value === "") {
    return UNKNOWN_BUILD_FIELD;
  }
  if (value.length > MAX_BUILD_FIELD_CHARS) {
    throw new ConfigError(
      `${name} must be at most ${MAX_BUILD_FIELD_CHARS} characters (got ${value.length}): ${previewRejectedValue(value)}`,
    );
  }
  if (!BUILD_FIELD_PATTERN.test(value)) {
    throw new ConfigError(
      `${name} contains characters outside [0-9A-Za-z._+-]: ${previewRejectedValue(value)}`,
    );
  }
  return value;
}

export function parseBuildIdentity(version: string | undefined, revision: string | undefined): BuildIdentity {
  return {
    version: parseBuildField(version, "AGENT_RUNTIME_VERSION"),
    revision: parseBuildField(revision, "AGENT_RUNTIME_REVISION"),
  };
}

export function parseLogLevel(raw: string | undefined): LogLevel {
  const value = nonEmpty(raw);
  if (value === undefined) {
    return "info";
  }
  switch (value) {
    case "debug":
    case "info":
    case "warn":
    case "error":
      return value;
    default:
      throw new ConfigError(`LOG_LEVEL must be one of debug, info, warn, error, got "${raw}"`);
  }
}

export function parsePort(raw: string | undefined): number {
  if (raw === undefined || raw === "") {
    return DEFAULT_PORT;
  }
  const port = Number(raw);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new ConfigError(`PORT must be an integer between 1 and 65535, got "${raw}"`);
  }
  return port;
}

export function parseAuth(token: string | undefined, disabledFlag: string | undefined): AuthConfig {
  const disabled = disabledFlag === "true";
  if (token !== undefined && token !== "" && !disabled) {
    return { token };
  }
  if (disabled && (token === undefined || token === "")) {
    return { disabled: true };
  }
  throw new ConfigError(
    "auth is fail-closed: set AUTH_TOKEN, or set AUTH_DISABLED=true explicitly (never both)",
  );
}

// Bounded by the built-in default, not by the ceiling: a knob that could raise a
// spend limit is a knob a typo can bill you with (ADR 0054), and a deployment that
// wants a *higher* fleet-wide default wants its own named variable rather than this
// one (ADR 0117). Range-style knobs (pause grace, port) are deliberately not
// tighten-only fuses; keep them out of this parser.
function parseTightenOnlyFuseDefault(
  raw: string | undefined,
  fuse: { envName: string; builtInDefault: number; describe: string; integer: boolean },
): number {
  if (raw === undefined || raw === "") {
    return fuse.builtInDefault;
  }
  const value = Number(raw);
  const wellFormed = fuse.integer ? Number.isInteger(value) : Number.isFinite(value);
  if (!wellFormed || value <= 0 || value > fuse.builtInDefault) {
    throw new ConfigError(
      `${fuse.envName} sets this fuse's default for runs that declare nothing and may only tighten ` +
        `it: it must be a positive ${fuse.describe} no greater than the built-in default of ` +
        `${fuse.builtInDefault}, got "${raw}"`,
    );
  }
  return value;
}

export function parseMaxBudgetUsd(raw: string | undefined): number {
  return parseTightenOnlyFuseDefault(raw, {
    envName: "AGENT_RUNTIME_MAX_BUDGET_USD",
    builtInDefault: DEFAULT_MAX_BUDGET_USD,
    describe: "number",
    integer: false,
  });
}

export function parseRunDeadlineMs(raw: string | undefined): number {
  return parseTightenOnlyFuseDefault(raw, {
    envName: "AGENT_RUNTIME_RUN_DEADLINE_MS",
    builtInDefault: DEFAULT_RUN_DEADLINE_MS,
    describe: "whole number of milliseconds",
    integer: true,
  });
}

export function parseMaxTurns(raw: string | undefined): number {
  return parseTightenOnlyFuseDefault(raw, {
    envName: "AGENT_RUNTIME_MAX_TURNS",
    builtInDefault: DEFAULT_MAX_TURNS,
    describe: "whole number of turns",
    integer: true,
  });
}

export function parsePauseGraceMinutes(raw: string | undefined): number {
  if (raw === undefined || raw === "") {
    return DEFAULT_PAUSE_GRACE_MINUTES;
  }
  const minutes = Number(raw);
  if (!Number.isInteger(minutes) || minutes < 0 || minutes > MAX_PAUSE_GRACE_MINUTES) {
    throw new ConfigError(
      "AGENT_RUNTIME_PAUSE_GRACE_MINUTES must be a whole number of minutes between 0 and " +
        `${MAX_PAUSE_GRACE_MINUTES}, got "${raw}"`,
    );
  }
  return minutes;
}

function parseSlotCount(envName: string, raw: string | undefined, fallback: number): number {
  if (raw === undefined || raw === "") {
    return fallback;
  }
  const slots = Number(raw);
  if (!Number.isInteger(slots) || slots < 0 || slots > UID_POOL_SIZE) {
    throw new ConfigError(
      `${envName} must be a whole number of slots between 0 and ${UID_POOL_SIZE}, got "${raw}"`,
    );
  }
  return slots;
}

export function parseAdmissionClassConfig(
  rawReserved: string | undefined,
  rawMax: string | undefined,
  rawHeadroom: string | undefined,
): AdmissionClassConfig {
  const chatReservedSlots = parseSlotCount(
    "AGENT_RUNTIME_CHAT_RESERVED_SLOTS",
    rawReserved,
    DEFAULT_CHAT_RESERVED_SLOTS,
  );
  const chatMaxSlots = parseSlotCount("AGENT_RUNTIME_CHAT_MAX_SLOTS", rawMax, DEFAULT_CHAT_MAX_SLOTS);
  if (chatReservedSlots >= UID_POOL_SIZE) {
    throw new ConfigError(
      `AGENT_RUNTIME_CHAT_RESERVED_SLOTS must leave workflow runs at least one of the ${UID_POOL_SIZE} ` +
        `slots, got ${chatReservedSlots}`,
    );
  }
  if (chatMaxSlots < chatReservedSlots) {
    throw new ConfigError(
      `AGENT_RUNTIME_CHAT_MAX_SLOTS must be at least AGENT_RUNTIME_CHAT_RESERVED_SLOTS ` +
        `(${chatReservedSlots}), got ${chatMaxSlots}`,
    );
  }
  const chatBorrowHeadroomSlots = parseSlotCount(
    "AGENT_RUNTIME_CHAT_BORROW_HEADROOM_SLOTS",
    rawHeadroom,
    DEFAULT_CHAT_BORROW_HEADROOM_SLOTS,
  );
  if (chatBorrowHeadroomSlots >= UID_POOL_SIZE) {
    throw new ConfigError(
      `AGENT_RUNTIME_CHAT_BORROW_HEADROOM_SLOTS must be below the ${UID_POOL_SIZE}-slot pool, or chat ` +
        `could never borrow and would be told the runtime is busy forever, got ${chatBorrowHeadroomSlots}`,
    );
  }
  return { chatReservedSlots, chatMaxSlots, chatBorrowHeadroomSlots };
}

// The `..`/`.` guard stands in for lexical normalization, since this module
// carries no imports on purpose. Deliberately *lexical*, not `realpath`: an
// operator who symlinks a root outside the veil would pass it, accepted
// because roots are operator config, not run-controlled.
function parseAbsoluteRoot(raw: string | undefined, varName: string, fallback: string): string {
  if (raw === undefined || raw === "") {
    return fallback;
  }
  if (!raw.startsWith("/")) {
    throw new ConfigError(`${varName} must be an absolute path, got "${raw}"`);
  }
  if (raw.split("/").some((segment) => segment === ".." || segment === ".")) {
    throw new ConfigError(`${varName} must be a normalized path with no "." or ".." segments, got "${raw}"`);
  }
  assertUnderSandboxDeniedRoot(raw, varName);
  return raw;
}

export function parseCheckpointRoot(raw: string | undefined): string {
  return parseAbsoluteRoot(raw, "AGENT_RUNTIME_CHECKPOINT_ROOT", DEFAULT_CHECKPOINT_ROOT);
}

export function parseDocumentRoot(raw: string | undefined): string {
  return parseAbsoluteRoot(raw, "AGENT_RUNTIME_DOCUMENT_ROOT", DEFAULT_DOCUMENT_ROOT);
}

export const SANDBOX_DENIED_READ_ROOTS: readonly string[] = [
  "/app",
  "/home/bun",
  "/root",
  WORKSPACE_ROOT,
  SHARED_TMP_ROOT,
];

// Necessary, not sufficient: a subtree of a denied root can still be carved back out by an
// explicit allowRead re-bind (the run workspace, the skill-assets dir — ADR 0116), so what
// keeps the mirror and the private HOME invisible is that no such re-bind ever names them.
export function assertUnderSandboxDeniedRoot(path: string, label: string): void {
  const contained = SANDBOX_DENIED_READ_ROOTS.some((root) => path === root || path.startsWith(`${root}/`));
  if (!contained) {
    throw new ConfigError(
      `${label} must sit under a sandbox-denied read root (${SANDBOX_DENIED_READ_ROOTS.join(", ")}), ` +
        `got "${path}" — anywhere else is reachable from sandboxed Bash`,
    );
  }
}

export function parseAllowedModels(raw: string | undefined, pinnedModel: string): string[] {
  if (raw === undefined || raw === "") {
    return [pinnedModel];
  }
  const models = raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry !== "");
  if (models.length === 0) {
    throw new ConfigError(`ANTHROPIC_ALLOWED_MODELS must name at least one model when set, got "${raw}"`);
  }
  if (!models.includes(pinnedModel)) {
    throw new ConfigError(
      "ANTHROPIC_ALLOWED_MODELS must contain the pinned ANTHROPIC_MODEL — the default model must always be allowed",
    );
  }
  return models;
}

function parseAllowedModelsForProvider(
  provider: ModelProvider,
  raw: string | undefined,
  pinnedModel: string,
): string[] {
  const allowedModels = parseAllowedModels(raw, pinnedModel);
  for (const model of allowedModels) {
    assertModelIdForProvider(provider, model, "ANTHROPIC_ALLOWED_MODELS");
  }
  return allowedModels;
}

export function loadServerConfig(env: Record<string, string | undefined>): ServerConfig {
  const modelProvider = parseModelProviderConfig(env);
  if (modelProvider.provider === "bedrock" && "credentialsFile" in modelProvider.identity) {
    assertUnderSandboxDeniedRoot(modelProvider.identity.credentialsFile, "AGENT_RUNTIME_CREDENTIALS_FILE");
  }
  const pinnedModel = pinnedModelOf(modelProvider.provider, env);
  return {
    port: parsePort(env["PORT"]),
    auth: parseAuth(env["AUTH_TOKEN"], env["AUTH_DISABLED"]),
    modelProvider,
    bodyLimitBytes: DEFAULT_BODY_LIMIT_BYTES,
    inflightUploadBudgetBytes: MAX_INFLIGHT_UPLOAD_BYTES,
    retainedInputBudgetBytes: MAX_RETAINED_INPUT_BYTES,
    retainedOutputBudgetBytes: MAX_RETAINED_OUTPUT_BYTES,
    documentBudgetBytes: MAX_TOTAL_DOCUMENT_BYTES,
    requestPhaseTimeoutMs: DEFAULT_REQUEST_PHASE_TIMEOUT_MS,
    fuses: {
      deadlineMs: {
        defaultValue: parseRunDeadlineMs(env["AGENT_RUNTIME_RUN_DEADLINE_MS"]),
        ceiling: RUN_DEADLINE_CEILING_MS,
      },
      maxBudgetUsd: {
        defaultValue: parseMaxBudgetUsd(env["AGENT_RUNTIME_MAX_BUDGET_USD"]),
        ceiling: RUN_BUDGET_CEILING_USD,
      },
      maxTurns: {
        defaultValue: parseMaxTurns(env["AGENT_RUNTIME_MAX_TURNS"]),
        ceiling: RUN_TURNS_CEILING,
      },
      maxRetainedOutputBytes: {
        defaultValue: DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
        ceiling: RETAINED_OUTPUT_WORST_CASE_BYTES,
      },
    },
    pauseGraceMinutes: parsePauseGraceMinutes(env["AGENT_RUNTIME_PAUSE_GRACE_MINUTES"]),
    admissionClasses: parseAdmissionClassConfig(
      env["AGENT_RUNTIME_CHAT_RESERVED_SLOTS"],
      env["AGENT_RUNTIME_CHAT_MAX_SLOTS"],
      env["AGENT_RUNTIME_CHAT_BORROW_HEADROOM_SLOTS"],
    ),
    checkpointRoot: parseCheckpointRoot(env["AGENT_RUNTIME_CHECKPOINT_ROOT"]),
    documentRoot: parseDocumentRoot(env["AGENT_RUNTIME_DOCUMENT_ROOT"]),
    subprocessEnv: buildProviderSubprocessEnv(modelProvider.provider, env),
    allowedModels: parseAllowedModelsForProvider(
      modelProvider.provider,
      env["ANTHROPIC_ALLOWED_MODELS"],
      pinnedModel,
    ),
    pinnedModel,
    buildIdentity: parseBuildIdentity(env["AGENT_RUNTIME_VERSION"], env["AGENT_RUNTIME_REVISION"]),
    logLevel: parseLogLevel(env["LOG_LEVEL"]),
  };
}
