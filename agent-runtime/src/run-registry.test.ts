import { describe, expect, it } from "bun:test";
import {
  createRunRegistry,
  isRetryableFailure,
  isTerminalStatus,
  RETENTION_MS,
  RETRYABLE_FAILURE_SUBTYPES,
  type RunMonitorView,
  type RunRegistry,
  type RunStatus,
  type TerminalRunFacts,
} from "./run-registry.js";
import { RUNTIME_FAILURE_SUBTYPES, type RunReportResult } from "./run-report.js";

const ZERO_USAGE = {
  inputTokens: 0,
  outputTokens: 0,
  cacheCreationInputTokens: 0,
  cacheReadInputTokens: 0,
};

const RESULT: RunReportResult = {
  executionId: "e-1",
  output: "the report",
  estimatedCostUsd: 0.42,
  sessionId: "s-1",
  usage: ZERO_USAGE,
};

const QUESTION = {
  id: "q-1",
  toolName: "mcp__platform__request_analyst_input",
  prompt: "Which fund?",
  input: { question: "Which fund?" },
};

const FINGERPRINT = "fp-1";

const TEST_FUSES = {
  deadlineMs: 5000,
  maxBudgetUsd: 1,
  maxTurns: 10,
  maxRetainedOutputBytes: 24 * 1024 * 1024,
};

const HOUR_MS = 60 * 60 * 1000;

function makeRegistry(
  startMs = 0,
  extra: {
    onTerminal?: (executionId: string, status: RunStatus, facts: TerminalRunFacts) => void;
    onPruned?: (executionId: string) => void;
    sweepIntervalMs?: number;
    orphanAgeMs?: number;
  } = {},
): { registry: RunRegistry; advance: (ms: number) => void } {
  let nowMs = startMs;
  return {
    registry: createRunRegistry({ now: () => nowMs, ...extra }),
    advance: (ms: number) => {
      nowMs += ms;
    },
  };
}

function registerRun(
  registry: RunRegistry,
  input: { executionId?: string; payloadFingerprint?: string; runToken?: string | null } = {},
): RunMonitorView {
  const outcome = registry.register({
    executionId: input.executionId ?? "e-1",
    payloadFingerprint: input.payloadFingerprint ?? FINGERPRINT,
    runToken: input.runToken ?? null,
    fuses: TEST_FUSES,
  });
  if (outcome.outcome !== "created") {
    throw new Error(`expected created, got ${outcome.outcome}`);
  }
  return outcome.view;
}

function awaitingRun(registry: RunRegistry, ttlMs = HOUR_MS, estimatedCostUsd = 0): string {
  registerRun(registry);
  const token = registry.markAwaitingInput("e-1", QUESTION, ttlMs, estimatedCostUsd);
  if (token === undefined) {
    throw new Error("expected a resume token");
  }
  return token;
}

