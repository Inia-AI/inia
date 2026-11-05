import { spawn } from "node:child_process";
import { join } from "node:path";
import { Readable, Writable } from "node:stream";
import { type AsyncQueue, createAsyncQueue } from "./async-queue.js";
import { withoutStaticCredentials } from "./aws-profile.js";
import { type CheckpointBundle, type CheckpointStore, checkpointKeyOf } from "./checkpoint-store.js";
import {
  type BuildIdentity,
  CHILD_GRACE_MS,
  DEFAULT_MAX_BUDGET_USD,
  DEFAULT_MAX_TURNS,
  DEFAULT_PAUSE_GRACE_MINUTES,
  DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
  DEFAULT_RUN_DEADLINE_MS,
  MAX_CHILD_FRAME_LENGTH,
  MAX_CHILD_STDERR_TAIL_BYTES,
  MAX_TOTAL_DOCUMENT_BYTES,
  MS_PER_MINUTE,
  RUN_CHILD_TASK_LIMIT,
  SHARED_TMP_ROOT,
} from "./config.js";
import { CARGO_FD, type CargoSource, writeCargo } from "./document-channel.js";
import type { DocumentStore, StoredInput } from "./document-store.js";
import { messageOf, toError } from "./errors.js";
import { readPersistedFinalResponse } from "./final-response-file.js";
import { createLogger, type Logger } from "./logger.js";
import { MODEL_PROVIDER_ENV_VAR, type ModelProviderConfig } from "./model-provider.js";
import {
  awaitProcessEnd,
  type ProcessEndOptions,
  type SpawnStderrPiped,
  spawnStderrPiped,
} from "./process-end.js";
import type { ResolvedRunAgent } from "./run-agents.js";
import {
  copyDocumentMeta,
  DOCUMENTS_AREA,
  RECARRIED_OUT_AREA,
  type StagedFileMeta,
} from "./run-documents.js";
import { FIRST_ATTEMPT } from "./run-registry.js";
import {
  type PauseResolution,
  RunFailedError,
  type RunOutputFormat,
  type RunReportOptions,
  type RunReportParams,
  type RunReportResult,
  type RunSystemPrompt,
} from "./run-report.js";
import {
  createLineSplitter,
  fromWireCheckpoint,
  parseWireChildFrame,
  RESULT_UNDELIVERED_EXIT_CODE,
  toWireCheckpointDescriptor,
  type WireAnswerFrame,
  type WireCheckpointDescriptor,
  type WirePausedFrame,
  type WireRequest,
  type WireResponse,
  type WireTerminalCheckpointFrame,
} from "./run-wire.js";
import { privilegeDropArgs, RunAsUidError, runAsUid, signalRunArgv } from "./setpriv.js";
import { type UidPool, UidPoolExhaustedError } from "./uid-pool.js";
import { isPidDeadOrZombie, type UidIdleWait, waitForUidIdle } from "./uid-processes.js";
import type { TokenUsage } from "./usage-cost.js";
import { ARTIFACTS_DIR_NAME, claimRunSubtree, retainedRootsUnder } from "./workspace-layout.js";

const ONE_SHOT_ENTRY = "src/run-one-shot.ts";

const spawnerLog = createLogger();

const CARGO_DEATH_POLL_MS = 250;

// A close still missing this long after a known death is Bun dropping the event, and `exited`
// settles from the death facts instead.
const MISSED_CLOSE_GRACE_MS = 2_000;

const SIGKILL_ESCALATION_MS = 5_000;

// A pkill ends in milliseconds; one still alive after this long is treated as unsettled.
const SIGNAL_SETTLE_TIMEOUT_MS = 10_000;

// Teardown keeps waitForUidIdle's own budget; a cold resume is about to put a new child on the
// uid, so a straggler slow to die after the KILL gets longer before the resumed run fails.
const COLD_RESUME_IDLE_BUDGET_MS = 10_000;

export type ChildDeathFacts = {
  code: number | null;
  signal: string | null;
};

// Tells the reader of a failure report that the stderr tail may be missing the stream's end.
export const MISSED_CLOSE_STDERR_NOTICE =
  "the child's close event was never delivered; the run settled from the death facts";

// Settles `exited` when Bun never delivers 'close'. Every death fact arms it, and the settle
// gets the richest facts any arm delivered, so a dropped 'close' does not turn a clean exit
// into a null one.
export function createMissedCloseBelt(input: { graceMs: number; settle: (facts: ChildDeathFacts) => void }): {
  arm(facts?: ChildDeathFacts): void;
  cancel(): void;
} {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let deathFacts: ChildDeathFacts = { code: null, signal: null };
  return {
    arm(facts) {
      // Copied, and only when it adds information — an all-null arm must not erase real
      // facts, and a caller mutating its object after the arm must not rewrite history.
      if (facts !== undefined && (facts.code !== null || facts.signal !== null)) {
        deathFacts = { code: facts.code, signal: facts.signal };
      }
      if (timer === undefined) {
        timer = setTimeout(() => input.settle(deathFacts), input.graceMs);
        timer.unref();
      }
    },
    cancel() {
      clearTimeout(timer);
    },
  };
}

export type TerminalCheckpointFn = (input: {
  sessionId: string;
  checkpoint: CheckpointBundle;
}) => Promise<void>;

// The bundle sits behind a one-shot take because it must not be pinned for the run's life: at
// 64 MiB per bundle, a pool's worth of continuations would dwarf the supervisor's memory
// reserve. Building the first frame empties it.
export type ContinuationStart = { takeCheckpoint(): CheckpointBundle };

// One chunk, zero copies: a Buffer view over the bundle's own bytes.
function checkpointCargoSourceOf(bundle: CheckpointBundle): CargoSource {
  const bytes = Buffer.from(bundle.bytes.buffer, bundle.bytes.byteOffset, bundle.bytes.byteLength);
  return { sizeBytes: bytes.byteLength, open: () => Readable.from([bytes]) };
}

