import { describe, expect, it } from "bun:test";
import { Writable } from "node:stream";
import {
  type CheckpointBundle,
  type CheckpointKey,
  type CheckpointStore,
  sealCheckpoint,
  terminalCheckpointKeyOf,
} from "./checkpoint-store.js";
import { NO_SERVED_FILES } from "./checkpoint-store-test-helpers.js";
import { DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES, RETAINED_OUTPUT_WORST_CASE_BYTES } from "./config.js";
import type { DocumentStore } from "./document-store.js";
import { sha256Hex } from "./hash.js";
import type { Logger } from "./logger.js";
import { createNoopMetrics, type Metrics } from "./metrics.js";
import { createRunDataLedger } from "./run-data-ledger.js";
import type { RetainedByteTotals, RunFilesView } from "./run-files.js";
import { createRunOrchestrator, type StartRunRequest } from "./run-orchestrator.js";
import { RunFailedError, type RunReportResult } from "./run-report.js";
import {
  type ChildHandle,
  type ChildRun,
  createRunSpawner,
  type SpawnRunFn,
  type SpawnRunOptions,
} from "./run-spawner.js";
import { fromWireCheckpoint, toWireCheckpoint } from "./run-wire.js";
import { RunAsUidError } from "./setpriv.js";
import { createUidPool, type UidPool } from "./uid-pool.js";

const NEVER_CANCELLED = new AbortController().signal;

const PINNED_MODEL = "us.anthropic.claude-sonnet-4-6";

const USAGE = {
  inputTokens: 40_600,
  outputTokens: 3_276,
  cacheCreationInputTokens: 8_315,
  cacheReadInputTokens: 32_140,
};

type LoggedEvent = { event: string; fields: Record<string, unknown> };

function capturingLogger(entries: LoggedEvent[]): Logger {
  const log: Logger = {
    debug: (event, fields) => entries.push({ event, fields: { ...fields } }),
    info: (event, fields) => entries.push({ event, fields: { ...fields } }),
    warn: (event, fields) => entries.push({ event, fields: { ...fields } }),
    error: (event, fields) => entries.push({ event, fields: { ...fields } }),
    with: () => log,
  };
  return log;
}

type GaugeCall = { name: string; value: number; dimensions: Record<string, string> | undefined };

function capturingMetrics(gauges: GaugeCall[]): Metrics {
  return {
    count: () => {},
    gauge: (name, value, options) => gauges.push({ name, value, dimensions: options?.dimensions }),
  };
}

function settledEventOf(entries: LoggedEvent[]): Record<string, unknown> {
  const entry = entries.find((candidate) => candidate.event === "run_settled");
  if (entry === undefined) {
    throw new Error("no run_settled event was logged");
  }
  return entry.fields;
}

function silentLogger(): Logger {
  const log: Logger = { debug: () => {}, info: () => {}, warn: () => {}, error: () => {}, with: () => log };
  return log;
}

function stubResult(overrides: Partial<RunReportResult> = {}): RunReportResult {
  return {
    executionId: "e-1",
    output: "done",
    estimatedCostUsd: 0.5,
    sessionId: "s-1",
    usage: {
      inputTokens: 0,
      outputTokens: 0,
      cacheCreationInputTokens: 0,
      cacheReadInputTokens: 0,
    },
    ...overrides,
  };
}

function baseRequest(overrides: Partial<StartRunRequest> = {}): StartRunRequest {
  return {
    executionId: "e-1",
    prompt: "p",
    runToken: null,
    documents: [],
    skillFiles: [],
    maxRetainedOutputBytes: 400,
    ...overrides,
  };
}

function documentOf(sizeBytes: number): { name: string; bytes: Uint8Array; sha256: string } {
  const bytes = new Uint8Array(sizeBytes);
  return { name: "a.txt", bytes, sha256: sha256Hex(bytes) };
}

type Harness = {
  orchestrator: ReturnType<typeof createRunOrchestrator>;
  ledger: ReturnType<typeof createRunDataLedger>;
  checkpoints: { stored: Map<string, CheckpointBundle>; saves: CheckpointKey[]; deletes: CheckpointKey[] };
  calls: {
    artifactPurges: string[];
    savedDocuments: string[];
    durations: number[];
    slotWakes: number;
    chatReleases: number;
    retainedBytesReads: number;
  };
};