describe("register", () => {
  it("creates a running record with zeroed progress and ISO timestamps from the injected clock", () => {
    const { registry } = makeRegistry(1_000);
    const view = registerRun(registry, { runToken: "run-token-1" });
    expect(view).toEqual({
      executionId: "e-1",
      attempt: 1,
      segment: 0,
      status: "running",
      createdAt: new Date(1_000).toISOString(),
      updatedAt: new Date(1_000).toISOString(),
      estimatedCostUsd: 0,
      error: null,
      result: null,
      requiredInput: null,
      fuses: TEST_FUSES,
      pausedAt: null,
      resumedAt: null,
      lastPauseMs: null,
      completed: false,
      model: null,
      mcpServerUrl: null,
    });
  });

  it("carries the effective fuses on the record and every monitor view, by value", () => {
    const { registry } = makeRegistry();
    const view = registerRun(registry);
    expect(view.fuses).toEqual(TEST_FUSES);
    const polled = registry.get("e-1");
    expect(polled?.fuses).toEqual(TEST_FUSES);
    if (polled !== undefined) {
      polled.fuses.maxTurns = 999;
    }
    expect(registry.get("e-1")?.fuses).toEqual(TEST_FUSES);
  });

  it("carries the chosen model on the record and the monitor view", () => {
    const { registry } = makeRegistry();
    const outcome = registry.register({
      executionId: "e-1",
      payloadFingerprint: FINGERPRINT,
      runToken: null,
      fuses: TEST_FUSES,
      model: "us.anthropic.claude-sonnet-4-6",
    });
    if (outcome.outcome !== "created") {
      throw new Error("expected created");
    }
    expect(outcome.view.model).toBe("us.anthropic.claude-sonnet-4-6");
    expect(registry.get("e-1")?.model).toBe("us.anthropic.claude-sonnet-4-6");
  });

  it("carries the MCP server URL on the record and the monitor view, null when unset", () => {
    const { registry } = makeRegistry();
    const outcome = registry.register({
      executionId: "e-1",
      payloadFingerprint: FINGERPRINT,
      runToken: "tok",
      fuses: TEST_FUSES,
      mcpServerUrl: "https://platform.internal/mcp",
    });
    if (outcome.outcome !== "created") {
      throw new Error("expected created");
    }
    expect(outcome.view.mcpServerUrl).toBe("https://platform.internal/mcp");
    expect(registry.get("e-1")?.mcpServerUrl).toBe("https://platform.internal/mcp");
    registerRun(registry, { executionId: "e-2" });
    expect(registry.get("e-2")?.mcpServerUrl).toBeNull();
  });

  it("never exposes the fingerprint or run token in the monitor view", () => {
    const { registry } = makeRegistry();
    registerRun(registry, { runToken: "secret-token" });
    const view = registry.get("e-1");
    expect(view).toBeDefined();
    expect(Object.keys(view ?? {})).not.toContain("runToken");
    expect(Object.keys(view ?? {})).not.toContain("payloadFingerprint");
  });

  it("hands the run token back through the internal accessor", () => {
    const { registry } = makeRegistry();
    registerRun(registry, { runToken: "secret-token" });
    expect(registry.runTokenOf("e-1")).toBe("secret-token");
  });

  it("returns the existing record for a same-fingerprint duplicate", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    const outcome = registry.register({
      executionId: "e-1",
      payloadFingerprint: FINGERPRINT,
      runToken: null,
      fuses: TEST_FUSES,
    });
    expect(outcome.outcome).toBe("duplicate");
    if (outcome.outcome !== "duplicate") {
      throw new Error("unreachable");
    }
    expect(outcome.view.status).toBe("running");
  });

  it("conflicts when the same executionId arrives with a different fingerprint", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    const outcome = registry.register({
      executionId: "e-1",
      payloadFingerprint: "fp-other",
      runToken: null,
      fuses: TEST_FUSES,
    });
    expect(outcome.outcome).toBe("conflict");
  });
});

describe("drain", () => {
  it("fails every non-terminal run — parked ones included — and leaves terminal records alone", () => {
    const { registry } = makeRegistry();
    registerRun(registry, { executionId: "running-1" });
    registerRun(registry, { executionId: "parked-1" });
    registry.markAwaitingInput("parked-1", QUESTION, HOUR_MS, 0.1);
    registerRun(registry, { executionId: "done-1" });
    registry.markSucceeded("done-1", RESULT);
    const drained = registry.drain({ subtype: "runtime_shutdown", estimatedCostUsd: 0 });
    expect(drained).toBe(2);
    expect(registry.get("running-1")?.status).toBe("failed");
    expect(registry.get("running-1")?.error).toEqual({ subtype: "runtime_shutdown" });
    expect(registry.get("parked-1")?.status).toBe("failed");
    expect(registry.get("parked-1")?.requiredInput).toBeNull();
    expect(registry.get("done-1")?.status).toBe("succeeded");
  });

  it("settles a cancelling run as cancelled, matching the failure path it was already on", () => {
    const { registry } = makeRegistry();
    registerRun(registry, { executionId: "e-1" });
    registry.requestCancel("e-1");
    registry.drain({ subtype: "runtime_shutdown", estimatedCostUsd: 0 });
    expect(registry.get("e-1")?.status).toBe("cancelled");
  });

  it("refuses registrations after the drain — a run created later would never be settled", () => {
    const { registry } = makeRegistry();
    registry.drain({ subtype: "runtime_shutdown", estimatedCostUsd: 0 });
    const outcome = registry.register({
      executionId: "late-1",
      payloadFingerprint: "fp",
      runToken: null,
      fuses: TEST_FUSES,
    });
    expect(outcome.outcome).toBe("draining");
    expect(registry.get("late-1")).toBeUndefined();
  });
});