// The two halves of the supervisor→child bundle contract, minted together so a resume can
// never declare a bundle without delivering it, or deliver it anywhere but first.
function checkpointWireOf(
  bundle: CheckpointBundle,
  cargo: readonly CargoSource[],
): { checkpoint: WireCheckpointDescriptor; cargo: CargoSource[] } {
  return {
    checkpoint: toWireCheckpointDescriptor(bundle),
    cargo: [checkpointCargoSourceOf(bundle), ...cargo],
  };
}

export function continuationStartOf(checkpoint: CheckpointBundle): ContinuationStart {
  let held: CheckpointBundle | undefined = checkpoint;
  return {
    takeCheckpoint() {
      if (held === undefined) {
        throw new Error("continuation checkpoint already taken");
      }
      const bundle = held;
      held = undefined;
      return bundle;
    },
  };
}

export type SpawnRunOptions = Pick<
  RunReportOptions,
  "signal" | "pause" | "deadlineMs" | "maxBudgetUsd" | "maxTurns" | "effort"
> & {
  onTerminalCheckpoint?: TerminalCheckpointFn;
  // Called in the same tick the uid returns to the pool, so a slot-holding count kept beside the
  // pool never lags the pool's own free count.
  onUidReleased?: () => void;
  continuation?: ContinuationStart;
  agents?: ResolvedRunAgent[];
  maxRetainedOutputBytes?: number;
  model?: string;
  outputFormat?: RunOutputFormat;
  systemPrompt?: RunSystemPrompt;
  mcpServerUrl?: string;
  runToken?: string;
};

export type SpawnRunFn = (params: RunReportParams, options?: SpawnRunOptions) => Promise<RunReportResult>;

export type ChildExit = {
  code: number | null;
  signal: string | null;
  stderr: string;
};

export type ChildHandle = {
  send(line: string): void;
  cargo: Writable;
  cargoTeardown: AbortSignal;
  kill(): void;
  isAlive(): boolean;
  exited: Promise<ChildExit>;
  // Settles once every signal sent to the child's uid has finished: true when each one's end
  // was seen, false when any was lost. A `pkill -u` still in flight when the child exits on
  // its own would otherwise land on whatever runs as that uid next: the residue sweep, or a
  // cold-resume child.
  signalsSettled(): Promise<boolean>;
};

export type ChildRun = (input: {
  uid: number;
  argv: string[];
  env: Record<string, string>;
  signal: AbortSignal;
  onFrame: (line: string) => void;
  maxFrameLength?: number;
  logger?: Logger;
  signalOptions?: SignalRunOptions;
}) => ChildHandle;

export type SweptRoot = { path: string; retain: readonly string[] };

export type UidResidueSweep = (input: { uid: number; roots: readonly SweptRoot[] }) => Promise<void>;

export type ClaimRunSubtrees = (executionId: string) => void;

export type RunSpawnerDeps = {
  pool: UidPool;
  workspaceRoot: string;
  subprocessEnv: Record<string, string>;
  // Required, no ambient default — a missing wiring must fail the build, not silently drop the run child to host credentials.
  modelProvider: ModelProviderConfig;
  // Required for the same reason: an unstamped bundle is indistinguishable from one written
  // before stamps existed, and the platform reads that as "degrade to scratch". A wiring this
  // quiet has to fail the build rather than the next redo.
  buildIdentity: BuildIdentity;
  childRun?: ChildRun;
  deadlineMs?: number;
  maxBudgetUsd?: number;
  maxTurns?: number;
  checkpointStore?: CheckpointStore;
  documentStore: DocumentStore;
  documentBudgetBytes?: number;
  pauseGraceMs?: number;
  childGraceMs?: number;
  tmpRoot?: string;
  sweepUidResidue?: UidResidueSweep;
  waitForUidIdle?: UidIdleWait;
  // When set it replaces tmpRoot, sweepUidResidue and waitForUidIdle.
  uidSweeper?: UidSweeper;
  claimRunSubtrees?: ClaimRunSubtrees;
  logger?: Logger;
};

function privilegeDroppingArgv(uid: number): string[] {
  return [
    "setpriv",
    ...privilegeDropArgs(uid),
    "--",
    "prlimit",
    `--nproc=${RUN_CHILD_TASK_LIMIT}:${RUN_CHILD_TASK_LIMIT}`,
    "--",
    "bun",
    ONE_SHOT_ENTRY,
  ];
}

export const sweepUidResidue: UidResidueSweep = (input) =>
  runAsUid(input.uid, [
    "find",
    ...input.roots.map((root) => root.path),
    "-mindepth",
    "1",
    "-maxdepth",
    "1",
    ...input.roots.flatMap((root) => root.retain.flatMap((path) => ["!", "-path", path])),
    "-uid",
    String(input.uid),
    "-exec",
    "rm",
    "-rf",
    "{}",
    "+",
  ]);

// pkill exits 1 when nothing matched, the ordinary already-dead case.
export function signalDidNotLand(code: number | null): boolean {
  return code === null || code > 1;
}

export type SignalRunOptions = {
  spawnProcess?: SpawnStderrPiped;
  processEnd?: ProcessEndOptions;
};