function makeHarness(input: {
  runReportFn: SpawnRunFn;
  retainedBytes?: (calls: Harness["calls"]) => RetainedByteTotals;
  purge?: (executionId: string) => Promise<void>;
  deleteDocuments?: (executionId: string) => Promise<void>;
  log?: Logger;
  metrics?: Metrics;
  pinnedModel?: string;
}): Harness {
  const calls: Harness["calls"] = {
    artifactPurges: [],
    savedDocuments: [],
    durations: [],
    slotWakes: 0,
    chatReleases: 0,
    retainedBytesReads: 0,
  };
  const checkpoints: Harness["checkpoints"] = { stored: new Map(), saves: [], deletes: [] };
  const checkpointIdOf = (key: CheckpointKey): string => `${key.executionId}.${key.attempt}.${key.segment}`;
  const checkpointStore: CheckpointStore = {
    ...NO_SERVED_FILES,
    save: (key, bundle) => {
      checkpoints.saves.push(key);
      checkpoints.stored.set(checkpointIdOf(key), bundle);
      return Promise.resolve();
    },
    load: (key) => Promise.resolve(checkpoints.stored.get(checkpointIdOf(key))),
    delete: (key) => {
      checkpoints.deletes.push(key);
      checkpoints.stored.delete(checkpointIdOf(key));
      return Promise.resolve();
    },
  };
  const documentStore: DocumentStore = {
    save: (executionId) => {
      calls.savedDocuments.push(executionId);
      return Promise.resolve();
    },
    openInputs: () => Promise.resolve(undefined),
    delete: () => Promise.resolve(),
  };
  const runFiles: RunFilesView = {
    list: () => Promise.resolve([]),
    retainedBytes: () => {
      calls.retainedBytesReads += 1;
      return Promise.resolve(
        input.retainedBytes === undefined ? { artifactBytes: 0, traceBytes: 0 } : input.retainedBytes(calls),
      );
    },
    resolve: () => Promise.resolve(undefined),
    purge: input.purge ?? (() => Promise.resolve()),
    purgeArtifacts: (executionId) => {
      calls.artifactPurges.push(executionId);
      return Promise.resolve();
    },
  };
  const log = input.log ?? silentLogger();
  const ledger = createRunDataLedger({
    retainedInputBudgetBytes: 1000,
    retainedOutputBudgetBytes: 1000,
    checkpointStore,
    documentStore,
    runFiles,
    log,
  });
  const orchestrator = createRunOrchestrator({
    runReportFn: input.runReportFn,
    ledger,
    documentStore,
    admission: {
      recordRunDuration: (ms) => calls.durations.push(ms),
      notifySlotFreed: () => {
        calls.slotWakes += 1;
      },
      releaseChatSlot: () => {
        calls.chatReleases += 1;
      },
    },
    log,
    metrics: input.metrics ?? createNoopMetrics(),
    pinnedModel: input.pinnedModel ?? PINNED_MODEL,
    runtimeVersion: "0.0.0-test",
    fuses: {
      deadlineMs: { defaultValue: 60_000, ceiling: 60_000 },
      maxBudgetUsd: { defaultValue: 1, ceiling: 1 },
      maxTurns: { defaultValue: 10, ceiling: 10 },
      maxRetainedOutputBytes: {
        defaultValue: DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
        ceiling: RETAINED_OUTPUT_WORST_CASE_BYTES,
      },
    },
  });
  return { orchestrator, ledger, checkpoints, calls };
}

async function until(check: () => boolean): Promise<void> {
  for (let i = 0; i < 100; i += 1) {
    if (check()) {
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 1));
  }
  expect(check()).toBe(true);
}

describe("success settlement path", () => {
  it("settles the output charge to the measured bytes, then complete releases everything exactly once", async () => {
    let finish: (result: RunReportResult) => void = () => {};
    const { orchestrator, ledger } = makeHarness({
      runReportFn: () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
      retainedBytes: () => ({ artifactBytes: 100, traceBytes: 50 }),
    });
    const outcome = await orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    expect(outcome.kind).toBe("started");
    expect(ledger.retainedOutputBytes()).toBe(400);
    finish(stubResult());
    await until(() => orchestrator.registry.get("e-1")?.status === "succeeded");
    await until(() => ledger.retainedOutputBytes() === 150);
    const completed = await orchestrator.completeRun("e-1");
    expect(completed.outcome).toBe("completed");
    await until(() => ledger.retainedOutputBytes() === 0 && ledger.retainedInputBytes() === 0);
    const again = await orchestrator.completeRun("e-1");
    expect(again.outcome).toBe("completed");
    expect(ledger.retainedOutputBytes()).toBe(0);
  });

  it("hands the finish check's measurement to the settle instead of walking the pack twice", async () => {
    let finish: (result: RunReportResult) => void = () => {};
    const { orchestrator, ledger, calls } = makeHarness({
      runReportFn: () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
      retainedBytes: () => ({ artifactBytes: 100, traceBytes: 50 }),
    });
    await orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    finish(stubResult());
    await until(() => orchestrator.registry.get("e-1")?.status === "succeeded");
    await until(() => ledger.retainedOutputBytes() === 150);
    expect(calls.retainedBytesReads).toBe(1);
  });

  it("answers a duplicate start from the record without charging a second reservation", async () => {
    const { orchestrator, ledger } = makeHarness({
      runReportFn: () => new Promise<RunReportResult>(() => {}),
    });
    await orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    const second = await orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    expect(second.kind).toBe("duplicate");
    expect(ledger.retainedOutputBytes()).toBe(400);
  });
});

