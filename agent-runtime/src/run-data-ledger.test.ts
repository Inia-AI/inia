import { describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  type CheckpointKey,
  type CheckpointStore,
  checkpointKeyOf,
  sealCheckpoint,
  terminalCheckpointKeyOf,
} from "./checkpoint-store.js";
import { NO_SERVED_FILES } from "./checkpoint-store-test-helpers.js";
import type { DocumentStore } from "./document-store.js";
import type { Logger } from "./logger.js";
import { createRunDataLedger, type RunDataLedgerDeps } from "./run-data-ledger.js";
import {
  ARTIFACTS_MANIFEST_NAME,
  artifactAttemptDirOf,
  createRunFilesView,
  publishRunArtifacts,
  type RetainedByteTotals,
  type RunFilesView,
} from "./run-files.js";
import { removeOwnTree } from "./run-files-test-helpers.js";
import { RunFailedError } from "./run-report.js";

const ATTEMPT = 1;

type LoggedEvent = { level: string; event: string; fields: Record<string, unknown> };

function stubLogger(): { log: Logger; events: LoggedEvent[] } {
  const events: LoggedEvent[] = [];
  const record =
    (level: string) =>
    (event: string, fields: Record<string, unknown> = {}): void => {
      events.push({ level, event, fields });
    };
  const log: Logger = {
    debug: record("debug"),
    info: record("info"),
    warn: record("warn"),
    error: record("error"),
    with: () => log,
  };
  return { log, events };
}