// Never rejects. Resolves false when the signaller's end could not be established: until it
// is gone it can still kill whatever runs as this uid next, so false keeps the uid out of the pool.
export async function signalRun(
  uid: number,
  signal: "KILL" | "TERM",
  log: Logger,
  options: SignalRunOptions = {},
): Promise<boolean> {
  const [command, ...args] = signalRunArgv(uid, signal);
  if (command === undefined) {
    return true;
  }
  try {
    const killer = (options.spawnProcess ?? spawnStderrPiped)(command, args);
    const ending = awaitProcessEnd(killer, { timeoutMs: SIGNAL_SETTLE_TIMEOUT_MS, ...options.processEnd });
    let stderr = "";
    killer.stderr?.setEncoding("utf8");
    killer.stderr?.on("data", (chunk: string) => {
      stderr += chunk;
    });
    const end = await ending;
    if (end.kind === "failed") {
      log.error("run_signal_failed", { uid, signal, error: end.error });
      return true;
    }
    // A signaller seen dead can no longer signal anything, whatever happened to its events.
    if (end.kind === "lost" && end.dead) {
      log.warn("run_signal_end_unreported", { uid, signal, reason: end.reason });
      return true;
    }
    if (end.kind === "lost") {
      log.error("run_signal_unsettled", { uid, signal, reason: end.reason });
      return false;
    }
    if (signalDidNotLand(end.code)) {
      log.error("run_signal_failed", {
        uid,
        signal,
        code: end.code,
        killerSignal: end.signal,
        stderr: stderr.trim(),
      });
    }
    return true;
  } catch (error) {
    // Thrown before any signaller process existed (EMFILE, say), so nothing is in flight.
    log.error("run_signal_failed", { uid, signal, error: toError(error) });
    return true;
  }
}

// GNU find reports an -exec child killed by a signal on stderr and exits 1, so a signal that
// hit `rm` rather than `find` reads only there.
const EXEC_CHILD_SIGNALLED = /terminated by signal \d+/;

function sweepWasSignalled(error: unknown): boolean {
  return error instanceof RunAsUidError && (error.signal !== null || EXEC_CHILD_SIGNALLED.test(error.stderr));
}

// A byte cap can land mid-character; drop orphaned continuation bytes so the decoded tail
// doesn't open with a replacement character.
function dropPartialLeadingCharacter(bytes: Buffer): Buffer {
  let start = 0;
  while (start < bytes.length) {
    const byte = bytes[start];
    if (byte === undefined || (byte & 0xc0) !== 0x80) {
      break;
    }
    start += 1;
  }
  return bytes.subarray(start);
}

function tailBytes(bytes: Buffer): Buffer {
  if (bytes.length <= MAX_CHILD_STDERR_TAIL_BYTES) {
    return bytes;
  }
  // Copied, not viewed — a subarray would retain the whole oversized allocation this cap exists to bound.
  return Buffer.from(dropPartialLeadingCharacter(bytes.subarray(-MAX_CHILD_STDERR_TAIL_BYTES)));
}

export const spawnChildRun: ChildRun = (input) => {
  const log = input.logger ?? spawnerLog;
  const [command, ...args] = input.argv;
  if (command === undefined) {
    throw new Error("child argv must name a command");
  }
  const child = spawn(command, args, {
    env: input.env,
    detached: true,
    stdio: ["pipe", "pipe", "pipe", "pipe"],
  });
  const cargo = child.stdio[CARGO_FD];
  if (!(cargo instanceof Writable)) {
    throw new Error("child stdio[3] did not open as the writable cargo pipe");
  }
  // Like stdin below: a child dying mid-stream may surface as an 'error' on this pipe,
  // and an unhandled 'error' event would crash the supervisor hosting every run.
  cargo.on("error", () => {});
  let stderrTail: Buffer = Buffer.alloc(0);
  let alive = true;
  const stderrText = (): string => stderrTail.toString("utf8");
  // Accumulated as bytes, not decoded per chunk — decoding per chunk would cut characters at chunk boundaries too.
  const noteStderr = (text: string): void => {
    stderrTail = tailBytes(Buffer.from([stderrText().trim(), text].filter((part) => part !== "").join(" ")));
  };
  let resolveExited: (exit: ChildExit) => void = () => {};
  const exited = new Promise<ChildExit>((resolve) => {
    resolveExited = resolve;
  });
  const missedCloseBelt = createMissedCloseBelt({
    graceMs: MISSED_CLOSE_GRACE_MS,
    settle: (facts) => {
      if (!alive) {
        return;
      }
      alive = false;
      log.warn("child_close_event_missed", { uid: input.uid });
      noteStderr(MISSED_CLOSE_STDERR_NOTICE);
      resolveExited({ code: facts.code, signal: facts.signal, stderr: stderrText().trim() });
    },
  });
  // The abort is the whole teardown the cargo writer needs: it settles every pending write,
  // which releases the store-file source stream, the fd whose leak pins tmpfs bytes.
  const cargoTeardown = new AbortController();
  const tearDownCargo = (facts?: ChildDeathFacts): void => {
    if (!cargoTeardown.signal.aborted) {
      cargoTeardown.abort();
      clearInterval(cargoDeathPoll);
    }
    missedCloseBelt.arm(facts);
  };
  const signalsInFlight = new Set<Promise<boolean>>();
  let signalLost = false;
  // Once the child is torn down nobody awaits signals any more, so none are sent: the uid's
  // stragglers are the idle check's job.
  const sendSignal = (signal: "KILL" | "TERM"): void => {
    if (!alive) {
      return;
    }
    const sending = signalRun(input.uid, signal, log, input.signalOptions);
    signalsInFlight.add(sending);
    void sending.then((settled) => {
      signalsInFlight.delete(sending);
      if (!settled) {
        signalLost = true;
      }
    });
  };
  child.on("exit", (code, signal) => tearDownCargo({ code, signal }));
  // Destroyed only at 'close': destroying at 'exit' races Bun's own stdio accounting and can
  // stall the 'close' event. The abort has already settled every pending write.
  child.on("close", () => cargo.destroy());
  const cargoDeathPoll = setInterval(() => {
    const pid = child.pid;
    if (pid === undefined) {
      return;
    }
    // An exotic /proc error must not become an uncaught throw in the supervisor hosting
    // every run; the 'exit' event remains the primary teardown, so the belt just retires.
    try {
      if (isPidDeadOrZombie(pid)) {
        tearDownCargo();
      }
    } catch (error) {
      clearInterval(cargoDeathPoll);
      log.error("cargo_death_poll_failed", { uid: input.uid, error: toError(error) });
    }
  }, CARGO_DEATH_POLL_MS);
  cargoDeathPoll.unref();
  child.stdout.setEncoding("utf8");
  // This reader faces the child→supervisor direction, whose frames still carry a base64
  // bundle — so its default is the child cap, not the (smaller) supervisor-direction cap.
  const feedFrames = createLineSplitter(input.onFrame, input.maxFrameLength ?? MAX_CHILD_FRAME_LENGTH);
  child.stdout.on("data", (chunk: string) => {
    // An uncaught throw here would crash the supervisor hosting every run; kill only the offending child instead.
    try {
      feedFrames(chunk);
    } catch (error) {
      noteStderr(toError(error).message);
      sendSignal("KILL");
    }
  });
  child.stderr.on("data", (chunk: Buffer) => {
    stderrTail = tailBytes(Buffer.concat([stderrTail, chunk]));
  });
  child.stdin.on("error", () => {});
  child.on("close", (code, signal) => {
    alive = false;
    missedCloseBelt.cancel();
    resolveExited({ code, signal, stderr: stderrText().trim() });
  });
  child.on("error", (error) => {
    // An abort fires 'error' before 'close', which still follows with the real signal — only a spawn
    // that never produced a pid settles here.
    if (child.pid === undefined) {
      alive = false;
      tearDownCargo();
      resolveExited({
        code: null,
        signal: null,
        stderr: [stderrText().trim(), error.message].join(" ").trim(),
      });
    }
  });
  const abortTheRun = (): void => sendSignal("KILL");
  input.signal.addEventListener("abort", abortTheRun, { once: true });
  void exited.finally(() => input.signal.removeEventListener("abort", abortTheRun));
  if (input.signal.aborted) {
    abortTheRun();
  }
  return {
    send(line) {
      child.stdin.write(`${line}\n`);
    },
    cargo,
    cargoTeardown: cargoTeardown.signal,
    kill() {
      sendSignal("TERM");
      const escalation = setTimeout(() => sendSignal("KILL"), SIGKILL_ESCALATION_MS);
      escalation.unref();
      void exited.finally(() => clearTimeout(escalation));
    },
    isAlive: () => alive,
    exited,
    async signalsSettled() {
      while (signalsInFlight.size > 0) {
        await Promise.all(signalsInFlight);
      }
      return !signalLost;
    },
  };
};