describe("retryable failure settlement path", () => {
  it("releases both reservations immediately and keeps the never-held slot out of the duration ring", async () => {
    let fail: (error: Error) => void = () => {};
    const { orchestrator, ledger, calls } = makeHarness({
      runReportFn: () =>
        new Promise((_resolve, reject) => {
          fail = reject;
        }),
    });
    const outcome = await orchestrator.admitRun(
      baseRequest({ documents: [documentOf(300)] }),
      NEVER_CANCELLED,
    );
    expect(outcome.kind).toBe("started");
    expect(calls.savedDocuments).toEqual(["e-1"]);
    expect(ledger.retainedInputBytes()).toBe(300);
    expect(ledger.retainedOutputBytes()).toBe(400);
    fail(
      new RunFailedError("all run uids are in use", {
        subtype: "uid_pool_exhausted",
        estimatedCostUsd: 0,
        sessionId: null,
      }),
    );
    await until(() => orchestrator.registry.get("e-1")?.status === "failed");
    await until(() => ledger.retainedInputBytes() === 0 && ledger.retainedOutputBytes() === 0);
    expect(calls.durations).toEqual([]);
    expect(calls.slotWakes).toBe(1);
  });
});

describe("a resend that replaces a retryable record", () => {
  type HeldRun = {
    signal: AbortSignal | undefined;
    options: SpawnRunOptions | undefined;
    settle: (outcome: RunReportResult | Error) => void;
  };

  function heldRuns(): { runs: HeldRun[]; runReportFn: SpawnRunFn } {
    const runs: HeldRun[] = [];
    const runReportFn: SpawnRunFn = (_params, options) =>
      new Promise((resolve, reject) => {
        runs.push({
          signal: options?.signal,
          options,
          settle: (outcome) => (outcome instanceof Error ? reject(outcome) : resolve(outcome)),
        });
      });
    return { runs, runReportFn };
  }

  async function supersedeFirstRun(harness: Harness, runs: HeldRun[]): Promise<void> {
    expect((await harness.orchestrator.admitRun(baseRequest(), NEVER_CANCELLED)).kind).toBe("started");
    harness.orchestrator.settleFailure("e-1", { subtype: "internal_error", estimatedCostUsd: 0 });
    expect(runs[0]?.signal?.aborted).toBe(true);
    expect((await harness.orchestrator.admitRun(baseRequest(), NEVER_CANCELLED)).kind).toBe("started");
    expect(runs).toHaveLength(2);
  }

  it("leaves the resend running and cancellable when the superseded run fails late", async () => {
    const { runs, runReportFn } = heldRuns();
    const harness = makeHarness({ runReportFn });
    await supersedeFirstRun(harness, runs);

    runs[0]?.settle(
      new RunFailedError("killed mid-run", { subtype: "aborted", estimatedCostUsd: 0.2, sessionId: null }),
    );
    await new Promise((resolve) => setTimeout(resolve, 5));

    const view = harness.orchestrator.registry.get("e-1");
    expect(view?.status).toBe("running");
    expect(view?.estimatedCostUsd).toBe(0);
    expect(runs[1]?.signal?.aborted).toBe(false);
    expect(harness.orchestrator.cancelRun("e-1").outcome).toBe("cancelling");
    expect(runs[1]?.signal?.aborted).toBe(true);
  });

  it("writes neither a pause checkpoint nor a terminal bundle for the superseded run", async () => {
    const { runs, runReportFn } = heldRuns();
    const harness = makeHarness({ runReportFn });
    await supersedeFirstRun(harness, runs);
    const pause = runs[0]?.options?.pause;
    const saveTerminalCheckpoint = runs[0]?.options?.onTerminalCheckpoint;
    if (pause === undefined || saveTerminalCheckpoint === undefined) {
      throw new Error("the superseded run was handed no pause or terminal checkpoint hook");
    }
    const bundle = sealCheckpoint(new TextEncoder().encode("the superseded run's session"));

    await expect(
      pause({
        question: { id: "q-1", toolName: "ask", prompt: "which year?", input: {} },
        sessionId: "s-old",
        estimatedCostUsd: 0.1,
        checkpoint: bundle,
      }),
    ).rejects.toThrow("run can no longer pause");
    const saving: Promise<void> = saveTerminalCheckpoint({ sessionId: "s-old", checkpoint: bundle });
    await saving;

    expect(harness.checkpoints.saves).toEqual([]);
    expect(harness.orchestrator.registry.get("e-1")?.status).toBe("running");
  });

  it("leaves the superseded run's finish measurement out of the resend's charge", async () => {
    const { runs, runReportFn } = heldRuns();
    let resendBytes = false;
    let harness: Harness | undefined;
    let superseded = false;
    harness = makeHarness({
      runReportFn,
      retainedBytes: () => {
        if (!superseded && harness !== undefined) {
          superseded = true;
          harness.orchestrator.settleFailure("e-1", { subtype: "internal_error", estimatedCostUsd: 0 });
          void harness.orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
        }
        return { artifactBytes: 0, traceBytes: resendBytes ? 300 : 100 };
      },
    });
    expect((await harness.orchestrator.admitRun(baseRequest(), NEVER_CANCELLED)).kind).toBe("started");
    runs[0]?.settle(stubResult());
    await until(() => runs.length === 2);
    resendBytes = true;

    runs[1]?.settle(
      new RunFailedError("over budget", {
        subtype: "budget_exhausted",
        estimatedCostUsd: 0.4,
        sessionId: null,
      }),
    );

    await until(() => harness?.orchestrator.registry.get("e-1")?.status === "failed");
    await until(() => harness?.ledger.retainedOutputBytes() === 300);
  });

  it("does not settle the resend with the superseded run's late result", async () => {
    const { runs, runReportFn } = heldRuns();
    const harness = makeHarness({ runReportFn });
    await supersedeFirstRun(harness, runs);

    runs[0]?.settle(stubResult({ output: "the superseded run's answer" }));
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(harness.orchestrator.registry.get("e-1")?.status).toBe("running");
    runs[1]?.settle(stubResult({ output: "the resend's answer" }));
    await until(() => harness.orchestrator.registry.get("e-1")?.status === "succeeded");
    expect(harness.orchestrator.registry.get("e-1")?.result?.output).toBe("the resend's answer");
  });
});