describe("transitions", () => {
  it("marks succeeded, projecting the result to output and session with its cost on the monitor", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", { ...RESULT, tracePath: "/workspaces/traces/e-1/attempt-1" });
    const view = registry.get("e-1");
    expect(view?.status).toBe("succeeded");
    expect(view?.result).toEqual({ output: "the report", sessionId: "s-1", structuredOutput: null });
    expect(view?.result).not.toHaveProperty("structured_output");
    expect(view?.estimatedCostUsd).toBe(0.42);
    expect(view?.error).toBeNull();
  });

  it("projects a structured output onto the monitor result, null when the run produced none", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", { ...RESULT, structuredOutput: { verdict: "pass" } });
    const view = registry.get("e-1");
    expect(view?.result).toEqual({
      output: "the report",
      sessionId: "s-1",
      structuredOutput: { verdict: "pass" },
    });
    expect(view?.result).not.toHaveProperty("structured_output");
  });

  it("keeps the monitor cost equal to the cost the dropped result carried, pause baseline included", () => {
    const { registry } = makeRegistry();
    const token = awaitingRun(registry, HOUR_MS, 0.3);
    registry.markResumed("e-1", token, "q-1");
    registry.markSucceeded("e-1", RESULT);
    const view = registry.get("e-1");
    expect(view?.estimatedCostUsd).toBe(RESULT.estimatedCostUsd);
    expect(view?.result).toEqual({ output: "the report", sessionId: "s-1", structuredOutput: null });
  });

  it("settles a failure, storing the error and retaining the cost", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.settleFailure("e-1", { subtype: "error_max_turns", estimatedCostUsd: 0.2 });
    const view = registry.get("e-1");
    expect(view?.status).toBe("failed");
    expect(view?.error).toEqual({ subtype: "error_max_turns" });
    expect(view?.estimatedCostUsd).toBe(0.2);
    expect(view?.result).toBeNull();
  });

  it("advances updatedAt on transitions while keeping createdAt", () => {
    const { registry, advance } = makeRegistry(0);
    registerRun(registry);
    advance(5_000);
    registry.markSucceeded("e-1", RESULT);
    const view = registry.get("e-1");
    expect(view?.createdAt).toBe(new Date(0).toISOString());
    expect(view?.updatedAt).toBe(new Date(5_000).toISOString());
  });

  it("ignores every status change once terminal", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    expect(registry.requestCancel("e-1").outcome).toBe("settled");
    expect(registry.markAwaitingInput("e-1", QUESTION, HOUR_MS, 0)).toBeUndefined();
    const view = registry.get("e-1");
    expect(view?.status).toBe("succeeded");
    expect(view?.estimatedCostUsd).toBe(0.42);
    expect(view?.error).toBeNull();
  });
});

describe("awaiting input and resume", () => {
  it("marks awaiting_input with a resume token, deadline, and the question", () => {
    const { registry } = makeRegistry(10_000);
    const token = awaitingRun(registry, HOUR_MS);
    const view = registry.get("e-1");
    expect(view?.status).toBe("awaiting_input");
    expect(view?.requiredInput).toEqual({
      resumeToken: token,
      expiresAt: new Date(10_000 + HOUR_MS).toISOString(),
      question: QUESTION,
    });
  });

  it("resumes with the right token: clears requiredInput, runs again, bumps the segment", () => {
    const { registry } = makeRegistry();
    const token = awaitingRun(registry);
    const outcome = registry.markResumed("e-1", token, "q-1");
    expect(outcome.outcome).toBe("ok");
    const view = registry.get("e-1");
    expect(view?.status).toBe("running");
    expect(view?.segment).toBe(1);
    expect(view?.requiredInput).toBeNull();
  });

  it("rejects a wrong token as stale without changing state", () => {
    const { registry } = makeRegistry();
    awaitingRun(registry);
    const outcome = registry.markResumed("e-1", "not-the-token", "q-1");
    expect(outcome).toEqual({ outcome: "stale_token", status: "awaiting_input" });
    expect(registry.get("e-1")?.status).toBe("awaiting_input");
  });

  it("rejects an answer to a question that is not the pending one without changing state", () => {
    const { registry } = makeRegistry();
    const token = awaitingRun(registry);
    const outcome = registry.markResumed("e-1", token, "q-other");
    expect(outcome).toEqual({ outcome: "unknown_question", status: "awaiting_input" });
    expect(registry.get("e-1")?.status).toBe("awaiting_input");
    expect(registry.markResumed("e-1", token, "q-1").outcome).toBe("ok");
  });

  it("rejects a resume when the run is not awaiting input", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    const outcome = registry.markResumed("e-1", "any", "q-1");
    expect(outcome).toEqual({ outcome: "not_awaiting", status: "running" });
  });

  it("reports not_found for an unknown run", () => {
    const { registry } = makeRegistry();
    expect(registry.markResumed("missing", "any", "q-1")).toEqual({ outcome: "not_found" });
  });

  it("issues a fresh token per pause", () => {
    const { registry } = makeRegistry();
    const first = awaitingRun(registry);
    const resumed = registry.markResumed("e-1", first, "q-1");
    expect(resumed.outcome).toBe("ok");
    const second = registry.markAwaitingInput("e-1", QUESTION, HOUR_MS, 0);
    expect(second).toBeDefined();
    expect(second).not.toBe(first);
    expect(registry.markResumed("e-1", first, "q-1")).toEqual({
      outcome: "stale_token",
      status: "awaiting_input",
    });
  });
});

