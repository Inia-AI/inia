import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { join } from "node:path";
import {
  type AdmissionClass,
  type AdmissionDecision,
  type AdmissionQueue,
  createDurationEstimator,
  DEFAULT_ADMISSION_CLASS,
  jitteredRetryAfterSeconds,
  MAX_ADMISSION_WAIT_MS,
  parseAdmissionClass,
} from "./admission-queue.js";
import { type ChargeBudget, createByteBudget, instrumentByteBudget } from "./byte-budget.js";
import { type CheckpointStore, createLocalCheckpointStore } from "./checkpoint-store.js";
import {
  CHECKPOINT_ORPHAN_MAX_AGE_MS,
  CHECKPOINT_REAP_INTERVAL_MS,
  MAX_ANSWER_CHARS,
  MAX_CHECKPOINT_BUNDLE_BYTES,
  RUN_INPUT_BODY_LIMIT_BYTES,
  type RunFuseConfig,
  type ServerConfig,
  WORKSPACE_ROOT,
} from "./config.js";
import { createLocalDocumentStore, type DocumentStore } from "./document-store.js";
import { RUNTIME_ENVIRONMENT_SECTION } from "./environment-section.js";
import { hasErrorCode, toError } from "./errors.js";
import {
  ADMISSION_SHED_REASON,
  AUTH_REJECTION_LOG_WINDOW_MS,
  BEARER_CHALLENGE_HEADERS,
  BodyLimitError,
  bearerTokenMatches,
  bodyReadBudgetMs,
  CapacityError,
  type CapacityReason,
  CHAT_DISABLED_REASON,
  createPhaseTimers,
  createStallGuard,
  createWindowedCounter,
  declaredContentLength,
  decodePathSegments,
  executionIdOf,
  HANDLER_PROCESSING_BUDGET_MS,
  HandlerDeadlineError,
  INFLIGHT_BUDGET_REASONS,
  INTERNAL_ERROR_BODY,
  NOT_FOUND_BODY,
  readJsonBody,
  sendJson,
  socketAbandonment,
  UnsupportedMediaTypeError,
  UPLOAD_BUDGET_REASON,
  uploadAdmissionChargeOf,
  withHandlerDeadline,
} from "./http-transport.js";
import { createLogger, type Logger } from "./logger.js";
import {
  createEmfMetrics,
  createGuardedMetrics,
  emitRuntimeGauges,
  GAUGE_SAMPLE_INTERVAL_MS,
  type Metrics,
} from "./metrics.js";
import { createNarrower, type Narrower, withNullsAsAbsent } from "./narrow.js";
import { parseRunAgentPayloads, type ResolvedRunAgent, resolveRunAgents } from "./run-agents.js";
import { createRunDataLedger } from "./run-data-ledger.js";
import {
  createDocumentBudget,
  decodeBoundedBase64,
  decodeRunDocuments,
  parseRecarriedOutPayloads,
  parseRequiredSha256,
  parseRunDocumentPayloads,
  parseSkillFilePayloads,
  RunDocumentBudgetError,
} from "./run-documents.js";
import { createRunFilesView, type RunFilesView } from "./run-files.js";
import { createRunFilesRoutes } from "./run-files-routes.js";
import {
  type AdmitRunOutcome,
  createRunOrchestrator,
  type StartRunContinuation,
  type StartRunRequest,
} from "./run-orchestrator.js";
import { isRetryableFailure, isTerminalStatus, type RunMonitorView, type RunStatus } from "./run-registry.js";
import {
  type PauseResolution,
  parseMcpServerUrl,
  parseRunEffort,
  parseRunOutputFormat,
  parseRunReportParams,
  parseRunSystemPrompt,
  parseRunToken,
  ValidationError,
} from "./run-report.js";
import type { SpawnRunFn } from "./run-spawner.js";
import { createPoolUidTreeRemover } from "./setpriv.js";
import { assertUniqueSkillIdentities, qualifiedSkillName } from "./skill-plugin.js";
import type { UidPool } from "./uid-pool.js";
import { ARTIFACTS_DIR_NAME, attemptRunDirAccess, TRACES_DIR_NAME } from "./workspace-layout.js";

export { bodyReadBudgetMs, socketAbandonment } from "./http-transport.js";

export type RunApiServer = Server & { drainRuns: () => number };

export type ServerDeps = {
  // Typed as the spawner's contract (not RunReportFn) so spawner-only options can't silently vanish.
  runReportFn: SpawnRunFn;
  uidPool: UidPool;
  now?: () => number;
  checkpointStore?: CheckpointStore;
  documentStore?: DocumentStore;
  runFiles?: RunFilesView;
  admission?: AdmissionQueue;
  logger?: Logger;
  metrics?: Metrics;
  gaugeIntervalMs?: number;
  checkpointReapIntervalMs?: number;
  checkpointOrphanMaxAgeMs?: number;
  handlerProcessingBudgetMs?: number;
  random?: () => number;
};