describe("admission class", () => {
  it("returns a chat grant once, when the uid goes back to the pool, not again when the run settles", async () => {
    let finish: (error: Error) => void = () => {};
    const { orchestrator, calls } = makeHarness({
      runReportFn: (_params, options) =>
        new Promise((_resolve, reject) => {
          finish = (error) => {
            options?.onUidReleased?.();
            reject(error);
          };
        }),
    });
    const outcome = await orchestrator.admitRun(baseRequest({ admissionClass: "chat" }), NEVER_CANCELLED, {
      heldChatGrant: true,
    });
    expect(outcome.kind).toBe("started");
    finish(new Error("run ended"));
    await until(() => calls.slotWakes === 1);
    expect(calls.chatReleases).toBe(1);
  });

  it("returns a chat grant at settle when the uid was never released back", async () => {
    let finish: (error: Error) => void = () => {};
    const { orchestrator, calls } = makeHarness({
      runReportFn: () =>
        new Promise((_resolve, reject) => {
          finish = reject;
        }),
    });
    await orchestrator.admitRun(baseRequest({ admissionClass: "chat" }), NEVER_CANCELLED, {
      heldChatGrant: true,
    });
    finish(
      new RunFailedError("all run uids are in use", {
        subtype: "uid_pool_exhausted",
        estimatedCostUsd: 0,
        sessionId: null,
      }),
    );
    await until(() => calls.slotWakes === 1);
    expect(calls.chatReleases).toBe(1);
  });

  it("never returns a chat slot for a run that took no chat grant", async () => {
    let finish: (error: Error) => void = () => {};
    const { orchestrator, calls } = makeHarness({
      runReportFn: (_params, options) =>
        new Promise((_resolve, reject) => {
          finish = (error) => {
            options?.onUidReleased?.();
            reject(error);
          };
        }),
    });
    await orchestrator.admitRun(baseRequest({ admissionClass: "chat" }), NEVER_CANCELLED);
    finish(new Error("run ended"));
    await until(() => calls.slotWakes === 1);
    expect(calls.chatReleases).toBe(0);
  });
});

describe("terminal failure settlement path", () => {
  it("keeps the charges described until consumer complete", async () => {
    let fail: (error: Error) => void = () => {};
    const { orchestrator, ledger, calls } = makeHarness({
      runReportFn: () =>
        new Promise((_resolve, reject) => {
          fail = reject;
        }),
      retainedBytes: () => ({ artifactBytes: 0, traceBytes: 120 }),
    });
    await orchestrator.admitRun(baseRequest({ documents: [documentOf(300)] }), NEVER_CANCELLED);
    fail(
      new RunFailedError("budget gone", {
        subtype: "budget_exhausted",
        estimatedCostUsd: 0.3,
        sessionId: "s-1",
      }),
    );
    await until(() => orchestrator.registry.get("e-1")?.status === "failed");
    await until(() => ledger.retainedOutputBytes() === 120);
    expect(ledger.retainedInputBytes()).toBe(300);
    expect(calls.durations.length).toBe(1);
    await orchestrator.completeRun("e-1");
    await until(() => ledger.retainedInputBytes() === 0 && ledger.retainedOutputBytes() === 0);
  });
});

describe("over-budget settlement path", () => {
  it("fails the finish, tears down deliverables, spares the trace, and the charge follows the spared bytes", async () => {
    let finish: (result: RunReportResult) => void = () => {};
    const { orchestrator, ledger, calls } = makeHarness({
      runReportFn: () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
      retainedBytes: (state) =>
        state.artifactPurges.length > 0
          ? { artifactBytes: 0, traceBytes: 200 }
          : { artifactBytes: 500, traceBytes: 200 },
    });
    await orchestrator.admitRun(baseRequest({ documents: [documentOf(300)] }), NEVER_CANCELLED);
    finish(stubResult());
    await until(() => orchestrator.registry.get("e-1")?.status === "failed");
    expect(orchestrator.registry.get("e-1")?.error?.subtype).toBe("retained_budget_exceeded");
    await until(() => calls.artifactPurges.length === 1);
    await until(() => ledger.retainedOutputBytes() === 200);
    expect(ledger.retainedInputBytes()).toBe(0);
  });
});