type ChildEvent =
  | { type: "frame"; frame: ReturnType<typeof parseWireChildFrame> }
  | { type: "exit"; exit: ChildExit };

type WireInputs = {
  documents: StagedFileMeta[];
  skillFiles: StagedFileMeta[];
  recarriedOut: StagedFileMeta[];
  cargo: CargoSource[];
  // Streamed after `cargo`, and only on the frame that carries the continuation: a cold
  // resume omits the continuation field, so streaming these bytes anyway would leave the
  // child's receiver short and kill the run as a cargo failure.
  recarriedCargo: CargoSource[];
};

function wireInputsOf(stored: readonly StoredInput[] | undefined): WireInputs {
  const documents: StagedFileMeta[] = [];
  const skillFiles: StagedFileMeta[] = [];
  const recarriedOut: StagedFileMeta[] = [];
  const documentCargo: CargoSource[] = [];
  const skillCargo: CargoSource[] = [];
  const recarriedCargo: CargoSource[] = [];
  for (const input of stored ?? []) {
    const assets = input.assets;
    const source: CargoSource = { sizeBytes: input.sizeBytes, open: input.open };
    if (input.area === RECARRIED_OUT_AREA) {
      recarriedOut.push({ name: input.name, sha256: input.sha256, sizeBytes: input.sizeBytes });
      recarriedCargo.push(source);
      continue;
    }
    const meta: StagedFileMeta = {
      name: input.name,
      sha256: input.sha256,
      sizeBytes: input.sizeBytes,
      ...copyDocumentMeta(input),
      ...(assets === undefined
        ? {}
        : {
            assets: assets.map((asset) => ({
              name: asset.name,
              sha256: asset.sha256,
              sizeBytes: asset.sizeBytes,
            })),
          }),
    };
    if (input.area === DOCUMENTS_AREA) {
      documents.push(meta);
      documentCargo.push(source);
    } else {
      skillFiles.push(meta);
      skillCargo.push(source);
      for (const asset of assets ?? []) {
        skillCargo.push({ sizeBytes: asset.sizeBytes, open: asset.open });
      }
    }
  }
  return {
    documents,
    skillFiles,
    recarriedOut,
    cargo: [...documentCargo, ...skillCargo],
    recarriedCargo,
  };
}

type ChildDeathContext = {
  cancelled: boolean;
  deadlineExceeded: boolean;
  lastKnownCostUsd: number;
  lastKnownUsage: TokenUsage | undefined;
  cargoFailure?: Error;
  missingResult?: string;
};

const TASK_EXHAUSTION_SIGNALS: readonly string[] = ["SIGABRT", "SIGTRAP", "SIGSYS"];

function taskLimitAttribution(exit: ChildExit): string {
  if (exit.signal === null || !TASK_EXHAUSTION_SIGNALS.includes(exit.signal) || exit.stderr !== "") {
    return "";
  }
  return (
    `(died silently, which is what exhausting the per-uid task cap ` +
    `RUN_CHILD_TASK_LIMIT=${String(RUN_CHILD_TASK_LIMIT)} looks like: a runtime that cannot create a ` +
    `thread aborts without writing to stderr)`
  );
}

function exitCauseOf(exit: ChildExit): string {
  if (exit.signal !== null) {
    return `was killed by ${exit.signal}`;
  }
  if (exit.code === RESULT_UNDELIVERED_EXIT_CODE) {
    return `exited with code ${String(exit.code)}, the code for a final frame it could not write to stdout`;
  }
  return `exited with code ${String(exit.code)}`;
}