describe("pause metrics", () => {
  it("stamps pausedAt when the run pauses and clears any earlier resumedAt", () => {
    const { registry } = makeRegistry(5_000);
    awaitingRun(registry);
    const view = registry.get("e-1");
    expect(view?.pausedAt).toBe(new Date(5_000).toISOString());
    expect(view?.resumedAt).toBeNull();
  });

  it("exposes the pause-to-resume latency once input arrives", () => {
    const { registry, advance } = makeRegistry(5_000);
    const token = awaitingRun(registry);
    advance(90_000);
    expect(registry.markResumed("e-1", token, "q-1").outcome).toBe("ok");
    const view = registry.get("e-1");
    expect(view?.pausedAt).toBe(new Date(5_000).toISOString());
    expect(view?.resumedAt).toBe(new Date(95_000).toISOString());
    expect(view?.lastPauseMs).toBe(90_000);
  });

  it("tracks the latest pause on a second pause of the same run", () => {
    const { registry, advance } = makeRegistry(0);
    const first = awaitingRun(registry);
    advance(10_000);
    registry.markResumed("e-1", first, "q-1");
    advance(10_000);
    expect(registry.markAwaitingInput("e-1", QUESTION, HOUR_MS, 0)).toBeDefined();
    const view = registry.get("e-1");
    expect(view?.pausedAt).toBe(new Date(20_000).toISOString());
    expect(view?.resumedAt).toBeNull();
    expect(view?.lastPauseMs).toBe(10_000);
  });
});

describe("lazy expiry", () => {
  it("keeps a run awaiting input until its deadline passes", () => {
    const { registry, advance } = makeRegistry();
    awaitingRun(registry, HOUR_MS);
    advance(HOUR_MS);
    expect(registry.get("e-1")?.status).toBe("awaiting_input");
  });

  it("flips an overdue awaiting run to expired on read and drops requiredInput", () => {
    const { registry, advance } = makeRegistry();
    awaitingRun(registry, HOUR_MS);
    advance(HOUR_MS + 1);
    const view = registry.get("e-1");
    expect(view?.status).toBe("expired");
    expect(view?.requiredInput).toBeNull();
  });

  it("refuses to resume an expired run", () => {
    const { registry, advance } = makeRegistry();
    const token = awaitingRun(registry, HOUR_MS);
    advance(HOUR_MS + 1);
    expect(registry.markResumed("e-1", token, "q-1")).toEqual({ outcome: "not_awaiting", status: "expired" });
  });
});

describe("retention", () => {
  it("keeps terminal records within the retention window", () => {
    const { registry, advance } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    advance(RETENTION_MS - 1);
    expect(registry.get("e-1")?.status).toBe("succeeded");
  });

  it("prunes terminal records past the retention window", () => {
    const { registry, advance } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    advance(RETENTION_MS + 1);
    expect(registry.get("e-1")).toBeUndefined();
  });

  it("keeps non-terminal records regardless of age", () => {
    const { registry, advance } = makeRegistry();
    registerRun(registry);
    advance(RETENTION_MS * 2);
    expect(registry.get("e-1")?.status).toBe("running");
  });

  it("lets a pruned executionId register again as a fresh run", () => {
    const { registry, advance } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    advance(RETENTION_MS + 1);
    const view = registerRun(registry, { payloadFingerprint: "fp-other" });
    expect(view.status).toBe("running");
  });
});

describe("cancellation", () => {
  it("reports not_found for an unknown run", () => {
    const { registry } = makeRegistry();
    expect(registry.requestCancel("missing")).toEqual({ outcome: "not_found" });
  });

  it("cancels an awaiting_input run immediately, dropping requiredInput", () => {
    const { registry } = makeRegistry();
    awaitingRun(registry);
    const outcome = registry.requestCancel("e-1");
    expect(outcome.outcome).toBe("settled");
    const view = registry.get("e-1");
    expect(view?.status).toBe("cancelled");
    expect(view?.requiredInput).toBeNull();
  });

  it("moves a running run to cancelling, then the settled failure lands it with the cost", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    const outcome = registry.requestCancel("e-1");
    expect(outcome.outcome).toBe("cancelling");
    expect(registry.get("e-1")?.status).toBe("cancelling");
    registry.settleFailure("e-1", { subtype: "aborted", estimatedCostUsd: 0.3 });
    const view = registry.get("e-1");
    expect(view?.status).toBe("cancelled");
    expect(view?.estimatedCostUsd).toBe(0.3);
    expect(view?.error).toBeNull();
  });

  it("keeps answering cancelling while a cancel is in flight", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.requestCancel("e-1");
    expect(registry.requestCancel("e-1").outcome).toBe("cancelling");
  });

  it("answers settled for a finished run without changing its status", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    const outcome = registry.requestCancel("e-1");
    expect(outcome.outcome).toBe("settled");
    if (outcome.outcome !== "settled") {
      throw new Error("unreachable");
    }
    expect(outcome.view.status).toBe("succeeded");
  });
});