describe("complete purges in the background", () => {
  it("answers at once and keeps the output bytes described until the purge has run", async () => {
    const purge = Promise.withResolvers<void>();
    const entries: LoggedEvent[] = [];
    const { orchestrator, ledger } = makeHarness({
      runReportFn: () => Promise.resolve(stubResult()),
      retainedBytes: () => ({ artifactBytes: 100, traceBytes: 50 }),
      purge: () => purge.promise,
      log: capturingLogger(entries),
    });
    await orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    await until(() => orchestrator.registry.get("e-1")?.status === "succeeded");
    await until(() => ledger.retainedOutputBytes() === 150);

    const completed = await orchestrator.completeRun("e-1");

    expect(completed.outcome).toBe("completed");
    expect(entries.some((entry) => entry.event === "run_completed")).toBe(true);
    expect(ledger.retainedOutputBytes()).toBe(150);
    purge.resolve();
    await until(() => ledger.retainedOutputBytes() === 0);
  });

  it("logs a purge that fails after the complete answered", async () => {
    const entries: LoggedEvent[] = [];
    const { orchestrator } = makeHarness({
      runReportFn: () => Promise.resolve(stubResult()),
      purge: () => Promise.reject(new Error("rm failed")),
      log: capturingLogger(entries),
    });
    await orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    await until(() => orchestrator.registry.get("e-1")?.status === "succeeded");
    expect((await orchestrator.completeRun("e-1")).outcome).toBe("completed");
    await until(() => entries.some((entry) => entry.event === "run_data_purge_failed"));
  });

  it("releases the input reservation once the documents are gone, without waiting for the output purge", async () => {
    const purge = Promise.withResolvers<void>();
    const { orchestrator, ledger } = makeHarness({
      runReportFn: () => Promise.resolve(stubResult()),
      retainedBytes: () => ({ artifactBytes: 100, traceBytes: 0 }),
      purge: () => purge.promise,
    });
    await orchestrator.admitRun(baseRequest({ documents: [documentOf(300)] }), NEVER_CANCELLED);
    await until(() => orchestrator.registry.get("e-1")?.status === "succeeded");
    expect(ledger.retainedInputBytes()).toBe(300);

    await orchestrator.completeRun("e-1");

    await until(() => ledger.retainedInputBytes() === 0);
    expect(ledger.retainedOutputBytes()).toBeGreaterThan(0);
    purge.resolve();
    await until(() => ledger.retainedOutputBytes() === 0);
  });
});

describe("cancel settlement path", () => {
  it("settles as cancelled on abort and holds the charges until complete", async () => {
    const { orchestrator, ledger } = makeHarness({
      runReportFn: (_params, options) =>
        new Promise((_resolve, reject) => {
          options?.signal?.addEventListener("abort", () => {
            reject(
              new RunFailedError("killed mid-run", {
                subtype: "aborted",
                estimatedCostUsd: 0.1,
                sessionId: null,
              }),
            );
          });
        }),
      retainedBytes: () => ({ artifactBytes: 0, traceBytes: 25 }),
    });
    await orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    const cancel = orchestrator.cancelRun("e-1");
    expect(cancel.outcome).toBe("cancelling");
    await until(() => orchestrator.registry.get("e-1")?.status === "cancelled");
    await until(() => ledger.retainedOutputBytes() === 25);
    await orchestrator.completeRun("e-1");
    await until(() => ledger.retainedOutputBytes() === 0);
  });
});

describe("retained-output cap plumbing", () => {
  it("hands the declared retained-output cap to the spawner so the publish path can enforce it", async () => {
    const seen: SpawnRunOptions[] = [];
    const { orchestrator } = makeHarness({
      runReportFn: (_params, options) => {
        seen.push(options ?? {});
        return Promise.resolve(stubResult());
      },
    });
    await orchestrator.admitRun(baseRequest({ maxRetainedOutputBytes: 400 }), NEVER_CANCELLED);
    await until(() => seen.length === 1);
    expect(seen[0]?.maxRetainedOutputBytes).toBe(400);
  });
});