function childDeathError(exit: ChildExit, context: ChildDeathContext): Error {
  const usageDetail = context.lastKnownUsage === undefined ? {} : { usage: context.lastKnownUsage };
  if (context.cancelled) {
    return new RunFailedError(`run child killed by run cancellation: ${exit.stderr}`, {
      subtype: "aborted",
      estimatedCostUsd: context.lastKnownCostUsd,
      sessionId: null,
      ...usageDetail,
    });
  }
  if (context.deadlineExceeded) {
    return new RunFailedError(`run child killed after exceeding its deadline: ${exit.stderr}`, {
      subtype: "deadline_exceeded",
      estimatedCostUsd: context.lastKnownCostUsd,
      sessionId: null,
      ...usageDetail,
    });
  }
  const cause = exitCauseOf(exit);
  // The child's SIGTERM handler exits 0, so without this a supervisor-side cargo failure
  // would read as a clean child exit and the real cause would live only in a warn log.
  const cargoAttribution =
    context.cargoFailure === undefined
      ? ""
      : ` after its document cargo stream failed (${context.cargoFailure.message})`;
  // Named outright: "exited with code 0" alone reads as a contradiction for a run that
  // delivered nothing, and buries the cause under the stderr tail.
  const resultAttribution =
    context.missingResult === undefined ? "" : ` and delivered no result (${context.missingResult})`;
  const message = `run child ${cause}${cargoAttribution}${resultAttribution}: ${exit.stderr}${taskLimitAttribution(exit)}`;
  // Zero spend stays retryable; a run that cost money must fail non-retryably so it's never silently re-run.
  if (context.lastKnownCostUsd > 0) {
    return new RunFailedError(message, {
      subtype: "run_child_error",
      estimatedCostUsd: context.lastKnownCostUsd,
      sessionId: null,
      ...usageDetail,
    });
  }
  return new Error(message);
}

export type UidSweepOutcome = { idle: boolean; swept: boolean };

export type UidSweeper = {
  idleOf: (uid: number, executionId?: string, settleBudgetMs?: number) => Promise<boolean>;
  sweepWhenIdle: (uid: number, executionId?: string) => Promise<UidSweepOutcome>;
};

export function createUidSweeper(deps: {
  workspaceRoot: string;
  log: Logger;
  tmpRoot?: string;
  sweepUidResidue?: UidResidueSweep;
  waitForUidIdle?: UidIdleWait;
}): UidSweeper {
  const { log } = deps;
  const sweepResidue = deps.sweepUidResidue ?? sweepUidResidue;
  const uidIdleWait = deps.waitForUidIdle ?? waitForUidIdle;
  const residueRoots: readonly SweptRoot[] = [
    { path: deps.workspaceRoot, retain: retainedRootsUnder(deps.workspaceRoot).map((root) => root.path) },
    { path: deps.tmpRoot ?? SHARED_TMP_ROOT, retain: [] },
  ];

  function logFieldsOf(executionId: string | undefined): { executionId?: string } {
    return executionId === undefined ? {} : { executionId };
  }

  async function idleOf(uid: number, executionId?: string, settleBudgetMs?: number): Promise<boolean> {
    return uidIdleWait(uid, settleBudgetMs === undefined ? undefined : { settleBudgetMs }).catch((error) => {
      log.error("uid_idle_check_failed", { ...logFieldsOf(executionId), uid, error: toError(error) });
      return false;
    });
  }

  async function sweptOnceIdle(
    uid: number,
    executionId?: string,
  ): Promise<{ idle: boolean; error?: unknown }> {
    const idle = await idleOf(uid, executionId);
    if (!idle) {
      return { idle };
    }
    try {
      await sweepResidue({ uid, roots: residueRoots });
      return { idle };
    } catch (error) {
      return { idle, error };
    }
  }

  // Idle first, then sweep: the sweep runs as the uid, so anything still signalling the
  // uid can kill it. A sweep a signal killed is retried once after idleness is re-proven.
  async function sweepWhenIdle(uid: number, executionId?: string): Promise<UidSweepOutcome> {
    let attempt = await sweptOnceIdle(uid, executionId);
    if (attempt.error !== undefined && sweepWasSignalled(attempt.error)) {
      log.warn("uid_sweep_retried", { ...logFieldsOf(executionId), uid, error: toError(attempt.error) });
      attempt = await sweptOnceIdle(uid, executionId);
    }
    if (attempt.error !== undefined) {
      log.error("uid_sweep_failed", { ...logFieldsOf(executionId), uid, error: toError(attempt.error) });
      return { idle: attempt.idle, swept: false };
    }
    return { idle: attempt.idle, swept: attempt.idle };
  }

  return { idleOf, sweepWhenIdle };
}