describe("terminal notification", () => {
  it("fires onTerminal exactly once when a read flips an overdue run", () => {
    const seen: [string, RunStatus][] = [];
    const { registry, advance } = makeRegistry(0, { onTerminal: (id, status) => seen.push([id, status]) });
    awaitingRun(registry, HOUR_MS);
    advance(HOUR_MS + 1);
    registry.get("e-1");
    registry.get("e-1");
    expect(seen).toEqual([["e-1", "expired"]]);
  });

  it("fires onTerminal when a resume meets an overdue run", () => {
    const seen: string[] = [];
    const { registry, advance } = makeRegistry(0, { onTerminal: (id) => seen.push(id) });
    const token = awaitingRun(registry, HOUR_MS);
    advance(HOUR_MS + 1);
    expect(registry.markResumed("e-1", token, "q-1")).toEqual({ outcome: "not_awaiting", status: "expired" });
    expect(seen).toEqual(["e-1"]);
  });

  it("fires onTerminal when a cancel meets an overdue run", () => {
    const seen: RunStatus[] = [];
    const { registry, advance } = makeRegistry(0, { onTerminal: (_, status) => seen.push(status) });
    awaitingRun(registry, HOUR_MS);
    advance(HOUR_MS + 1);
    expect(registry.requestCancel("e-1").outcome).toBe("settled");
    expect(seen).toEqual(["expired"]);
  });

  it("fires onTerminal for every settled path", () => {
    const seen: RunStatus[] = [];
    const succeeded = makeRegistry(0, { onTerminal: (_, status) => seen.push(status) });
    registerRun(succeeded.registry);
    succeeded.registry.markSucceeded("e-1", RESULT);
    const failed = makeRegistry(0, { onTerminal: (_, status) => seen.push(status) });
    registerRun(failed.registry);
    failed.registry.settleFailure("e-1", { subtype: "s", estimatedCostUsd: 0 });
    const cancelled = makeRegistry(0, { onTerminal: (_, status) => seen.push(status) });
    registerRun(cancelled.registry);
    cancelled.registry.requestCancel("e-1");
    cancelled.registry.settleFailure("e-1", { subtype: "aborted", estimatedCostUsd: 0 });
    expect(seen).toEqual(["succeeded", "failed", "cancelled"]);
  });
});

describe("pause guard", () => {
  it("refuses to pause a cancelling run instead of regressing it to awaiting_input", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    expect(registry.requestCancel("e-1").outcome).toBe("cancelling");
    expect(registry.markAwaitingInput("e-1", QUESTION, HOUR_MS, 0)).toBeUndefined();
    expect(registry.get("e-1")?.status).toBe("cancelling");
  });
});

describe("run token retention", () => {
  it("drops the run token at every terminal transition", () => {
    const succeeded = makeRegistry();
    registerRun(succeeded.registry, { runToken: "tok" });
    succeeded.registry.markSucceeded("e-1", RESULT);
    expect(succeeded.registry.runTokenOf("e-1")).toBeNull();

    const failed = makeRegistry();
    registerRun(failed.registry, { runToken: "tok" });
    failed.registry.settleFailure("e-1", { subtype: "s", estimatedCostUsd: 0 });
    expect(failed.registry.runTokenOf("e-1")).toBeNull();

    const cancelled = makeRegistry();
    registerRun(cancelled.registry, { runToken: "tok" });
    cancelled.registry.requestCancel("e-1");
    cancelled.registry.settleFailure("e-1", { subtype: "aborted", estimatedCostUsd: 0 });
    expect(cancelled.registry.runTokenOf("e-1")).toBeNull();

    const expired = makeRegistry();
    registerRun(expired.registry, { runToken: "tok" });
    expired.registry.markAwaitingInput("e-1", QUESTION, HOUR_MS, 0);
    expired.advance(HOUR_MS + 1);
    expect(expired.registry.runTokenOf("e-1")).toBeNull();
  });

  it("returns nothing for a pruned record", () => {
    const { registry, advance } = makeRegistry();
    registerRun(registry, { runToken: "tok" });
    registry.markSucceeded("e-1", RESULT);
    advance(RETENTION_MS + 1);
    expect(registry.runTokenOf("e-1")).toBeUndefined();
  });
});

