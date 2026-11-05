import { type AdmissionClass, type AdmissionQueue, DEFAULT_ADMISSION_CLASS } from "./admission-queue.js";
import { type CheckpointBundle, checkpointKeyOf } from "./checkpoint-store.js";
import type { RunFuseConfig } from "./config.js";
import type { DocumentStore } from "./document-store.js";
import { messageOf, toError } from "./errors.js";
import { sha256HexOfJson } from "./hash.js";
import type { Logger } from "./logger.js";
import { emitRunSettledMetrics, type Metrics } from "./metrics.js";
import type { ResolvedRunAgent } from "./run-agents.js";
import type { RunDataLedger } from "./run-data-ledger.js";
import {
  DOCUMENTS_AREA,
  RECARRIED_OUT_AREA,
  type RunDocument,
  SKILLS_AREA,
  type StagedInput,
} from "./run-documents.js";
import {
  type CancelOutcome,
  type CompleteOutcome,
  createRunRegistry,
  type EffectiveRunFuses,
  isRetryableFailure,
  isTerminalStatus,
  RETENTION_MS,
  type ResumeOutcome,
  type RunMonitorView,
  type RunRegistry,
  type RunStatus,
  type SettledRunFailure,
  type TerminalRunFacts,
} from "./run-registry.js";
import {
  type PauseFn,
  type PauseResolution,
  type RunEffort,
  RunFailedError,
  type RunOutputFormat,
  type RunSystemPrompt,
} from "./run-report.js";
import { continuationStartOf, type SpawnRunFn, type SpawnRunOptions } from "./run-spawner.js";

// Must never outlive the record it belongs to.
export const DEFAULT_PAUSE_TTL_MS = RETENTION_MS;

export type StartRunContinuation = {
  checkpoint: CheckpointBundle;
  outFiles: RunDocument[];
};

export type StartRunRequest = {
  executionId: string;
  prompt: string;
  runToken: string | null;
  documents: RunDocument[];
  skillFiles: RunDocument[];
  continuation?: StartRunContinuation;
  agents?: ResolvedRunAgent[];
  model?: string;
  outputFormat?: RunOutputFormat;
  systemPrompt?: RunSystemPrompt;
  mcpServerUrl?: string;
  maxTurns?: number;
  maxBudgetUsd?: number;
  deadlineMs?: number;
  effort?: RunEffort;
  maxRetainedOutputBytes?: number;
  admissionClass?: AdmissionClass;
};

// file.sha256 is always the server-recomputed hash, never trusted from the client, so it's safe to
// fingerprint on. A skill file's description joins its identity too: it changes what registers with the SDK.
function fingerprintOf(request: StartRunRequest): string {
  const identityOf = (file: RunDocument): { name: string; sha256: string } => ({
    name: file.name,
    sha256: file.sha256,
  });
  const skillIdentityOf = (
    file: RunDocument,
  ): {
    name: string;
    sha256: string;
    description: string | null;
    assets?: { name: string; sha256: string }[];
  } => ({
    ...identityOf(file),
    description: file.description ?? null,
    ...(file.assets === undefined ? {} : { assets: file.assets.map(identityOf) }),
  });
  return sha256HexOfJson({
    executionId: request.executionId,
    prompt: request.prompt,
    documents: request.documents.map(identityOf),
    skillFiles: request.skillFiles.map(skillIdentityOf),
    continuation:
      request.continuation === undefined
        ? null
        : {
            bundleSha256: request.continuation.checkpoint.sha256,
            outFiles: request.continuation.outFiles.map(identityOf),
          },
    agents: request.agents ?? null,
    model: request.model ?? null,
    outputFormat: request.outputFormat ?? null,
    systemPrompt: request.systemPrompt ?? null,
    mcpServerUrl: request.mcpServerUrl ?? null,
    maxTurns: request.maxTurns ?? null,
    maxBudgetUsd: request.maxBudgetUsd ?? null,
    deadlineMs: request.deadlineMs ?? null,
    effort: request.effort ?? null,
    maxRetainedOutputBytes: request.maxRetainedOutputBytes ?? null,
  });
}