describe("settle observability", () => {
  it("logs the full analysis record on run_settled for a successful run", async () => {
    const entries: LoggedEvent[] = [];
    let finish: (result: RunReportResult) => void = () => {};
    const { orchestrator } = makeHarness({
      runReportFn: () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
      log: capturingLogger(entries),
    });
    const document = documentOf(700);
    await orchestrator.admitRun(baseRequest({ documents: [document] }), NEVER_CANCELLED);
    finish(stubResult({ usage: USAGE }));
    await until(() => orchestrator.registry.get("e-1")?.status === "succeeded");
    const fields = settledEventOf(entries);
    expect(fields["status"]).toBe("succeeded");
    expect(fields["estimatedCostUsd"]).toBe(0.5);
    expect(typeof fields["durationMs"]).toBe("number");
    expect(fields["pausedMs"]).toBe(0);
    expect(fields["inputTokens"]).toBe(USAGE.inputTokens);
    expect(fields["outputTokens"]).toBe(USAGE.outputTokens);
    expect(fields["cacheCreationInputTokens"]).toBe(USAGE.cacheCreationInputTokens);
    expect(fields["cacheReadInputTokens"]).toBe(USAGE.cacheReadInputTokens);
    expect(fields["documentBytes"]).toBe(700);
    expect(fields["documentCount"]).toBe(1);
    expect(fields["model"]).toBe(PINNED_MODEL);
    expect(fields["runtimeVersion"]).toBe("0.0.0-test");
    expect(fields["workFingerprint"]).toMatch(/^[0-9a-f]{64}$/);
  });

  it("keeps the work fingerprint constant across executionIds and document sets", async () => {
    const entries: LoggedEvent[] = [];
    const { orchestrator } = makeHarness({
      runReportFn: (params) => Promise.resolve(stubResult({ executionId: params.executionId })),
      log: capturingLogger(entries),
    });
    await orchestrator.admitRun(
      baseRequest({ executionId: "e-1", documents: [documentOf(700)] }),
      NEVER_CANCELLED,
    );
    await orchestrator.admitRun(
      baseRequest({ executionId: "e-2", documents: [documentOf(200)] }),
      NEVER_CANCELLED,
    );
    await until(() => orchestrator.registry.get("e-2")?.status === "succeeded");
    const fingerprints = entries
      .filter((entry) => entry.event === "run_settled")
      .map((entry) => entry.fields["workFingerprint"]);
    expect(fingerprints).toHaveLength(2);
    expect(fingerprints[0]).toBe(fingerprints[1]);
  });

  it("moves the work fingerprint when the prompt changes and when a skill file changes", async () => {
    const entries: LoggedEvent[] = [];
    const { orchestrator } = makeHarness({
      runReportFn: (params) => Promise.resolve(stubResult({ executionId: params.executionId })),
      log: capturingLogger(entries),
    });
    const skillBytes = new TextEncoder().encode("skill body");
    const skillFile = {
      name: "extract.md",
      bytes: skillBytes,
      sha256: sha256Hex(skillBytes),
      description: "how to extract",
    };
    await orchestrator.admitRun(baseRequest({ executionId: "e-1" }), NEVER_CANCELLED);
    await orchestrator.admitRun(baseRequest({ executionId: "e-2", prompt: "different" }), NEVER_CANCELLED);
    await orchestrator.admitRun(
      baseRequest({ executionId: "e-3", skillFiles: [skillFile] }),
      NEVER_CANCELLED,
    );
    await until(() => orchestrator.registry.get("e-3")?.status === "succeeded");
    const fingerprints = entries
      .filter((entry) => entry.event === "run_settled")
      .map((entry) => entry.fields["workFingerprint"]);
    expect(new Set(fingerprints).size).toBe(3);
  });

  it("answers payload conflict when a repeat post changes only a skill asset", async () => {
    const { orchestrator } = makeHarness({
      runReportFn: () => new Promise(() => {}),
    });
    const skillBytes = new TextEncoder().encode("skill body");
    const withAsset = (assetText: string): Partial<StartRunRequest> => {
      const assetBytes = new TextEncoder().encode(assetText);
      return {
        skillFiles: [
          {
            name: "verify.md",
            bytes: skillBytes,
            sha256: sha256Hex(skillBytes),
            assets: [{ name: "verify.py", bytes: assetBytes, sha256: sha256Hex(assetBytes) }],
          },
        ],
      };
    };
    const first = await orchestrator.admitRun(baseRequest(withAsset("print(1)")), NEVER_CANCELLED);
    expect(first.kind).toBe("started");
    const changed = await orchestrator.admitRun(baseRequest(withAsset("print(2)")), NEVER_CANCELLED);
    expect(changed.kind).toBe("conflict");
    const same = await orchestrator.admitRun(baseRequest(withAsset("print(1)")), NEVER_CANCELLED);
    expect(same.kind).toBe("duplicate");
  });

  it("moves the work fingerprint when only a skill asset changes", async () => {
    const entries: LoggedEvent[] = [];
    const { orchestrator } = makeHarness({
      runReportFn: (params) => Promise.resolve(stubResult({ executionId: params.executionId })),
      log: capturingLogger(entries),
    });
    const skillBytes = new TextEncoder().encode("skill body");
    const requestWithAsset = (executionId: string, assetText: string): StartRunRequest => {
      const assetBytes = new TextEncoder().encode(assetText);
      return baseRequest({
        executionId,
        skillFiles: [
          {
            name: "verify.md",
            bytes: skillBytes,
            sha256: sha256Hex(skillBytes),
            assets: [{ name: "verify.py", bytes: assetBytes, sha256: sha256Hex(assetBytes) }],
          },
        ],
      });
    };
    await orchestrator.admitRun(requestWithAsset("e-1", "print(1)"), NEVER_CANCELLED);
    await orchestrator.admitRun(requestWithAsset("e-2", "print(2)"), NEVER_CANCELLED);
    await until(() => orchestrator.registry.get("e-2")?.status === "succeeded");
    const fingerprints = entries
      .filter((entry) => entry.event === "run_settled")
      .map((entry) => entry.fields["workFingerprint"]);
    expect(new Set(fingerprints).size).toBe(2);
  });

  it("charges skill asset bytes against the retained-input budget", async () => {
    const { orchestrator, ledger } = makeHarness({
      runReportFn: () => new Promise(() => {}),
    });
    const skillBytes = new Uint8Array(100);
    const assetBytes = new Uint8Array(40);
    await orchestrator.admitRun(
      baseRequest({
        skillFiles: [
          {
            name: "verify.md",
            bytes: skillBytes,
            sha256: sha256Hex(skillBytes),
            assets: [{ name: "verify.py", bytes: assetBytes, sha256: sha256Hex(assetBytes) }],
          },
        ],
      }),
      NEVER_CANCELLED,
    );
    expect(ledger.retainedInputBytes()).toBe(140);
  });

  it("fingerprints the effective model, so naming the pinned model matches leaving it defaulted", async () => {
    const entries: LoggedEvent[] = [];
    const { orchestrator } = makeHarness({
      runReportFn: (params) => Promise.resolve(stubResult({ executionId: params.executionId })),
      log: capturingLogger(entries),
    });
    await orchestrator.admitRun(baseRequest({ executionId: "e-1" }), NEVER_CANCELLED);
    await orchestrator.admitRun(baseRequest({ executionId: "e-2", model: PINNED_MODEL }), NEVER_CANCELLED);
    await until(() => orchestrator.registry.get("e-2")?.status === "succeeded");
    const fingerprints = entries
      .filter((entry) => entry.event === "run_settled")
      .map((entry) => entry.fields["workFingerprint"]);
    expect(fingerprints[0]).toBe(fingerprints[1]);
  });

  it("emits the per-run EMF series dimensioned by terminal status at settle", async () => {
    const gauges: GaugeCall[] = [];
    let finish: (result: RunReportResult) => void = () => {};
    const { orchestrator } = makeHarness({
      runReportFn: () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
      metrics: capturingMetrics(gauges),
    });
    await orchestrator.admitRun(baseRequest({ documents: [documentOf(700)] }), NEVER_CANCELLED);
    finish(stubResult({ usage: USAGE }));
    await until(() => orchestrator.registry.get("e-1")?.status === "succeeded");
    const settled = gauges.filter((call) => call.dimensions?.["status"] === "succeeded");
    const names = settled.map((call) => call.name).sort();
    expect(names).toEqual([
      "RunCostUsd",
      "RunDocumentBytes",
      "RunDurationMs",
      "RunInputTokens",
      "RunOutputTokens",
    ]);
    const inputTokens = settled.find((call) => call.name === "RunInputTokens");
    expect(inputTokens?.value).toBe(
      USAGE.inputTokens + USAGE.cacheCreationInputTokens + USAGE.cacheReadInputTokens,
    );
    const documentBytes = settled.find((call) => call.name === "RunDocumentBytes");
    expect(documentBytes?.value).toBe(700);
  });

  it("carries a failure's last-known usage onto run_settled", async () => {
    const entries: LoggedEvent[] = [];
    let fail: (error: Error) => void = () => {};
    const { orchestrator } = makeHarness({
      runReportFn: () =>
        new Promise((_resolve, reject) => {
          fail = reject;
        }),
      log: capturingLogger(entries),
    });
    await orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    fail(
      new RunFailedError("deadline", {
        subtype: "deadline_exceeded",
        estimatedCostUsd: 0.3,
        sessionId: null,
        usage: USAGE,
      }),
    );
    await until(() => orchestrator.registry.get("e-1")?.status === "failed");
    const fields = settledEventOf(entries);
    expect(fields["status"]).toBe("failed");
    expect(fields["subtype"]).toBe("deadline_exceeded");
    expect(fields["inputTokens"]).toBe(USAGE.inputTokens);
    expect(fields["outputTokens"]).toBe(USAGE.outputTokens);
  });
});