// Bounded by construction: one name per route pattern, never a raw path or an executionId.
type RouteName =
  | "healthz"
  | "limits"
  | "environment"
  | "runs_start"
  | "run_monitor"
  | "run_cancel"
  | "run_complete"
  | "run_input"
  | "run_files"
  | "run_checkpoint"
  | "unresolved";

// Which fault produced the failure. The deliberate 5xx paths (capacity shedding, draining) carry
// their own metrics and are absent here by design — ADR 0131.
type RequestFailureCause = "unhandled" | "handler_deadline" | "persist_failed" | "stream_failed";

const ACTIVE_RETRY_AFTER_SECONDS = "2";
const AWAITING_INPUT_RETRY_AFTER_SECONDS = "15";
const CAPACITY_RETRY_BASELINE_MS = 5_000;

const RUNTIME_SHUTDOWN_FAILURE = { subtype: "runtime_shutdown", estimatedCostUsd: 0 } as const;

function admitEverything(uidPool: UidPool): AdmissionQueue {
  return {
    admit: () => Promise.resolve({ outcome: "admitted" }),
    recordRunDuration: () => {},
    notifySlotFreed: () => {},
    settleAdmission: () => {},
    waitingCount: () => 0,
    chatSlotsHeld: () => 0,
    workflowSlotsFree: () => uidPool.available(),
    releaseChatSlot: () => {},
    drain: () => {},
    estimatedWaitMs: () => 0,
  };
}

const parse: Narrower = createNarrower((message) => new ValidationError(message));

export const CONFLICT_CODES = [
  "not_awaiting_input",
  "not_completable",
  "payload_conflict",
  "stale_resume_token",
  "unknown_question",
] as const;

type ConflictCode = (typeof CONFLICT_CODES)[number];

const API_VERSION_SEGMENT = "v1";

function sendConflict(res: ServerResponse, code: ConflictCode, status?: RunStatus): void {
  sendJson(res, 409, { code, ...(status === undefined ? {} : { status }) });
}

export function monitorPollHeaders(status: RunStatus): Record<string, string> {
  if (isTerminalStatus(status)) {
    return {};
  }
  return {
    "retry-after":
      status === "awaiting_input" ? AWAITING_INPUT_RETRY_AFTER_SECONDS : ACTIVE_RETRY_AFTER_SECONDS,
  };
}

// Keyed over RunFuseConfig's own fuse names so a typo'd unit or a missing/extra fuse is a compile error.
type FuseUnit = "milliseconds" | "usd" | "turns" | "bytes";
type FuseLimitView = { default: number; ceiling: number; unit: FuseUnit };
export type RunLimitsView = Record<keyof RunFuseConfig, FuseLimitView>;

export function buildLimitsView(fuses: RunFuseConfig): RunLimitsView {
  return {
    deadlineMs: {
      default: fuses.deadlineMs.defaultValue,
      ceiling: fuses.deadlineMs.ceiling,
      unit: "milliseconds",
    },
    maxBudgetUsd: {
      default: fuses.maxBudgetUsd.defaultValue,
      ceiling: fuses.maxBudgetUsd.ceiling,
      unit: "usd",
    },
    maxTurns: {
      default: fuses.maxTurns.defaultValue,
      ceiling: fuses.maxTurns.ceiling,
      unit: "turns",
    },
    maxRetainedOutputBytes: {
      default: fuses.maxRetainedOutputBytes.defaultValue,
      ceiling: fuses.maxRetainedOutputBytes.ceiling,
      unit: "bytes",
    },
  };
}

const START_RUN_FIELDS: readonly string[] = [
  "executionId",
  "prompt",
  "runToken",
  "continuation",
  "documents",
  "skillFiles",
  "agents",
  "model",
  "outputFormat",
  "systemPrompt",
  "mcpServerUrl",
  "maxTurns",
  "maxBudgetUsd",
  "deadlineMs",
  "effort",
  "maxRetainedOutputBytes",
  "admissionClass",
];

const CONTINUATION_FIELDS: readonly string[] = ["sessionBundle", "outFiles"];

const SESSION_BUNDLE_FIELDS: readonly string[] = ["sha256", "bytesBase64"];

// Everything here refuses with 400 before admission: an undescribable workspace never reaches a
// child. Every fuse field is declared fresh exactly as a scratch start declares it.
async function parseContinuationRequest(value: unknown, budget: ChargeBudget): Promise<StartRunContinuation> {
  const raw = parse.asRecord(value, "continuation");
  parse.rejectUnknownFields(raw, CONTINUATION_FIELDS, "continuation");
  const record = withNullsAsAbsent(raw);
  const bundleRaw = parse.asRecord(record["sessionBundle"], "continuation.sessionBundle");
  parse.rejectUnknownFields(bundleRaw, SESSION_BUNDLE_FIELDS, "continuation.sessionBundle");
  const declaredSha256 = parseRequiredSha256(bundleRaw, parse, "continuation.sessionBundle");
  // Structural refusals cost nothing; the ≤64 MiB bundle decode runs only after every one
  // of them has passed.
  const outPayloads = parseRecarriedOutPayloads(record["outFiles"], parse);
  const { bytes, sha256 } = await decodeBoundedBase64({
    contentBase64: parse.requireString(bundleRaw, "bytesBase64", "continuation.sessionBundle"),
    label: "continuation.sessionBundle.bytesBase64",
    maxBytes: MAX_CHECKPOINT_BUNDLE_BYTES,
    parse,
  });
  if (sha256 !== declaredSha256) {
    parse.fail("continuation.sessionBundle bytes do not match the declared sha256");
  }
  const outFiles = await decodeRunDocuments(outPayloads, parse, budget, "re-carried out/ file");
  return { checkpoint: { bytes, sha256 }, outFiles };
}

