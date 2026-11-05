export type LogLevel = "debug" | "info" | "warn" | "error";

export type LogFieldValue = string | number | boolean | null | Error;

export type LogEventFields = {
  admission_chat_release_unbalanced: { detail: string };
  ambient_credentials: { detail: string };
  // anthropic_proxy_* lines are the key holder's (ADR 0151): request metadata only — never the key
  anthropic_proxy_child_error: { error: Error };
  anthropic_proxy_client_departed: { path: string; durationMs: number };
  anthropic_proxy_exited: { code: number | null; signal: string | null };
  anthropic_proxy_listening: { port: number };
  anthropic_proxy_request: { path: string; status: number; requestId?: string; durationMs: number };
  anthropic_proxy_started: { pid: number; port: number };
  anthropic_proxy_upstream_failed: { path?: string; error: Error };
  auth_rejected: { method: string; suppressed: number };
  budget_refused: { budget: string; requestedBytes: number; reservedBytes: number } & BudgetLogContext;
  budget_released: { budget: string; bytes: number; reservedBytes: number } & BudgetLogContext;
  budget_reserved: { budget: string; bytes: number; reservedBytes: number } & BudgetLogContext;
  capacity_refused: {
    reason: string;
    retryAfterSeconds?: number;
    queueDepth?: number;
    admissionClass?: string;
  };
  // cargo entries carry document bytes — only counts and sizes may appear here
  cargo_death_poll_failed: { uid: number; error: Error };
  cargo_stream_failed: { error: Error };
  cargo_streamed: { files: number; bytes: number };
  checkpoint_delete_failed: { executionId: string; segment: number; error: Error };
  child_close_event_missed: { uid: number };
  configuration_error: { error: Error };
  drain: { failedRuns: number };
  failed_run_publish_failed: { error: Error };
  final_frame_write_failed: { error: Error };
  final_response_persist_failed: { error: Error };
  final_response_recovered: { ok: boolean };
  frame_channel_errored: { error: Error };
  handler_deadline_exceeded: { executionId?: string; path?: string; error: Error };
  listening: { port: number; authMode: string; provider: string; version: string; revision: string };
  metric_emit_failed: { error: Error };
  oom_bias_failed: { detail: string };
  publish_forfeit_failed: { error: Error };
  request_failed: { route: string; error: Error };
  retained_output_measure_failed: { executionId: string; error: Error };
  retained_output_teardown_failed: { executionId: string; error: Error };
  run_cancel_requested: { executionId: string };
  run_completed: { executionId: string };
  run_data_purge_failed: { executionId: string; error: Error };
  run_documents_persist_failed: { executionId: string; error: Error };
  run_failed: {
    executionId: string;
    subtype: string;
    estimatedCostUsd: number;
    sessionId?: string | null;
    superseded?: true;
    error: Error;
  };
  run_file_stream_failed: { path: string; error: Error };
  run_finished: {
    executionId: string;
    estimatedCostUsd: number;
    sessionId: string | null;
    durationMs: number;
    superseded?: true;
  };
  run_paused: { executionId: string; segment: number; estimatedCostUsd: number };
  run_polled: { executionId: string; status: string };
  run_resumed: { executionId: string };
  run_settled: {
    executionId: string;
    status: string;
    subtype?: string;
    estimatedCostUsd: number;
    durationMs: number;
    pausedMs: number;
    inputTokens: number;
    outputTokens: number;
    cacheCreationInputTokens: number;
    cacheReadInputTokens: number;
    workFingerprint?: string;
    documentBytes?: number;
    documentCount?: number;
    model: string;
    runtimeVersion: string;
  };
  run_signal_end_unreported: { uid: number; signal: string; reason: string };
  run_signal_failed: {
    uid: number;
    signal: string;
    error?: Error;
    code?: number | null;
    killerSignal?: string | null;
    stderr?: string;
  };
  run_signal_unsettled: { uid: number; signal: string; reason: string };
  run_started: { executionId: string; admissionClass?: string; model?: string };
  server_error: { error: Error };
  shutdown_forced: { signal: string };
  shutdown_started: { signal: string };
  terminal_checkpoint_failed: { error: Error };
  terminal_checkpoint_save_failed: { error: Error };
  terminal_checkpoint_saved: { executionId: string; bytes: number };
  terminal_checkpoint_shed: { executionId: string; bytes: number };
  checkpoint_orphans_reaped: { count: number };
  checkpoint_orphan_reap_failed: { name?: string; error: Error };
  tool_call_errored: { toolName: string; erroredCalls: number };
  cost_update_dropped: { error: Error };
  trace_creation_failed: { error: Error };
  trace_write_failed: { packDir: string; error: Error };
  uid_idle_check_failed: { executionId?: string; uid: number; error: Error };
  uid_leased: { executionId: string; uid: number };
  uid_released: { executionId: string; uid: number };
  uid_reclaim_errored: { uid: number; error: Error };
  uid_reclaim_failed: { uid: number; idle: boolean; attempts: number };
  uid_reclaimed: { uid: number; attempts: number };
  uid_retained_dirty: { executionId: string; uid: number; idle: boolean; signalsSettled: boolean };
  uid_sweep_failed: { executionId?: string; uid: number; error: Error };
  uid_sweep_retried: { executionId?: string; uid: number; error: Error };
  uncaught_exception: { error: Error };
  unhandled_rejection: { error: Error };
  // wire frames carry runToken + checkpoint bytes — never add a payload field here
  wire_frame: {
    direction: "to_child" | "from_child";
    frame: "start" | "resume" | "answer" | "paused" | "cost" | "terminal_checkpoint" | "final";
    bytes: number;
  };
};