describe("terminal session bundle lifecycle", () => {
  it("keeps a finished run's bundle loadable after settle, then complete purges it", async () => {
    const bundle = sealCheckpoint(new TextEncoder().encode("finished state"));
    const harness = makeHarness({
      runReportFn: async (_params, options?: SpawnRunOptions) => {
        const saveTerminalCheckpoint = options?.onTerminalCheckpoint;
        if (saveTerminalCheckpoint !== undefined) {
          const saving: Promise<void> = saveTerminalCheckpoint({ sessionId: "s-1", checkpoint: bundle });
          await saving;
        }
        return stubResult();
      },
      retainedBytes: () => ({ artifactBytes: 100, traceBytes: 50 }),
    });
    await harness.orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    await until(() => harness.orchestrator.registry.get("e-1")?.status === "succeeded");
    await until(() => harness.ledger.retainedOutputBytes() === 150 + bundle.bytes.byteLength);
    expect(harness.checkpoints.saves).toEqual([terminalCheckpointKeyOf("e-1", 1)]);
    expect(harness.checkpoints.stored.size).toBe(1);
    const completed = await harness.orchestrator.completeRun("e-1");
    expect(completed.outcome).toBe("completed");
    await until(() => harness.checkpoints.stored.size === 0 && harness.ledger.retainedOutputBytes() === 0);
  });

  it("hands the store the same bytes the wire decode produced, without converting or hashing them again", async () => {
    const wire = toWireCheckpoint(sealCheckpoint(new TextEncoder().encode("finished state")));
    let decoded: CheckpointBundle | undefined;
    const harness = makeHarness({
      runReportFn: async (_params, options?: SpawnRunOptions) => {
        const saveTerminalCheckpoint = options?.onTerminalCheckpoint;
        if (saveTerminalCheckpoint !== undefined) {
          decoded = fromWireCheckpoint(wire);
          const saving: Promise<void> = saveTerminalCheckpoint({ sessionId: "s-1", checkpoint: decoded });
          await saving;
        }
        return stubResult();
      },
      retainedBytes: () => ({ artifactBytes: 0, traceBytes: 0 }),
    });
    await harness.orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    await until(() => harness.checkpoints.stored.size === 1);
    const stored = harness.checkpoints.stored.get("e-1.1.-1");
    expect(stored?.bytes).toBe(decoded?.bytes);
    expect(stored?.sha256).toBe(wire.sha256);
  });

  it("sheds the bundle when the shared budget lacks headroom and the run still succeeds", async () => {
    const oversized = sealCheckpoint(new Uint8Array(700));
    const entries: LoggedEvent[] = [];
    const harness = makeHarness({
      runReportFn: async (_params, options?: SpawnRunOptions) => {
        const saveTerminalCheckpoint = options?.onTerminalCheckpoint;
        if (saveTerminalCheckpoint !== undefined) {
          const saving: Promise<void> = saveTerminalCheckpoint({ sessionId: "s-1", checkpoint: oversized });
          await saving;
        }
        return stubResult();
      },
      log: capturingLogger(entries),
    });
    await harness.orchestrator.admitRun(baseRequest(), NEVER_CANCELLED);
    await until(() => harness.orchestrator.registry.get("e-1")?.status === "succeeded");
    expect(harness.checkpoints.saves).toEqual([]);
    expect(entries.some((entry) => entry.event === "terminal_checkpoint_shed")).toBe(true);
  });
});