function parseRequestedFuse(
  record: Record<string, unknown>,
  key: string,
  ceiling: number,
  unit: "integer" | "number",
): number | undefined {
  const value = record[key];
  if (value === undefined) {
    return undefined;
  }
  const wellFormed =
    typeof value === "number" &&
    Number.isFinite(value) &&
    value > 0 &&
    (unit === "number" || Number.isInteger(value));
  if (!wellFormed) {
    throw new ValidationError(
      `${key} must be a positive ${unit === "integer" ? "whole " : ""}number when present`,
    );
  }
  if (value > ceiling) {
    throw new ValidationError(
      `${key} ${value} is above this runtime's ceiling of ${ceiling} — ` +
        "a request may tighten a fuse, never widen it",
    );
  }
  return value;
}

function parseMaxRetainedOutputBytes(record: Record<string, unknown>, ceiling: number): number | undefined {
  if (record["maxRetainedOutputBytes"] === undefined) {
    return undefined;
  }
  const value = parse.requirePositiveNumber(record, "maxRetainedOutputBytes");
  if (!Number.isInteger(value) || value > ceiling) {
    parse.fail(`maxRetainedOutputBytes must be a whole number of bytes no greater than ${ceiling}`);
  }
  return value;
}

function parseRequestedModel(record: Record<string, unknown>, allowedModels: string[]): string | undefined {
  const model = parse.optionalString(record, "model");
  if (model !== undefined && !allowedModels.includes(model)) {
    throw new ValidationError(
      `model "${model}" is not allowed on this runtime; allowed models: ${allowedModels.join(", ")}`,
    );
  }
  return model;
}

async function parseStartRunRequest(
  body: unknown,
  config: Pick<ServerConfig, "documentBudgetBytes" | "allowedModels" | "fuses">,
): Promise<StartRunRequest> {
  const raw = parse.asRecord(body, "request body");
  parse.rejectUnknownFields(raw, START_RUN_FIELDS);
  const record = withNullsAsAbsent(raw);
  const { executionId, prompt } = parseRunReportParams({
    executionId: record["executionId"],
    prompt: record["prompt"],
  });
  const rawRunToken = record["runToken"] ?? null;
  const runToken = rawRunToken === null ? null : parseRunToken(rawRunToken, parse, "runToken");
  const model = parseRequestedModel(record, config.allowedModels);
  const outputFormat = parse.optionalWith(record, "outputFormat", parseRunOutputFormat);
  const systemPrompt = parse.optionalWith(record, "systemPrompt", parseRunSystemPrompt);
  const mcpServerUrl = parse.optionalWith(record, "mcpServerUrl", parseMcpServerUrl);
  const maxTurns = parseRequestedFuse(record, "maxTurns", config.fuses.maxTurns.ceiling, "integer");
  const maxBudgetUsd = parseRequestedFuse(
    record,
    "maxBudgetUsd",
    config.fuses.maxBudgetUsd.ceiling,
    "number",
  );
  const deadlineMs = parseRequestedFuse(record, "deadlineMs", config.fuses.deadlineMs.ceiling, "integer");
  const effort = parse.optionalWith(record, "effort", parseRunEffort);
  const admissionClass = parse.optionalWith(record, "admissionClass", parseAdmissionClass);
  const maxRetainedOutputBytes = parseMaxRetainedOutputBytes(
    record,
    config.fuses.maxRetainedOutputBytes.ceiling,
  );
  const budget = createDocumentBudget(config.documentBudgetBytes);
  const documents =
    record["documents"] === undefined
      ? []
      : await decodeRunDocuments(parseRunDocumentPayloads(record["documents"], parse), parse, budget);
  const skillFiles =
    record["skillFiles"] === undefined
      ? []
      : await decodeRunDocuments(parseSkillFilePayloads(record["skillFiles"], parse), parse, budget);
  const continuation =
    record["continuation"] === undefined
      ? undefined
      : await parseContinuationRequest(record["continuation"], budget);
  const rawAgents =
    record["agents"] === undefined ? undefined : parseRunAgentPayloads(record["agents"], parse);
  let agents: ResolvedRunAgent[] | undefined;
  if (rawAgents !== undefined) {
    const stagedQualifiedNames = assertUniqueSkillIdentities(skillFiles, (problem) => {
      throw new ValidationError(
        problem.kind === "no-identity"
          ? `skillFiles[${problem.index}].name "${problem.name}" keeps no skill identity`
          : `skillFiles[${problem.index}].name "${problem.name}" collides with "${problem.priorName}"`,
      );
    }).map(({ identity }) => qualifiedSkillName(identity));
    agents = resolveRunAgents(rawAgents, stagedQualifiedNames, (message) => {
      throw new ValidationError(message);
    });
  }
  return {
    executionId,
    prompt,
    runToken,
    documents,
    skillFiles,
    ...(continuation === undefined ? {} : { continuation }),
    ...(agents === undefined ? {} : { agents }),
    ...(model === undefined ? {} : { model }),
    ...(outputFormat === undefined ? {} : { outputFormat }),
    ...(systemPrompt === undefined ? {} : { systemPrompt }),
    ...(mcpServerUrl === undefined ? {} : { mcpServerUrl }),
    ...(maxTurns === undefined ? {} : { maxTurns }),
    ...(maxBudgetUsd === undefined ? {} : { maxBudgetUsd }),
    ...(deadlineMs === undefined ? {} : { deadlineMs }),
    ...(effort === undefined ? {} : { effort }),
    ...(maxRetainedOutputBytes === undefined ? {} : { maxRetainedOutputBytes }),
    ...(admissionClass === undefined ? {} : { admissionClass }),
  };
}