// The trend's segmentation key: everything instruction-bearing that shapes how a run works —
// and nothing that legitimately varies per run (executionId, documents) or merely bounds the
// harness (deadline/budget/turn fuses). Same fingerprint = same work; compare within, never across.
function workFingerprintOf(request: StartRunRequest, effectiveModel: string): string {
  return sha256HexOfJson({
    prompt: request.prompt,
    systemPrompt: request.systemPrompt ?? null,
    model: effectiveModel,
    effort: request.effort ?? null,
    outputFormat: request.outputFormat ?? null,
    skillFiles: request.skillFiles.map((file) => ({
      name: file.name,
      sha256: file.sha256,
      description: file.description ?? null,
      ...(file.assets === undefined
        ? {}
        : { assets: file.assets.map((asset) => ({ name: asset.name, sha256: asset.sha256 })) }),
    })),
    platformTools: request.mcpServerUrl !== undefined,
  });
}

type AdmissionFacts = {
  workFingerprint: string;
  documentBytes: number;
  documentCount: number;
};

export type AdmitRunOutcome =
  | { kind: "draining" }
  | { kind: "conflict" }
  | { kind: "duplicate"; view: RunMonitorView }
  | { kind: "cancelled" }
  | { kind: "capacity"; reason: "retained_output_budget" | "retained_input_budget" }
  | { kind: "persist_failed" }
  | { kind: "superseded"; view: RunMonitorView | undefined }
  | { kind: "started"; view: RunMonitorView };

export type AdmissionGrant = { heldChatGrant: boolean };

const NO_GRANT: AdmissionGrant = { heldChatGrant: false };

export type RunOrchestratorDeps = {
  runReportFn: SpawnRunFn;
  ledger: RunDataLedger;
  documentStore: Pick<DocumentStore, "save">;
  admission: Pick<AdmissionQueue, "recordRunDuration" | "notifySlotFreed" | "releaseChatSlot">;
  log: Logger;
  metrics: Metrics;
  now?: () => number;
  fuses: RunFuseConfig;
  pinnedModel: string;
  runtimeVersion: string;
};

export type RunOrchestrator = {
  registry: RunRegistry;
  admitRun(
    request: StartRunRequest,
    cancelled: AbortSignal,
    grant?: AdmissionGrant,
  ): Promise<AdmitRunOutcome>;
  settleFailure(executionId: string, failure: SettledRunFailure): void;
  cancelRun(executionId: string): CancelOutcome;
  completeRun(executionId: string): Promise<CompleteOutcome>;
  resumeRun(
    executionId: string,
    resumeToken: string,
    questionId: string,
    resolution: PauseResolution,
  ): ResumeOutcome;
};