describe("chat grant across the spawner's teardown paths", () => {
  // A child that delivers its result and is still alive at teardown, so the spawner kills it:
  // `signalSettles` is what the kill's pkill reports back.
  function finishingChild(signalSettles: boolean): ChildRun {
    return (input) => {
      let alive = true;
      let resolveExit: () => void = () => {};
      const exited = new Promise<{ code: number | null; signal: string | null; stderr: string }>(
        (resolve) => {
          resolveExit = () => resolve({ code: null, signal: "SIGTERM", stderr: "" });
        },
      );
      let killed = false;
      const handle: ChildHandle = {
        send: () => {},
        cargo: new Writable({
          write(_chunk, _encoding, callback) {
            callback();
          },
        }),
        cargoTeardown: new AbortController().signal,
        kill: () => {
          killed = true;
          alive = false;
          resolveExit();
        },
        isAlive: () => alive,
        exited,
        signalsSettled: () => Promise.resolve(!killed || signalSettles),
      };
      queueMicrotask(() =>
        input.onFrame(
          JSON.stringify({
            kind: "final",
            response: {
              ok: true,
              result: {
                executionId: "e-1",
                output: "done",
                estimatedCostUsd: 0.1,
                sessionId: "s-1",
                usage: USAGE,
              },
            },
          }),
        ),
      );
      return handle;
    };
  }

  function spawnerFor(input: {
    pool: UidPool;
    signalSettles: boolean;
    sweep: () => Promise<void>;
  }): SpawnRunFn {
    return createRunSpawner({
      pool: input.pool,
      childRun: finishingChild(input.signalSettles),
      workspaceRoot: "/workspaces",
      subprocessEnv: { PATH: "/usr/bin" },
      modelProvider: { provider: "bedrock", identity: { ambient: true } },
      buildIdentity: { version: "0.0.0-test", revision: "testrev" },
      documentStore: {
        save: () => Promise.resolve(),
        openInputs: () => Promise.resolve(undefined),
        delete: () => Promise.resolve(),
      },
      sweepUidResidue: input.sweep,
      waitForUidIdle: () => Promise.resolve(true),
      claimRunSubtrees: () => {},
      logger: silentLogger(),
    });
  }

  const signalledSweepFailure = (): RunAsUidError =>
    new RunAsUidError("setpriv uid 20001 find was killed by SIGTERM", {
      code: null,
      signal: "SIGTERM",
      stderr: "",
    });

  const cases: {
    name: string;
    signalSettles: boolean;
    sweeps: (() => Promise<void>)[];
    released: boolean;
  }[] = [
    { name: "a lost signal leaves the uid unswept", signalSettles: false, sweeps: [], released: false },
    {
      name: "a failed sweep retains the uid dirty",
      signalSettles: true,
      sweeps: [() => Promise.reject(new Error("find exited with 1"))],
      released: false,
    },
    {
      name: "a sweep a signal killed is retried and the uid released",
      signalSettles: true,
      sweeps: [() => Promise.reject(signalledSweepFailure()), () => Promise.resolve()],
      released: true,
    },
  ];

  for (const testCase of cases) {
    it(`returns the chat slot exactly once when ${testCase.name}`, async () => {
      const pool = createUidPool({ baseUid: 20001, size: 1 });
      let sweepCalls = 0;
      const spawner = spawnerFor({
        pool,
        signalSettles: testCase.signalSettles,
        sweep: () => {
          const sweep = testCase.sweeps[sweepCalls] ?? (() => Promise.reject(new Error("unexpected sweep")));
          sweepCalls += 1;
          return sweep();
        },
      });
      const { orchestrator, calls } = makeHarness({ runReportFn: spawner });
      await orchestrator.admitRun(baseRequest({ admissionClass: "chat" }), NEVER_CANCELLED, {
        heldChatGrant: true,
      });
      await until(() => calls.slotWakes === 1);
      await new Promise((resolve) => setTimeout(resolve, 10));
      expect(calls.chatReleases).toBe(1);
      expect(sweepCalls).toBe(testCase.sweeps.length);
      expect(pool.available()).toBe(testCase.released ? 1 : 0);
    });
  }
});