describe("cost retention", () => {
  it("a pause records the cost the segment had already incurred", () => {
    const { registry } = makeRegistry();
    awaitingRun(registry, HOUR_MS, 0.3);
    expect(registry.get("e-1")?.estimatedCostUsd).toBe(0.3);
  });

  it("a raised cost stamps updatedAt; a cost that does not raise leaves it alone", () => {
    const { registry, advance } = makeRegistry(0);
    registry.register({ executionId: "e-1", payloadFingerprint: "fp-1", runToken: null, fuses: TEST_FUSES });
    registry.requestCancel("e-1");
    const afterCancel = registry.get("e-1")?.updatedAt;
    advance(1_000);
    registry.settleFailure("e-1", { subtype: "aborted", estimatedCostUsd: 0.3 });
    const afterRaise = registry.get("e-1")?.updatedAt;
    expect(afterRaise).not.toBe(afterCancel);
    advance(1_000);
    registry.settleFailure("e-1", { subtype: "aborted", estimatedCostUsd: 0.1 });
    expect(registry.get("e-1")?.updatedAt).toBe(afterRaise);
  });

  it("a settled cancellation never lowers a known cost", () => {
    const { registry } = makeRegistry();
    const token = awaitingRun(registry, HOUR_MS, 0.3);
    registry.markResumed("e-1", token, "q-1");
    registry.requestCancel("e-1");
    registry.settleFailure("e-1", { subtype: "aborted", estimatedCostUsd: 0 });
    const view = registry.get("e-1");
    expect(view?.status).toBe("cancelled");
    expect(view?.estimatedCostUsd).toBe(0.3);
  });

  it("a settled failure never lowers a known cost", () => {
    const { registry } = makeRegistry();
    const token = awaitingRun(registry, HOUR_MS, 0.3);
    registry.markResumed("e-1", token, "q-1");
    registry.settleFailure("e-1", { subtype: "s", estimatedCostUsd: 0.1 });
    expect(registry.get("e-1")?.estimatedCostUsd).toBe(0.3);
  });

  it("still folds a late-arriving cost into a terminal record", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.requestCancel("e-1");
    registry.settleFailure("e-1", { subtype: "aborted", estimatedCostUsd: 0.3 });
    const view = registry.get("e-1");
    expect(view?.status).toBe("cancelled");
    expect(view?.estimatedCostUsd).toBe(0.3);
  });
});

describe("sweep", () => {
  it("prunes aged-out terminal records without any read", () => {
    const { registry, advance } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    advance(RETENTION_MS + 1);
    expect(registry.sweep()).toBe(1);
    expect(registry.sweep()).toBe(0);
    expect(registry.get("e-1")).toBeUndefined();
  });

  it("expires overdue awaiting runs during a sweep, notifying onTerminal", () => {
    const seen: string[] = [];
    const { registry, advance } = makeRegistry(0, { onTerminal: (id) => seen.push(id) });
    awaitingRun(registry, HOUR_MS);
    advance(HOUR_MS + 1);
    registry.sweep();
    expect(seen).toEqual(["e-1"]);
    expect(registry.get("e-1")?.status).toBe("expired");
  });

  it("expires a non-terminal record nothing has touched past the orphan bound", () => {
    const seen: RunStatus[] = [];
    const { registry, advance } = makeRegistry(0, {
      orphanAgeMs: HOUR_MS,
      onTerminal: (_, status) => seen.push(status),
    });
    registerRun(registry);
    advance(HOUR_MS + 1);
    registry.sweep();
    expect(seen).toEqual(["expired"]);
    expect(registry.get("e-1")?.status).toBe("expired");
  });

  it("runs the sweep on its own interval until closed", async () => {
    const { registry, advance } = makeRegistry(0, { sweepIntervalMs: 5 });
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    advance(RETENTION_MS + 1);
    await new Promise((resolve) => setTimeout(resolve, 40));
    expect(registry.sweep()).toBe(0);
    registry.close();
  });
});