export type LogEventName = keyof LogEventFields;

export type BudgetLogContext = { path?: string; executionId?: string };

export type BoundLogFields = { executionId?: string; uid?: number };

export type ExactEventFields<E extends LogEventName, T> = LogEventFields[E] & {
  [K in Exclude<keyof T, keyof LogEventFields[E]>]: never;
};

export type ExactBoundFields<T> = BoundLogFields & { [K in Exclude<keyof T, keyof BoundLogFields>]: never };

export const LOG_EVENT_KEYS = {
  admission_chat_release_unbalanced: ["detail"],
  ambient_credentials: ["detail"],
  anthropic_proxy_child_error: ["error"],
  anthropic_proxy_client_departed: ["path", "durationMs"],
  anthropic_proxy_exited: ["code", "signal"],
  anthropic_proxy_listening: ["port"],
  anthropic_proxy_request: ["path", "status", "requestId", "durationMs"],
  anthropic_proxy_started: ["pid", "port"],
  anthropic_proxy_upstream_failed: ["path", "error"],
  auth_rejected: ["method", "suppressed"],
  budget_refused: ["budget", "requestedBytes", "reservedBytes", "path", "executionId"],
  budget_released: ["budget", "bytes", "reservedBytes", "path", "executionId"],
  budget_reserved: ["budget", "bytes", "reservedBytes", "path", "executionId"],
  capacity_refused: ["reason", "retryAfterSeconds", "queueDepth", "admissionClass"],
  cargo_death_poll_failed: ["uid", "error"],
  cargo_stream_failed: ["error"],
  cargo_streamed: ["files", "bytes"],
  checkpoint_delete_failed: ["executionId", "segment", "error"],
  child_close_event_missed: ["uid"],
  configuration_error: ["error"],
  drain: ["failedRuns"],
  failed_run_publish_failed: ["error"],
  final_frame_write_failed: ["error"],
  final_response_persist_failed: ["error"],
  final_response_recovered: ["ok"],
  frame_channel_errored: ["error"],
  handler_deadline_exceeded: ["executionId", "path", "error"],
  listening: ["port", "authMode", "provider", "version", "revision"],
  metric_emit_failed: ["error"],
  oom_bias_failed: ["detail"],
  publish_forfeit_failed: ["error"],
  request_failed: ["route", "error"],
  retained_output_measure_failed: ["executionId", "error"],
  retained_output_teardown_failed: ["executionId", "error"],
  run_cancel_requested: ["executionId"],
  run_completed: ["executionId"],
  run_data_purge_failed: ["executionId", "error"],
  run_documents_persist_failed: ["executionId", "error"],
  run_failed: ["executionId", "subtype", "estimatedCostUsd", "sessionId", "superseded", "error"],
  run_file_stream_failed: ["path", "error"],
  run_finished: ["executionId", "estimatedCostUsd", "sessionId", "durationMs", "superseded"],
  run_paused: ["executionId", "segment", "estimatedCostUsd"],
  run_polled: ["executionId", "status"],
  run_resumed: ["executionId"],
  run_settled: [
    "executionId",
    "status",
    "subtype",
    "estimatedCostUsd",
    "durationMs",
    "pausedMs",
    "inputTokens",
    "outputTokens",
    "cacheCreationInputTokens",
    "cacheReadInputTokens",
    "workFingerprint",
    "documentBytes",
    "documentCount",
    "model",
    "runtimeVersion",
  ],
  run_signal_end_unreported: ["uid", "signal", "reason"],
  run_signal_failed: ["uid", "signal", "error", "code", "killerSignal", "stderr"],
  run_signal_unsettled: ["uid", "signal", "reason"],
  run_started: ["executionId", "admissionClass", "model"],
  server_error: ["error"],
  shutdown_forced: ["signal"],
  shutdown_started: ["signal"],
  terminal_checkpoint_failed: ["error"],
  terminal_checkpoint_save_failed: ["error"],
  terminal_checkpoint_saved: ["executionId", "bytes"],
  terminal_checkpoint_shed: ["executionId", "bytes"],
  checkpoint_orphans_reaped: ["count"],
  checkpoint_orphan_reap_failed: ["name", "error"],
  tool_call_errored: ["toolName", "erroredCalls"],
  cost_update_dropped: ["error"],
  trace_creation_failed: ["error"],
  trace_write_failed: ["packDir", "error"],
  uid_idle_check_failed: ["executionId", "uid", "error"],
  uid_leased: ["executionId", "uid"],
  uid_released: ["executionId", "uid"],
  uid_reclaim_errored: ["uid", "error"],
  uid_reclaim_failed: ["uid", "idle", "attempts"],
  uid_reclaimed: ["uid", "attempts"],
  uid_retained_dirty: ["executionId", "uid", "idle", "signalsSettled"],
  uid_sweep_failed: ["executionId", "uid", "error"],
  uid_sweep_retried: ["executionId", "uid", "error"],
  uncaught_exception: ["error"],
  unhandled_rejection: ["error"],
  wire_frame: ["direction", "frame", "bytes"],
} as const satisfies { [E in LogEventName]: readonly Extract<keyof LogEventFields[E], string>[] };