export function createRunOrchestrator(deps: RunOrchestratorDeps): RunOrchestrator {
  const { runReportFn, ledger, documentStore, admission, log, metrics } = deps;
  const now = deps.now ?? Date.now;
  type RunEntry = {
    registration: symbol;
    controller: AbortController;
    admissionClass: AdmissionClass;
    heldChatGrant: boolean;
    waiter?: { resolve: (resolution: PauseResolution) => void; reject: (reason: Error) => void } | undefined;
  };
  const runs = new Map<string, RunEntry>();
  const registrations = new Map<string, symbol>();
  const premeasuredAtFinish = new Map<string, { attempt: number; bytes: number }>();
  const admissionFacts = new Map<string, AdmissionFacts>();
  const registry: RunRegistry = createRunRegistry({
    ...(deps.now === undefined ? {} : { now: deps.now }),
    // Dimensioned by status, not failure subtype: a failure on a cancelling record settles `cancelled`.
    onTerminal: (executionId, status, facts: TerminalRunFacts) => {
      const view = registry.get(executionId);
      const admitted = admissionFacts.get(executionId);
      admissionFacts.delete(executionId);
      log.info("run_settled", {
        executionId,
        status,
        ...(view?.error === null || view?.error === undefined ? {} : { subtype: view.error.subtype }),
        estimatedCostUsd: facts.estimatedCostUsd,
        durationMs: facts.durationMs,
        pausedMs: facts.pausedMs,
        inputTokens: facts.usage.inputTokens,
        outputTokens: facts.usage.outputTokens,
        cacheCreationInputTokens: facts.usage.cacheCreationInputTokens,
        cacheReadInputTokens: facts.usage.cacheReadInputTokens,
        ...(admitted === undefined
          ? {}
          : {
              workFingerprint: admitted.workFingerprint,
              documentBytes: admitted.documentBytes,
              documentCount: admitted.documentCount,
            }),
        model: view?.model ?? deps.pinnedModel,
        runtimeVersion: deps.runtimeVersion,
      });
      metrics.count("RunsSettled", { dimensions: { status } });
      emitRunSettledMetrics(metrics, {
        status,
        durationMs: facts.durationMs,
        costUsd: facts.estimatedCostUsd,
        inputTokens:
          facts.usage.inputTokens + facts.usage.cacheCreationInputTokens + facts.usage.cacheReadInputTokens,
        outputTokens: facts.usage.outputTokens,
        documentBytes: admitted?.documentBytes ?? 0,
      });
      abandonRun(executionId, status);
      if (view !== undefined) {
        const premeasured = premeasuredAtFinish.get(executionId);
        void ledger.settleRetainedOutputCharge(
          executionId,
          view.attempt,
          premeasured !== undefined && premeasured.attempt === view.attempt ? premeasured.bytes : undefined,
        );
      }
    },
    onPruned: (executionId) => {
      registrations.delete(executionId);
      releaseRunDataInBackground(executionId);
    },
    orphanAgeMs: deps.fuses.deadlineMs.ceiling + DEFAULT_PAUSE_TTL_MS,
  });

  function releaseRunDataInBackground(executionId: string): void {
    ledger.releaseRunData(executionId).catch((error) => {
      log.error("run_data_purge_failed", { executionId, error: toError(error) });
    });
  }

  function abandonRun(executionId: string, status: RunStatus): void {
    const entry = runs.get(executionId);
    if (entry !== undefined) {
      runs.delete(executionId);
      entry.waiter?.reject(new Error(`run ${status}`));
      entry.controller.abort();
    }
    ledger.discardCheckpoints(executionId);
  }

  function settleFailure(executionId: string, failure: SettledRunFailure): void {
    registry.settleFailure(executionId, failure);
    const settled = registry.get(executionId);
    if (settled !== undefined && isRetryableFailure(settled)) {
      releaseRunDataInBackground(executionId);
    }
  }

  function isCurrent(executionId: string, entry: RunEntry): boolean {
    return registrations.get(executionId) === entry.registration;
  }

  function currentView(executionId: string, entry: RunEntry): RunMonitorView | undefined {
    return isCurrent(executionId, entry) ? registry.get(executionId) : undefined;
  }

  function settleFailedRun(executionId: string, entry: RunEntry, error: unknown): void {
    const superseded = !isCurrent(executionId, entry);
    const failure =
      error instanceof RunFailedError
        ? {
            subtype: error.subtype,
            estimatedCostUsd: error.estimatedCostUsd,
            ...(error.usage === undefined ? {} : { usage: error.usage }),
          }
        : { subtype: "internal_error", estimatedCostUsd: 0 };
    log.error("run_failed", {
      executionId,
      subtype: failure.subtype,
      estimatedCostUsd: failure.estimatedCostUsd,
      ...(error instanceof RunFailedError ? { sessionId: error.sessionId } : {}),
      ...(superseded ? { superseded } : {}),
      error: toError(error),
    });
    if (!superseded) {
      settleFailure(executionId, failure);
    }
  }

  function returnChatGrant(entry: RunEntry): void {
    if (entry.heldChatGrant) {
      entry.heldChatGrant = false;
      admission.releaseChatSlot();
    }
  }

  function startBackgroundRun(request: StartRunRequest, grant: AdmissionGrant, registration: symbol): void {
    const { executionId, prompt } = request;
    const admissionClass = request.admissionClass ?? DEFAULT_ADMISSION_CLASS;
    const entry: RunEntry = {
      registration,
      controller: new AbortController(),
      admissionClass,
      heldChatGrant: grant.heldChatGrant,
    };
    runs.set(executionId, entry);
    const runToken =
      request.mcpServerUrl === undefined || request.runToken === null ? undefined : request.runToken;
    const options: SpawnRunOptions = {
      ...(request.continuation === undefined
        ? {}
        : { continuation: continuationStartOf(request.continuation.checkpoint) }),
      ...(request.agents === undefined ? {} : { agents: request.agents }),
      ...(request.model === undefined ? {} : { model: request.model }),
      ...(request.outputFormat === undefined ? {} : { outputFormat: request.outputFormat }),
      ...(request.systemPrompt === undefined ? {} : { systemPrompt: request.systemPrompt }),
      ...(request.mcpServerUrl === undefined ? {} : { mcpServerUrl: request.mcpServerUrl }),
      ...(runToken === undefined ? {} : { runToken }),
      ...(request.maxTurns === undefined ? {} : { maxTurns: request.maxTurns }),
      ...(request.maxBudgetUsd === undefined ? {} : { maxBudgetUsd: request.maxBudgetUsd }),
      ...(request.deadlineMs === undefined ? {} : { deadlineMs: request.deadlineMs }),
      ...(request.effort === undefined ? {} : { effort: request.effort }),
      ...(request.maxRetainedOutputBytes === undefined
        ? {}
        : { maxRetainedOutputBytes: request.maxRetainedOutputBytes }),
    };
    log.info("run_started", {
      executionId,
      admissionClass,
      ...(request.model === undefined ? {} : { model: request.model }),
    });
    metrics.count("RunsStarted");
    void runToCompletion(executionId, prompt, entry, options);
  }

  // Suspended for the run's whole life, and a suspended async frame keeps every binding in
  // its scope reachable — so this function receives only what the run needs, never the
  // StartRunRequest, whose decoded document payloads would otherwise stay pinned until the
  // run settles.
  async function runToCompletion(
    executionId: string,
    prompt: string,
    entry: RunEntry,
    spawnOptions: SpawnRunOptions,
  ): Promise<void> {
    const pause: PauseFn = (input) =>
      new Promise<PauseResolution>((resolve, reject) => {
        void (async () => {
          const view = currentView(executionId, entry);
          if (view === undefined) {
            reject(new Error("run can no longer pause"));
            return;
          }
          // Checked before the checkpoint lands: an over-cap run fails here, not after parking bytes.
          const pauseCheck = await ledger.checkRetainedBudget(executionId, view.attempt, input);
          if (pauseCheck.overBudget !== undefined) {
            reject(pauseCheck.overBudget);
            return;
          }
          const key = checkpointKeyOf(executionId, view.attempt, view.segment);
          if (!isCurrent(executionId, entry)) {
            reject(new Error("run can no longer pause"));
            return;
          }
          try {
            await ledger.saveCheckpoint(key, input.checkpoint);
          } catch (error) {
            reject(new Error(`checkpoint save failed: ${messageOf(error)}`));
            return;
          }
          // Terminal teardown may have already run mid-save — a bundle landing now is deleted here or never.
          const afterSave = currentView(executionId, entry);
          if (afterSave === undefined || isTerminalStatus(afterSave.status)) {
            ledger.discardCheckpointKey(executionId, key);
            reject(new Error("run can no longer pause"));
            return;
          }
          ledger.recordCheckpoint(executionId, key);
          const resumeToken = registry.markAwaitingInput(
            executionId,
            input.question,
            DEFAULT_PAUSE_TTL_MS,
            input.estimatedCostUsd,
          );
          if (resumeToken === undefined) {
            reject(new Error("run can no longer pause"));
            return;
          }
          log.info("run_paused", {
            executionId,
            segment: view.segment,
            estimatedCostUsd: input.estimatedCostUsd,
          });
          metrics.count("RunsPaused");
          entry.waiter = { resolve, reject };
        })();
      });
    const startedAt = now();
    let heldNoSlot = false;
    try {
      const running = runReportFn(
        { executionId, prompt },
        {
          ...spawnOptions,
          signal: entry.controller.signal,
          pause,
          ...(entry.heldChatGrant
            ? {
                onUidReleased: () => {
                  returnChatGrant(entry);
                },
              }
            : {}),
          onTerminalCheckpoint: async (input) => {
            const view = currentView(executionId, entry);
            if (view === undefined) {
              return;
            }
            const bytes = input.checkpoint.bytes.byteLength;
            const outcome = await ledger.saveTerminalCheckpoint(executionId, view.attempt, input.checkpoint);
            if (outcome === "budget_refused") {
              log.warn("terminal_checkpoint_shed", { executionId, bytes });
              return;
            }
            log.info("terminal_checkpoint_saved", { executionId, bytes });
          },
        },
      );
      const result = await running;
      const finishedView = currentView(executionId, entry);
      if (finishedView !== undefined) {
        const finishCheck = await ledger.checkRetainedBudget(executionId, finishedView.attempt, result);
        if (finishCheck.overBudget !== undefined) {
          throw finishCheck.overBudget;
        }
        if (finishCheck.measuredBytes !== undefined && isCurrent(executionId, entry)) {
          premeasuredAtFinish.set(executionId, {
            attempt: finishedView.attempt,
            bytes: finishCheck.measuredBytes,
          });
        }
      }
      const superseded = !isCurrent(executionId, entry);
      log.info("run_finished", {
        executionId,
        estimatedCostUsd: result.estimatedCostUsd,
        sessionId: result.sessionId,
        durationMs: now() - startedAt,
        ...(superseded ? { superseded } : {}),
      });
      if (!superseded) {
        try {
          registry.markSucceeded(executionId, result);
        } finally {
          premeasuredAtFinish.delete(executionId);
        }
      }
    } catch (error) {
      heldNoSlot = error instanceof RunFailedError && error.subtype === "uid_pool_exhausted";
      settleFailedRun(executionId, entry, error);
    } finally {
      if (runs.get(executionId) === entry) {
        runs.delete(executionId);
      }
      // Excluded: an exhaustion failure never held a slot, and its near-zero ms would skew the p75.
      if (!heldNoSlot) {
        admission.recordRunDuration(now() - startedAt, entry.admissionClass);
      }
      returnChatGrant(entry);
      admission.notifySlotFreed();
    }
  }

  async function admitRun(
    request: StartRunRequest,
    cancelled: AbortSignal,
    grant: AdmissionGrant = NO_GRANT,
  ): Promise<AdmitRunOutcome> {
    const effectiveFuses: EffectiveRunFuses = {
      deadlineMs: request.deadlineMs ?? deps.fuses.deadlineMs.defaultValue,
      maxBudgetUsd: request.maxBudgetUsd ?? deps.fuses.maxBudgetUsd.defaultValue,
      maxTurns: request.maxTurns ?? deps.fuses.maxTurns.defaultValue,
      maxRetainedOutputBytes:
        request.maxRetainedOutputBytes ?? deps.fuses.maxRetainedOutputBytes.defaultValue,
    };
    const outcome = registry.register({
      executionId: request.executionId,
      payloadFingerprint: fingerprintOf(request),
      runToken: request.runToken,
      fuses: effectiveFuses,
      ...(request.model === undefined ? {} : { model: request.model }),
      ...(request.mcpServerUrl === undefined ? {} : { mcpServerUrl: request.mcpServerUrl }),
    });
    if (outcome.outcome === "draining") {
      return { kind: "draining" };
    }
    if (outcome.outcome === "conflict") {
      return { kind: "conflict" };
    }
    if (outcome.outcome === "duplicate") {
      return { kind: "duplicate", view: outcome.view };
    }
    const registration = Symbol(request.executionId);
    registrations.set(request.executionId, registration);
    admissionFacts.set(request.executionId, {
      workFingerprint: workFingerprintOf(request, request.model ?? deps.pinnedModel),
      documentBytes: request.documents.reduce((total, file) => total + file.bytes.byteLength, 0),
      documentCount: request.documents.length,
    });
    // A reaped predecessor's in-flight release must drain first, or its purge deletes the fresh run's data.
    await ledger.awaitPendingRelease(request.executionId);
    if (cancelled.aborted) {
      return { kind: "cancelled" };
    }
    if (!ledger.reserveRetainedOutput(request.executionId, effectiveFuses.maxRetainedOutputBytes)) {
      settleFailure(request.executionId, { subtype: "capacity_rejected", estimatedCostUsd: 0 });
      return { kind: "capacity", reason: "retained_output_budget" };
    }
    // Must be durable before start: cold resume re-stages from the store copy, so a failed stage fails here.
    const recarriedOutFiles = request.continuation?.outFiles ?? [];
    const stagedInputBytes = [...request.documents, ...request.skillFiles, ...recarriedOutFiles].reduce(
      (total, file) =>
        total +
        file.bytes.byteLength +
        (file.assets ?? []).reduce((assetTotal, asset) => assetTotal + asset.bytes.byteLength, 0),
      0,
    );
    if (stagedInputBytes > 0) {
      // Reserved before bytes are committed, so a tmpfs-exhausting burst is refused while still in memory.
      if (!ledger.reserveRetainedInput(request.executionId, stagedInputBytes)) {
        settleFailure(request.executionId, { subtype: "capacity_rejected", estimatedCostUsd: 0 });
        return { kind: "capacity", reason: "retained_input_budget" };
      }
    }
    if (request.documents.length > 0 || request.skillFiles.length > 0 || recarriedOutFiles.length > 0) {
      try {
        const staged: StagedInput[] = [
          ...request.documents.map((document): StagedInput => ({ ...document, area: DOCUMENTS_AREA })),
          ...request.skillFiles.map((skillFile): StagedInput => ({ ...skillFile, area: SKILLS_AREA })),
          ...recarriedOutFiles.map((outFile): StagedInput => ({ ...outFile, area: RECARRIED_OUT_AREA })),
        ];
        await documentStore.save(request.executionId, staged);
        if (cancelled.aborted) {
          return { kind: "cancelled" };
        }
      } catch (error) {
        log.error("run_documents_persist_failed", {
          executionId: request.executionId,
          error: toError(error),
        });
        settleFailure(request.executionId, { subtype: "internal_error", estimatedCostUsd: 0 });
        return { kind: "persist_failed" };
      }
    }
    // Catches a concurrent client cancel, not the handler-deadline signal above.
    const afterAwait = registry.get(request.executionId);
    if (afterAwait === undefined || afterAwait.status !== "running") {
      if (afterAwait?.status === "cancelling") {
        settleFailure(request.executionId, { subtype: "aborted", estimatedCostUsd: 0 });
      }
      return { kind: "superseded", view: registry.get(request.executionId) };
    }
    startBackgroundRun(request, grant, registration);
    return { kind: "started", view: outcome.view };
  }

  return {
    registry,
    admitRun,
    settleFailure,
    cancelRun(executionId) {
      const outcome = registry.requestCancel(executionId);
      if (outcome.outcome === "cancelling") {
        log.info("run_cancel_requested", { executionId });
        runs.get(executionId)?.controller.abort();
      }
      return outcome;
    },
    completeRun(executionId) {
      const outcome = registry.markCompleted(executionId);
      if (outcome.outcome === "completed") {
        releaseRunDataInBackground(executionId);
        log.info("run_completed", { executionId });
      }
      return Promise.resolve(outcome);
    },
    resumeRun(executionId, resumeToken, questionId, resolution) {
      const outcome = registry.markResumed(executionId, resumeToken, questionId);
      if (outcome.outcome === "ok") {
        const entry = runs.get(executionId);
        entry?.waiter?.resolve(resolution);
        if (entry !== undefined) {
          entry.waiter = undefined;
        }
        log.info("run_resumed", { executionId });
      }
      return outcome;
    },
  };
}