describe("markCompleted", () => {
  it("marks a terminal run completed, idempotently, without changing its status", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    const first = registry.markCompleted("e-1");
    if (first.outcome !== "completed") {
      throw new Error(`expected completed, got ${first.outcome}`);
    }
    expect(first.view.status).toBe("succeeded");
    expect(registry.get("e-1")?.completed).toBe(true);
    expect(registry.markCompleted("e-1").outcome).toBe("completed");
  });

  it("winds a parked run down to cancelled, firing onTerminal like any other teardown", () => {
    const seen: RunStatus[] = [];
    const { registry } = makeRegistry(0, { onTerminal: (_, status) => seen.push(status) });
    awaitingRun(registry);
    const outcome = registry.markCompleted("e-1");
    if (outcome.outcome !== "completed") {
      throw new Error(`expected completed, got ${outcome.outcome}`);
    }
    expect(outcome.view.status).toBe("cancelled");
    expect(outcome.view.requiredInput).toBeNull();
    expect(seen).toEqual(["cancelled"]);
    expect(registry.get("e-1")?.completed).toBe(true);
  });

  it("refuses to complete a run that is still running or cancelling", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    expect(registry.markCompleted("e-1")).toEqual({ outcome: "not_completable", status: "running" });
    registry.requestCancel("e-1");
    expect(registry.markCompleted("e-1")).toEqual({ outcome: "not_completable", status: "cancelling" });
    expect(registry.get("e-1")?.completed).toBe(false);
  });

  it("reports not_found for an unknown run", () => {
    const { registry } = makeRegistry();
    expect(registry.markCompleted("ghost")).toEqual({ outcome: "not_found" });
    expect(registry.get("ghost")).toBeUndefined();
  });

  it("keeps answering the monitor after complete until retention prunes the record", () => {
    const { registry, advance } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    registry.markCompleted("e-1");
    expect(registry.get("e-1")?.status).toBe("succeeded");
    advance(RETENTION_MS + 1);
    expect(registry.get("e-1")).toBeUndefined();
  });
});

describe("onPruned", () => {
  it("fires when a read prunes a record past retention", () => {
    const seen: string[] = [];
    const { registry, advance } = makeRegistry(0, { onPruned: (id) => seen.push(id) });
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    advance(RETENTION_MS + 1);
    expect(registry.get("e-1")).toBeUndefined();
    expect(seen).toEqual(["e-1"]);
  });

  it("fires when the sweep prunes without any read", () => {
    const seen: string[] = [];
    const { registry, advance } = makeRegistry(0, { onPruned: (id) => seen.push(id) });
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    advance(RETENTION_MS + 1);
    expect(registry.sweep()).toBe(1);
    expect(seen).toEqual(["e-1"]);
  });
});

describe("isTerminalStatus", () => {
  it("marks exactly the four terminal statuses", () => {
    expect(isTerminalStatus("succeeded")).toBe(true);
    expect(isTerminalStatus("failed")).toBe(true);
    expect(isTerminalStatus("cancelled")).toBe(true);
    expect(isTerminalStatus("expired")).toBe(true);
    expect(isTerminalStatus("running")).toBe(false);
    expect(isTerminalStatus("awaiting_input")).toBe(false);
    expect(isTerminalStatus("cancelling")).toBe(false);
  });
});

describe("statusCounts", () => {
  it("counts records by status across the registry", () => {
    const { registry } = makeRegistry();
    registerRun(registry, { executionId: "e-1" });
    registerRun(registry, { executionId: "e-2", payloadFingerprint: "fp-2" });
    registry.markAwaitingInput("e-2", QUESTION, HOUR_MS, 0);
    registerRun(registry, { executionId: "e-3", payloadFingerprint: "fp-3" });
    registry.settleFailure("e-3", { subtype: "internal_error", estimatedCostUsd: 0 });
    const counts = registry.statusCounts();
    expect(counts["running"]).toBe(1);
    expect(counts["awaiting_input"]).toBe(1);
    expect(counts["failed"]).toBe(1);
    expect(counts["succeeded"]).toBe(0);
  });
});

