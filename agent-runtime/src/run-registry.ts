import { randomUUID } from "node:crypto";
import type { PauseQuestion, RunFailure, RunReportResult } from "./run-report.js";
import { type TokenUsage, ZERO_TOKEN_USAGE } from "./usage-cost.js";

export const RETENTION_MS = 24 * 60 * 60 * 1000;

const DEFAULT_SWEEP_INTERVAL_MS = 60 * 60 * 1000;

export const FIRST_ATTEMPT = 1;

export const RUN_STATUSES = [
  "running",
  "awaiting_input",
  "cancelling",
  "succeeded",
  "failed",
  "cancelled",
  "expired",
] as const;

export type RunStatus = (typeof RUN_STATUSES)[number];

export const TERMINAL_STATUSES: readonly RunStatus[] = ["succeeded", "failed", "cancelled", "expired"];

export function isTerminalStatus(status: RunStatus): boolean {
  return TERMINAL_STATUSES.includes(status);
}

// Freezing these into the idempotent record would turn a capacity blip into a permanently dead executionId.
export const RETRYABLE_FAILURE_SUBTYPES: readonly string[] = [
  "uid_pool_exhausted",
  "capacity_rejected",
  "internal_error",
];

export function isRetryableFailure(view: Pick<RunMonitorView, "status" | "error">): boolean {
  return (
    view.status === "failed" && view.error !== null && RETRYABLE_FAILURE_SUBTYPES.includes(view.error.subtype)
  );
}

export type RunErrorInfo = Pick<RunFailure, "subtype">;

export type RequiredInput = {
  resumeToken: string;
  expiresAt: string;
  question: PauseQuestion;
};

export type StructuredOutput = NonNullable<unknown> | null;

// tracePath stays off the wire — it's a container-internal path no client can dereference.
export type RunResultInfo = Pick<RunReportResult, "output" | "sessionId" | "budgetExhausted"> & {
  structuredOutput: StructuredOutput;
};

export type EffectiveRunFuses = {
  deadlineMs: number;
  maxBudgetUsd: number;
  maxTurns: number;
  maxRetainedOutputBytes: number;
};

export type RunMonitorView = {
  executionId: string;
  attempt: number;
  segment: number;
  status: RunStatus;
  createdAt: string;
  updatedAt: string;
  estimatedCostUsd: number;
  error: RunErrorInfo | null;
  result: RunResultInfo | null;
  requiredInput: RequiredInput | null;
  pausedAt: string | null;
  resumedAt: string | null;
  lastPauseMs: number | null;
  // True once torn down — distinct from merely terminal.
  completed: boolean;
  // null = pinned default; a resumed run continues on the model it started with.
  model: string | null;
  // null = no platform tool surface.
  mcpServerUrl: string | null;
  fuses: EffectiveRunFuses;
};

type RunRecord = Omit<RunMonitorView, "result"> & {
  result: RunReportResult | null;
  payloadFingerprint: string;
  runToken: string | null;
  usage: TokenUsage;
  totalPausedMs: number;
};

export type RegisterInput = {
  executionId: string;
  payloadFingerprint: string;
  runToken: string | null;
  fuses: EffectiveRunFuses;
  model?: string;
  mcpServerUrl?: string;
};

export type RegisterOutcome =
  | { outcome: "created"; view: RunMonitorView }
  | { outcome: "duplicate"; view: RunMonitorView }
  | { outcome: "conflict" }
  | { outcome: "draining" };

export type ResumeOutcome =
  | { outcome: "ok"; view: RunMonitorView }
  | { outcome: "stale_token"; status: RunStatus }
  | { outcome: "unknown_question"; status: RunStatus }
  | { outcome: "not_awaiting"; status: RunStatus }
  | { outcome: "not_found" };

export type CancelOutcome =
  | { outcome: "not_found" }
  | { outcome: "settled"; view: RunMonitorView }
  | { outcome: "cancelling"; view: RunMonitorView };

export type CompleteOutcome =
  | { outcome: "not_found" }
  | { outcome: "not_completable"; status: RunStatus }
  | { outcome: "completed"; view: RunMonitorView };