function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void; reject: (e: Error) => void } {
  let resolve: (value: T) => void = () => {};
  let reject: (e: Error) => void = () => {};
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

type LedgerHarness = {
  ledger: ReturnType<typeof createRunDataLedger>;
  events: LoggedEvent[];
  calls: {
    documentDeletes: string[];
    filePurges: string[];
    artifactPurges: string[];
    checkpointDeletes: CheckpointKey[];
    checkpointSaves: CheckpointKey[];
    retainedBytesReads: number;
  };
};

function makeLedger(
  overrides: {
    retainedInputBudgetBytes?: number;
    retainedOutputBudgetBytes?: number;
    retainedBytes?: RunFilesView["retainedBytes"];
    documentDelete?: DocumentStore["delete"];
    filePurge?: RunFilesView["purge"];
    checkpointDelete?: CheckpointStore["delete"];
    checkpointSave?: CheckpointStore["save"];
  } = {},
): LedgerHarness {
  const { log, events } = stubLogger();
  const calls: LedgerHarness["calls"] = {
    documentDeletes: [],
    filePurges: [],
    artifactPurges: [],
    checkpointDeletes: [],
    checkpointSaves: [],
    retainedBytesReads: 0,
  };
  const checkpointStore: CheckpointStore = {
    ...NO_SERVED_FILES,
    save: (key, bundle) => {
      calls.checkpointSaves.push(key);
      return overrides.checkpointSave === undefined
        ? Promise.resolve()
        : overrides.checkpointSave(key, bundle);
    },
    load: () => Promise.resolve(undefined),
    delete: (key) => {
      calls.checkpointDeletes.push(key);
      return overrides.checkpointDelete === undefined ? Promise.resolve() : overrides.checkpointDelete(key);
    },
  };
  const documentStore: DocumentStore = {
    save: () => Promise.resolve(),
    openInputs: () => Promise.resolve(undefined),
    delete: (executionId) => {
      calls.documentDeletes.push(executionId);
      return overrides.documentDelete === undefined
        ? Promise.resolve()
        : overrides.documentDelete(executionId);
    },
  };
  const runFiles: RunFilesView = {
    list: () => Promise.resolve([]),
    retainedBytes: (executionId, attempt) => {
      calls.retainedBytesReads += 1;
      return overrides.retainedBytes === undefined
        ? Promise.resolve({ artifactBytes: 0, traceBytes: 0 })
        : overrides.retainedBytes(executionId, attempt);
    },
    resolve: () => Promise.resolve(undefined),
    purge: (executionId) => {
      calls.filePurges.push(executionId);
      return overrides.filePurge === undefined ? Promise.resolve() : overrides.filePurge(executionId);
    },
    purgeArtifacts: (executionId) => {
      calls.artifactPurges.push(executionId);
      return Promise.resolve();
    },
  };
  const deps: RunDataLedgerDeps = {
    retainedInputBudgetBytes: overrides.retainedInputBudgetBytes ?? 1000,
    retainedOutputBudgetBytes: overrides.retainedOutputBudgetBytes ?? 1000,
    checkpointStore,
    documentStore,
    runFiles,
    log,
  };
  return { ledger: createRunDataLedger(deps), events, calls };
}

describe("run-data ledger reservations", () => {
  it("charges retained output at admission and refuses past the budget", () => {
    const { ledger } = makeLedger({ retainedOutputBudgetBytes: 1000 });
    expect(ledger.reserveRetainedOutput("a", 600)).toBe(true);
    expect(ledger.reserveRetainedOutput("b", 600)).toBe(false);
    expect(ledger.retainedOutputBytes()).toBe(600);
  });

  it("charges retained input at admission and refuses past the budget", () => {
    const { ledger } = makeLedger({ retainedInputBudgetBytes: 500 });
    expect(ledger.reserveRetainedInput("a", 300)).toBe(true);
    expect(ledger.reserveRetainedInput("b", 300)).toBe(false);
    expect(ledger.retainedInputBytes()).toBe(300);
  });
});

describe("release path (complete, reap, retryable failure)", () => {
  it("frees both reservations, discards checkpoints, and purges documents and files", async () => {
    const { ledger, calls } = makeLedger();
    ledger.reserveRetainedInput("a", 300);
    ledger.reserveRetainedOutput("a", 400);
    ledger.recordCheckpoint("a", checkpointKeyOf("a", ATTEMPT, 0));
    await ledger.releaseRunData("a");
    expect(ledger.retainedInputBytes()).toBe(0);
    expect(ledger.retainedOutputBytes()).toBe(0);
    expect(calls.checkpointDeletes).toEqual([checkpointKeyOf("a", ATTEMPT, 0)]);
    expect(calls.documentDeletes).toEqual(["a"]);
    expect(calls.filePurges).toEqual(["a"]);
  });

  it("releases exactly once: a second release is a no-op that cannot free other runs' bytes", async () => {
    const { ledger } = makeLedger({ retainedOutputBudgetBytes: 1000 });
    ledger.reserveRetainedOutput("a", 400);
    ledger.reserveRetainedOutput("b", 400);
    await ledger.releaseRunData("a");
    expect(ledger.retainedOutputBytes()).toBe(400);
    await ledger.releaseRunData("a");
    expect(ledger.retainedOutputBytes()).toBe(400);
  });

  it("frees the reservations even when the purge fails, and surfaces the failure", async () => {
    const { ledger } = makeLedger({
      documentDelete: () => Promise.reject(new Error("tmpfs gone")),
    });
    ledger.reserveRetainedInput("a", 300);
    ledger.reserveRetainedOutput("a", 400);
    await expect(ledger.releaseRunData("a")).rejects.toThrow("tmpfs gone");
    expect(ledger.retainedInputBytes()).toBe(0);
    expect(ledger.retainedOutputBytes()).toBe(0);
  });

  it("serializes releases per executionId and a failed predecessor does not block the next", async () => {
    const firstDelete = deferred<void>();
    let deleteCount = 0;
    const { ledger } = makeLedger({
      documentDelete: () => {
        deleteCount += 1;
        return deleteCount === 1 ? firstDelete.promise : Promise.resolve();
      },
    });
    const first = ledger.releaseRunData("a");
    const second = ledger.releaseRunData("a");
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(deleteCount).toBe(1);
    firstDelete.reject(new Error("boom"));
    await expect(first).rejects.toThrow("boom");
    await second;
    expect(deleteCount).toBe(2);
  });

  it("settles only once both halves have, so a successor never reserves under a purge still running", async () => {
    const purge = deferred<void>();
    const { ledger } = makeLedger({
      documentDelete: () => Promise.reject(new Error("tmpfs gone")),
      filePurge: () => purge.promise,
    });
    ledger.reserveRetainedInput("a", 300);
    ledger.reserveRetainedOutput("a", 400);
    const release = ledger.releaseRunData("a");
    let drained = false;
    const successorWait = ledger.awaitPendingRelease("a").then(() => {
      drained = true;
    });
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(drained).toBe(false);
    expect(ledger.retainedInputBytes()).toBe(0);
    expect(ledger.retainedOutputBytes()).toBe(400);

    purge.resolve();
    await expect(release).rejects.toThrow("tmpfs gone");
    await successorWait;
    ledger.reserveRetainedOutput("a", 250);
    expect(ledger.retainedOutputBytes()).toBe(250);
  });

  it("a pending release is awaitable so a successor can order behind it, even when it fails", async () => {
    const firstDelete = deferred<void>();
    const { ledger } = makeLedger({ documentDelete: () => firstDelete.promise });
    const release = ledger.releaseRunData("a");
    const drained = ledger.awaitPendingRelease("a");
    firstDelete.reject(new Error("boom"));
    await expect(release).rejects.toThrow("boom");
    await drained;
    await ledger.awaitPendingRelease("never-released");
  });
});

describe("terminal settle path", () => {
  it("settles the charge down to the measured bytes when within the declared cap", async () => {
    const { ledger, calls } = makeLedger({
      retainedBytes: () => Promise.resolve({ artifactBytes: 100, traceBytes: 50 }),
    });
    ledger.reserveRetainedOutput("a", 400);
    await ledger.settleRetainedOutputCharge("a", ATTEMPT);
    expect(ledger.retainedOutputBytes()).toBe(150);
    expect(calls.artifactPurges).toEqual([]);
    expect(calls.filePurges).toEqual([]);
  });

  it("leaves the charge as declared and logs when the measurement fails", async () => {
    const { ledger, events } = makeLedger({
      retainedBytes: () => Promise.reject(new Error("walk failed")),
    });
    ledger.reserveRetainedOutput("a", 400);
    await ledger.settleRetainedOutputCharge("a", ATTEMPT);
    expect(ledger.retainedOutputBytes()).toBe(400);
    expect(events.some((e) => e.event === "retained_output_measure_failed")).toBe(true);
  });

  it("does nothing without a reservation", async () => {
    const { ledger, calls } = makeLedger();
    await ledger.settleRetainedOutputCharge("a", ATTEMPT);
    expect(calls.retainedBytesReads).toBe(0);
  });

  it("a stale predecessor settle is inert on a retried successor's reservation", async () => {
    const measure = deferred<RetainedByteTotals>();
    let reads = 0;
    const { ledger } = makeLedger({
      retainedBytes: () => {
        reads += 1;
        return reads === 1 ? measure.promise : Promise.resolve({ artifactBytes: 0, traceBytes: 0 });
      },
    });
    ledger.reserveRetainedOutput("a", 400);
    const settling = ledger.settleRetainedOutputCharge("a", ATTEMPT);
    await ledger.releaseRunData("a");
    ledger.reserveRetainedOutput("a", 400);
    measure.resolve({ artifactBytes: 10, traceBytes: 0 });
    await settling;
    expect(ledger.retainedOutputBytes()).toBe(400);
  });
});

describe("over-budget teardown path", () => {
  it("purges deliverables, spares the trace, frees the input charge, and keeps the trace bytes charged", async () => {
    let reads = 0;
    const { ledger, calls } = makeLedger({
      retainedBytes: () => {
        reads += 1;
        return Promise.resolve(
          reads === 1 ? { artifactBytes: 500, traceBytes: 200 } : { artifactBytes: 0, traceBytes: 200 },
        );
      },
    });
    ledger.reserveRetainedInput("a", 300);
    ledger.reserveRetainedOutput("a", 400);
    ledger.recordCheckpoint("a", checkpointKeyOf("a", ATTEMPT, 0));
    await ledger.settleRetainedOutputCharge("a", ATTEMPT);
    expect(calls.artifactPurges).toEqual(["a"]);
    expect(calls.filePurges).toEqual([]);
    expect(calls.documentDeletes).toEqual(["a"]);
    expect(calls.checkpointDeletes).toEqual([checkpointKeyOf("a", ATTEMPT, 0)]);
    expect(ledger.retainedInputBytes()).toBe(0);
    expect(ledger.retainedOutputBytes()).toBe(200);
  });

  it("keeps spared bytes charged past the declared cap when the trace alone exceeds it", async () => {
    const { ledger } = makeLedger({
      retainedBytes: () => Promise.resolve({ artifactBytes: 0, traceBytes: 600 }),
    });
    ledger.reserveRetainedOutput("a", 400);
    await ledger.settleRetainedOutputCharge("a", ATTEMPT);
    expect(ledger.retainedOutputBytes()).toBe(600);
  });
});

describe("checkRetainedBudget", () => {
  const spend = { estimatedCostUsd: 1.25, sessionId: "s-1" };

  it("hands back the measurement within the declared cap, with no error", async () => {
    const { ledger } = makeLedger({
      retainedBytes: () => Promise.resolve({ artifactBytes: 100, traceBytes: 0 }),
    });
    ledger.reserveRetainedOutput("a", 400);
    const check = await ledger.checkRetainedBudget("a", ATTEMPT, spend);
    expect(check.overBudget).toBeUndefined();
    expect(check.measuredBytes).toBe(100);
  });

  it("returns a retained_budget_exceeded failure carrying the run's spend when over, and no measurement", async () => {
    const { ledger } = makeLedger({
      retainedBytes: () => Promise.resolve({ artifactBytes: 500, traceBytes: 0 }),
    });
    ledger.reserveRetainedOutput("a", 400);
    const check = await ledger.checkRetainedBudget("a", ATTEMPT, spend);
    expect(check.overBudget).toBeInstanceOf(RunFailedError);
    expect(check.overBudget?.subtype).toBe("retained_budget_exceeded");
    expect(check.overBudget?.estimatedCostUsd).toBe(1.25);
    expect(check.overBudget?.sessionId).toBe("s-1");
    expect(check.measuredBytes).toBeUndefined();
  });

  it("fails open when the measurement fails, logging it and handing back nothing", async () => {
    const { ledger, events } = makeLedger({
      retainedBytes: () => Promise.reject(new Error("walk failed")),
    });
    ledger.reserveRetainedOutput("a", 400);
    const check = await ledger.checkRetainedBudget("a", ATTEMPT, spend);
    expect(check.overBudget).toBeUndefined();
    expect(check.measuredBytes).toBeUndefined();
    expect(events.some((e) => e.event === "retained_output_measure_failed")).toBe(true);
  });

  it("answers empty without a reservation and never measures", async () => {
    const { ledger, calls } = makeLedger();
    expect(await ledger.checkRetainedBudget("a", ATTEMPT, spend)).toEqual({});
    expect(calls.retainedBytesReads).toBe(0);
  });
});

describe("premeasured settle handoff", () => {
  it("settles from the handed measurement without re-walking the pack", async () => {
    const { ledger, calls } = makeLedger();
    ledger.reserveRetainedOutput("a", 400);
    await ledger.settleRetainedOutputCharge("a", ATTEMPT, 150);
    expect(ledger.retainedOutputBytes()).toBe(150);
    expect(calls.retainedBytesReads).toBe(0);
  });

  it("distrusts a handed measurement above the cap and walks for itself", async () => {
    let reads = 0;
    const { ledger, calls } = makeLedger({
      retainedBytes: () => {
        reads += 1;
        return Promise.resolve(
          reads === 1 ? { artifactBytes: 500, traceBytes: 200 } : { artifactBytes: 0, traceBytes: 200 },
        );
      },
    });
    ledger.reserveRetainedOutput("a", 400);
    await ledger.settleRetainedOutputCharge("a", ATTEMPT, 700);
    expect(calls.artifactPurges).toEqual(["a"]);
    expect(ledger.retainedOutputBytes()).toBe(200);
  });
});

describe("checkpoint bookkeeping", () => {
  it("saves through the store", async () => {
    const { ledger } = makeLedger();
    await ledger.saveCheckpoint(checkpointKeyOf("a", ATTEMPT, 0), sealCheckpoint(new Uint8Array([1])));
  });

  it("supersedes earlier segments only once the new bundle is recorded", () => {
    const { ledger, calls } = makeLedger();
    ledger.recordCheckpoint("a", checkpointKeyOf("a", ATTEMPT, 0));
    expect(calls.checkpointDeletes).toEqual([]);
    ledger.recordCheckpoint("a", checkpointKeyOf("a", ATTEMPT, 1));
    expect(calls.checkpointDeletes).toEqual([checkpointKeyOf("a", ATTEMPT, 0)]);
    ledger.recordCheckpoint("a", checkpointKeyOf("a", ATTEMPT, 1));
    expect(calls.checkpointDeletes).toEqual([checkpointKeyOf("a", ATTEMPT, 0)]);
  });

  it("discards all recorded checkpoints exactly once", () => {
    const { ledger, calls } = makeLedger();
    ledger.recordCheckpoint("a", checkpointKeyOf("a", ATTEMPT, 2));
    ledger.discardCheckpoints("a");
    expect(calls.checkpointDeletes).toEqual([checkpointKeyOf("a", ATTEMPT, 2)]);
    ledger.discardCheckpoints("a");
    expect(calls.checkpointDeletes).toEqual([checkpointKeyOf("a", ATTEMPT, 2)]);
  });

  it("discards a named key and logs a failed delete instead of throwing", async () => {
    const failure = deferred<void>();
    const { ledger, events, calls } = makeLedger({ checkpointDelete: () => failure.promise });
    ledger.discardCheckpointKey("a", checkpointKeyOf("a", ATTEMPT, 3));
    expect(calls.checkpointDeletes).toEqual([checkpointKeyOf("a", ATTEMPT, 3)]);
    failure.reject(new Error("store gone"));
    await Promise.resolve();
    await Promise.resolve();
    expect(events.some((e) => e.event === "checkpoint_delete_failed")).toBe(true);
  });
});

describe("retained-output budget over the real files view", () => {
  it("keeps resident published bytes charged when a kill between the publish renames left no manifest", async () => {
    const root = mkdtempSync(join(tmpdir(), "ledger-disk-truth-"));
    try {
      const artifactsRoot = join(root, "artifacts");
      const traceRoot = join(root, "traces");
      const workspacePath = join(root, "workspace");
      mkdirSync(artifactsRoot, { recursive: true });
      mkdirSync(traceRoot, { recursive: true });
      mkdirSync(join(workspacePath, "out"), { recursive: true });
      writeFileSync(join(workspacePath, "out", "report.md"), "# report");
      publishRunArtifacts({ artifactsRoot, executionId: "a", attempt: ATTEMPT, workspacePath });
      rmSync(join(artifactAttemptDirOf(artifactsRoot, "a", ATTEMPT), ARTIFACTS_MANIFEST_NAME));
      const view = createRunFilesView({ artifactsRoot, traceRoot, removeTree: removeOwnTree });
      const { ledger } = makeLedger({
        retainedBytes: (executionId, attempt) => view.retainedBytes(executionId, attempt),
      });
      expect(ledger.reserveRetainedOutput("a", 100)).toBe(true);
      await ledger.settleRetainedOutputCharge("a", ATTEMPT);
      expect(ledger.retainedOutputBytes()).toBe(8);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});

describe("terminal session bundle bookkeeping", () => {
  const bundle = sealCheckpoint(new TextEncoder().encode("x".repeat(64)));

  it("charges the retained-output budget and saves through the store", async () => {
    const { ledger, calls } = makeLedger();
    expect(ledger.reserveRetainedOutput("e-1", 400)).toBe(true);
    expect(await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle)).toBe("saved");
    expect(calls.checkpointSaves).toEqual([terminalCheckpointKeyOf("e-1", ATTEMPT)]);
    expect(ledger.retainedOutputBytes()).toBe(400 + 64);
  });

  it("refuses the bundle when the shared budget lacks headroom, saving nothing", async () => {
    const { ledger, calls } = makeLedger({ retainedOutputBudgetBytes: 460 });
    expect(ledger.reserveRetainedOutput("e-1", 400)).toBe(true);
    expect(await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle)).toBe("budget_refused");
    expect(calls.checkpointSaves).toEqual([]);
    expect(ledger.retainedOutputBytes()).toBe(400);
  });

  it("spares the terminal bundle when the settle discards the run's pause checkpoints", async () => {
    const { ledger, calls } = makeLedger();
    ledger.reserveRetainedOutput("e-1", 400);
    const pauseKey = checkpointKeyOf("e-1", ATTEMPT, 0);
    ledger.recordCheckpoint("e-1", pauseKey);
    await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle);
    ledger.discardCheckpoints("e-1");
    expect(calls.checkpointDeletes).toEqual([pauseKey]);
    expect(ledger.retainedOutputBytes()).toBe(400 + 64);
  });

  it("deletes the terminal bundle and frees its charge when the run's data is released", async () => {
    const { ledger, calls } = makeLedger();
    ledger.reserveRetainedOutput("e-1", 400);
    await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle);
    await ledger.releaseRunData("e-1");
    expect(calls.checkpointDeletes).toContainEqual(terminalCheckpointKeyOf("e-1", ATTEMPT));
    expect(ledger.retainedOutputBytes()).toBe(0);
  });

  it("tears the terminal bundle down with an over-budget run's artifacts", async () => {
    const { ledger, calls } = makeLedger({
      retainedBytes: () => Promise.resolve({ artifactBytes: 500, traceBytes: 100 }),
    });
    ledger.reserveRetainedOutput("e-1", 400);
    await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle);
    await ledger.settleRetainedOutputCharge("e-1", ATTEMPT);
    expect(calls.checkpointDeletes).toContainEqual(terminalCheckpointKeyOf("e-1", ATTEMPT));
  });

  it("keeps the declared-cap verdict blind to the bundle's own bytes", async () => {
    const { ledger } = makeLedger({
      retainedBytes: () => Promise.resolve({ artifactBytes: 390, traceBytes: 0 }),
    });
    ledger.reserveRetainedOutput("e-1", 400);
    await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle);
    const check = await ledger.checkRetainedBudget("e-1", ATTEMPT, {
      estimatedCostUsd: 0,
      sessionId: null,
    });
    expect(check.overBudget).toBeUndefined();
    expect(check.measuredBytes).toBe(390);
  });

  it("keeps the bundle's charge alongside the settled artifact charge", async () => {
    const { ledger } = makeLedger({
      retainedBytes: () => Promise.resolve({ artifactBytes: 100, traceBytes: 50 }),
    });
    ledger.reserveRetainedOutput("e-1", 400);
    await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle);
    await ledger.settleRetainedOutputCharge("e-1", ATTEMPT);
    expect(ledger.retainedOutputBytes()).toBe(150 + 64);
  });

  it("frees the charge when the store cannot save the bundle", async () => {
    const { ledger } = makeLedger({ checkpointSave: () => Promise.reject(new Error("disk full")) });
    ledger.reserveRetainedOutput("e-1", 400);
    await expect(ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle)).rejects.toThrow("disk full");
    expect(ledger.retainedOutputBytes()).toBe(400);
  });

  it("supersedes an earlier bundle's charge instead of stacking a second one", async () => {
    const { ledger } = makeLedger();
    ledger.reserveRetainedOutput("e-1", 400);
    await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle);
    const bigger = sealCheckpoint(new TextEncoder().encode("y".repeat(100)));
    await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bigger);
    expect(ledger.retainedOutputBytes()).toBe(400 + 100);
  });
});

describe("terminal session bundle after release", () => {
  it("refuses a bundle for a run whose data has already been released", async () => {
    const { ledger, calls } = makeLedger();
    ledger.reserveRetainedOutput("e-1", 400);
    await ledger.releaseRunData("e-1");
    const bundle = sealCheckpoint(new TextEncoder().encode("late"));
    expect(await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle)).toBe("budget_refused");
    expect(calls.checkpointSaves).toEqual([]);
    expect(ledger.retainedOutputBytes()).toBe(0);
  });

  it("refuses a bundle for a run that never reserved retained output", async () => {
    const { ledger, calls } = makeLedger();
    const bundle = sealCheckpoint(new TextEncoder().encode("unadmitted"));
    expect(await ledger.saveTerminalCheckpoint("e-1", ATTEMPT, bundle)).toBe("budget_refused");
    expect(calls.checkpointSaves).toEqual([]);
  });
});
