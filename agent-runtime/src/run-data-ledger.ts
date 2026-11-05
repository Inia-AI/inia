import {
  type ByteReservation,
  createByteBudget,
  type InstrumentedByteBudget,
  instrumentByteBudget,
} from "./byte-budget.js";
import {
  type CheckpointBundle,
  type CheckpointKey,
  type CheckpointStore,
  terminalCheckpointKeyOf,
} from "./checkpoint-store.js";
import type { DocumentStore } from "./document-store.js";
import { toError } from "./errors.js";
import type { Logger } from "./logger.js";
import type { RunFilesView } from "./run-files.js";
import { RunFailedError, type RunFailure } from "./run-report.js";

export type RunDataLedgerDeps = {
  retainedInputBudgetBytes: number;
  retainedOutputBudgetBytes: number;
  checkpointStore: CheckpointStore;
  documentStore: DocumentStore;
  runFiles: RunFilesView;
  log: Logger;
};

// measuredBytes is set only on a live within-budget verdict, so it is safe to
// hand straight to settleRetainedOutputCharge to skip a redundant re-measurement.
export type RetainedBudgetCheck = {
  overBudget?: RunFailedError;
  measuredBytes?: number;
};

export type TerminalCheckpointSaveOutcome = "saved" | "budget_refused";

export type RunDataLedger = {
  reserveRetainedInput(executionId: string, bytes: number): boolean;
  reserveRetainedOutput(executionId: string, capBytes: number): boolean;
  retainedInputBytes(): number;
  retainedOutputBytes(): number;
  awaitPendingRelease(executionId: string): Promise<void>;
  releaseRunData(executionId: string): Promise<void>;
  settleRetainedOutputCharge(executionId: string, attempt: number, premeasuredBytes?: number): Promise<void>;
  checkRetainedBudget(
    executionId: string,
    attempt: number,
    spend: Pick<RunFailure, "estimatedCostUsd" | "sessionId">,
  ): Promise<RetainedBudgetCheck>;
  saveCheckpoint(key: CheckpointKey, bundle: CheckpointBundle): Promise<void>;
  recordCheckpoint(executionId: string, key: CheckpointKey): void;
  discardCheckpointKey(executionId: string, key: CheckpointKey): void;
  discardCheckpoints(executionId: string): void;
  saveTerminalCheckpoint(
    executionId: string,
    attempt: number,
    bundle: CheckpointBundle,
  ): Promise<TerminalCheckpointSaveOutcome>;
  discardTerminalCheckpoint(executionId: string): void;
};