type RunInputRequest = {
  resumeToken: string;
  questionId: string;
  resolution: PauseResolution;
};

const RUN_INPUT_FIELDS: readonly string[] = ["resumeToken", "questionId", "answer", "allowFurtherPause"];

function parseRunInputRequest(body: unknown): RunInputRequest {
  const raw = parse.asRecord(body, "request body");
  parse.rejectUnknownFields(raw, RUN_INPUT_FIELDS);
  const record = withNullsAsAbsent(raw);
  const allowFurtherPause = parse.optionalBoolean(record, "allowFurtherPause") ?? true;
  const answer = parse.requirePossiblyEmptyString(record, "answer");
  if (answer.length > MAX_ANSWER_CHARS) {
    throw new ValidationError(`answer must not exceed ${MAX_ANSWER_CHARS} characters`);
  }
  return {
    resumeToken: parse.requireString(record, "resumeToken"),
    questionId: parse.requireString(record, "questionId"),
    resolution: { answer, allowFurtherPause },
  };
}

function sendMonitor(res: ServerResponse, httpStatus: number, view: RunMonitorView): void {
  sendJson(res, httpStatus, view, monitorPollHeaders(view.status));
}

export function startServer(config: ServerConfig, deps: ServerDeps): RunApiServer {
  const runReportFn = deps.runReportFn;
  const checkpointStore = deps.checkpointStore ?? createLocalCheckpointStore(config.checkpointRoot);
  const documentStore = deps.documentStore ?? createLocalDocumentStore(config.documentRoot);
  const runFiles =
    deps.runFiles ??
    createRunFilesView({
      artifactsRoot: join(WORKSPACE_ROOT, ARTIFACTS_DIR_NAME),
      traceRoot: join(WORKSPACE_ROOT, TRACES_DIR_NAME),
      removeTree: createPoolUidTreeRemover({ pool: deps.uidPool, parentAccess: attemptRunDirAccess }),
    });
  const now = deps.now ?? Date.now;
  const admission = deps.admission ?? admitEverything(deps.uidPool);
  const log = deps.logger ?? createLogger({ level: config.logLevel });
  const rawMetrics = deps.metrics ?? createEmfMetrics();
  const metrics = createGuardedMetrics(rawMetrics, (error) => {
    log.error("metric_emit_failed", { error });
  });
  const handlerProcessingBudgetMs = deps.handlerProcessingBudgetMs ?? HANDLER_PROCESSING_BUDGET_MS;
  const random = deps.random ?? Math.random;
  const bodyReadDurations = createDurationEstimator({ coldStartMs: CAPACITY_RETRY_BASELINE_MS });
  const authRejections = createWindowedCounter(AUTH_REJECTION_LOG_WINDOW_MS, now);
  const inflightUploads = instrumentByteBudget(
    createByteBudget(config.inflightUploadBudgetBytes),
    "inflight_upload",
    log,
  );
  const ledger = createRunDataLedger({
    retainedInputBudgetBytes: config.retainedInputBudgetBytes,
    retainedOutputBudgetBytes: config.retainedOutputBudgetBytes,
    checkpointStore,
    documentStore,
    runFiles,
    log,
  });
  const orchestrator = createRunOrchestrator({
    runReportFn,
    ledger,
    documentStore,
    admission,
    log,
    metrics,
    ...(deps.now === undefined ? {} : { now: deps.now }),
    fuses: config.fuses,
    pinnedModel: config.pinnedModel,
    runtimeVersion: config.buildIdentity.version,
  });
  const registry = orchestrator.registry;
  const phaseTimers = createPhaseTimers();
  const filesRoutes = createRunFilesRoutes({
    registry,
    runFiles,
    checkpointStore,
    inflightBytes: inflightUploads,
    timers: phaseTimers,
    requestPhaseTimeoutMs: config.requestPhaseTimeoutMs,
    log,
    metrics,
  });

  // The only way to answer 500: every unexpected failure is counted exactly where it is answered,
  // so a new 500 cannot be added without also being alarmed on.
  function sendInternalError(res: ServerResponse, route: RouteName, cause: RequestFailureCause): void {
    sendJson(res, 500, INTERNAL_ERROR_BODY);
    countRequestFailure(route, cause);
  }

  function countRequestFailure(route: RouteName, cause: RequestFailureCause): void {
    metrics.count("RequestFailed", { dimensions: { route, cause }, aggregate: true });
  }

  function respondError(res: ServerResponse, error: unknown, route: RouteName): void {
    if (error instanceof ValidationError) {
      sendJson(res, 400, { error: error.message });
    } else if (error instanceof SyntaxError) {
      sendJson(res, 400, { error: "invalid JSON body" });
    } else if (error instanceof UnsupportedMediaTypeError) {
      sendJson(res, 415, { error: error.message });
    } else if (error instanceof BodyLimitError) {
      sendJson(res, 413, { error: "request body too large" });
    } else if (error instanceof RunDocumentBudgetError) {
      sendJson(res, 413, { error: error.message });
    } else if (error instanceof CapacityError) {
      refuseForCapacity(res, error.reason);
    } else if (error instanceof HandlerDeadlineError) {
      // Answered before any telemetry: a sink that throws must cost the log line, never the response.
      sendInternalError(res, route, "handler_deadline");
      log.error("handler_deadline_exceeded", { ...error.fields, error });
      metrics.count("HandlerDeadlineExceeded");
    } else if (hasErrorCode(error, "ECONNRESET", "ERR_STREAM_PREMATURE_CLOSE")) {
      res.destroy();
    } else {
      sendInternalError(res, route, "unhandled");
      log.error("request_failed", { route, error: toError(error) });
    }
  }

  function capacityRetryEstimateMs(reason: CapacityReason): number {
    return INFLIGHT_BUDGET_REASONS.has(reason) ? bodyReadDurations.estimateMs() : CAPACITY_RETRY_BASELINE_MS;
  }

  function refuseForCapacity(
    res: ServerResponse,
    reason: CapacityReason,
    retryAfterSeconds?: number,
    fields: { queueDepth?: number; admissionClass?: AdmissionClass } = {},
  ): void {
    const advisedSeconds =
      retryAfterSeconds ?? jitteredRetryAfterSeconds(capacityRetryEstimateMs(reason), random);
    log.warn("capacity_refused", {
      reason,
      retryAfterSeconds: advisedSeconds,
      ...(fields.queueDepth === undefined ? {} : { queueDepth: fields.queueDepth }),
      ...(fields.admissionClass === undefined ? {} : { admissionClass: fields.admissionClass }),
    });
    metrics.count("CapacityRefused", { dimensions: { reason } });
    sendJson(res, 503, { error: "capacity", reason }, { "retry-after": String(advisedSeconds) });
  }

  // Chat admission switched off on this deployment (a chat cap of 0): no Retry-After, because
  // retrying cannot succeed until an operator changes the setting.
  function refuseChatDisabled(res: ServerResponse): void {
    log.warn("capacity_refused", { reason: CHAT_DISABLED_REASON, admissionClass: "chat" });
    metrics.count("CapacityRefused", { dimensions: { reason: CHAT_DISABLED_REASON } });
    sendJson(res, 503, { error: "capacity", reason: CHAT_DISABLED_REASON });
  }

  // No phase timer runs across this wait: the handler deadline's sojourn cap is its bound.
  async function waitForAdmission(res: ServerResponse, admissionClass: AdmissionClass): Promise<boolean> {
    const socket = res.socket;
    if (socket !== null) {
      phaseTimers.clear(socket);
    }
    const abandonment = socketAbandonment(socket);
    let decision: AdmissionDecision;
    try {
      decision = await admission.admit({ abandoned: abandonment.signal, admissionClass });
    } finally {
      abandonment.unsubscribe();
    }
    if (decision.outcome === "admitted") {
      return true;
    }
    if (decision.outcome === "abandoned") {
      res.destroy();
      return false;
    }
    if (decision.outcome === "disabled") {
      refuseChatDisabled(res);
      return false;
    }
    refuseForCapacity(res, ADMISSION_SHED_REASON, decision.retryAfterSeconds, {
      queueDepth: admission.waitingCount(admissionClass),
      admissionClass,
    });
    return false;
  }

  async function handleStartRun(res: ServerResponse, body: unknown): Promise<void> {
    const request = await parseStartRunRequest(body, config);
    const existing = registry.get(request.executionId);
    const needsAdmission = existing === undefined || isRetryableFailure(existing);
    const admissionClass = request.admissionClass ?? DEFAULT_ADMISSION_CLASS;
    if (needsAdmission && !(await waitForAdmission(res, admissionClass))) {
      return;
    }
    let backgroundRunStarted = false;
    try {
      // Deadlined separately from the route so this finally still runs on expiry and settles the grant.
      await withHandlerDeadline(async (cancelled) => {
        const outcome = await orchestrator.admitRun(request, cancelled, {
          heldChatGrant: needsAdmission && admissionClass === "chat",
        });
        backgroundRunStarted = outcome.kind === "started";
        respondStartOutcome(res, outcome);
      }, handlerProcessingBudgetMs);
    } catch (error) {
      if (error instanceof HandlerDeadlineError) {
        error.fields["executionId"] = request.executionId;
        orchestrator.settleFailure(request.executionId, { subtype: "internal_error", estimatedCostUsd: 0 });
      }
      throw error;
    } finally {
      if (needsAdmission) {
        admission.settleAdmission({ becameRun: backgroundRunStarted, admissionClass });
      }
    }
  }

  // "cancelled" answers nothing: the handler deadline that cancelled it already rejected the route.
  function respondStartOutcome(res: ServerResponse, outcome: AdmitRunOutcome): void {
    switch (outcome.kind) {
      case "draining":
        sendJson(res, 503, { error: "shutting down" });
        return;
      case "conflict":
        sendConflict(res, "payload_conflict");
        return;
      case "duplicate":
        sendMonitor(res, 200, outcome.view);
        return;
      case "cancelled":
        return;
      case "capacity":
        refuseForCapacity(res, outcome.reason);
        return;
      case "persist_failed":
        sendInternalError(res, "runs_start", "persist_failed");
        return;
      case "superseded":
        if (outcome.view === undefined) {
          sendJson(res, 404, NOT_FOUND_BODY);
        } else {
          sendMonitor(res, 200, outcome.view);
        }
        return;
      case "started":
        sendMonitor(res, 202, outcome.view);
        return;
    }
  }

  function handleGetRun(res: ServerResponse, executionId: string): void {
    const view = registry.get(executionId);
    if (view === undefined) {
      sendJson(res, 404, NOT_FOUND_BODY);
      return;
    }
    log.debug("run_polled", { executionId, status: view.status });
    sendMonitor(res, 200, view);
  }

  async function handleCompleteRun(res: ServerResponse, executionId: string): Promise<void> {
    const outcome = await orchestrator.completeRun(executionId);
    switch (outcome.outcome) {
      case "not_found":
        sendJson(res, 404, NOT_FOUND_BODY);
        return;
      case "not_completable":
        sendConflict(res, "not_completable", outcome.status);
        return;
      case "completed":
        sendMonitor(res, 200, outcome.view);
        return;
    }
  }

  function handleCancelRun(res: ServerResponse, executionId: string): void {
    const outcome = orchestrator.cancelRun(executionId);
    switch (outcome.outcome) {
      case "not_found":
        sendJson(res, 404, NOT_FOUND_BODY);
        return;
      case "settled":
        sendMonitor(res, 200, outcome.view);
        return;
      case "cancelling":
        sendMonitor(res, 202, outcome.view);
        return;
    }
  }

  function handleRunInput(res: ServerResponse, body: unknown, executionId: string): void {
    const request = parseRunInputRequest(body);
    const outcome = orchestrator.resumeRun(
      executionId,
      request.resumeToken,
      request.questionId,
      request.resolution,
    );
    switch (outcome.outcome) {
      case "not_found":
        sendJson(res, 404, NOT_FOUND_BODY);
        return;
      case "stale_token":
        sendConflict(res, "stale_resume_token", outcome.status);
        return;
      case "unknown_question":
        sendConflict(res, "unknown_question", outcome.status);
        return;
      case "not_awaiting":
        sendConflict(res, "not_awaiting_input", outcome.status);
        return;
      case "ok":
        sendMonitor(res, 200, outcome.view);
        return;
    }
  }

  type Route = {
    name: RouteName;
    method: string;
    open?: boolean;
    maxBodyBytes?: number;
    mayWaitForAdmission?: boolean;
    run: (res: ServerResponse, body: unknown) => void | Promise<void>;
  };

  type RunActionSpec = {
    name: RouteName;
    method: string;
    maxBodyBytes?: number;
    rest: "none" | "path";
    run: (input: {
      res: ServerResponse;
      body: unknown;
      executionId: string;
      restPath: string;
    }) => void | Promise<void>;
  };

  // A Map, never an object literal: the action segment is untrusted, and a prototype-bearing table
  // would resolve "toString"/"constructor"/"__proto__" as routes.
  const runActionRoutes = new Map<string, RunActionSpec>([
    [
      "cancel",
      {
        name: "run_cancel",
        method: "POST",
        rest: "none",
        run: ({ res, executionId }) => handleCancelRun(res, executionId),
      },
    ],
    [
      "complete",
      {
        name: "run_complete",
        method: "POST",
        rest: "none",
        run: ({ res, executionId }) => handleCompleteRun(res, executionId),
      },
    ],
    [
      "input",
      {
        name: "run_input",
        method: "POST",
        rest: "none",
        maxBodyBytes: RUN_INPUT_BODY_LIMIT_BYTES,
        run: ({ res, body, executionId }) => handleRunInput(res, body, executionId),
      },
    ],
    [
      "files",
      {
        name: "run_files",
        method: "GET",
        rest: "path",
        run: ({ res, executionId, restPath }) =>
          restPath === "" ? filesRoutes.list(res, executionId) : filesRoutes.get(res, executionId, restPath),
      },
    ],
    [
      "checkpoint",
      {
        name: "run_checkpoint",
        method: "GET",
        rest: "none",
        run: ({ res, executionId }) => filesRoutes.checkpoint(res, executionId),
      },
    ],
  ]);

  const healthzBody = { status: "ok", ...config.buildIdentity };
  const limitsBody = buildLimitsView(config.fuses);
  const environmentBody = { runtimeVersion: config.buildIdentity.version, text: RUNTIME_ENVIRONMENT_SECTION };

  function resolveRoute(pathname: string): Route | undefined {
    if (pathname === "/healthz") {
      return { name: "healthz", method: "GET", open: true, run: (res) => sendJson(res, 200, healthzBody) };
    }
    const [version, root, executionId, action, ...restSegments] = pathname
      .split("/")
      .filter((segment) => segment !== "");
    if (version !== API_VERSION_SEGMENT) {
      return undefined;
    }
    if (root === "limits") {
      if (executionId !== undefined) {
        return undefined;
      }
      return { name: "limits", method: "GET", run: (res) => sendJson(res, 200, limitsBody) };
    }
    if (root === "environment") {
      if (executionId !== undefined) {
        return undefined;
      }
      return { name: "environment", method: "GET", run: (res) => sendJson(res, 200, environmentBody) };
    }
    if (root !== "runs") {
      return undefined;
    }
    if (executionId === undefined) {
      return {
        name: "runs_start",
        method: "POST",
        maxBodyBytes: config.bodyLimitBytes,
        mayWaitForAdmission: true,
        run: handleStartRun,
      };
    }
    if (action === undefined) {
      return { name: "run_monitor", method: "GET", run: (res) => handleGetRun(res, executionId) };
    }
    const spec = runActionRoutes.get(action);
    if (spec === undefined) {
      return undefined;
    }
    if (spec.rest === "none" && restSegments.length > 0) {
      return undefined;
    }
    const restPath = spec.rest === "path" ? decodePathSegments(restSegments) : "";
    if (restPath === undefined) {
      return undefined;
    }
    return {
      name: spec.name,
      method: spec.method,
      ...(spec.maxBodyBytes === undefined ? {} : { maxBodyBytes: spec.maxBodyBytes }),
      run: (res, body) => spec.run({ res, body, executionId, restPath }),
    };
  }

  async function handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
    let routeName: RouteName = "unresolved";
    let pathname = "/";
    try {
      const url = new URL(req.url ?? "/", "http://localhost");
      pathname = url.pathname;
      const route = resolveRoute(url.pathname);
      if (route !== undefined) {
        routeName = route.name;
      }
      if (
        route?.open !== true &&
        "token" in config.auth &&
        !bearerTokenMatches(req.headers.authorization, config.auth.token)
      ) {
        phaseTimers.clear(req.socket);
        const emit = authRejections.take();
        if (emit !== undefined) {
          log.warn("auth_rejected", { method: req.method ?? "", suppressed: emit.suppressed });
          metrics.count("AuthRejected", { properties: { suppressed: emit.suppressed } });
        }
        sendJson(res, 401, { error: "unauthorized" }, BEARER_CHALLENGE_HEADERS);
        return;
      }
      if (route === undefined) {
        phaseTimers.clear(req.socket);
        sendJson(res, 404, NOT_FOUND_BODY);
        return;
      }
      if (req.method !== route.method) {
        phaseTimers.clear(req.socket);
        sendJson(res, 405, { error: "method not allowed" }, { allow: route.method });
        return;
      }
      const routeDeadlineMs =
        handlerProcessingBudgetMs + (route.mayWaitForAdmission === true ? MAX_ADMISSION_WAIT_MS : 0);
      let body: unknown;
      if (route.maxBodyBytes !== undefined) {
        const declaredBytes = declaredContentLength(req);
        if (declaredBytes !== undefined && declaredBytes > route.maxBodyBytes) {
          throw new BodyLimitError(`request body exceeds ${route.maxBodyBytes} bytes`);
        }
        const uploadReservation = inflightUploads.tryReserve(
          uploadAdmissionChargeOf(declaredBytes, route.maxBodyBytes),
          { path: url.pathname },
        );
        if (uploadReservation === undefined) {
          throw new CapacityError(UPLOAD_BUDGET_REASON);
        }
        const armStallTimer = createStallGuard({
          timers: phaseTimers,
          socket: req.socket,
          totalBudgetMs: bodyReadBudgetMs(declaredBytes ?? route.maxBodyBytes, config.requestPhaseTimeoutMs),
          phaseTimeoutMs: config.requestPhaseTimeoutMs,
        });
        armStallTimer();
        try {
          const readStartedAt = now();
          body = await readJsonBody(req, {
            limitBytes: route.maxBodyBytes,
            ...(declaredBytes === undefined ? {} : { declaredBytes }),
            onProgress: armStallTimer,
            tryChargeTo: (totalBytes) => uploadReservation.tryGrowTo(totalBytes),
            onSized: (totalBytes) => uploadReservation.settleTo(totalBytes),
          });
          bodyReadDurations.record(now() - readStartedAt);
          // Only the start route reads executionId from the body; action routes take it from the path.
          const executionId = route.mayWaitForAdmission === true ? executionIdOf(body) : undefined;
          if (executionId !== undefined) {
            uploadReservation.tag({ executionId });
          }
          // Must clear here, not later: an armed phase timer would destroy the socket before the
          // handler deadline below ever runs on a body-carrying route.
          phaseTimers.clear(req.socket);
          await withHandlerDeadline(() => Promise.resolve(route.run(res, body)), routeDeadlineMs);
        } finally {
          phaseTimers.clear(req.socket);
          uploadReservation.release();
        }
        return;
      }
      phaseTimers.clear(req.socket);
      await withHandlerDeadline(() => Promise.resolve(route.run(res, body)), routeDeadlineMs);
    } catch (error) {
      phaseTimers.clear(req.socket);
      if (error instanceof HandlerDeadlineError) {
        error.fields["path"] = pathname;
      }
      respondError(res, error, routeName);
    }
  }

  const server = createServer((req, res) => {
    // Bun reports finish/writableLength as flushed while bytes are still queued, so never end/destroy
    // the socket here — re-arm the phase timer instead to police the next request's header phase.
    res.once("finish", () => {
      phaseTimers.arm(req.socket, config.requestPhaseTimeoutMs);
    });
    void handle(req, res);
  });

  function sampleRuntimeGauges(): void {
    const counts = registry.statusCounts();
    emitRuntimeGauges(metrics, {
      uidPoolFree: deps.uidPool.available(),
      uidPoolSize: deps.uidPool.size(),
      uidPoolRetained: deps.uidPool.retained().length,
      workflowSlotsFree: admission.workflowSlotsFree(),
      chatSlotsHeld: admission.chatSlotsHeld(),
      admissionQueueDepth: admission.waitingCount(),
      workflowQueueDepth: admission.waitingCount("workflow"),
      inflightUploadBytes: inflightUploads.reservedBytes(),
      retainedInputBytes: ledger.retainedInputBytes(),
      retainedOutputBytes: ledger.retainedOutputBytes(),
      runsActive: counts.running + counts.cancelling,
      runsAwaitingInput: counts.awaiting_input,
    });
  }

  const gaugeTimer = setInterval(sampleRuntimeGauges, deps.gaugeIntervalMs ?? GAUGE_SAMPLE_INTERVAL_MS);
  gaugeTimer.unref();

  // Disk-level, because the record that would have named these files died with the supervisor
  // that held it: the registry is the only thing that can still say a file belongs to a run.
  function reapOrphanedCheckpoints(): void {
    void checkpointStore
      .reapOrphans({
        isLive: (executionId) => registry.get(executionId) !== undefined,
        olderThanMs: deps.checkpointOrphanMaxAgeMs ?? CHECKPOINT_ORPHAN_MAX_AGE_MS,
        onSkipped: (name, error) => log.error("checkpoint_orphan_reap_failed", { name, error }),
      })
      .then((count) => {
        if (count > 0) {
          log.warn("checkpoint_orphans_reaped", { count });
        }
      })
      .catch((error: unknown) => {
        log.error("checkpoint_orphan_reap_failed", { error: toError(error) });
      });
  }

  const checkpointReapTimer = setInterval(
    reapOrphanedCheckpoints,
    deps.checkpointReapIntervalMs ?? CHECKPOINT_REAP_INTERVAL_MS,
  );
  checkpointReapTimer.unref();

  server.on("close", () => {
    clearInterval(gaugeTimer);
    clearInterval(checkpointReapTimer);
    registry.close();
  });

  server.on("connection", (socket) => {
    phaseTimers.arm(socket, config.requestPhaseTimeoutMs);
    socket.once("close", () => phaseTimers.clear(socket));
  });

  server.on("listening", () => {
    const address = server.address();
    const boundPort = address !== null && typeof address === "object" ? address.port : config.port;
    const authMode = "token" in config.auth ? "token" : "DISABLED";
    log.info("listening", {
      port: boundPort,
      authMode,
      provider: config.modelProvider.provider,
      ...config.buildIdentity,
    });
  });

  server.listen(config.port);
  return Object.assign(server, {
    // Order matters: registry must already be draining before admission.drain() releases waiters into it.
    drainRuns: () => {
      const drained = registry.drain(RUNTIME_SHUTDOWN_FAILURE);
      admission.drain();
      log.warn("drain", { failedRuns: drained });
      return drained;
    },
  });
}