describe("terminal facts", () => {
  const USAGE = {
    inputTokens: 40_600,
    outputTokens: 3_276,
    cacheCreationInputTokens: 8_315,
    cacheReadInputTokens: 32_140,
  };

  function captureFacts(startMs = 0): {
    registry: RunRegistry;
    advance: (ms: number) => void;
    facts: TerminalRunFacts[];
  } {
    const facts: TerminalRunFacts[] = [];
    const { registry, advance } = makeRegistry(startMs, {
      onTerminal: (_id, _status, terminalFacts) => facts.push(terminalFacts),
    });
    return { registry, advance, facts };
  }

  it("hands duration, zero paused time, and the result's token usage to onTerminal on success", () => {
    const { registry, advance, facts } = captureFacts(1_000);
    registerRun(registry);
    advance(44_000);
    registry.markSucceeded("e-1", { ...RESULT, usage: USAGE });
    expect(facts).toEqual([
      { estimatedCostUsd: RESULT.estimatedCostUsd, usage: USAGE, durationMs: 44_000, pausedMs: 0 },
    ]);
  });

  it("accumulates paused time across pauses and excludes it from nothing but pausedMs", () => {
    const { registry, advance, facts } = captureFacts(0);
    registerRun(registry);
    const token = registry.markAwaitingInput("e-1", QUESTION, HOUR_MS, 0.1);
    if (token === undefined) {
      throw new Error("expected a resume token");
    }
    advance(10_000);
    registry.markResumed("e-1", token, QUESTION.id);
    advance(5_000);
    const token2 = registry.markAwaitingInput("e-1", QUESTION, HOUR_MS, 0.2);
    if (token2 === undefined) {
      throw new Error("expected a second resume token");
    }
    advance(20_000);
    registry.markResumed("e-1", token2, QUESTION.id);
    advance(2_000);
    registry.markSucceeded("e-1", { ...RESULT, usage: USAGE });
    expect(facts).toHaveLength(1);
    expect(facts[0]?.pausedMs).toBe(30_000);
    expect(facts[0]?.durationMs).toBe(37_000);
  });

  it("counts an unfinished pause into pausedMs when the run settles while awaiting input", () => {
    const { registry, advance, facts } = captureFacts(0);
    awaitingRun(registry, HOUR_MS, 0.1);
    advance(15_000);
    registry.requestCancel("e-1");
    expect(facts).toHaveLength(1);
    expect(facts[0]?.pausedMs).toBe(15_000);
  });

  it("hands a failure's token usage to onTerminal when the failure carries one", () => {
    const { registry, advance, facts } = captureFacts(0);
    registerRun(registry);
    advance(9_000);
    registry.settleFailure("e-1", { subtype: "deadline_exceeded", estimatedCostUsd: 0.3, usage: USAGE });
    expect(facts).toEqual([{ estimatedCostUsd: 0.3, usage: USAGE, durationMs: 9_000, pausedMs: 0 }]);
  });

  it("reports zeroed usage for a failure that never learned its tokens", () => {
    const { registry, facts } = captureFacts(0);
    registerRun(registry);
    registry.settleFailure("e-1", { subtype: "internal_error", estimatedCostUsd: 0 });
    expect(facts[0]?.usage).toEqual({
      inputTokens: 0,
      outputTokens: 0,
      cacheCreationInputTokens: 0,
      cacheReadInputTokens: 0,
    });
  });
});

describe("budget-exhausted flag on the monitor result", () => {
  it("projects the flag onto the monitor result when the run settled at its budget", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", {
      ...RESULT,
      structuredOutput: { status: "ok" },
      budgetExhausted: true,
    });
    const view = registry.get("e-1");
    expect(view?.status).toBe("succeeded");
    expect(view?.result).toEqual({
      output: "the report",
      sessionId: "s-1",
      structuredOutput: { status: "ok" },
      budgetExhausted: true,
    });
  });

  it("leaves the flag absent on a result that settled inside its budget", () => {
    const { registry } = makeRegistry();
    registerRun(registry);
    registry.markSucceeded("e-1", RESULT);
    expect(registry.get("e-1")?.result).not.toHaveProperty("budgetExhausted");
  });
});

describe("restore-failure subtypes", () => {
  const RESTORE_SUBTYPES = [
    "checkpoint_restore_failed",
    "checkpoint_version_mismatch",
    "session_id_mismatch",
  ] as const;

  it("keeps every restore subtype in the closed runtime vocabulary under its literal wire string", () => {
    for (const subtype of RESTORE_SUBTYPES) {
      expect(RUNTIME_FAILURE_SUBTYPES).toContain(subtype);
    }
  });

  it("keeps upstream_error non-retryable, distinct from errored_result, and in the closed vocabulary", () => {
    expect(RUNTIME_FAILURE_SUBTYPES).toContain("upstream_error");
    expect(RUNTIME_FAILURE_SUBTYPES).toContain("errored_result");
    expect(RETRYABLE_FAILURE_SUBTYPES).not.toContain("upstream_error");
    expect(isRetryableFailure({ status: "failed", error: { subtype: "upstream_error" } })).toBe(false);
  });

  it("treats none of the restore subtypes as retryable — a broken bundle restores broken every time", () => {
    for (const subtype of RESTORE_SUBTYPES) {
      expect(RETRYABLE_FAILURE_SUBTYPES).not.toContain(subtype);
      expect(isRetryableFailure({ status: "failed", error: { subtype } })).toBe(false);
    }
  });
});