export const BOUND_LOG_KEYS = ["executionId", "uid"] as const satisfies readonly Extract<
  keyof BoundLogFields,
  string
>[];

export type LogSink = (line: string, level: LogLevel) => void;

export type Logger = {
  debug<E extends LogEventName, T extends ExactEventFields<E, T>>(event: E, fields: T): void;
  info<E extends LogEventName, T extends ExactEventFields<E, T>>(event: E, fields: T): void;
  warn<E extends LogEventName, T extends ExactEventFields<E, T>>(event: E, fields: T): void;
  error<E extends LogEventName, T extends ExactEventFields<E, T>>(event: E, fields: T): void;
  with<T extends ExactBoundFields<T>>(fields: T): Logger;
};

export type LoggerDeps = {
  sink?: LogSink;
  now?: () => number;
  level?: LogLevel;
};

export const stdoutStderrSink: LogSink = (line, level) => {
  const stream = level === "error" ? process.stderr : process.stdout;
  stream.write(`${line}\n`);
};

// the run child's stdout IS the NDJSON frame wire — a log line there gets parsed as a frame
export const stderrOnlySink: LogSink = (line) => {
  process.stderr.write(`${line}\n`);
};

// For a process whose stdout is a pipe another process owns: a write that throws (the EPIPE of a
// pipe that died under a living parent, ADR 0129) costs the line, never the process. Nothing can
// be logged about it — the sink is the log.
export function bestEffortSink(sink: LogSink): LogSink {
  return (line, level) => {
    try {
      sink(line, level);
    } catch {
      // the line is lost; the process continues
    }
  };
}