export function createRunSpawner(deps: RunSpawnerDeps): SpawnRunFn {
  const log = deps.logger ?? spawnerLog;
  const childRun = deps.childRun ?? spawnChildRun;
  const defaultDeadlineMs = deps.deadlineMs ?? DEFAULT_RUN_DEADLINE_MS;
  const defaultMaxBudgetUsd = deps.maxBudgetUsd ?? DEFAULT_MAX_BUDGET_USD;
  const defaultMaxTurns = deps.maxTurns ?? DEFAULT_MAX_TURNS;
  const pauseGraceMs = deps.pauseGraceMs ?? DEFAULT_PAUSE_GRACE_MINUTES * MS_PER_MINUTE;
  const documentBudgetBytes = deps.documentBudgetBytes ?? MAX_TOTAL_DOCUMENT_BYTES;
  const childGraceMs = deps.childGraceMs ?? CHILD_GRACE_MS;
  const retainedRoots = retainedRootsUnder(deps.workspaceRoot);
  const artifactsRoot = join(deps.workspaceRoot, ARTIFACTS_DIR_NAME);
  const sweeper =
    deps.uidSweeper ??
    createUidSweeper({
      workspaceRoot: deps.workspaceRoot,
      log,
      ...(deps.tmpRoot === undefined ? {} : { tmpRoot: deps.tmpRoot }),
      ...(deps.sweepUidResidue === undefined ? {} : { sweepUidResidue: deps.sweepUidResidue }),
      ...(deps.waitForUidIdle === undefined ? {} : { waitForUidIdle: deps.waitForUidIdle }),
    });
  // Retained roots are world-writable, so the per-execution level is claimed here, not lazily by whichever uid publishes first.
  const claimSubtrees =
    deps.claimRunSubtrees ??
    ((executionId: string): void => {
      for (const root of retainedRoots) {
        claimRunSubtree(root.path, executionId);
      }
    });

  function childEnv(): Record<string, string> {
    const modelProvider = deps.modelProvider;
    // Where this repo already puts a process constant the child needs: the child stamps its
    // own bundles and cannot read the supervisor's config.
    const runtimeFacts = {
      WORKSPACE_ROOT: deps.workspaceRoot,
      AGENT_RUNTIME_VERSION: deps.buildIdentity.version,
      AGENT_RUNTIME_REVISION: deps.buildIdentity.revision,
    };
    if (modelProvider.provider !== "bedrock") {
      return { ...deps.subprocessEnv, ...runtimeFacts, [MODEL_PROVIDER_ENV_VAR]: modelProvider.provider };
    }
    const identity = modelProvider.identity;
    return {
      ...("ambient" in identity ? deps.subprocessEnv : withoutStaticCredentials(deps.subprocessEnv)),
      ...runtimeFacts,
      ...("roleArn" in identity ? { AGENT_RUNTIME_ROLE_ARN: identity.roleArn } : {}),
      ...("metadataCredentials" in identity ? { AGENT_RUNTIME_METADATA_CREDENTIALS: "true" } : {}),
    };
  }

  return async function spawnRun(
    params: RunReportParams,
    options: SpawnRunOptions = {},
  ): Promise<RunReportResult> {
    const executionId = params.executionId;
    claimSubtrees(executionId);
    let uid: number;
    let runLog = log.with({ executionId });
    try {
      uid = deps.pool.acquire();
      log.info("uid_leased", { executionId, uid });
      runLog = log.with({ executionId, uid });
    } catch (error) {
      if (error instanceof UidPoolExhaustedError) {
        throw new RunFailedError(error.message, {
          subtype: "uid_pool_exhausted",
          estimatedCostUsd: 0,
          sessionId: null,
        });
      }
      throw error;
    }
    const abortController = new AbortController();
    const pauseSeam = options.pause;
    const terminalCheckpointSeam = options.onTerminalCheckpoint;
    // Captured once so a resumed run can't switch model, drop the structured-output contract, or change its system prompt mid-run.
    const model = options.model;
    const outputFormat = options.outputFormat;
    const systemPrompt = options.systemPrompt;
    const agents = options.agents;
    const deadlineMs = options.deadlineMs ?? defaultDeadlineMs;
    const maxBudgetUsd = options.maxBudgetUsd ?? defaultMaxBudgetUsd;
    const maxTurns = options.maxTurns ?? defaultMaxTurns;
    const maxRetainedOutputBytes =
      options.maxRetainedOutputBytes ?? DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES;
    const effort = options.effort;
    const mcpFrameFields: { mcpServerUrl?: string; runToken?: string } =
      options.mcpServerUrl === undefined
        ? {}
        : {
            mcpServerUrl: options.mcpServerUrl,
            ...(options.runToken === undefined ? {} : { runToken: options.runToken }),
          };
    const pinnedFrameFields = {
      maxBudgetUsd,
      maxTurns,
      ...(effort === undefined ? {} : { effort }),
      deadlineMs,
      documentBudgetBytes,
      maxRetainedOutputBytes,
      ...(model === undefined ? {} : { model }),
      ...(outputFormat === undefined ? {} : { outputFormat }),
      ...(systemPrompt === undefined ? {} : { systemPrompt }),
      ...(agents === undefined ? {} : { agents }),
      ...mcpFrameFields,
    };
    const externalSignal = options.signal;
    const runSignal =
      externalSignal === undefined
        ? abortController.signal
        : AbortSignal.any([abortController.signal, externalSignal]);

    let killTimer: ReturnType<typeof setTimeout> | undefined;
    const armKillTimer = (): void => {
      clearTimeout(killTimer);
      killTimer = setTimeout(() => abortController.abort(), deadlineMs + childGraceMs);
    };

    let handle: ChildHandle | undefined;
    let events: AsyncQueue<ChildEvent> = createAsyncQueue<ChildEvent>();

    const sendFrame = (child: ChildHandle, frame: WireRequest | WireAnswerFrame): void => {
      const line = JSON.stringify(frame);
      child.send(line);
      runLog.debug("wire_frame", {
        direction: "to_child",
        frame: frame.kind,
        bytes: Buffer.byteLength(line, "utf8"),
      });
    };

    // Fire-and-forget by design: cargo write failures matter only when the child is still
    // alive to starve on them, in which case it is killed and the exit path reports; a
    // child that already died mid-stream (EPIPE here) is reported by the exit path alone.
    const streamCargo = (
      child: ChildHandle,
      sources: readonly CargoSource[],
      teardown: AbortSignal,
    ): void => {
      void writeCargo(sources, child.cargo, { signal: teardown })
        .then(() => {
          runLog.debug("cargo_streamed", {
            files: sources.length,
            bytes: sources.reduce((total, source) => total + source.sizeBytes, 0),
          });
        })
        .catch((error: unknown) => {
          const failure = toError(error);
          runLog.warn("cargo_stream_failed", { error: failure });
          // Checked on the teardown signal, not isAlive(): teardown trips on 'exit' and isAlive()
          // on 'close', and a rejection in that window would blame the cargo stream for a death
          // it did not cause.
          if (!child.cargoTeardown.aborted && child.isAlive()) {
            cargoFailure = failure;
            child.kill();
          }
        });
    };

    const startChild = (request: WireRequest, cargoSources: readonly CargoSource[]): ChildHandle => {
      const queue = createAsyncQueue<ChildEvent>();
      events = queue;
      const started = childRun({
        uid,
        argv: privilegeDroppingArgv(uid),
        env: childEnv(),
        signal: runSignal,
        logger: runLog,
        maxFrameLength: MAX_CHILD_FRAME_LENGTH,
        onFrame: (line) => {
          try {
            const frame = parseWireChildFrame(JSON.parse(line) as unknown);
            runLog.debug("wire_frame", {
              direction: "from_child",
              frame: frame.kind,
              bytes: Buffer.byteLength(line, "utf8"),
            });
            queue.push({ type: "frame", frame });
          } catch (error) {
            queue.fail(toError(error));
          }
        },
      });
      handle = started;
      void started.exited.then((exit) => queue.push({ type: "exit", exit }));
      sendFrame(started, request);
      streamCargo(started, cargoSources, started.cargoTeardown);
      armKillTimer();
      return started;
    };

    async function handlePause(child: ChildHandle, frame: WirePausedFrame, segment: number): Promise<void> {
      const pauseFn = pauseSeam;
      if (pauseFn === undefined) {
        child.kill();
        throw new RunFailedError("run paused but no pause handler is wired", {
          subtype: "pause_unsupported",
          estimatedCostUsd: frame.estimatedCostUsd,
          sessionId: frame.sessionId,
        });
      }
      const resolutionPromise: Promise<PauseResolution> = pauseFn({
        question: frame.question,
        sessionId: frame.sessionId,
        estimatedCostUsd: frame.estimatedCostUsd,
        checkpoint: fromWireCheckpoint(frame.checkpoint),
      });
      clearTimeout(killTimer);
      // kill() only requests termination — isAlive() can still lag it — so warm-vs-cold keys off the request.
      let killRequested = false;
      const requestKill = (): void => {
        killRequested = true;
        child.kill();
      };
      let graceTimer: ReturnType<typeof setTimeout> | undefined;
      if (pauseGraceMs <= 0) {
        requestKill();
      } else {
        graceTimer = setTimeout(requestKill, pauseGraceMs);
      }
      let resolution: PauseResolution;
      try {
        resolution = await resolutionPromise;
      } catch (error) {
        child.kill();
        // A RunFailedError from the seam is the server's own verdict (mirrors runReport's in-process pause) — pass it through.
        if (error instanceof RunFailedError) {
          throw error;
        }
        throw new RunFailedError(`pause rejected before an answer arrived: ${toError(error).message}`, {
          subtype: "pause_rejected",
          estimatedCostUsd: frame.estimatedCostUsd,
          sessionId: frame.sessionId,
        });
      } finally {
        clearTimeout(graceTimer);
      }
      if (!killRequested && child.isAlive()) {
        sendFrame(child, { kind: "answer", ...resolution });
        armKillTimer();
        return;
      }
      // The dying child and its cold replacement share a uid: never overlap, and never let
      // a signal meant for the old child land on the new one.
      // A descendant that ignored the TERM would also outlive the child into the replacement,
      // so the uid must be idle too; the sweep waits for the run's end.
      await child.exited;
      if (!(await child.signalsSettled())) {
        throw new Error(`a signal to uid ${String(uid)} did not settle before the cold resume`);
      }
      if (!(await sweeper.idleOf(uid, executionId, COLD_RESUME_IDLE_BUDGET_MS))) {
        throw new Error(`uid ${String(uid)} did not go idle before the cold resume`);
      }
      await coldResume(frame, resolution, segment);
    }

    // The frame is best-effort end to end: a save that cannot land is logged and the final result still flows.
    async function handleTerminalCheckpoint(frame: WireTerminalCheckpointFrame): Promise<void> {
      const seam = terminalCheckpointSeam;
      if (seam === undefined) {
        return;
      }
      try {
        const saving: Promise<void> = seam({
          sessionId: frame.sessionId,
          checkpoint: fromWireCheckpoint(frame.checkpoint),
        });
        await saving;
      } catch (error) {
        runLog.error("terminal_checkpoint_save_failed", { error: toError(error) });
      }
    }

    // Cached for the run's whole life: the store's inputs are immutable while the run
    // lives, and each entry is scalars plus an open() factory — never document bytes.
    let wireInputs: WireInputs = wireInputsOf(undefined);

    const wireInputFrameFields = (): {
      documents?: StagedFileMeta[];
      skillFiles?: StagedFileMeta[];
    } => ({
      ...(wireInputs.documents.length === 0 ? {} : { documents: wireInputs.documents }),
      ...(wireInputs.skillFiles.length === 0 ? {} : { skillFiles: wireInputs.skillFiles }),
    });

    async function coldResume(
      frame: WirePausedFrame,
      resolution: PauseResolution,
      segment: number,
    ): Promise<void> {
      const store = deps.checkpointStore;
      const key = checkpointKeyOf(params.executionId, FIRST_ATTEMPT, segment);
      const bundle = store === undefined ? undefined : await store.load(key);
      if (bundle === undefined) {
        throw new RunFailedError(
          `no checkpoint found for ${params.executionId} attempt ${key.attempt} segment ${key.segment}`,
          {
            subtype: "checkpoint_missing",
            estimatedCostUsd: frame.estimatedCostUsd,
            sessionId: frame.sessionId,
          },
        );
      }
      // No continuation field and no re-carried cargo, even on a continuation leg: by the
      // time a leg pauses it has published its own out/ mirror, which restage reads and
      // which is fresher than anything the platform sent.
      const wire = checkpointWireOf(bundle, wireInputs.cargo);
      startChild(
        {
          kind: "resume",
          executionId: params.executionId,
          attempt: FIRST_ATTEMPT,
          ...pinnedFrameFields,
          priorCostUsd: frame.estimatedCostUsd,
          ...(lastKnownUsage === undefined ? {} : { priorUsage: lastKnownUsage }),
          checkpoint: wire.checkpoint,
          ...wireInputFrameFields(),
          ...resolution,
        },
        wire.cargo,
      );
    }

    let currentSegment = 0;
    let lastKnownCostUsd = 0;
    let lastKnownUsage: TokenUsage | undefined;
    let cargoFailure: Error | undefined;
    // Last frame wins, not a running max — frames arrive in order on one pipe, so the newest is always best-known.
    const noteReportedCost = (estimatedCostUsd: number, usage?: TokenUsage): void => {
      lastKnownCostUsd = estimatedCostUsd;
      if (usage !== undefined) {
        lastKnownUsage = usage;
      }
    };

    const continuationStart = options.continuation;

    const outcomeOf = (response: WireResponse): RunReportResult => {
      if (response.ok) {
        return response.result;
      }
      throw RunFailedError.from(response.error);
    };

    // A child that died unexplained is asked for the response it persisted before its death is
    // reported: a stdout pipe that died mid-run loses the final frame, not the work. A kill the
    // supervisor itself ordered (cancel, deadline) keeps its own verdict.
    const recoverPersistedResponse = ():
      | { kind: "recovered"; response: WireResponse }
      | { kind: "missing"; detail: string } => {
      try {
        const response = readPersistedFinalResponse({ artifactsRoot, executionId, attempt: FIRST_ATTEMPT });
        if (response === undefined) {
          return {
            kind: "missing",
            detail: "no final frame arrived on stdout and no final response was persisted on disk",
          };
        }
        runLog.warn("final_response_recovered", { ok: response.ok });
        return { kind: "recovered", response };
      } catch (error) {
        return {
          kind: "missing",
          detail: `no final frame arrived on stdout and the persisted final response could not be read: ${messageOf(error)}`,
        };
      }
    };

    const startFirstChild = (): ChildHandle => {
      if (continuationStart === undefined) {
        return startChild(
          {
            kind: "start",
            executionId: params.executionId,
            attempt: FIRST_ATTEMPT,
            prompt: params.prompt,
            ...pinnedFrameFields,
            ...wireInputFrameFields(),
          },
          wireInputs.cargo,
        );
      }
      // The prompt is the revision turn, delivered as the resumed session's answer; the
      // leg's fuses are its own, so nothing prior is carried in cost or usage.
      const bundle = continuationStart.takeCheckpoint();
      const wire = checkpointWireOf(bundle, [...wireInputs.cargo, ...wireInputs.recarriedCargo]);
      return startChild(
        {
          kind: "resume",
          executionId: params.executionId,
          attempt: FIRST_ATTEMPT,
          ...pinnedFrameFields,
          priorCostUsd: 0,
          checkpoint: wire.checkpoint,
          continuation: { outFiles: wireInputs.recarriedOut },
          ...wireInputFrameFields(),
          answer: params.prompt,
          allowFurtherPause: true,
        },
        wire.cargo,
      );
    };

    try {
      wireInputs = wireInputsOf(await deps.documentStore.openInputs(executionId));
      startFirstChild();
      while (true) {
        const event = await events.next();
        if (event.type === "exit") {
          const cancelled = externalSignal?.aborted === true;
          const deadlineExceeded = runSignal.aborted;
          const recovery = cancelled || deadlineExceeded ? undefined : recoverPersistedResponse();
          if (recovery?.kind === "recovered") {
            return outcomeOf(recovery.response);
          }
          throw childDeathError(event.exit, {
            cancelled,
            deadlineExceeded,
            lastKnownCostUsd,
            lastKnownUsage,
            ...(cargoFailure === undefined ? {} : { cargoFailure }),
            ...(recovery === undefined ? {} : { missingResult: recovery.detail }),
          });
        }
        if (event.frame.kind === "cost") {
          noteReportedCost(event.frame.estimatedCostUsd, event.frame.usage);
          continue;
        }
        if (event.frame.kind === "terminal_checkpoint") {
          await handleTerminalCheckpoint(event.frame);
          continue;
        }
        if (event.frame.kind === "final") {
          return outcomeOf(event.frame.response);
        }
        if (handle === undefined) {
          throw new Error("received a pause frame with no live child");
        }
        noteReportedCost(event.frame.estimatedCostUsd);
        await handlePause(handle, event.frame, currentSegment);
        currentSegment += 1;
      }
    } catch (error) {
      if (error instanceof RunFailedError || lastKnownCostUsd <= 0) {
        throw error;
      }
      throw new RunFailedError(toError(error).message, {
        subtype: "run_child_error",
        estimatedCostUsd: lastKnownCostUsd,
        sessionId: null,
        ...(lastKnownUsage === undefined ? {} : { usage: lastKnownUsage }),
      });
    } finally {
      clearTimeout(killTimer);
      let signalsSettled = true;
      if (handle !== undefined) {
        if (handle.isAlive()) {
          handle.kill();
        }
        await handle.exited;
        signalsSettled = await handle.signalsSettled();
      }
      const { idle, swept } = signalsSettled
        ? await sweeper.sweepWhenIdle(uid, executionId)
        : { idle: false, swept: false };
      if (swept) {
        deps.pool.release(uid);
        options.onUidReleased?.();
        log.info("uid_released", { executionId, uid });
      } else {
        deps.pool.retain(uid);
        log.error("uid_retained_dirty", { executionId, uid, idle, signalsSettled });
      }
    }
  };
}