export type SettledRunFailure = Pick<RunFailure, "subtype" | "estimatedCostUsd" | "usage">;

export type TerminalRunFacts = {
  estimatedCostUsd: number;
  usage: TokenUsage;
  durationMs: number;
  pausedMs: number;
};

export type RunRegistry = {
  register(input: RegisterInput): RegisterOutcome;
  get(executionId: string): RunMonitorView | undefined;
  markSucceeded(executionId: string, result: RunReportResult): void;
  settleFailure(executionId: string, failure: SettledRunFailure): void;
  markAwaitingInput(
    executionId: string,
    question: PauseQuestion,
    ttlMs: number,
    estimatedCostUsd: number,
  ): string | undefined;
  markResumed(executionId: string, resumeToken: string, questionId: string): ResumeOutcome;
  requestCancel(executionId: string): CancelOutcome;
  markCompleted(executionId: string): CompleteOutcome;
  drain(failure: SettledRunFailure): number;
  sweep(): number;
  runTokenOf(executionId: string): string | null | undefined;
  statusCounts(): Record<RunStatus, number>;
  close(): void;
};

export type RunRegistryOptions = {
  now?: () => number;
  onTerminal?: (executionId: string, status: RunStatus, facts: TerminalRunFacts) => void;
  onPruned?: (executionId: string) => void;
  sweepIntervalMs?: number;
  orphanAgeMs?: number;
};

function toView(record: RunRecord): RunMonitorView {
  return {
    executionId: record.executionId,
    attempt: record.attempt,
    segment: record.segment,
    status: record.status,
    createdAt: record.createdAt,
    updatedAt: record.updatedAt,
    estimatedCostUsd: record.estimatedCostUsd,
    error: record.error === null ? null : { ...record.error },
    result:
      record.result === null
        ? null
        : {
            output: record.result.output,
            sessionId: record.result.sessionId,
            structuredOutput: record.result.structuredOutput ?? null,
            ...(record.result.budgetExhausted === true ? { budgetExhausted: true as const } : {}),
          },
    requiredInput:
      record.requiredInput === null
        ? null
        : {
            resumeToken: record.requiredInput.resumeToken,
            expiresAt: record.requiredInput.expiresAt,
            question: {
              ...record.requiredInput.question,
              input: { ...record.requiredInput.question.input },
            },
          },
    pausedAt: record.pausedAt,
    resumedAt: record.resumedAt,
    lastPauseMs: record.lastPauseMs,
    completed: record.completed,
    model: record.model,
    mcpServerUrl: record.mcpServerUrl,
    fuses: { ...record.fuses },
  };
}