const LEVEL_RANK: Record<LogLevel, number> = { debug: 0, info: 1, warn: 2, error: 3 };

const DEFAULT_LOG_LEVEL: LogLevel = "info";

type EmittableFields = Record<string, LogFieldValue | undefined>;

const EVENT_KEY_SETS: Record<string, ReadonlySet<string>> = Object.fromEntries(
  Object.entries(LOG_EVENT_KEYS).map(([event, keys]) => [event, new Set<string>(keys)]),
);

const BOUND_KEY_SET: ReadonlySet<string> = new Set<string>(BOUND_LOG_KEYS);

// unreachable via the typed surface, but fail-closed if it ever isn't
const NO_PERMITTED_KEYS: ReadonlySet<string> = new Set<string>();

function serializedError(error: Error): Record<string, unknown> {
  return {
    message: error.message,
    stack: error.stack ?? "",
    ...("subtype" in error && typeof error.subtype === "string" ? { subtype: error.subtype } : {}),
  };
}

type DroppedKeyCount = { count: number };

function projectFields(
  fields: EmittableFields,
  permitted: ReadonlySet<string>,
  dropped: DroppedKeyCount,
): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(fields)) {
    if (!permitted.has(key)) {
      dropped.count += 1;
      continue;
    }
    out[key] = value instanceof Error ? serializedError(value) : value;
  }
  return out;
}

function stringifyRecord(record: Record<string, unknown>): string {
  return JSON.stringify(record, (_key, value: unknown) =>
    typeof value === "bigint" ? value.toString() : value,
  );
}

export function renderLogRecord(record: Record<string, unknown>): string {
  try {
    return stringifyRecord(record);
  } catch {
    return stringifyRecord({
      ts: record["ts"],
      level: record["level"],
      event: record["event"],
      logError: "fields were not serializable and were dropped",
    });
  }
}

export function createLogger(deps: LoggerDeps = {}): Logger {
  const sink = deps.sink ?? stdoutStderrSink;
  const now = deps.now ?? Date.now;
  const threshold = LEVEL_RANK[deps.level ?? DEFAULT_LOG_LEVEL];

  function emit(level: LogLevel, event: string, bound: EmittableFields, fields: EmittableFields): void {
    if (LEVEL_RANK[level] < threshold) {
      return;
    }
    const permitted = EVENT_KEY_SETS[event] ?? NO_PERMITTED_KEYS;
    const dropped: DroppedKeyCount = { count: 0 };
    const record = {
      ts: new Date(now()).toISOString(),
      level,
      event,
      ...projectFields(bound, BOUND_KEY_SET, dropped),
      ...projectFields(fields, permitted, dropped),
      ...(dropped.count === 0 ? {} : { droppedLogKeys: dropped.count }),
    };
    sink(renderLogRecord(record), level);
  }

  function loggerWith(bound: EmittableFields): Logger {
    return {
      debug: (event: LogEventName, fields: EmittableFields) => emit("debug", event, bound, fields),
      info: (event: LogEventName, fields: EmittableFields) => emit("info", event, bound, fields),
      warn: (event: LogEventName, fields: EmittableFields) => emit("warn", event, bound, fields),
      error: (event: LogEventName, fields: EmittableFields) => emit("error", event, bound, fields),
      with: (fields: BoundLogFields) => loggerWith({ ...bound, ...fields }),
    };
  }

  return loggerWith({});
}