export function createRunDataLedger(deps: RunDataLedgerDeps): RunDataLedger {
  const { checkpointStore, documentStore, runFiles, log } = deps;
  const retainedInputs = instrumentByteBudget(
    createByteBudget(deps.retainedInputBudgetBytes),
    "retained_input",
    log,
  );
  const retainedInputReservations = new Map<string, ByteReservation>();
  const retainedOutputs = instrumentByteBudget(
    createByteBudget(deps.retainedOutputBudgetBytes),
    "retained_output",
    log,
  );
  const retainedOutputReservations = new Map<string, ByteReservation>();
  const savedCheckpoints = new Map<string, CheckpointKey[]>();
  // Kept apart from the pause checkpoints: the settle-time discard must spare the terminal
  // bundle (the finished state a later resume starts from), while every run-data release
  // must take it. Its bytes carry their own retained-output charge, reserved before the
  // save, so the declared-cap verdict over artifacts + trace never sees them.
  const terminalCheckpoints = new Map<string, { key: CheckpointKey; reservation: ByteReservation }>();

  function reserve(
    budget: InstrumentedByteBudget,
    reservations: Map<string, ByteReservation>,
    executionId: string,
    bytes: number,
  ): boolean {
    const reservation = budget.tryReserve(bytes, { executionId });
    if (reservation === undefined) {
      return false;
    }
    reservations.set(executionId, reservation);
    return true;
  }

  function releaseReservation(reservations: Map<string, ByteReservation>, key: string): void {
    const reservation = reservations.get(key);
    if (reservation !== undefined) {
      reservations.delete(key);
      reservation.release();
    }
  }

  function deleteCheckpointKey(executionId: string, key: CheckpointKey): void {
    checkpointStore.delete(key).catch((error) => {
      log.error("checkpoint_delete_failed", { executionId, segment: key.segment, error: toError(error) });
    });
  }

  async function saveTerminalCheckpoint(
    executionId: string,
    attempt: number,
    bundle: CheckpointBundle,
  ): Promise<TerminalCheckpointSaveOutcome> {
    // A run whose retained-output reservation is gone has been released — refusing here keeps
    // the safety structural: a save can never land bytes past the run's data lifecycle, however
    // the caller's ordering drifts.
    if (!retainedOutputReservations.has(executionId)) {
      return "budget_refused";
    }
    const reservation = retainedOutputs.tryReserve(bundle.bytes.byteLength, { executionId });
    if (reservation === undefined) {
      return "budget_refused";
    }
    const key = terminalCheckpointKeyOf(executionId, attempt);
    try {
      await checkpointStore.save(key, bundle);
    } catch (error) {
      reservation.release();
      throw error;
    }
    const superseded = terminalCheckpoints.get(executionId);
    terminalCheckpoints.set(executionId, { key, reservation });
    if (superseded !== undefined) {
      superseded.reservation.release();
      if (superseded.key.attempt !== key.attempt) {
        deleteCheckpointKey(executionId, superseded.key);
      }
    }
    return "saved";
  }

  function discardTerminalCheckpoint(executionId: string): void {
    const entry = terminalCheckpoints.get(executionId);
    if (entry === undefined) {
      return;
    }
    terminalCheckpoints.delete(executionId);
    entry.reservation.release();
    deleteCheckpointKey(executionId, entry.key);
  }

  function discardCheckpoints(executionId: string): void {
    const keys = savedCheckpoints.get(executionId);
    if (keys === undefined) {
      return;
    }
    savedCheckpoints.delete(executionId);
    for (const key of keys) {
      deleteCheckpointKey(executionId, key);
    }
  }

  function measureRetainedTotalBytes(executionId: string, attempt: number): Promise<number> {
    return runFiles
      .retainedBytes(executionId, attempt)
      .then((totals) => totals.artifactBytes + totals.traceBytes);
  }

  // A rejected predecessor release is awaited then ignored, not inherited by the next task.
  const pendingReleases = new Map<string, Promise<void>>();

  function enqueueRunDataTask(executionId: string, task: () => Promise<void>): Promise<void> {
    const previous = pendingReleases.get(executionId) ?? Promise.resolve();
    const release = previous.catch(() => {}).then(task);
    pendingReleases.set(executionId, release);
    const settled = release.catch(() => {});
    void settled.then(() => {
      if (pendingReleases.get(executionId) === release) {
        pendingReleases.delete(executionId);
      }
    });
    return release;
  }

  function releaseRunData(executionId: string): Promise<void> {
    return enqueueRunDataTask(executionId, async () => {
      discardCheckpoints(executionId);
      discardTerminalCheckpoint(executionId);
      // Each budget is freed as its own half settles (the output purge can wait for its owner uid's
      // lease to end; the staged inputs need no uid), but the task settles only after both: a
      // successor waiting on it must not reserve while a half is still running for this id.
      const halves = await Promise.allSettled([
        documentStore.delete(executionId).finally(() => {
          releaseReservation(retainedInputReservations, executionId);
        }),
        runFiles.purge(executionId).finally(() => {
          releaseReservation(retainedOutputReservations, executionId);
        }),
      ]);
      for (const half of halves) {
        if (half.status === "rejected") {
          throw toError(half.reason);
        }
      }
    });
  }

  // Liveness is checked when the purge runs, not when scheduled, so a purge queued
  // behind a predecessor's release can't reach a retried successor's data. The
  // charge is settled either side of the purge so a failed purge's stranded bytes
  // stay described.
  function tearDownOverBudgetRun(
    executionId: string,
    attempt: number,
    reservation: ByteReservation,
    measuredTotalBytes: number,
  ): Promise<void> {
    return enqueueRunDataTask(executionId, async () => {
      if (!reservation.live) {
        return;
      }
      reservation.settleTo(measuredTotalBytes);
      discardCheckpoints(executionId);
      discardTerminalCheckpoint(executionId);
      await Promise.all([documentStore.delete(executionId), runFiles.purgeArtifacts(executionId)]);
      releaseReservation(retainedInputReservations, executionId);
      const remaining = await runFiles.retainedBytes(executionId, attempt);
      reservation.settleTo(remaining.artifactBytes + remaining.traceBytes);
    });
  }

  function settleRetainedOutputCharge(
    executionId: string,
    attempt: number,
    premeasuredBytes?: number,
  ): Promise<void> {
    const reservation = retainedOutputReservations.get(executionId);
    if (reservation === undefined) {
      return Promise.resolve();
    }
    if (premeasuredBytes !== undefined && reservation.live && premeasuredBytes <= reservation.bytes) {
      reservation.settleTo(premeasuredBytes);
      return Promise.resolve();
    }
    return measureRetainedTotalBytes(executionId, attempt)
      .then(async (retainedBytes) => {
        if (!reservation.live) {
          return;
        }
        if (retainedBytes > reservation.bytes) {
          await tearDownOverBudgetRun(executionId, attempt, reservation, retainedBytes).catch((error) => {
            log.error("retained_output_teardown_failed", { executionId, error: toError(error) });
          });
          return;
        }
        reservation.settleTo(retainedBytes);
      })
      .catch((error) => {
        log.error("retained_output_measure_failed", { executionId, error: toError(error) });
      });
  }

  // Fail-open on a measurement error: the terminal settle re-measures and tears
  // down whatever it finds over.
  async function checkRetainedBudget(
    executionId: string,
    attempt: number,
    spend: Pick<RunFailure, "estimatedCostUsd" | "sessionId">,
  ): Promise<RetainedBudgetCheck> {
    const reservation = retainedOutputReservations.get(executionId);
    if (reservation === undefined) {
      return {};
    }
    let retainedBytes: number;
    try {
      retainedBytes = await measureRetainedTotalBytes(executionId, attempt);
    } catch (error) {
      log.error("retained_output_measure_failed", { executionId, error: toError(error) });
      return {};
    }
    if (!reservation.live) {
      return {};
    }
    if (retainedBytes <= reservation.bytes) {
      return { measuredBytes: retainedBytes };
    }
    return {
      overBudget: new RunFailedError(
        `run retained ${retainedBytes} bytes, over its ${reservation.bytes}-byte reservation`,
        {
          subtype: "retained_budget_exceeded",
          estimatedCostUsd: spend.estimatedCostUsd,
          sessionId: spend.sessionId,
        },
      ),
    };
  }

  return {
    reserveRetainedInput: (executionId, bytes) =>
      reserve(retainedInputs, retainedInputReservations, executionId, bytes),
    reserveRetainedOutput: (executionId, capBytes) =>
      reserve(retainedOutputs, retainedOutputReservations, executionId, capBytes),
    retainedInputBytes: () => retainedInputs.reservedBytes(),
    retainedOutputBytes: () => retainedOutputs.reservedBytes(),
    async awaitPendingRelease(executionId) {
      try {
        await pendingReleases.get(executionId);
      } catch {
        // a failed predecessor release must not block a new run
      }
    },
    releaseRunData,
    settleRetainedOutputCharge,
    checkRetainedBudget,
    saveCheckpoint: (key, bundle) => checkpointStore.save(key, bundle),
    recordCheckpoint(executionId, key) {
      const superseded = savedCheckpoints.get(executionId) ?? [];
      savedCheckpoints.set(executionId, [key]);
      for (const stale of superseded) {
        if (stale.segment !== key.segment) {
          deleteCheckpointKey(executionId, stale);
        }
      }
    },
    discardCheckpointKey: deleteCheckpointKey,
    discardCheckpoints,
    saveTerminalCheckpoint,
    discardTerminalCheckpoint,
  };
}