export function createRunRegistry(options: RunRegistryOptions = {}): RunRegistry {
  const now = options.now ?? Date.now;
  const onTerminal = options.onTerminal;
  const orphanAgeMs = options.orphanAgeMs ?? RETENTION_MS;
  const records = new Map<string, RunRecord>();
  // One-way: a post-drain record would never settle — no second sweep.
  let draining = false;

  function isoNow(): string {
    return new Date(now()).toISOString();
  }

  function transition(record: RunRecord, apply: (target: RunRecord) => void, cost?: number): void {
    // Only a raised cost stamps updatedAt, since retention keys off it.
    if (cost !== undefined && cost > record.estimatedCostUsd) {
      record.estimatedCostUsd = cost;
      record.updatedAt = isoNow();
    }
    if (isTerminalStatus(record.status)) {
      return;
    }
    const pausedAtBefore = record.status === "awaiting_input" ? record.pausedAt : null;
    apply(record);
    record.updatedAt = isoNow();
    if (isTerminalStatus(record.status)) {
      if (pausedAtBefore !== null) {
        record.totalPausedMs += now() - Date.parse(pausedAtBefore);
      }
      record.runToken = null;
      onTerminal?.(record.executionId, record.status, {
        estimatedCostUsd: record.estimatedCostUsd,
        usage: record.usage,
        durationMs: now() - Date.parse(record.createdAt),
        pausedMs: record.totalPausedMs,
      });
    }
  }

  function expireIfOverdue(record: RunRecord): void {
    if (record.status !== "awaiting_input" || record.requiredInput === null) {
      return;
    }
    if (now() <= Date.parse(record.requiredInput.expiresAt)) {
      return;
    }
    expire(record);
  }

  function settleTerminal(
    record: RunRecord,
    terminal: {
      status: RunStatus;
      error?: RunErrorInfo;
      result?: RunReportResult;
      cost?: number;
      usage?: TokenUsage;
    },
  ): void {
    transition(
      record,
      (target) => {
        target.status = terminal.status;
        if (terminal.error !== undefined) {
          target.error = { ...terminal.error };
        }
        if (terminal.result !== undefined) {
          target.result = { ...terminal.result };
        }
        if (terminal.usage !== undefined) {
          target.usage = terminal.usage;
        }
        target.requiredInput = null;
      },
      terminal.cost,
    );
  }

  function expire(record: RunRecord): void {
    settleTerminal(record, { status: "expired" });
  }

  function settleCancelled(record: RunRecord): void {
    settleTerminal(record, { status: "cancelled" });
  }

  function settleFailedRecord(record: RunRecord, failure: SettledRunFailure): void {
    const usageDetail = failure.usage === undefined ? {} : { usage: failure.usage };
    if (record.status === "cancelling") {
      settleTerminal(record, { status: "cancelled", cost: failure.estimatedCostUsd, ...usageDetail });
      return;
    }
    settleTerminal(record, {
      status: "failed",
      error: { subtype: failure.subtype },
      cost: failure.estimatedCostUsd,
      ...usageDetail,
    });
  }

  function isPastRetention(record: RunRecord): boolean {
    return isTerminalStatus(record.status) && now() - Date.parse(record.updatedAt) > RETENTION_MS;
  }

  function isOrphaned(record: RunRecord): boolean {
    return !isTerminalStatus(record.status) && now() - Date.parse(record.updatedAt) > orphanAgeMs;
  }

  function prune(executionId: string): void {
    records.delete(executionId);
    options.onPruned?.(executionId);
  }

  function liveRecord(executionId: string): RunRecord | undefined {
    const record = records.get(executionId);
    if (record === undefined) {
      return undefined;
    }
    expireIfOverdue(record);
    if (isPastRetention(record)) {
      prune(executionId);
      return undefined;
    }
    return record;
  }

  function sweep(): number {
    let pruned = 0;
    for (const [executionId, record] of records) {
      expireIfOverdue(record);
      if (isOrphaned(record)) {
        expire(record);
      }
      if (isPastRetention(record)) {
        prune(executionId);
        pruned += 1;
      }
    }
    return pruned;
  }

  const sweepTimer = setInterval(sweep, options.sweepIntervalMs ?? DEFAULT_SWEEP_INTERVAL_MS);
  sweepTimer.unref();

  return {
    register(input) {
      if (draining) {
        return { outcome: "draining" };
      }
      const existing = liveRecord(input.executionId);
      if (existing !== undefined) {
        if (existing.payloadFingerprint !== input.payloadFingerprint) {
          return { outcome: "conflict" };
        }
        if (!isRetryableFailure(existing)) {
          return { outcome: "duplicate", view: toView(existing) };
        }
        records.delete(input.executionId);
      }
      const createdAt = isoNow();
      // Born running — the caller starts the run in the same tick, so no earlier status is ever observed.
      const record: RunRecord = {
        executionId: input.executionId,
        attempt: FIRST_ATTEMPT,
        segment: 0,
        status: "running",
        createdAt,
        updatedAt: createdAt,
        estimatedCostUsd: 0,
        error: null,
        result: null,
        requiredInput: null,
        pausedAt: null,
        resumedAt: null,
        lastPauseMs: null,
        usage: ZERO_TOKEN_USAGE,
        totalPausedMs: 0,
        payloadFingerprint: input.payloadFingerprint,
        runToken: input.runToken,
        completed: false,
        model: input.model ?? null,
        mcpServerUrl: input.mcpServerUrl ?? null,
        fuses: { ...input.fuses },
      };
      records.set(input.executionId, record);
      return { outcome: "created", view: toView(record) };
    },

    get(executionId) {
      const record = liveRecord(executionId);
      return record === undefined ? undefined : toView(record);
    },

    markSucceeded(executionId, result) {
      const record = liveRecord(executionId);
      if (record === undefined) {
        return;
      }
      settleTerminal(record, {
        status: "succeeded",
        result,
        cost: result.estimatedCostUsd,
        usage: result.usage,
      });
    },

    settleFailure(executionId, failure) {
      const record = liveRecord(executionId);
      if (record === undefined) {
        return;
      }
      settleFailedRecord(record, failure);
    },

    // Checkpoints live on tmpfs/memory, so a restart loses non-terminal state — settle failed, not zombie.
    drain(failure) {
      draining = true;
      let drained = 0;
      for (const record of records.values()) {
        if (isTerminalStatus(record.status)) {
          continue;
        }
        settleFailedRecord(record, failure);
        drained += 1;
      }
      return drained;
    },

    markAwaitingInput(executionId, question, ttlMs, estimatedCostUsd) {
      const record = liveRecord(executionId);
      if (record === undefined || record.status !== "running") {
        return undefined;
      }
      const resumeToken = randomUUID();
      transition(
        record,
        (target) => {
          target.status = "awaiting_input";
          target.pausedAt = isoNow();
          target.resumedAt = null;
          target.requiredInput = {
            resumeToken,
            expiresAt: new Date(now() + ttlMs).toISOString(),
            question: { ...question, input: { ...question.input } },
          };
        },
        estimatedCostUsd,
      );
      return resumeToken;
    },

    markResumed(executionId, resumeToken, questionId) {
      const record = liveRecord(executionId);
      if (record === undefined) {
        return { outcome: "not_found" };
      }
      if (record.status !== "awaiting_input" || record.requiredInput === null) {
        return { outcome: "not_awaiting", status: record.status };
      }
      if (record.requiredInput.resumeToken !== resumeToken) {
        return { outcome: "stale_token", status: record.status };
      }
      if (record.requiredInput.question.id !== questionId) {
        return { outcome: "unknown_question", status: record.status };
      }
      transition(record, (target) => {
        target.status = "running";
        target.requiredInput = null;
        target.segment += 1;
        target.resumedAt = isoNow();
        target.lastPauseMs = target.pausedAt === null ? null : now() - Date.parse(target.pausedAt);
        target.totalPausedMs += target.lastPauseMs ?? 0;
      });
      return { outcome: "ok", view: toView(record) };
    },

    markCompleted(executionId) {
      const record = liveRecord(executionId);
      if (record === undefined) {
        return { outcome: "not_found" };
      }
      if (record.status === "awaiting_input") {
        settleCancelled(record);
      } else if (!isTerminalStatus(record.status)) {
        return { outcome: "not_completable", status: record.status };
      }
      record.completed = true;
      return { outcome: "completed", view: toView(record) };
    },

    requestCancel(executionId) {
      const record = liveRecord(executionId);
      if (record === undefined) {
        return { outcome: "not_found" };
      }
      if (record.status === "running" || record.status === "cancelling") {
        transition(record, (target) => {
          target.status = "cancelling";
        });
        return { outcome: "cancelling", view: toView(record) };
      }
      settleCancelled(record);
      return { outcome: "settled", view: toView(record) };
    },

    sweep,

    runTokenOf(executionId) {
      return liveRecord(executionId)?.runToken;
    },

    statusCounts() {
      const counts: Record<RunStatus, number> = {
        running: 0,
        awaiting_input: 0,
        cancelling: 0,
        succeeded: 0,
        failed: 0,
        cancelled: 0,
        expired: 0,
      };
      for (const record of records.values()) {
        counts[record.status] += 1;
      }
      return counts;
    },

    close() {
      clearInterval(sweepTimer);
    },
  };
}
