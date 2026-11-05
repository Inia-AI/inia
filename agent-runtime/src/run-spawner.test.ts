import { describe, expect, it } from "bun:test";
import { spawn } from "node:child_process";
import { EventEmitter } from "node:events";
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Readable, Writable } from "node:stream";
import { createLocalCheckpointStore, sealCheckpoint } from "./checkpoint-store.js";
import {
  DEFAULT_MAX_TURNS,
  DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
  MAX_CHILD_FRAME_LENGTH,
  MAX_CHILD_STDERR_TAIL_BYTES,
  MAX_TOTAL_DOCUMENT_BYTES,
  RUN_CHILD_TASK_LIMIT,
  UID_POOL_BASE,
} from "./config.js";
import { createLocalDocumentStore, type DocumentStore } from "./document-store.js";
import { messageOf } from "./errors.js";
import { finalResponsePathOf, persistFinalResponse } from "./final-response-file.js";
import { sha256Hex } from "./hash.js";
import type { Logger } from "./logger.js";
import { type PauseRequest, RunFailedError } from "./run-report.js";
import {
  type ChildExit,
  type ChildHandle,
  type ChildRun,
  continuationStartOf,
  createMissedCloseBelt,
  createRunSpawner,
  MISSED_CLOSE_STDERR_NOTICE,
  type SpawnRunOptions,
  signalDidNotLand,
  signalRun,
  spawnChildRun,
  type UidResidueSweep,
} from "./run-spawner.js";
import { RESULT_UNDELIVERED_EXIT_CODE, type WireResponse } from "./run-wire.js";
import { privilegeDropArgs, RunAsUidError } from "./setpriv.js";
import { createUidPool } from "./uid-pool.js";
import { ARTIFACTS_DIR_NAME, claimRetainedRoot, RETAINED_WORKSPACE_ROOTS } from "./workspace-layout.js";

const ZERO_USAGE = {
  inputTokens: 0,
  outputTokens: 0,
  cacheCreationInputTokens: 0,
  cacheReadInputTokens: 0,
};

const RESULT = {
  executionId: "e-1",
  output: "the report",
  estimatedCostUsd: 0.4,
  sessionId: "s-1",
  usage: ZERO_USAGE,
};

const QUESTION = {
  id: "q-1",
  toolName: "mcp__platform__request_analyst_input",
  prompt: "Which fund?",
  input: { question: "Which fund?" },
};
const RESOLUTION = { answer: "the second fund", allowFurtherPause: true };
const CHECKPOINT = sealCheckpoint(new TextEncoder().encode('{"sessionId":"s-1"}'));

type FakeChild = {
  input: Parameters<ChildRun>[0];
  sent: string[];
  cargoBytes: () => Buffer;
  // Settles once the supervisor has ended the cargo pipe (every entry written) or torn it down.
  cargoEnded: Promise<void>;
  killed: boolean;
  isAlive: () => boolean;
  emit: (frame: unknown) => void;
  exit: (exit?: Partial<ChildExit>) => void;
  onSend?: (line: string) => void;
  // Signals still in flight to the child's uid, as a real kill's `pkill` process is until it closes.
  // Each resolves true when the signaller's end was seen, false when it was lost.
  pendingSignals: Promise<boolean>[];
  onKill?: () => void;
};

type FakeChildOptions = {
  cargoPipe?: () => Writable;
  // How the default pipe settles a chunk's write callback — immediately unless a test wants
  // the chunks to land one per turn of the event loop, as a loaded runner delivers them.
  acceptCargoChunk?: (accepted: () => void) => void;
};

function makeFakeChildRun(
  onSpawn: (child: FakeChild) => void,
  options: FakeChildOptions = {},
): {
  runner: ChildRun;
  children: FakeChild[];
} {
  const children: FakeChild[] = [];
  const runner: ChildRun = (input) => {
    let alive = true;
    let resolveExit: (exit: ChildExit) => void = () => {};
    const exited = new Promise<ChildExit>((resolve) => {
      resolveExit = resolve;
    });
    const cargoParts: Buffer[] = [];
    const cargoPipe =
      options.cargoPipe?.() ??
      new Writable({
        write(chunk: Buffer, _encoding, callback) {
          cargoParts.push(Buffer.from(chunk));
          (options.acceptCargoChunk ?? ((accepted) => accepted()))(() => callback());
        },
      });
    const cargoEnded = new Promise<void>((resolve) => {
      cargoPipe.once("finish", resolve);
      cargoPipe.once("close", resolve);
    });
    // Mirrors spawnChildRun: the teardown signal trips the moment the child dies.
    const cargoTeardown = new AbortController();
    const child: FakeChild = {
      input,
      sent: [],
      cargoBytes: () => Buffer.concat(cargoParts),
      cargoEnded,
      killed: false,
      pendingSignals: [],
      isAlive: () => alive,
      emit: (frame) => input.onFrame(JSON.stringify(frame)),
      exit: (exit = {}) => {
        if (!alive) {
          return;
        }
        alive = false;
        cargoTeardown.abort();
        cargoPipe.destroy();
        resolveExit({ code: 0, signal: null, stderr: "", ...exit });
      },
    };
    const handle: ChildHandle = {
      send: (line) => {
        child.sent.push(line);
        child.onSend?.(line);
      },
      cargo: cargoPipe,
      cargoTeardown: cargoTeardown.signal,
      // A real kill only sends SIGTERM; the fake models that gap instead of dying synchronously.
      kill: () => {
        child.killed = true;
        child.onKill?.();
        setTimeout(() => child.exit({ code: null, signal: "SIGTERM" }), 5);
      },
      isAlive: () => alive,
      exited,
      signalsSettled: async () => (await Promise.all(child.pendingSignals)).every(Boolean),
    };
    children.push(child);
    onSpawn(child);
    return handle;
  };
  return { runner, children };
}

// The spawner reads the document store before it spawns, so a test that means "abort
// while the child runs" must let that await settle first — a listener added to an
// already-aborted signal never fires.
async function firstSpawnOf(children: FakeChild[]): Promise<FakeChild> {
  for (let turn = 0; turn < 1000 && children.length === 0; turn += 1) {
    await Promise.resolve();
  }
  const child = children[0];
  if (child === undefined) {
    throw new Error("no child was spawned");
  }
  return child;
}

function oneShotSuccessChild(): { runner: ChildRun; children: FakeChild[] } {
  return makeFakeChildRun((child) => {
    queueMicrotask(() => {
      child.emit({ kind: "final", response: { ok: true, result: RESULT } });
      child.exit();
    });
  });
}

function pausedFrame(): unknown {
  return {
    kind: "paused",
    question: QUESTION,
    sessionId: "s-1",
    estimatedCostUsd: 0.2,
    checkpoint: { sha256: CHECKPOINT.sha256, bytesBase64: Buffer.from(CHECKPOINT.bytes).toString("base64") },
  };
}

type CapturedLogEvent = { level: string; event: string; fields: Record<string, unknown> };

function captureLogger(): {
  events: CapturedLogEvent[];
  logger: Logger;
  logged: (event: string) => Promise<void>;
} {
  const events: CapturedLogEvent[] = [];
  const waiters: { event: string; resolve: () => void }[] = [];
  const push = (entry: CapturedLogEvent): void => {
    events.push(entry);
    for (const waiter of waiters.splice(0)) {
      if (waiter.event === entry.event) {
        waiter.resolve();
      } else {
        waiters.push(waiter);
      }
    }
  };
  const boundLogger = (bound: Record<string, unknown>): Logger => ({
    debug: (event, fields) => push({ level: "debug", event, fields: { ...bound, ...fields } }),
    info: (event, fields) => push({ level: "info", event, fields: { ...bound, ...fields } }),
    warn: (event, fields) => push({ level: "warn", event, fields: { ...bound, ...fields } }),
    error: (event, fields) => push({ level: "error", event, fields: { ...bound, ...fields } }),
    with: (fields) => boundLogger({ ...bound, ...fields }),
  });
  const logged = (event: string): Promise<void> =>
    events.some((entry) => entry.event === event)
      ? Promise.resolve()
      : new Promise((resolve) => waiters.push({ event, resolve }));
  return { events, logger: boundLogger({}), logged };
}

function emptyDocumentStore(): DocumentStore {
  return {
    save: () => Promise.resolve(),
    openInputs: () => Promise.resolve(undefined),
    delete: () => Promise.resolve(),
  };
}

// A real child consumes its whole cargo during staging before it ever finishes; a fake
// that exits instantly would tear the pipe down mid-stream instead. Finishing only once
// the supervisor has ended the pipe mirrors the real staging order: the pipe's end is the
// fact, where a byte count guessed by the test raced the stream on a loaded runner.
function finishOnceCargoDrained(child: FakeChild): void {
  void child.cargoEnded.then(() => {
    child.emit({ kind: "final", response: { ok: true, result: RESULT } });
    child.exit();
  });
}

function cargoConsumingSuccessChild(): {
  runner: ChildRun;
  children: FakeChild[];
} {
  return makeFakeChildRun((child) => {
    finishOnceCargoDrained(child);
  });
}

// Cargo streams fire-and-forget beside the frame, so an assertion over the collected
// bytes waits for the supervisor to end the pipe rather than for a clock.
async function cargoFlushedOf(child: FakeChild): Promise<Buffer> {
  await child.cargoEnded;
  return child.cargoBytes();
}

function cargoPrefixed(bodies: Uint8Array[]): Buffer {
  return Buffer.concat(
    bodies.flatMap((body) => {
      const prefix = Buffer.alloc(8);
      prefix.writeBigUInt64BE(BigInt(body.byteLength));
      return [prefix, Buffer.from(body)];
    }),
  );
}

function spawnerWith(
  runner: ChildRun,
  overrides: Partial<Parameters<typeof createRunSpawner>[0]> = {},
): ReturnType<typeof createRunSpawner> {
  return createRunSpawner({
    pool: createUidPool({ baseUid: 20001, size: 2 }),
    childRun: runner,
    workspaceRoot: "/workspaces",
    subprocessEnv: { PATH: "/usr/bin" },
    modelProvider: { provider: "bedrock", identity: { ambient: true } },
    buildIdentity: { version: "0.0.0-test", revision: "testrev" },
    pauseGraceMs: 60_000,
    documentStore: emptyDocumentStore(),
    sweepUidResidue: () => Promise.resolve(),
    claimRunSubtrees: () => {},
    ...overrides,
  });
}

function newStoreRoot(): string {
  return join(mkdtempSync(join(tmpdir(), "spawner-ckpt-")), "store");
}

describe("run spawner", () => {
  it("sends the start request as the first frame and returns the child's final result", async () => {
    const { runner, children } = oneShotSuccessChild();
    const spawner = spawnerWith(runner, { deadlineMs: 1234, maxBudgetUsd: 7 });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(first["kind"]).toBe("start");
    expect(first["executionId"]).toBe("e-1");
    expect(first["prompt"]).toBe("p");
    expect(first["maxBudgetUsd"]).toBe(7);
    expect(first["deadlineMs"]).toBe(1234);
    expect(first["maxTurns"]).toBe(DEFAULT_MAX_TURNS);
    expect(first["attempt"]).toBe(1);
    expect(first["documentBudgetBytes"]).toBe(MAX_TOTAL_DOCUMENT_BYTES);
  });

  it("emits wire_frame debug lines describing each frame's kind and size, never its payload", async () => {
    const { events, logger } = captureLogger();
    const multiByteResult = { ...RESULT, output: "résumé — Ωμέγα 報告" };
    const finalFrame = { kind: "final", response: { ok: true, result: multiByteResult } };
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 0.1, usage: ZERO_USAGE });
        child.emit(finalFrame);
        child.exit();
      });
    });
    await spawnerWith(runner, { logger })({ executionId: "e-1", prompt: "résumé the naïve café" });
    const frames = events.filter((entry) => entry.event === "wire_frame");
    expect(frames.every((entry) => entry.level === "debug")).toBe(true);
    expect(frames.map((entry) => [entry.fields["direction"], entry.fields["frame"]])).toEqual([
      ["to_child", "start"],
      ["from_child", "cost"],
      ["from_child", "final"],
    ]);
    for (const entry of frames) {
      expect(Object.keys(entry.fields).sort()).toEqual(["bytes", "direction", "executionId", "frame", "uid"]);
      expect(typeof entry.fields["bytes"]).toBe("number");
    }
    // Pins that bytes means UTF-8 bytes on the pipe, not UTF-16 code units.
    const startLine = children[0]?.sent[0] ?? "";
    expect(Buffer.byteLength(startLine, "utf8")).toBeGreaterThan(startLine.length);
    expect(frames[0]?.fields["bytes"]).toBe(Buffer.byteLength(startLine, "utf8"));
    const finalLine = JSON.stringify(finalFrame);
    expect(Buffer.byteLength(finalLine, "utf8")).toBeGreaterThan(finalLine.length);
    expect(frames[2]?.fields["bytes"]).toBe(Buffer.byteLength(finalLine, "utf8"));
  });

  it("caps child-to-supervisor frames at the child frame length, not the wire cap", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    expect(children[0]?.input.maxFrameLength).toBe(MAX_CHILD_FRAME_LENGTH);
  });

  it("drops privilege to a leased pool uid with every capability cleared", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    const argv = children[0]?.input.argv ?? [];
    expect(argv[0]).toBe("setpriv");
    expect(argv).toContain("--reuid=20001");
    expect(argv).toContain("--regid=20001");
    expect(argv).toContain("--clear-groups");
    expect(argv).toContain("--inh-caps=-all");
    expect(argv).toContain("--ambient-caps=-all");
    expect(argv[argv.length - 1]).toBe("src/run-one-shot.ts");
  });

  it("caps the run uid's task count after the privilege drop, before bun", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    const argv = children[0]?.input.argv ?? [];
    const prlimitAt = argv.indexOf("prlimit");
    expect(prlimitAt).toBeGreaterThan(argv.indexOf("setpriv"));
    expect(argv[prlimitAt + 1]).toBe(`--nproc=${RUN_CHILD_TASK_LIMIT}:${RUN_CHILD_TASK_LIMIT}`);
    expect(prlimitAt).toBeLessThan(argv.indexOf("bun"));
  });

  it("never lets two concurrent runs share a uid", async () => {
    let release = (): void => {};
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { runner, children } = makeFakeChildRun((child) => {
      void held.then(() => {
        child.emit({ kind: "final", response: { ok: true, result: RESULT } });
        child.exit();
      });
    });
    const spawner = spawnerWith(runner);
    const both = Promise.all([
      spawner({ executionId: "a", prompt: "p" }),
      spawner({ executionId: "b", prompt: "p" }),
    ]);
    release();
    await both;
    const uids = children.map((child) => child.input.argv[1]);
    expect(new Set(uids).size).toBe(2);
  });

  it("returns a uid to the pool after the run, so runs can outnumber the pool", async () => {
    const { runner } = oneShotSuccessChild();
    const spawner = spawnerWith(runner, {
      pool: createUidPool({ baseUid: 20001, size: 1 }),
    });
    await spawner({ executionId: "a", prompt: "p" });
    await spawner({ executionId: "b", prompt: "p" });
    expect(await spawner({ executionId: "c", prompt: "p" })).toEqual(RESULT);
  });

  it("holds the uid until the child is confirmed dead, killing a lingering child first", async () => {
    let child0AliveWhenChild1Spawned: boolean | undefined;
    const { runner, children } = makeFakeChildRun((child) => {
      if (children.length === 2) {
        child0AliveWhenChild1Spawned = children[0]?.isAlive();
      }
      queueMicrotask(() => {
        // Mirrors a child whose stdin listener keeps its event loop alive after the final frame.
        child.emit({ kind: "final", response: { ok: true, result: RESULT } });
      });
    });
    const spawner = spawnerWith(runner, { pool: createUidPool({ baseUid: 20001, size: 1 }) });
    await spawner({ executionId: "a", prompt: "p" });
    expect(children[0]?.killed).toBe(true);
    expect(children[0]?.isAlive()).toBe(false);
    await spawner({ executionId: "b", prompt: "p" });
    expect(child0AliveWhenChild1Spawned).toBe(false);
  });

  it("sweeps the uid's residue after death and before the uid returns to the pool", async () => {
    const order: string[] = [];
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const spyPool = {
      ...pool,
      release: (uid: number) => {
        order.push("release");
        pool.release(uid);
      },
    };
    const cleanups: Parameters<UidResidueSweep>[0][] = [];
    let childAliveAtCleanup: boolean | undefined;
    const { runner, children } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
    });
    const spawner = spawnerWith(runner, {
      pool: spyPool,
      sweepUidResidue: (input) => {
        order.push("cleanup");
        cleanups.push(input);
        childAliveAtCleanup = children[0]?.isAlive();
        return Promise.resolve();
      },
    });
    const controller = new AbortController();
    const promise = spawner({ executionId: "e-1", prompt: "p" }, { signal: controller.signal });
    await firstSpawnOf(children);
    controller.abort();
    await expect(promise).rejects.toThrow();
    expect(order).toEqual(["cleanup", "release"]);
    expect(cleanups).toEqual([
      {
        uid: 20001,
        roots: [
          { path: "/workspaces", retain: ["/workspaces/traces", "/workspaces/artifacts"] },
          { path: "/tmp", retain: [] },
        ],
      },
    ]);
    expect(childAliveAtCleanup).toBe(false);
  });

  it("tells the caller its uid is back in the pool in the same tick it is released", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { runner } = oneShotSuccessChild();
    const spawner = spawnerWith(runner, { pool });
    const freeAtCallback: number[] = [];
    expect(
      await spawner(
        { executionId: "e-1", prompt: "p" },
        {
          onUidReleased: () => {
            freeAtCallback.push(pool.available());
          },
        },
      ),
    ).toEqual(RESULT);
    expect(freeAtCallback).toEqual([1]);
  });

  it("never reports a uid released when the residue sweep held it back", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { runner } = oneShotSuccessChild();
    const spawner = spawnerWith(runner, {
      pool,
      sweepUidResidue: () => Promise.reject(new Error("sweep failed")),
    });
    let released = 0;
    await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        onUidReleased: () => {
          released += 1;
        },
      },
    );
    expect(released).toBe(0);
  });

  it("holds the uid when the residue sweep fails, rather than releasing it dirty", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { runner } = oneShotSuccessChild();
    const spawner = spawnerWith(runner, {
      pool,
      sweepUidResidue: () => Promise.reject(new Error("sweep failed")),
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(pool.available()).toBe(0);
    expect(pool.retained()).toEqual([20001]);
  });

  it("does not sweep the uid until a signal still in flight to it has settled", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    let settleSignal: () => void = () => {};
    const signalInFlight = new Promise<void>((resolve) => {
      settleSignal = resolve;
    });
    const order: string[] = [];
    const { runner } = makeFakeChildRun((child) => {
      // The kill's pkill is still on its way when the child exits by itself.
      child.onKill = () => {
        child.pendingSignals.push(
          signalInFlight.then(() => {
            order.push("signal settled");
            return true;
          }),
        );
        queueMicrotask(() => child.exit());
      };
      queueMicrotask(() => child.emit({ kind: "final", response: { ok: true, result: RESULT } }));
    });
    const spawner = spawnerWith(runner, {
      pool,
      waitForUidIdle: () => {
        order.push("idle check");
        return Promise.resolve(true);
      },
      sweepUidResidue: () => {
        order.push("sweep");
        return Promise.resolve();
      },
    });
    const run = spawner({ executionId: "e-1", prompt: "p" });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(order).toEqual([]);
    expect(pool.available()).toBe(0);
    settleSignal();
    expect(await run).toEqual(RESULT);
    expect(order).toEqual(["signal settled", "idle check", "sweep"]);
    expect(pool.available()).toBe(1);
  });

  it("re-checks idleness and sweeps once more when a signal killed the sweep, then releases the uid", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const order: string[] = [];
    const { logger, events } = captureLogger();
    const { runner } = oneShotSuccessChild();
    let sweeps = 0;
    const spawner = spawnerWith(runner, {
      pool,
      logger,
      waitForUidIdle: () => {
        order.push("idle check");
        return Promise.resolve(true);
      },
      sweepUidResidue: () => {
        order.push("sweep");
        sweeps += 1;
        return sweeps === 1
          ? Promise.reject(
              new RunAsUidError("setpriv uid 20001 find was killed by SIGTERM", {
                code: null,
                signal: "SIGTERM",
                stderr: "",
              }),
            )
          : Promise.resolve();
      },
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(order).toEqual(["idle check", "sweep", "idle check", "sweep"]);
    expect(pool.available()).toBe(1);
    expect(events.map((entry) => entry.event)).toContain("uid_sweep_retried");
    expect(events.map((entry) => entry.event)).not.toContain("uid_sweep_failed");
  });

  it("does not retry a sweep that failed on its own, and holds the uid", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { runner } = oneShotSuccessChild();
    let sweeps = 0;
    const spawner = spawnerWith(runner, {
      pool,
      sweepUidResidue: () => {
        sweeps += 1;
        return Promise.reject(
          new RunAsUidError("setpriv uid 20001 find exited with 1: denied", {
            code: 1,
            signal: null,
            stderr: "denied",
          }),
        );
      },
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(sweeps).toBe(1);
    expect(pool.available()).toBe(0);
  });

  it("retries the sweep when find reports that a signal killed its rm child", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { runner } = oneShotSuccessChild();
    let sweeps = 0;
    const spawner = spawnerWith(runner, {
      pool,
      sweepUidResidue: () => {
        sweeps += 1;
        return sweeps === 1
          ? Promise.reject(
              new RunAsUidError("setpriv uid 20001 find exited with 1", {
                code: 1,
                signal: null,
                stderr: "find: 'rm' terminated by signal 15\n",
              }),
            )
          : Promise.resolve();
      },
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(sweeps).toBe(2);
    expect(pool.available()).toBe(1);
  });

  it("keeps the uid out of the pool unswept when a signal's end was lost, and still settles the run", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { logger, events } = captureLogger();
    let sweeps = 0;
    const { runner } = makeFakeChildRun((child) => {
      child.onKill = () => {
        child.pendingSignals.push(Promise.resolve(false));
      };
      queueMicrotask(() => child.emit({ kind: "final", response: { ok: true, result: RESULT } }));
    });
    const spawner = spawnerWith(runner, {
      pool,
      logger,
      sweepUidResidue: () => {
        sweeps += 1;
        return Promise.resolve();
      },
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(sweeps).toBe(0);
    expect(pool.available()).toBe(0);
    expect(events.find((entry) => entry.event === "uid_retained_dirty")?.fields).toMatchObject({
      uid: 20001,
      signalsSettled: false,
    });
  });

  function pausingThenFinishingChild(): ReturnType<typeof makeFakeChildRun> {
    return makeFakeChildRun((child) => {
      // The request frame is sent right after the spawn returns, so it is there by the microtask.
      queueMicrotask(() => {
        if ((JSON.parse(child.sent[0] ?? "{}") as Record<string, unknown>)["kind"] === "resume") {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
          return;
        }
        child.emit(pausedFrame());
      });
    });
  }

  async function coldResumeRun(
    runner: ChildRun,
    overrides: Partial<Parameters<typeof createRunSpawner>[0]>,
  ): Promise<unknown> {
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore, ...overrides });
    return spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
  }

  it("proves the uid idle before a cold resume spawns its replacement", async () => {
    const order: string[] = [];
    const budgets: (number | undefined)[] = [];
    const { runner, children } = pausingThenFinishingChild();
    const counting: ChildRun = (input) => {
      order.push("spawn");
      return runner(input);
    };
    const result = await coldResumeRun(counting, {
      waitForUidIdle: (_uid, options) => {
        order.push("idle check");
        budgets.push(options?.settleBudgetMs);
        return Promise.resolve(true);
      },
    });
    expect(result).toEqual(RESULT);
    expect(children.length).toBe(2);
    expect(order).toEqual(["spawn", "idle check", "spawn", "idle check"]);
    // The resume gives stragglers longer to die than teardown's default does.
    expect(budgets).toEqual([10_000, undefined]);
  });

  it("fails the run rather than cold-resume onto a uid that will not go idle", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { runner, children } = pausingThenFinishingChild();
    await expect(
      coldResumeRun(runner, { pool, waitForUidIdle: () => Promise.resolve(false) }),
    ).rejects.toThrow(/did not go idle before the cold resume/);
    expect(children.length).toBe(1);
    expect(pool.available()).toBe(0);
  });

  it("fails the run rather than cold-resume while a lost signal may still land on the uid", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { runner, children } = makeFakeChildRun((child) => {
      child.onKill = () => {
        child.pendingSignals.push(Promise.resolve(false));
      };
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    await expect(coldResumeRun(runner, { pool })).rejects.toThrow(/did not settle before the cold resume/);
    expect(children.length).toBe(1);
    expect(pool.available()).toBe(0);
  });

  it("does not start a cold resume while the paused child's kill is still in flight to the shared uid", async () => {
    let settleSignal: () => void = () => {};
    const signalInFlight = new Promise<void>((resolve) => {
      settleSignal = resolve;
    });
    let signalSettled = false;
    const settledWhenSpawned: boolean[] = [];
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      settledWhenSpawned.push(signalSettled);
      if (settledWhenSpawned.length === 1) {
        child.onKill = () => {
          child.pendingSignals.push(
            signalInFlight.then(() => {
              signalSettled = true;
              return true;
            }),
          );
        };
        queueMicrotask(() => child.emit(pausedFrame()));
        return;
      }
      queueMicrotask(() => {
        child.emit({ kind: "final", response: { ok: true, result: RESULT } });
        child.exit();
      });
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore });
    const run = spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(children.length).toBe(1);
    settleSignal();
    expect(await run).toEqual(RESULT);
    expect(settledWhenSpawned).toEqual([false, true]);
  });

  it("claims each retained root's per-execution dir as the supervisor before the child starts", async () => {
    const workspaceRoot = mkdtempSync(join(tmpdir(), "spawner-claim-"));
    for (const root of RETAINED_WORKSPACE_ROOTS) {
      claimRetainedRoot(join(workspaceRoot, root.name), root.mode);
    }
    const claimedWhenChildStarted: boolean[] = [];
    const { runner } = makeFakeChildRun((child) => {
      claimedWhenChildStarted.push(existsSync(join(workspaceRoot, "artifacts", "e-1")));
      queueMicrotask(() => child.emit({ kind: "final", response: { ok: true, result: RESULT } }));
    });
    const spawner = createRunSpawner({
      pool: createUidPool({ baseUid: 20001, size: 1 }),
      childRun: runner,
      workspaceRoot,
      subprocessEnv: { PATH: "/usr/bin" },
      modelProvider: { provider: "bedrock", identity: { ambient: true } },
      buildIdentity: { version: "0.0.0-test", revision: "testrev" },
      pauseGraceMs: 60_000,
      documentStore: emptyDocumentStore(),
      sweepUidResidue: () => Promise.resolve(),
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(claimedWhenChildStarted).toEqual([true]);
    const ownUid = process.getuid?.();
    for (const root of RETAINED_WORKSPACE_ROOTS) {
      const runDir = join(workspaceRoot, root.name, "e-1");
      const stats = statSync(runDir);
      // Claimed by the supervisor uid, not a pool uid, though mode stays 1777 like the root above it.
      expect((stats.mode & 0o7777).toString(8)).toBe("1777");
      if (ownUid !== undefined) {
        expect(stats.uid).toBe(ownUid);
      }
    }
  });

  it("the real uid sweep clears the uid's leftovers but spares the supervisor-claimed trace root", async () => {
    const workspaceRoot = mkdtempSync(join(tmpdir(), "spawner-sweep-"));
    const traceRoot = join(workspaceRoot, "traces");
    claimRetainedRoot(traceRoot);
    const packFile = join(traceRoot, "e-1", "attempt-1", "trace.ndjson");
    mkdirSync(join(traceRoot, "e-1", "attempt-1"), { recursive: true });
    writeFileSync(packFile, '{"type":"envelope"}\n');
    const leftover = join(workspaceRoot, "run-leftover");
    mkdirSync(leftover);
    const ownUid = process.getuid?.();
    if (ownUid === undefined) {
      throw new Error("this suite requires a POSIX uid");
    }
    const { runner } = oneShotSuccessChild();
    const spawner = createRunSpawner({
      pool: createUidPool({ baseUid: ownUid, size: 1 }),
      childRun: runner,
      workspaceRoot,
      tmpRoot: mkdtempSync(join(tmpdir(), "spawner-tmp-")),
      subprocessEnv: { PATH: "/usr/bin" },
      modelProvider: { provider: "bedrock", identity: { ambient: true } },
      buildIdentity: { version: "0.0.0-test", revision: "testrev" },
      pauseGraceMs: 60_000,
      documentStore: emptyDocumentStore(),
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(existsSync(leftover)).toBe(false);
    expect(existsSync(packFile)).toBe(true);
  });

  it("the real uid sweep spares the supervisor-claimed artifacts root", async () => {
    const workspaceRoot = mkdtempSync(join(tmpdir(), "spawner-sweep-"));
    const artifactsRoot = join(workspaceRoot, "artifacts");
    claimRetainedRoot(artifactsRoot);
    const publishedFile = join(artifactsRoot, "e-1", "attempt-1", "out", "report.md");
    mkdirSync(join(artifactsRoot, "e-1", "attempt-1", "out"), { recursive: true });
    writeFileSync(publishedFile, "# report");
    const leftover = join(workspaceRoot, "run-leftover");
    mkdirSync(leftover);
    const ownUid = process.getuid?.();
    if (ownUid === undefined) {
      throw new Error("this suite requires a POSIX uid");
    }
    const { runner } = oneShotSuccessChild();
    const spawner = createRunSpawner({
      pool: createUidPool({ baseUid: ownUid, size: 1 }),
      childRun: runner,
      workspaceRoot,
      tmpRoot: mkdtempSync(join(tmpdir(), "spawner-tmp-")),
      subprocessEnv: { PATH: "/usr/bin" },
      modelProvider: { provider: "bedrock", identity: { ambient: true } },
      buildIdentity: { version: "0.0.0-test", revision: "testrev" },
      pauseGraceMs: 60_000,
      documentStore: emptyDocumentStore(),
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(existsSync(leftover)).toBe(false);
    expect(existsSync(publishedFile)).toBe(true);
  });

  it("the real sweep clears the uid's shared-tmp residue before the same uid serves the next run", async () => {
    const workspaceRoot = mkdtempSync(join(tmpdir(), "spawner-sweep-"));
    const tmpRoot = mkdtempSync(join(tmpdir(), "spawner-tmp-"));
    const ownUid = process.getuid?.();
    if (ownUid === undefined) {
      throw new Error("this suite requires a POSIX uid");
    }
    const sdkTempDir = join(tmpRoot, `claude-${ownUid}`);
    const marker = join(sdkTempDir, "exfil.txt");
    let tenantAHasRun = false;
    let markerVisibleToTenantB: boolean | undefined;
    const { runner } = makeFakeChildRun((child) => {
      if (tenantAHasRun) {
        markerVisibleToTenantB = existsSync(marker);
      } else {
        mkdirSync(sdkTempDir, { mode: 0o700 });
        writeFileSync(marker, "CUSTOMER-A-FINANCIALS");
        tenantAHasRun = true;
      }
      queueMicrotask(() => {
        child.emit({ kind: "final", response: { ok: true, result: RESULT } });
        child.exit();
      });
    });
    const spawner = createRunSpawner({
      pool: createUidPool({ baseUid: ownUid, size: 1 }),
      childRun: runner,
      workspaceRoot,
      tmpRoot,
      subprocessEnv: { PATH: "/usr/bin" },
      modelProvider: { provider: "bedrock", identity: { ambient: true } },
      buildIdentity: { version: "0.0.0-test", revision: "testrev" },
      pauseGraceMs: 60_000,
      documentStore: emptyDocumentStore(),
    });
    await spawner({ executionId: "tenant-a", prompt: "p" });
    expect(existsSync(sdkTempDir)).toBe(false);
    await spawner({ executionId: "tenant-b", prompt: "p" });
    expect(markerVisibleToTenantB).toBe(false);
  });

  it("never returns a uid to the pool while a process still runs under it", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const { runner } = oneShotSuccessChild();
    const spawner = spawnerWith(runner, {
      pool,
      waitForUidIdle: () => Promise.resolve(false),
    });
    expect(await spawner({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
    expect(pool.available()).toBe(0);
  });

  it("returns the uid even when the child fails", async () => {
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.exit({ code: 1, stderr: "child died" }));
    });
    const spawner = spawnerWith(runner, { pool: createUidPool({ baseUid: 20001, size: 1 }) });
    await expect(spawner({ executionId: "a", prompt: "p" })).rejects.toThrow(/child died/);
    await expect(spawner({ executionId: "b", prompt: "p" })).rejects.toThrow(/child died/);
  });

  it("rethrows a child failure envelope as RunFailedError, keeping cost and session", async () => {
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({
          kind: "final",
          response: {
            ok: false,
            error: { message: "nope", subtype: "error_max_turns", estimatedCostUsd: 0.2, sessionId: "s-9" },
          },
        });
        child.exit();
      });
    });
    let caught: unknown;
    try {
      await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("error_max_turns");
    expect(caught.estimatedCostUsd).toBe(0.2);
    expect(caught.sessionId).toBe("s-9");
  });

  it("fails the run under a stable subtype when every uid is busy, rather than sharing one", async () => {
    let release = (): void => {};
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { runner } = makeFakeChildRun((child) => {
      void held.then(() => {
        child.emit({ kind: "final", response: { ok: true, result: RESULT } });
        child.exit();
      });
    });
    const spawner = spawnerWith(runner, { pool: createUidPool({ baseUid: 20001, size: 1 }) });
    const first = spawner({ executionId: "a", prompt: "p" });
    let caught: unknown;
    try {
      await spawner({ executionId: "b", prompt: "p" });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("uid_pool_exhausted");
    release();
    await first;
  });

  it("kills the child when the caller aborts the run", async () => {
    const { runner, children } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
    });
    const controller = new AbortController();
    const promise = spawnerWith(runner)({ executionId: "e-1", prompt: "p" }, { signal: controller.signal });
    await firstSpawnOf(children);
    controller.abort();
    await expect(promise).rejects.toThrow(/killed/);
  });

  it("hands the configured role down to the child so it can assume it", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner, {
      modelProvider: { provider: "bedrock", identity: { roleArn: "arn:aws:iam::1:role/agent" } },
    })({
      executionId: "e-1",
      prompt: "p",
    });
    expect(children[0]?.input.env["AGENT_RUNTIME_ROLE_ARN"]).toBe("arn:aws:iam::1:role/agent");
  });

  it("keeps host credentials out of the child once a role is configured", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner, {
      subprocessEnv: { PATH: "/usr/bin", AWS_ACCESS_KEY_ID: "AKIA", AWS_SESSION_TOKEN: "t" },
      modelProvider: { provider: "bedrock", identity: { roleArn: "arn:aws:iam::1:role/agent" } },
    })({ executionId: "e-1", prompt: "p" });
    expect(children[0]?.input.env["AWS_ACCESS_KEY_ID"]).toBeUndefined();
    expect(children[0]?.input.env["AWS_SESSION_TOKEN"]).toBeUndefined();
    expect(children[0]?.input.env["PATH"]).toBe("/usr/bin");
  });

  it("strips host credentials and marks metadata mode for the child when metadata credentials are on", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner, {
      subprocessEnv: { PATH: "/usr/bin", AWS_ACCESS_KEY_ID: "AKIA", AWS_SESSION_TOKEN: "t" },
      modelProvider: { provider: "bedrock", identity: { metadataCredentials: true } },
    })({ executionId: "e-1", prompt: "p" });
    expect(children[0]?.input.env["AWS_ACCESS_KEY_ID"]).toBeUndefined();
    expect(children[0]?.input.env["AWS_SESSION_TOKEN"]).toBeUndefined();
    expect(children[0]?.input.env["PATH"]).toBe("/usr/bin");
    expect(children[0]?.input.env["AGENT_RUNTIME_METADATA_CREDENTIALS"]).toBe("true");
    expect("AGENT_RUNTIME_ROLE_ARN" in (children[0]?.input.env ?? {})).toBe(false);
  });

  it("leaves ambient credentials in place when no role is configured", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner, {
      subprocessEnv: { PATH: "/usr/bin", AWS_ACCESS_KEY_ID: "AKIA" },
    })({ executionId: "e-1", prompt: "p" });
    expect(children[0]?.input.env["AWS_ACCESS_KEY_ID"]).toBe("AKIA");
  });

  it("omits the role variable entirely when running on ambient credentials", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    expect("AGENT_RUNTIME_ROLE_ARN" in (children[0]?.input.env ?? {})).toBe(false);
  });

  it("bedrock role mode hands the child exactly the historical env, in the historical key order", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner, {
      subprocessEnv: { PATH: "/usr/bin", AWS_REGION: "us-west-2", AWS_ACCESS_KEY_ID: "AKIA" },
      modelProvider: { provider: "bedrock", identity: { roleArn: "arn:aws:iam::1:role/agent" } },
      buildIdentity: { version: "0.0.0-test", revision: "testrev" },
    })({ executionId: "e-1", prompt: "p" });
    const env = children[0]?.input.env ?? {};
    expect(Object.entries(env)).toEqual([
      ["PATH", "/usr/bin"],
      ["AWS_REGION", "us-west-2"],
      ["WORKSPACE_ROOT", env["WORKSPACE_ROOT"] ?? ""],
      ["AGENT_RUNTIME_VERSION", "0.0.0-test"],
      ["AGENT_RUNTIME_REVISION", "testrev"],
      ["AGENT_RUNTIME_ROLE_ARN", "arn:aws:iam::1:role/agent"],
    ]);
  });

  it("bedrock mode omits the provider variable entirely, keeping the child env byte-identical", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    expect("AGENT_RUNTIME_MODEL_PROVIDER" in (children[0]?.input.env ?? {})).toBe(false);
  });

  it("foundry mode hands the provider down and never an AWS identity variable", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner, {
      subprocessEnv: { PATH: "/usr/bin", CLAUDE_CODE_USE_FOUNDRY: "1", ANTHROPIC_FOUNDRY_RESOURCE: "res" },
      modelProvider: { provider: "foundry" },
    })({ executionId: "e-1", prompt: "p" });
    const env = children[0]?.input.env ?? {};
    expect(env["AGENT_RUNTIME_MODEL_PROVIDER"]).toBe("foundry");
    expect(env["ANTHROPIC_FOUNDRY_RESOURCE"]).toBe("res");
    expect(env["PATH"]).toBe("/usr/bin");
    expect("AGENT_RUNTIME_ROLE_ARN" in env).toBe(false);
    expect("AGENT_RUNTIME_METADATA_CREDENTIALS" in env).toBe(false);
  });

  it("anthropic mode hands the provider down and never the key the key holder keeps", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner, {
      subprocessEnv: {
        PATH: "/usr/bin",
        ANTHROPIC_BASE_URL: "http://127.0.0.1:8131",
        ANTHROPIC_API_KEY: "agent-runtime-anthropic-proxy",
      },
      modelProvider: { provider: "anthropic", proxy: { apiKey: "sk-ant-api03-real-key" } },
    })({ executionId: "e-1", prompt: "p" });
    const env = children[0]?.input.env ?? {};
    expect(env["AGENT_RUNTIME_MODEL_PROVIDER"]).toBe("anthropic");
    expect(env["ANTHROPIC_API_KEY"]).toBe("agent-runtime-anthropic-proxy");
    expect(JSON.stringify(env)).not.toContain("sk-ant-api03-real-key");
  });

  it("rejects a child that emits something other than a run frame", async () => {
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.input.onFrame("not json at all"));
    });
    await expect(spawnerWith(runner)({ executionId: "e-1", prompt: "p" })).rejects.toThrow();
  });
});

describe("run spawner documents", () => {
  const DOC_BYTES = new TextEncoder().encode("stored document body");

  function storeWithDocuments(): DocumentStore {
    return createLocalDocumentStore(join(mkdtempSync(join(tmpdir(), "spawner-docs-")), "store"));
  }

  function spyStore(opens: string[]): DocumentStore {
    return {
      save: () => Promise.resolve(),
      openInputs: (executionId) => {
        opens.push(executionId);
        return Promise.resolve(undefined);
      },
      delete: () => Promise.resolve(),
    };
  }

  it("carries the stored documents on the start frame as metadata and streams the bytes over cargo", async () => {
    const documentStore = storeWithDocuments();
    await documentStore.save("e-1", [
      {
        name: "report.pdf",
        bytes: DOC_BYTES,
        sha256: sha256Hex(DOC_BYTES),
        area: "documents",
        mediaType: "application/pdf",
      },
    ]);
    const { runner, children } = cargoConsumingSuccessChild();
    await spawnerWith(runner, { documentStore })({ executionId: "e-1", prompt: "p" });
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(first["documents"]).toEqual([
      {
        name: "report.pdf",
        sha256: sha256Hex(DOC_BYTES),
        sizeBytes: DOC_BYTES.byteLength,
        mediaType: "application/pdf",
      },
    ]);
    const firstChild = children[0];
    if (firstChild === undefined) {
      throw new Error("no child was spawned");
    }
    expect((await cargoFlushedOf(firstChild)).equals(cargoPrefixed([DOC_BYTES]))).toBe(true);
  });

  it("omits the documents field entirely for a run the store holds nothing for", async () => {
    const opens: string[] = [];
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner, { documentStore: spyStore(opens) })({ executionId: "e-1", prompt: "p" });
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect("documents" in first).toBe(false);
    expect(opens).toEqual(["e-1"]);
    expect(children[0]?.cargoBytes().byteLength).toBe(0);
  });

  it("kills the child on a same-tick abort even when the run carries documents", async () => {
    const { runner, children } = makeFakeChildRun((child) => {
      const exitKilled = (): void => {
        child.exit({ code: null, signal: "SIGKILL", stderr: "killed" });
      };
      if (child.input.signal.aborted) {
        queueMicrotask(exitKilled);
      } else {
        child.input.signal.addEventListener("abort", exitKilled, { once: true });
      }
    });
    const documentStore = storeWithDocuments();
    await documentStore.save("e-1", [
      { name: "report.pdf", bytes: DOC_BYTES, sha256: sha256Hex(DOC_BYTES), area: "documents" },
    ]);
    const controller = new AbortController();
    const promise = spawnerWith(runner, { documentStore })(
      { executionId: "e-1", prompt: "p" },
      { signal: controller.signal },
    );
    controller.abort();
    await expect(promise).rejects.toThrow(/killed/);
    expect(children).toHaveLength(1);
  });

  it("streams the stored documents to the cold-resume child again, metadata on the resume frame", async () => {
    const documentStore = storeWithDocuments();
    await documentStore.save("e-1", [
      { name: "report.pdf", bytes: DOC_BYTES, sha256: sha256Hex(DOC_BYTES), area: "documents" },
    ]);
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      if (children.length === 1) {
        queueMicrotask(() => child.emit(pausedFrame()));
        return;
      }
      finishOnceCargoDrained(child);
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore, documentStore });
    const result = await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    expect(result).toEqual(RESULT);
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["kind"]).toBe("resume");
    expect(resumeRequest["documents"]).toEqual([
      { name: "report.pdf", sha256: sha256Hex(DOC_BYTES), sizeBytes: DOC_BYTES.byteLength },
    ]);
    const resumeChild = children[1];
    if (resumeChild === undefined) {
      throw new Error("no resume child was spawned");
    }
    const expectedResumeCargo = cargoPrefixed([CHECKPOINT.bytes, DOC_BYTES]);
    expect((await cargoFlushedOf(resumeChild)).equals(expectedResumeCargo)).toBe(true);
  });
});

describe("run spawner skill files", () => {
  const SKILL_BYTES = new TextEncoder().encode("# how to grade");
  const DOC_BYTES = new TextEncoder().encode("stored document body");
  const ASSET_BYTES = new TextEncoder().encode("print('check')");

  function storeWith(inputs: Parameters<DocumentStore["save"]>[1]): Promise<DocumentStore> {
    const store = createLocalDocumentStore(join(mkdtempSync(join(tmpdir(), "spawner-skills-")), "store"));
    return store.save("e-1", inputs).then(() => store);
  }

  it("carries skill metas under their own field and streams documents, skills, then assets in order", async () => {
    const documentStore = await storeWith([
      { name: "report.pdf", bytes: DOC_BYTES, sha256: sha256Hex(DOC_BYTES), area: "documents" },
      {
        name: "grading.md",
        bytes: SKILL_BYTES,
        sha256: sha256Hex(SKILL_BYTES),
        area: "skills",
        description: "How to grade",
        assets: [{ name: "verify.py", bytes: ASSET_BYTES, sha256: sha256Hex(ASSET_BYTES) }],
      },
    ]);
    const { runner, children } = cargoConsumingSuccessChild();
    await spawnerWith(runner, { documentStore })({ executionId: "e-1", prompt: "p" });
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(first["documents"]).toEqual([
      { name: "report.pdf", sha256: sha256Hex(DOC_BYTES), sizeBytes: DOC_BYTES.byteLength },
    ]);
    expect(first["skillFiles"]).toEqual([
      {
        name: "grading.md",
        sha256: sha256Hex(SKILL_BYTES),
        sizeBytes: SKILL_BYTES.byteLength,
        description: "How to grade",
        assets: [{ name: "verify.py", sha256: sha256Hex(ASSET_BYTES), sizeBytes: ASSET_BYTES.byteLength }],
      },
    ]);
    const firstChild = children[0];
    if (firstChild === undefined) {
      throw new Error("no child was spawned");
    }
    const expectedCargo = cargoPrefixed([DOC_BYTES, SKILL_BYTES, ASSET_BYTES]);
    expect((await cargoFlushedOf(firstChild)).equals(expectedCargo)).toBe(true);
  });

  it("omits the skillFiles field entirely for a run that carries none", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect("skillFiles" in first).toBe(false);
  });

  it("re-reads stored skill files onto the cold resume frame, split from the documents", async () => {
    const documentStore = await storeWith([
      { name: "report.pdf", bytes: DOC_BYTES, sha256: sha256Hex(DOC_BYTES), area: "documents" },
      { name: "grading.md", bytes: SKILL_BYTES, sha256: sha256Hex(SKILL_BYTES), area: "skills" },
    ]);
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      if (children.length === 1) {
        queueMicrotask(() => child.emit(pausedFrame()));
        return;
      }
      finishOnceCargoDrained(child);
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore, documentStore });
    const result = await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    expect(result).toEqual(RESULT);
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["documents"]).toEqual([
      { name: "report.pdf", sha256: sha256Hex(DOC_BYTES), sizeBytes: DOC_BYTES.byteLength },
    ]);
    expect(resumeRequest["skillFiles"]).toEqual([
      { name: "grading.md", sha256: sha256Hex(SKILL_BYTES), sizeBytes: SKILL_BYTES.byteLength },
    ]);
    const resumeChild = children[1];
    if (resumeChild === undefined) {
      throw new Error("no resume child was spawned");
    }
    const expectedResumeCargo = cargoPrefixed([CHECKPOINT.bytes, DOC_BYTES, SKILL_BYTES]);
    expect((await cargoFlushedOf(resumeChild)).equals(expectedResumeCargo)).toBe(true);
  });
});

describe("run spawner cargo delivery under a slow pipe", () => {
  const SKILL_BYTES = new TextEncoder().encode("# how to grade");
  const DOC_BYTES = new TextEncoder().encode("stored document body");

  it("delivers the whole cold-resume cargo before the fake child may finish", async () => {
    const documentStore = createLocalDocumentStore(
      join(mkdtempSync(join(tmpdir(), "spawner-slow-")), "store"),
    );
    await documentStore.save("e-1", [
      { name: "report.pdf", bytes: DOC_BYTES, sha256: sha256Hex(DOC_BYTES), area: "documents" },
      { name: "grading.md", bytes: SKILL_BYTES, sha256: sha256Hex(SKILL_BYTES), area: "skills" },
    ]);
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    // The pipe accepts one chunk per turn of the event loop, the way a store-backed source
    // lands on a loaded runner: every intermediate byte count is observable between chunks.
    const { runner, children } = makeFakeChildRun(
      (child) => {
        if (children.length === 1) {
          queueMicrotask(() => child.emit(pausedFrame()));
          return;
        }
        finishOnceCargoDrained(child);
      },
      { acceptCargoChunk: (accepted) => setImmediate(accepted) },
    );
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore, documentStore });
    const result = await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    expect(result).toEqual(RESULT);
    const resumeChild = children[1];
    if (resumeChild === undefined) {
      throw new Error("no resume child was spawned");
    }
    const expectedResumeCargo = cargoPrefixed([CHECKPOINT.bytes, DOC_BYTES, SKILL_BYTES]);
    expect((await cargoFlushedOf(resumeChild)).equals(expectedResumeCargo)).toBe(true);
  });
});

describe("run spawner cargo failures", () => {
  const DOC_BYTES = new TextEncoder().encode("stored document body");

  function storeWithBrokenSource(): DocumentStore {
    return {
      save: () => Promise.resolve(),
      openInputs: () =>
        Promise.resolve([
          {
            area: "documents",
            name: "report.pdf",
            sha256: sha256Hex(DOC_BYTES),
            sizeBytes: DOC_BYTES.byteLength,
            open: () =>
              new Readable({
                read() {
                  this.destroy(new Error("store bytes vanished"));
                },
              }),
          },
        ]),
      delete: () => Promise.resolve(),
    };
  }

  it("kills the child, logs cargo_stream_failed, and names the cause when the cargo source fails", async () => {
    const { runner, children } = makeFakeChildRun(() => {});
    const { events, logger } = captureLogger();
    const spawner = spawnerWith(runner, { documentStore: storeWithBrokenSource(), logger });
    const failure = await spawner({ executionId: "e-1", prompt: "p" }).then(
      () => {
        throw new Error("the run must fail");
      },
      (error: unknown) => error,
    );
    expect(String(failure)).toContain("document cargo stream failed");
    expect(String(failure)).toContain("store bytes vanished");
    expect(children[0]?.killed).toBe(true);
    const warns = events.filter((event) => event.event === "cargo_stream_failed");
    expect(warns).toHaveLength(1);
    expect(warns[0]?.level).toBe("warn");
  });

  it("settles a cargo write left pending when the child dies, leaving the supervisor healthy", async () => {
    const documentStore = createLocalDocumentStore(
      join(mkdtempSync(join(tmpdir(), "spawner-cargo-")), "store"),
    );
    await documentStore.save("e-1", [
      { name: "report.pdf", bytes: DOC_BYTES, sha256: sha256Hex(DOC_BYTES), area: "documents" },
    ]);
    const { events, logger, logged } = captureLogger();
    const { runner, children } = makeFakeChildRun(
      (child) => {
        setTimeout(() => child.exit({ code: null, signal: "SIGKILL", stderr: "oom killed" }), 10);
      },
      {
        // A pipe that accepts nothing: every write stays pending until teardown settles it.
        cargoPipe: () => {
          const pipe = new Writable({
            highWaterMark: 1,
            write() {},
          });
          pipe.on("error", () => {});
          return pipe;
        },
      },
    );
    const spawner = spawnerWith(runner, { documentStore, logger });
    const failure = await spawner({ executionId: "e-1", prompt: "p" }).then(
      () => {
        throw new Error("the run must fail");
      },
      (error: unknown) => error,
    );
    expect(String(failure)).toContain("SIGKILL");
    // The child died on its own, so its death is the story — not the cargo pipe rejecting afterwards.
    expect(String(failure)).not.toContain("document cargo stream failed");
    expect(children).toHaveLength(1);
    await logged("cargo_stream_failed");
    expect(events.some((event) => event.event === "cargo_stream_failed")).toBe(true);
  });
});

describe("run spawner agents", () => {
  const AGENT_DECLS = [
    { name: "verifier", description: "checks figures", prompt: "verify", skills: ["grading.md"] },
  ];

  it("carries the declared agents on the start frame and omits the field when undeclared", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner)({ executionId: "e-1", prompt: "p" }, { agents: AGENT_DECLS });
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(first["agents"]).toEqual(AGENT_DECLS);
    const bare = oneShotSuccessChild();
    await spawnerWith(bare.runner)({ executionId: "e-2", prompt: "p" });
    const bareFirst = JSON.parse(bare.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect("agents" in bareFirst).toBe(false);
  });

  it("re-pins the declared agents on the cold resume frame", async () => {
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore });
    await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        agents: AGENT_DECLS,
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["agents"]).toEqual(AGENT_DECLS);
  });
});

describe("run spawner model", () => {
  it("carries the chosen model on the start frame and omits it when unset", async () => {
    const first = oneShotSuccessChild();
    await spawnerWith(first.runner)(
      { executionId: "e-1", prompt: "p" },
      { model: "us.anthropic.claude-sonnet-4-6" },
    );
    const withModel = JSON.parse(first.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(withModel["model"]).toBe("us.anthropic.claude-sonnet-4-6");
    const second = oneShotSuccessChild();
    await spawnerWith(second.runner)({ executionId: "e-1", prompt: "p" });
    const withoutModel = JSON.parse(second.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect("model" in withoutModel).toBe(false);
  });

  it("carries the model onto the cold resume frame — a resumed run continues on its model", async () => {
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore });
    await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        model: "us.anthropic.claude-sonnet-4-6",
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["kind"]).toBe("resume");
    expect(resumeRequest["model"]).toBe("us.anthropic.claude-sonnet-4-6");
  });
});

describe("run spawner per-run fuses", () => {
  it("prefers the caller's fuse values over the runtime defaults on the start frame", async () => {
    const { runner, children } = oneShotSuccessChild();
    const spawner = spawnerWith(runner, { deadlineMs: 5000, maxBudgetUsd: 2 });
    await spawner(
      { executionId: "e-1", prompt: "p" },
      { maxBudgetUsd: 0.5, deadlineMs: 600, maxTurns: 25, effort: "low", maxRetainedOutputBytes: 4096 },
    );
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(first["maxBudgetUsd"]).toBe(0.5);
    expect(first["deadlineMs"]).toBe(600);
    expect(first["maxTurns"]).toBe(25);
    expect(first["effort"]).toBe("low");
    expect(first["maxRetainedOutputBytes"]).toBe(4096);
  });

  it("sends the runtime turn cap and omits effort when the caller names neither", async () => {
    const { runner, children } = oneShotSuccessChild();
    const spawner = spawnerWith(runner, { maxTurns: 120 });
    await spawner({ executionId: "e-1", prompt: "p" });
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(first["maxTurns"]).toBe(120);
    expect("effort" in first).toBe(false);
    expect(first["maxRetainedOutputBytes"]).toBe(DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES);
  });

  it("carries the caller's fuse values onto the cold resume frame — a resumed run keeps its limits", async () => {
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore });
    await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        maxBudgetUsd: 0.5,
        deadlineMs: 60_000,
        maxTurns: 25,
        effort: "medium",
        maxRetainedOutputBytes: 4096,
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["kind"]).toBe("resume");
    expect(resumeRequest["maxBudgetUsd"]).toBe(0.5);
    expect(resumeRequest["deadlineMs"]).toBe(60_000);
    expect(resumeRequest["maxTurns"]).toBe(25);
    expect(resumeRequest["effort"]).toBe("medium");
    expect(resumeRequest["maxRetainedOutputBytes"]).toBe(4096);
  });

  it("enforces a caller-shortened deadline: the supervisor kills the child on the per-run clock", async () => {
    const { runner } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
    });
    const spawner = spawnerWith(runner, { deadlineMs: 60_000, childGraceMs: 0 });
    let caught: unknown;
    try {
      await spawner({ executionId: "e-1", prompt: "p" }, { deadlineMs: 20 });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("deadline_exceeded");
  });
});

describe("run spawner outputFormat", () => {
  const OUTPUT_FORMAT = { type: "json_schema" as const, schema: { type: "object" } };

  it("carries the output format on the start frame and omits it when unset", async () => {
    const first = oneShotSuccessChild();
    await spawnerWith(first.runner)({ executionId: "e-1", prompt: "p" }, { outputFormat: OUTPUT_FORMAT });
    const withFormat = JSON.parse(first.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(withFormat["outputFormat"]).toEqual({ type: "json_schema", schema: { type: "object" } });
    const second = oneShotSuccessChild();
    await spawnerWith(second.runner)({ executionId: "e-1", prompt: "p" });
    const withoutFormat = JSON.parse(second.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect("outputFormat" in withoutFormat).toBe(false);
  });

  it("carries the output format onto the cold resume frame", async () => {
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore });
    await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        outputFormat: OUTPUT_FORMAT,
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["kind"]).toBe("resume");
    expect(resumeRequest["outputFormat"]).toEqual({ type: "json_schema", schema: { type: "object" } });
  });
});

describe("run spawner systemPrompt", () => {
  const SYSTEM_PROMPT = ["static role and rules", "per-pass context"];

  it("carries the system prompt on the start frame and omits it when unset", async () => {
    const first = oneShotSuccessChild();
    await spawnerWith(first.runner)({ executionId: "e-1", prompt: "p" }, { systemPrompt: SYSTEM_PROMPT });
    const withPrompt = JSON.parse(first.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(withPrompt["systemPrompt"]).toEqual(SYSTEM_PROMPT);
    const second = oneShotSuccessChild();
    await spawnerWith(second.runner)({ executionId: "e-1", prompt: "p" });
    const withoutPrompt = JSON.parse(second.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect("systemPrompt" in withoutPrompt).toBe(false);
  });

  it("carries the system prompt onto the cold resume frame", async () => {
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore });
    await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        systemPrompt: SYSTEM_PROMPT,
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["kind"]).toBe("resume");
    expect(resumeRequest["systemPrompt"]).toEqual(SYSTEM_PROMPT);
  });
});

describe("run spawner MCP server and run token", () => {
  it("carries the MCP URL and run token on the start frame and omits both when unset", async () => {
    const first = oneShotSuccessChild();
    await spawnerWith(first.runner)(
      { executionId: "e-1", prompt: "p" },
      { mcpServerUrl: "https://platform.internal/mcp", runToken: "tok-1" },
    );
    const withMcp = JSON.parse(first.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(withMcp["mcpServerUrl"]).toBe("https://platform.internal/mcp");
    expect(withMcp["runToken"]).toBe("tok-1");
    const second = oneShotSuccessChild();
    await spawnerWith(second.runner)({ executionId: "e-1", prompt: "p" });
    const bare = JSON.parse(second.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect("mcpServerUrl" in bare).toBe(false);
    expect("runToken" in bare).toBe(false);
  });

  it("never sends the run token without the MCP binding it is the credential for", async () => {
    // Checked on both frame kinds, since they're built in two separate places.
    const start = oneShotSuccessChild();
    await spawnerWith(start.runner)({ executionId: "e-1", prompt: "p" }, { runToken: "tok-1" });
    const startFrame = JSON.parse(start.children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect("mcpServerUrl" in startFrame).toBe(false);
    expect("runToken" in startFrame).toBe(false);

    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    await spawnerWith(runner, { pauseGraceMs: 0, checkpointStore })(
      { executionId: "e-1", prompt: "p" },
      {
        runToken: "tok-1",
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    const resumeFrame = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeFrame["kind"]).toBe("resume");
    expect("mcpServerUrl" in resumeFrame).toBe(false);
    expect("runToken" in resumeFrame).toBe(false);
  });

  it("keeps the run token memory-only: it rides the cold resume frame, never the child env", async () => {
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore });
    await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        mcpServerUrl: "https://platform.internal/mcp",
        runToken: "tok-1",
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["kind"]).toBe("resume");
    expect(resumeRequest["mcpServerUrl"]).toBe("https://platform.internal/mcp");
    expect(resumeRequest["runToken"]).toBe("tok-1");
    for (const child of children) {
      expect(JSON.stringify(child.input.env)).not.toContain("tok-1");
    }
  });
});

describe("run spawner pause and resume", () => {
  it("keeps the child alive through a warm resume: answers flow back over stdin", async () => {
    const { runner, children } = makeFakeChildRun((child) => {
      if (children.length > 1) {
        throw new Error("warm resume must not spawn a second child");
      }
      child.onSend = (line) => {
        if ((JSON.parse(line) as Record<string, unknown>)["kind"] === "answer") {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      };
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    const pauses: PauseRequest[] = [];
    const spawner = spawnerWith(runner, { pauseGraceMs: 60_000 });
    const promise = spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: (request) => {
          pauses.push(request);
          return Promise.resolve(RESOLUTION);
        },
      },
    );
    expect(await promise).toEqual(RESULT);
    expect(children).toHaveLength(1);
    expect(children[0]?.killed).toBe(false);
    const answerFrame = JSON.parse(children[0]?.sent[1] ?? "{}") as Record<string, unknown>;
    expect(answerFrame["kind"]).toBe("answer");
    expect(answerFrame["answer"]).toBe("the second fund");
    expect(answerFrame["allowFurtherPause"]).toBe(true);
    expect(pauses[0]?.question).toEqual(QUESTION);
    expect(pauses[0]?.sessionId).toBe("s-1");
    expect(pauses[0]?.estimatedCostUsd).toBe(0.2);
    expect(pauses[0]?.checkpoint.sha256).toBe(CHECKPOINT.sha256);
  });

  it("kills the paused child immediately under a zero grace window, then resumes cold", async () => {
    const store = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    let releaseAnswers = (): void => {};
    const answersHeld = new Promise<void>((resolve) => {
      releaseAnswers = resolve;
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore: store });
    const promise = spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          await store.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          await answersHeld;
          return RESOLUTION;
        },
      },
    );
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(children[0]?.killed).toBe(true);
    expect(children[0]?.isAlive()).toBe(false);
    releaseAnswers();
    expect(await promise).toEqual(RESULT);
    expect(children).toHaveLength(2);
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["kind"]).toBe("resume");
    expect(resumeRequest["executionId"]).toBe("e-1");
    expect(resumeRequest["answer"]).toBe("the second fund");
    expect(resumeRequest["allowFurtherPause"]).toBe(true);
    expect(resumeRequest["priorCostUsd"]).toBe(0.2);
    const checkpoint = resumeRequest["checkpoint"];
    expect(checkpoint).toEqual({
      sha256: CHECKPOINT.sha256,
      sizeBytes: CHECKPOINT.bytes.byteLength,
    });
  });

  it("kills the child once the grace window lapses, leaving the checkpoint intact", async () => {
    const store = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    let releaseAnswers = (): void => {};
    const answersHeld = new Promise<void>((resolve) => {
      releaseAnswers = resolve;
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 25, checkpointStore: store });
    const promise = spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          await store.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          await answersHeld;
          return RESOLUTION;
        },
      },
    );
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(children[0]?.killed).toBe(false);
    await new Promise((resolve) => setTimeout(resolve, 40));
    expect(children[0]?.killed).toBe(true);
    expect(await store.load({ executionId: "e-1", attempt: 1, segment: 0 })).toBeDefined();
    releaseAnswers();
    expect(await promise).toEqual(RESULT);
    expect(children).toHaveLength(2);
  });

  it("keys a second pause at segment 1 so its cold resume carries the later bundle, not the first", async () => {
    const store = createLocalCheckpointStore(newStoreRoot());
    const secondCheckpoint = sealCheckpoint(new TextEncoder().encode('{"sessionId":"s-2"}'));
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else if (children.length === 2) {
          child.emit({
            kind: "paused",
            question: QUESTION,
            sessionId: "s-2",
            estimatedCostUsd: 0.3,
            checkpoint: {
              sha256: secondCheckpoint.sha256,
              bytesBase64: Buffer.from(secondCheckpoint.bytes).toString("base64"),
            },
          });
        } else {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    let pauseIndex = 0;
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore: store });
    const result = await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          const segment = pauseIndex;
          pauseIndex += 1;
          await store.save({ executionId: "e-1", attempt: 1, segment }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    expect(result).toEqual(RESULT);
    expect(children).toHaveLength(3);
    const firstResume = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(firstResume["checkpoint"]).toEqual({
      sha256: CHECKPOINT.sha256,
      sizeBytes: CHECKPOINT.bytes.byteLength,
    });
    const secondResume = JSON.parse(children[2]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(secondResume["kind"]).toBe("resume");
    expect(secondResume["checkpoint"]).toEqual({
      sha256: secondCheckpoint.sha256,
      sizeBytes: secondCheckpoint.bytes.byteLength,
    });
    expect(await store.load({ executionId: "e-1", attempt: 1, segment: 1 })).toBeDefined();
  });

  it("routes answers to the cold path once a kill was requested, even before the child has died", async () => {
    const store = createLocalCheckpointStore(newStoreRoot());
    let child0AliveWhenChild1Spawned: boolean | undefined;
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child0AliveWhenChild1Spawned = children[0]?.isAlive();
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      });
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore: store });
    const result = await spawner(
      { executionId: "e-1", prompt: "p" },
      {
        pause: async (request) => {
          await store.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return RESOLUTION;
        },
      },
    );
    expect(result).toEqual(RESULT);
    expect(children).toHaveLength(2);
    expect(children[0]?.killed).toBe(true);
    expect(children[0]?.sent).toHaveLength(1);
    expect(child0AliveWhenChild1Spawned).toBe(false);
  });

  it("disarms the kill timer while paused: a pause outliving the deadline still resumes warm", async () => {
    const { runner, children } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed by the deadline" }),
        { once: true },
      );
      child.onSend = (line) => {
        if ((JSON.parse(line) as Record<string, unknown>)["kind"] === "answer") {
          child.emit({ kind: "final", response: { ok: true, result: RESULT } });
          child.exit();
        }
      };
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    const spawner = spawnerWith(runner, { deadlineMs: 20, childGraceMs: 0, pauseGraceMs: 60_000 });
    const result = await spawner(
      { executionId: "e-1", prompt: "p" },
      { pause: () => new Promise((resolve) => setTimeout(() => resolve(RESOLUTION), 100)) },
    );
    expect(result).toEqual(RESULT);
    expect(children).toHaveLength(1);
    expect(children[0]?.killed).toBe(false);
  });

  it("re-arms the kill timer on resume: a child hanging after its answers is killed", async () => {
    const { runner, children } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    const spawner = spawnerWith(runner, { deadlineMs: 30, childGraceMs: 0, pauseGraceMs: 60_000 });
    const promise = spawner(
      { executionId: "e-1", prompt: "p" },
      { pause: () => Promise.resolve(RESOLUTION) },
    );
    await expect(promise).rejects.toThrow(/deadline/);
    expect(children[0]?.sent).toHaveLength(2);
  });

  it("fails the run when a cold resume finds no checkpoint in the store", async () => {
    const store = createLocalCheckpointStore(newStoreRoot());
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore: store });
    let caught: unknown;
    try {
      await spawner({ executionId: "e-1", prompt: "p" }, { pause: () => Promise.resolve(RESOLUTION) });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("checkpoint_missing");
  });

  it("kills the child and fails the run when the pause is rejected", async () => {
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 60_000 });
    let caught: unknown;
    try {
      await spawner(
        { executionId: "e-1", prompt: "p" },
        { pause: () => Promise.reject(new Error("run cancelled")) },
      );
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("pause_rejected");
    expect(children[0]?.killed).toBe(true);
  });

  it("keeps a RunFailedError's own failure identity when the pause seam rejects with one", async () => {
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 60_000 });
    let caught: unknown;
    try {
      await spawner(
        { executionId: "e-1", prompt: "p" },
        {
          pause: () =>
            Promise.reject(
              new RunFailedError("run retained more than it declared", {
                subtype: "retained_budget_exceeded",
                estimatedCostUsd: 0.07,
                sessionId: "s-pause",
              }),
            ),
        },
      );
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("retained_budget_exceeded");
    expect(caught.estimatedCostUsd).toBe(0.07);
    expect(children[0]?.killed).toBe(true);
  });

  it("fails the run when the child pauses but no pause handler is wired", async () => {
    const { runner, children } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    let caught: unknown;
    try {
      await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("pause_unsupported");
    expect(children[0]?.killed).toBe(true);
  });
});

// A zombie is not running, and with a test process as PID 1 nothing reaps the grandchild — so
// `kill(pid, 0)` alone would read a dead process as alive.
function isProcessAlive(pid: number): boolean {
  try {
    const stat = readFileSync(`/proc/${pid}/stat`, "utf8");
    return stat.slice(stat.lastIndexOf(") ") + 2).split(" ")[0] !== "Z";
  } catch {
    return false;
  }
}

function asPoolUid(argv: string[]): string[] {
  return ["setpriv", ...privilegeDropArgs(UID_POOL_BASE), "--", ...argv];
}

function childDyingWith(exit: Partial<ChildExit>): { runner: ChildRun; children: FakeChild[] } {
  return makeFakeChildRun((child) => {
    queueMicrotask(() => child.exit(exit));
  });
}

describe("task-cap attribution in the death message", () => {
  it("names the task cap when a child dies silently on an abort signal", async () => {
    const { runner } = childDyingWith({ code: null, signal: "SIGABRT", stderr: "" });
    await expect(spawnerWith(runner)({ executionId: "e-1", prompt: "p" })).rejects.toThrow(
      new RegExp(`RUN_CHILD_TASK_LIMIT=${RUN_CHILD_TASK_LIMIT}`),
    );
  });

  it("stays quiet when the child said why it died", async () => {
    const { runner } = childDyingWith({ code: null, signal: "SIGABRT", stderr: "a real runtime error" });
    const failure = await spawnerWith(runner)({ executionId: "e-1", prompt: "p" }).catch(
      (error: unknown) => error,
    );
    expect(String(failure)).toContain("a real runtime error");
    expect(String(failure)).not.toContain("RUN_CHILD_TASK_LIMIT");
  });

  it("stays quiet for an ordinary non-zero exit, which is not the cap's shape", async () => {
    const { runner } = childDyingWith({ code: 1, signal: null, stderr: "" });
    const failure = await spawnerWith(runner)({ executionId: "e-1", prompt: "p" }).catch(
      (error: unknown) => error,
    );
    expect(String(failure)).not.toContain("RUN_CHILD_TASK_LIMIT");
  });
});

describe("createMissedCloseBelt", () => {
  const tick = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

  it("settles once after the grace when a death fact arrives and no close follows", async () => {
    let settled = 0;
    const belt = createMissedCloseBelt({ graceMs: 10, settle: () => (settled += 1) });
    belt.arm();
    expect(settled).toBe(0);
    await tick(30);
    expect(settled).toBe(1);
  });

  it("keeps the first death fact's clock: re-arming does not push the grace out", async () => {
    let settled = 0;
    const belt = createMissedCloseBelt({ graceMs: 20, settle: () => (settled += 1) });
    belt.arm();
    await tick(12);
    belt.arm();
    await tick(15);
    expect(settled).toBe(1);
  });

  it("never settles when close arrives inside the grace", async () => {
    let settled = 0;
    const belt = createMissedCloseBelt({ graceMs: 10, settle: () => (settled += 1) });
    belt.arm();
    belt.cancel();
    await tick(30);
    expect(settled).toBe(0);
  });

  it("the forced settle carries the death facts the arming exit event delivered", async () => {
    let settledWith: { code: number | null; signal: string | null } | undefined;
    const belt = createMissedCloseBelt({ graceMs: 10, settle: (facts) => (settledWith = facts) });
    belt.arm({ code: 0, signal: null });
    await tick(30);
    expect(settledWith).toEqual({ code: 0, signal: null });
  });

  it("a fact-less re-arm from the death poll does not erase facts the exit event delivered", async () => {
    let settledWith: { code: number | null; signal: string | null } | undefined;
    const belt = createMissedCloseBelt({ graceMs: 10, settle: (facts) => (settledWith = facts) });
    belt.arm({ code: 1, signal: null });
    belt.arm();
    await tick(30);
    expect(settledWith).toEqual({ code: 1, signal: null });
  });

  it("a late exit event upgrades the facts of a poll-armed belt without moving its clock", async () => {
    let settledWith: { code: number | null; signal: string | null } | undefined;
    const belt = createMissedCloseBelt({ graceMs: 20, settle: (facts) => (settledWith = facts) });
    belt.arm();
    await tick(12);
    belt.arm({ code: 0, signal: null });
    await tick(15);
    expect(settledWith).toEqual({ code: 0, signal: null });
  });

  it("an all-null facts arm does not erase real facts an earlier arm delivered", async () => {
    let settledWith: { code: number | null; signal: string | null } | undefined;
    const belt = createMissedCloseBelt({ graceMs: 10, settle: (facts) => (settledWith = facts) });
    belt.arm({ code: 0, signal: null });
    belt.arm({ code: null, signal: null });
    await tick(30);
    expect(settledWith).toEqual({ code: 0, signal: null });
  });

  it("keeps its own copy of the facts, immune to caller mutation after the arm", async () => {
    let settledWith: { code: number | null; signal: string | null } | undefined;
    const belt = createMissedCloseBelt({ graceMs: 10, settle: (facts) => (settledWith = facts) });
    const facts = { code: 0, signal: null };
    belt.arm(facts);
    facts.code = 137;
    await tick(30);
    expect(settledWith).toEqual({ code: 0, signal: null });
  });

  it("settles with null facts when no death fact carried them", async () => {
    let settledWith: { code: number | null; signal: string | null } | undefined;
    const belt = createMissedCloseBelt({ graceMs: 10, settle: (facts) => (settledWith = facts) });
    belt.arm();
    await tick(30);
    expect(settledWith).toEqual({ code: null, signal: null });
  });
});

describe("spawnChildRun", () => {
  it("reports the kill signal when the run is aborted", async () => {
    const abortController = new AbortController();
    const handle = spawnChildRun({
      uid: UID_POOL_BASE,
      argv: asPoolUid(["sleep", "30"]),
      env: {},
      signal: abortController.signal,
      onFrame: () => {},
    });
    abortController.abort();
    const exit = await handle.exited;
    expect(exit.signal).toBe("SIGKILL");
  });

  // A belt settle (Bun dropping the child's close event, seen under accumulated test-runner
  // state) annotates the stderr tail and can race the stream's end, so a stream-content
  // assertion is only meaningful on a run the belt did not settle. Same shape as
  // retryOnUserNamespaceFlake: retry the environmental flake, assert strictly on a clean run.
  const exitedWithoutBeltSettle = async (script: string): Promise<ChildExit> => {
    const spawnOnce = (): Promise<ChildExit> =>
      spawnChildRun({
        uid: UID_POOL_BASE,
        argv: asPoolUid(["/usr/local/bin/bun", "-e", script]),
        env: {},
        signal: new AbortController().signal,
        onFrame: () => {},
      }).exited;
    let exit = await spawnOnce();
    // Two retries: three belt settles in a row is no longer the known flake, so the last
    // exit goes to the assertions and the failure shows what the belt delivered.
    for (let attempt = 0; attempt < 2 && exit.stderr.includes(MISSED_CLOSE_STDERR_NOTICE); attempt++) {
      exit = await spawnOnce();
    }
    return exit;
  };

  it("keeps only a bounded tail of child stderr, so a crash-looping child cannot grow supervisor heap", async () => {
    const exit = await exitedWithoutBeltSettle("process.stderr.write('s'.repeat(200000) + 'THE-END');");
    expect(Buffer.byteLength(exit.stderr, "utf8")).toBeLessThanOrEqual(MAX_CHILD_STDERR_TAIL_BYTES);
    expect(exit.stderr.endsWith("THE-END")).toBe(true);
  });

  it("bounds that tail in bytes, not code units, so multi-byte output cannot exceed the cap", async () => {
    // 'é' is one UTF-16 code unit but two UTF-8 bytes, so a code-unit cap would retain twice the bytes.
    const exit = await exitedWithoutBeltSettle("process.stderr.write('é'.repeat(200000) + 'THE-END');");
    expect(Buffer.byteLength(exit.stderr, "utf8")).toBeLessThanOrEqual(MAX_CHILD_STDERR_TAIL_BYTES);
    expect(exit.stderr.endsWith("THE-END")).toBe(true);
    expect(exit.stderr.startsWith("\uFFFD")).toBe(false);
    expect(exit.stderr).not.toContain("\uFFFD");
  });

  it("terminates politely first when killed", async () => {
    const handle = spawnChildRun({
      uid: UID_POOL_BASE,
      argv: asPoolUid(["sleep", "30"]),
      env: {},
      signal: new AbortController().signal,
      onFrame: () => {},
    });
    handle.kill();
    const exit = await handle.exited;
    expect(exit.signal).toBe("SIGTERM");
    expect(handle.isAlive()).toBe(false);
  });

  it("delivers stdout lines as frames and stdin lines to the child", async () => {
    const frames: string[] = [];
    const handle = spawnChildRun({
      uid: UID_POOL_BASE,
      argv: asPoolUid(["sh", "-c", 'read line; echo "echo:$line"']),
      env: {},
      signal: new AbortController().signal,
      onFrame: (line) => frames.push(line),
    });
    handle.send("hello");
    const exit = await handle.exited;
    expect(exit.code).toBe(0);
    expect(frames).toEqual(["echo:hello"]);
  });

  it("kills a child whose stdout line exceeds the frame cap instead of buffering it", async () => {
    const frames: string[] = [];
    const handle = spawnChildRun({
      uid: UID_POOL_BASE,
      argv: asPoolUid(["sh", "-c", "printf 'this line is far too long\\n'; sleep 2"]),
      env: {},
      signal: new AbortController().signal,
      onFrame: (line) => frames.push(line),
      maxFrameLength: 10,
    });
    const exit = await handle.exited;
    expect(exit.signal).toBe("SIGKILL");
    expect(exit.stderr).toContain("exceeds");
    expect(frames).toEqual([]);
  });

  it("kills the whole process tree, not just the direct child, when the run is aborted", async () => {
    const frames: string[] = [];
    const abortController = new AbortController();
    const handle = spawnChildRun({
      uid: UID_POOL_BASE,
      argv: asPoolUid(["sh", "-c", "sleep 60 <&- >&- 2>&- & echo $!; exec sleep 60"]),
      env: { PATH: "/usr/bin:/bin" },
      signal: abortController.signal,
      onFrame: (line) => frames.push(line),
    });
    while (frames.length === 0) {
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
    const grandchildPid = Number(frames[0]);
    expect(Number.isInteger(grandchildPid)).toBe(true);
    expect(isProcessAlive(grandchildPid)).toBe(true);
    abortController.abort();
    await handle.exited;
    for (let attempt = 0; attempt < 100 && isProcessAlive(grandchildPid); attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
    expect(isProcessAlive(grandchildPid)).toBe(false);
  });

  it("sends no signal once the child is torn down, so nothing is left in flight to the uid", async () => {
    let signallersSpawned = 0;
    const handle = spawnChildRun({
      uid: UID_POOL_BASE,
      argv: asPoolUid(["true"]),
      env: {},
      signal: new AbortController().signal,
      onFrame: () => {},
      signalOptions: {
        spawnProcess: (command, args) => {
          signallersSpawned += 1;
          return spawn(command, args, { stdio: ["ignore", "ignore", "pipe"] });
        },
      },
    });
    await handle.exited;
    handle.kill();
    expect(await handle.signalsSettled()).toBe(true);
    expect(signallersSpawned).toBe(0);
  });

  it("captures stderr for the exit report", async () => {
    const handle = spawnChildRun({
      uid: UID_POOL_BASE,
      argv: asPoolUid(["sh", "-c", "echo broken >&2; exit 3"]),
      env: {},
      signal: new AbortController().signal,
      onFrame: () => {},
    });
    const exit = await handle.exited;
    expect(exit.code).toBe(3);
    expect(exit.stderr).toContain("broken");
  });
});

describe("teardown under a real pool uid", () => {
  const TEARDOWN_ITERATIONS = 40;
  const SHARED_DIR_MODE = 0o777;
  const RESIDUE_FILES = 200;

  // A child that leaves residue as the pool uid, delivers its final frame and exits at once.
  // The supervisor often still sees it alive and asks it to terminate, and the child is gone
  // before that `pkill -u` lands: the window where the late signal used to kill the sweep.
  // The residue gives the sweep enough work to still be running when the signal arrives.
  function exitingChildScript(tmpRoot: string): string {
    const finalFrame = JSON.stringify({ kind: "final", response: { ok: true, result: RESULT } });
    const residue = join(tmpRoot, `claude-${String(UID_POOL_BASE)}`);
    return [
      `mkdir -p ${residue}`,
      `i=0; while [ $i -lt ${String(RESIDUE_FILES)} ]; do : > ${residue}/f$i; i=$((i+1)); done`,
      `printf '%s\\n' '${finalFrame}'`,
    ].join("; ");
  }

  it("returns the uid swept after every run, even when the child exits before its kill lands", async () => {
    const workspaceRoot = mkdtempSync(join(tmpdir(), "spawner-teardown-ws-"));
    const tmpRoot = mkdtempSync(join(tmpdir(), "spawner-teardown-tmp-"));
    chmodSync(workspaceRoot, SHARED_DIR_MODE);
    chmodSync(tmpRoot, SHARED_DIR_MODE);
    const pool = createUidPool({ baseUid: UID_POOL_BASE, size: 1 });
    const { events, logger } = captureLogger();
    const spawner = createRunSpawner({
      pool,
      childRun: (input) =>
        spawnChildRun({
          ...input,
          argv: asPoolUid(["sh", "-c", exitingChildScript(tmpRoot)]),
        }),
      workspaceRoot,
      tmpRoot,
      subprocessEnv: { PATH: "/usr/bin:/bin" },
      modelProvider: { provider: "bedrock", identity: { ambient: true } },
      buildIdentity: { version: "0.0.0-test", revision: "testrev" },
      pauseGraceMs: 60_000,
      documentStore: emptyDocumentStore(),
      claimRunSubtrees: () => {},
      logger,
    });
    const failures: string[] = [];
    for (let iteration = 0; iteration < TEARDOWN_ITERATIONS; iteration += 1) {
      try {
        await spawner({ executionId: `teardown-${String(iteration)}`, prompt: "p" });
      } catch (error) {
        failures.push(`run ${String(iteration)}: ${messageOf(error)}`);
      }
    }
    // No retries either: the retry is the backstop, and this proves the ordering alone holds.
    const sweepTrouble = events.filter(
      (entry) => entry.event === "uid_sweep_failed" || entry.event === "uid_sweep_retried",
    );
    expect(sweepTrouble.map((entry) => ({ event: entry.event, ...entry.fields }))).toEqual([]);
    expect(failures).toEqual([]);
    expect(pool.available()).toBe(1);
    expect(existsSync(join(tmpRoot, `claude-${String(UID_POOL_BASE)}`))).toBe(false);
  }, 60_000);
});

describe("signalRun", () => {
  // A signaller whose events never arrive: the shape Bun leaves when it drops both 'exit' and
  // 'close' for a process that is in fact gone (ADR 0121).
  function silentSignaller(): EventEmitter & { pid: number; stderr: undefined } {
    return Object.assign(new EventEmitter(), { pid: 2_147_483_000, stderr: undefined });
  }

  it("counts a signaller seen dead as settled even when none of its events arrived", async () => {
    const { logger, events } = captureLogger();
    const settled = await signalRun(UID_POOL_BASE, "TERM", logger, {
      spawnProcess: silentSignaller,
      processEnd: { isDead: () => true, pollMs: 1, graceMs: 10 },
    });
    expect(settled).toBe(true);
    expect(events.map((entry) => entry.event)).toEqual(["run_signal_end_unreported"]);
  });

  it("resolves false when the signaller neither ends nor reports within the timeout", async () => {
    const { logger, events } = captureLogger();
    const settled = await signalRun(UID_POOL_BASE, "TERM", logger, {
      spawnProcess: silentSignaller,
      processEnd: { isDead: () => false, pollMs: 1, timeoutMs: 20 },
    });
    expect(settled).toBe(false);
    expect(events.map((entry) => entry.event)).toEqual(["run_signal_unsettled"]);
    expect(events[0]?.fields).toMatchObject({ uid: UID_POOL_BASE, signal: "TERM" });
  });

  it("resolves, never rejects, when the signaller cannot be spawned at all", async () => {
    const { logger, events } = captureLogger();
    const settled = await signalRun(UID_POOL_BASE, "KILL", logger, {
      spawnProcess: () => {
        throw new Error("spawn EMFILE");
      },
    });
    expect(settled).toBe(true);
    expect(events.map((entry) => entry.event)).toEqual(["run_signal_failed"]);
  });

  it("resolves when a signaller with no stderr pipe emits an error", async () => {
    const { logger, events } = captureLogger();
    const killer = silentSignaller();
    const settling = signalRun(UID_POOL_BASE, "KILL", logger, { spawnProcess: () => killer });
    killer.emit("error", new Error("spawn EMFILE"));
    expect(await settling).toBe(true);
    expect(events.map((entry) => entry.event)).toEqual(["run_signal_failed"]);
  });

  it("resolves true for a real pkill that matched nothing", async () => {
    const { logger, events } = captureLogger();
    expect(await signalRun(UID_POOL_BASE, "TERM", logger)).toBe(true);
    expect(events).toEqual([]);
  });
});

describe("signalDidNotLand", () => {
  it("treats a signalled signaller as a failure, since it reports no exit code", () => {
    expect(signalDidNotLand(null)).toBe(true);
  });

  it("accepts pkill's no-match exit, which is the ordinary already-dead case", () => {
    expect(signalDidNotLand(1)).toBe(false);
    expect(signalDidNotLand(0)).toBe(false);
  });

  it("treats any other non-zero exit as a failure", () => {
    expect(signalDidNotLand(2)).toBe(true);
  });
});

describe("cost carried across the kill paths", () => {
  function costReportingChild(costs: number[]): ReturnType<typeof makeFakeChildRun> {
    return makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
      queueMicrotask(() => {
        for (const estimatedCostUsd of costs) {
          child.emit({ kind: "cost", estimatedCostUsd, usage: ZERO_USAGE });
        }
      });
    });
  }

  async function failureOf(promise: Promise<unknown>): Promise<RunFailedError> {
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    return caught;
  }

  it("reports deadline_exceeded with the last streamed cost when the supervisor kills the child", async () => {
    const { runner } = costReportingChild([0.5, 1.5]);
    const spawner = spawnerWith(runner, { deadlineMs: 20, childGraceMs: 0 });
    const failure = await failureOf(spawner({ executionId: "e-1", prompt: "p" }));
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.estimatedCostUsd).toBe(1.5);
  });

  it("reports aborted with the last streamed cost when the caller cancels the run", async () => {
    const { runner, children } = costReportingChild([0.7]);
    const controller = new AbortController();
    const spawner = spawnerWith(runner);
    const promise = spawner({ executionId: "e-1", prompt: "p" }, { signal: controller.signal });
    await new Promise<void>((resolve) => setTimeout(resolve, 10));
    expect(children[0]?.sent).toHaveLength(1);
    controller.abort();
    const failure = await failureOf(promise);
    expect(failure.subtype).toBe("aborted");
    expect(failure.estimatedCostUsd).toBe(0.7);
  });

  it("reports an unexpected child death that spent money as a non-retryable failure with that spend", async () => {
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 0.7, usage: ZERO_USAGE });
        child.exit({ code: 1, stderr: "segfault" });
      });
    });
    const failure = await failureOf(spawnerWith(runner)({ executionId: "e-1", prompt: "p" }));
    expect(failure.subtype).toBe("run_child_error");
    expect(failure.estimatedCostUsd).toBe(0.7);
  });

  it("keeps a spend-free unexpected child death as a plain retryable error", async () => {
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.exit({ code: 1, stderr: "child died" }));
    });
    let caught: unknown;
    try {
      await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(Error);
    expect(caught).not.toBeInstanceOf(RunFailedError);
  });

  it("keeps known spend when a child frame fails to parse, under a non-retryable subtype", async () => {
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 0.7, usage: ZERO_USAGE });
        child.emit({ kind: "paused" });
      });
    });
    const failure = await failureOf(spawnerWith(runner)({ executionId: "e-1", prompt: "p" }));
    expect(failure.subtype).toBe("run_child_error");
    expect(failure.estimatedCostUsd).toBe(0.7);
  });

  it("keeps a spend-free unparseable frame as a plain retryable error", async () => {
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.emit({ kind: "paused" }));
    });
    let caught: unknown;
    try {
      await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(Error);
    expect(caught).not.toBeInstanceOf(RunFailedError);
  });

  it("counts a paused frame's cost as last known when the kill lands after a warm resume", async () => {
    const { runner } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
      queueMicrotask(() => child.emit(pausedFrame()));
    });
    const spawner = spawnerWith(runner, { deadlineMs: 30, childGraceMs: 0, pauseGraceMs: 60_000 });
    const failure = await failureOf(
      spawner({ executionId: "e-1", prompt: "p" }, { pause: () => Promise.resolve(RESOLUTION) }),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.estimatedCostUsd).toBe(0.2);
  });

  it("lets the settled pause cost supersede a higher pre-settle estimate: last frame wins", async () => {
    const { runner } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 3, usage: ZERO_USAGE });
        child.emit(pausedFrame());
      });
    });
    const spawner = spawnerWith(runner, { deadlineMs: 30, childGraceMs: 0, pauseGraceMs: 60_000 });
    const failure = await failureOf(
      spawner({ executionId: "e-1", prompt: "p" }, { pause: () => Promise.resolve(RESOLUTION) }),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.estimatedCostUsd).toBe(0.2);
  });

  it("carries a cold-resumed child's cost frames onto a later kill", async () => {
    const store = createLocalCheckpointStore(newStoreRoot());
    const { runner, children } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
      queueMicrotask(() => {
        if (children.length === 1) {
          child.emit(pausedFrame());
        } else {
          child.emit({ kind: "cost", estimatedCostUsd: 0.9, usage: ZERO_USAGE });
        }
      });
    });
    const spawner = spawnerWith(runner, {
      deadlineMs: 40,
      childGraceMs: 0,
      pauseGraceMs: 0,
      checkpointStore: store,
    });
    const failure = await failureOf(
      spawner(
        { executionId: "e-1", prompt: "p" },
        {
          pause: async (request) => {
            await store.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
            return RESOLUTION;
          },
        },
      ),
    );
    expect(children).toHaveLength(2);
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.estimatedCostUsd).toBe(0.9);
  });
});

describe("token usage carried across the kill paths", () => {
  const USAGE = {
    inputTokens: 5_000,
    outputTokens: 800,
    cacheCreationInputTokens: 100,
    cacheReadInputTokens: 9_000,
  };

  it("carries the last streamed token usage onto the kill-path failure", async () => {
    const { runner } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
      queueMicrotask(() => {
        child.emit({
          kind: "cost",
          estimatedCostUsd: 0.5,
          usage: { inputTokens: 1, outputTokens: 1, cacheCreationInputTokens: 0, cacheReadInputTokens: 0 },
        });
        child.emit({ kind: "cost", estimatedCostUsd: 1.5, usage: USAGE });
      });
    });
    const spawner = spawnerWith(runner, { deadlineMs: 20, childGraceMs: 0 });
    let caught: unknown;
    try {
      await spawner({ executionId: "e-1", prompt: "p" });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("deadline_exceeded");
    expect(caught.usage).toEqual(USAGE);
  });
});

describe("terminal session bundle over the wire", () => {
  function terminalCheckpointFrame(): unknown {
    return {
      kind: "terminal_checkpoint",
      sessionId: "s-1",
      checkpoint: {
        sha256: CHECKPOINT.sha256,
        bytesBase64: Buffer.from(CHECKPOINT.bytes).toString("base64"),
      },
    };
  }

  function terminalThenFinalChild(): { runner: ChildRun; children: FakeChild[] } {
    return makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit(terminalCheckpointFrame());
        child.emit({ kind: "final", response: { ok: true, result: RESULT } });
        child.exit();
      });
    });
  }

  it("hands the frame's verified bundle to the seam before the run resolves", async () => {
    const { runner } = terminalThenFinalChild();
    const order: string[] = [];
    const seen: { sessionId: string; bytes: Uint8Array }[] = [];
    const result = await spawnerWith(runner)(
      { executionId: "e-1", prompt: "p" },
      {
        onTerminalCheckpoint: async (input) => {
          await new Promise((resolve) => setTimeout(resolve, 5));
          seen.push({ sessionId: input.sessionId, bytes: input.checkpoint.bytes });
          order.push("seam-settled");
        },
      },
    );
    order.push("run-resolved");
    expect(result).toEqual(RESULT);
    expect(order).toEqual(["seam-settled", "run-resolved"]);
    expect(seen).toHaveLength(1);
    expect(seen[0]?.sessionId).toBe("s-1");
    expect(Buffer.from(seen[0]?.bytes ?? new Uint8Array()).equals(Buffer.from(CHECKPOINT.bytes))).toBe(true);
  });

  it("logs a rejecting seam and still returns the run's result", async () => {
    const { events, logger } = captureLogger();
    const { runner } = terminalThenFinalChild();
    const result = await spawnerWith(runner, { logger })(
      { executionId: "e-1", prompt: "p" },
      {
        onTerminalCheckpoint: () => Promise.reject(new Error("store unavailable")),
      },
    );
    expect(result).toEqual(RESULT);
    const failure = events.find((entry) => entry.event === "terminal_checkpoint_save_failed");
    expect(failure?.level).toBe("error");
  });

  it("ignores the frame when no seam is wired", async () => {
    const { runner } = terminalThenFinalChild();
    expect(await spawnerWith(runner)({ executionId: "e-1", prompt: "p" })).toEqual(RESULT);
  });

  it("logs a corrupt bundle and never hands it to the seam", async () => {
    const { events, logger } = captureLogger();
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({
          kind: "terminal_checkpoint",
          sessionId: "s-1",
          checkpoint: {
            sha256: sha256Hex(new TextEncoder().encode("something else")),
            bytesBase64: Buffer.from(CHECKPOINT.bytes).toString("base64"),
          },
        });
        child.emit({ kind: "final", response: { ok: true, result: RESULT } });
        child.exit();
      });
    });
    const seamCalls: string[] = [];
    const result = await spawnerWith(runner, { logger })(
      { executionId: "e-1", prompt: "p" },
      {
        onTerminalCheckpoint: (input) => {
          seamCalls.push(input.sessionId);
          return Promise.resolve();
        },
      },
    );
    expect(result).toEqual(RESULT);
    expect(seamCalls).toEqual([]);
    expect(events.some((entry) => entry.event === "terminal_checkpoint_save_failed")).toBe(true);
  });
});

describe("run spawner continuation", () => {
  const OUT_BYTES = new TextEncoder().encode("print('check')");
  const DOC_BYTES = new TextEncoder().encode("stored document body");
  const BUNDLE = sealCheckpoint(new TextEncoder().encode("platform-held bundle"));

  function storeWithContinuationInputs(): DocumentStore {
    return createLocalDocumentStore(join(mkdtempSync(join(tmpdir(), "spawner-cont-")), "store"));
  }

  it("sends the first frame as a continuation resume: bundle, answer, re-carried metas, fresh prior spend", async () => {
    const documentStore = storeWithContinuationInputs();
    await documentStore.save("e-c1", [
      { name: "report.pdf", bytes: DOC_BYTES, sha256: sha256Hex(DOC_BYTES), area: "documents" },
      { name: "checks/model.py", bytes: OUT_BYTES, sha256: sha256Hex(OUT_BYTES), area: "recarried_out" },
    ]);
    const expectedCargo = cargoPrefixed([BUNDLE.bytes, DOC_BYTES, OUT_BYTES]);
    const { runner, children } = cargoConsumingSuccessChild();
    await spawnerWith(runner, { documentStore })(
      { executionId: "e-c1", prompt: "revise with these findings" },
      { continuation: continuationStartOf(BUNDLE) },
    );
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(first["kind"]).toBe("resume");
    expect(first["answer"]).toBe("revise with these findings");
    expect(first["priorCostUsd"]).toBe(0);
    expect(first["allowFurtherPause"]).toBe(true);
    expect(first["checkpoint"]).toEqual({
      sha256: BUNDLE.sha256,
      sizeBytes: BUNDLE.bytes.byteLength,
    });
    expect(first["continuation"]).toEqual({
      outFiles: [{ name: "checks/model.py", sha256: sha256Hex(OUT_BYTES), sizeBytes: OUT_BYTES.byteLength }],
    });
    expect(first["documents"]).toEqual([
      { name: "report.pdf", sha256: sha256Hex(DOC_BYTES), sizeBytes: DOC_BYTES.byteLength },
    ]);
    const firstChild = children[0];
    if (firstChild === undefined) {
      throw new Error("no child was spawned");
    }
    // The bundle leads the cargo channel, then the documents, then the re-carried bytes.
    expect((await cargoFlushedOf(firstChild)).equals(expectedCargo)).toBe(true);
  });

  it("keeps an ordinary start an ordinary start when no continuation is passed", async () => {
    const { runner, children } = oneShotSuccessChild();
    await spawnerWith(runner)({ executionId: "e-1", prompt: "p" });
    const first = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(first["kind"]).toBe("start");
    expect("continuation" in first).toBe(false);
  });

  it("empties the one-shot bundle owner when it sends the first frame", async () => {
    const { runner } = oneShotSuccessChild();
    const options: SpawnRunOptions = { continuation: continuationStartOf(BUNDLE) };
    await spawnerWith(runner)({ executionId: "e-c2", prompt: "revise" }, options);
    expect(() => options.continuation?.takeCheckpoint()).toThrow("continuation checkpoint already taken");
  });

  it("cold-resumes a paused continuation leg from its pause bundle, without the continuation field or re-carried cargo", async () => {
    const documentStore = storeWithContinuationInputs();
    await documentStore.save("e-1", [
      { name: "draft.md", bytes: OUT_BYTES, sha256: sha256Hex(OUT_BYTES), area: "recarried_out" },
    ]);
    const checkpointStore = createLocalCheckpointStore(newStoreRoot());
    const expectedResumeCargo = cargoPrefixed([CHECKPOINT.bytes]);
    const { runner, children } = makeFakeChildRun((child) => {
      if (children.length === 1) {
        queueMicrotask(() => child.emit(pausedFrame()));
        return;
      }
      // The cold-resume child must drain its cargo — the pause bundle now rides it —
      // before it exits, or the teardown aborts the stream mid-entry.
      finishOnceCargoDrained(child);
    });
    const spawner = spawnerWith(runner, { pauseGraceMs: 0, checkpointStore, documentStore });
    const result = await spawner(
      { executionId: "e-1", prompt: "revise with these findings" },
      {
        continuation: continuationStartOf(BUNDLE),
        pause: async (request) => {
          await checkpointStore.save({ executionId: "e-1", attempt: 1, segment: 0 }, request.checkpoint);
          return { answer: "carry on", allowFurtherPause: false };
        },
      },
    );
    expect(result).toEqual(RESULT);
    const firstFrame = JSON.parse(children[0]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(firstFrame["continuation"]).toEqual({
      outFiles: [{ name: "draft.md", sha256: sha256Hex(OUT_BYTES), sizeBytes: OUT_BYTES.byteLength }],
    });
    const resumeFrame = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeFrame["kind"]).toBe("resume");
    expect("continuation" in resumeFrame).toBe(false);
    expect(resumeFrame["answer"]).toBe("carry on");
    const resumeChild = children[1];
    if (resumeChild === undefined) {
      throw new Error("no cold-resume child was spawned");
    }
    expect((await cargoFlushedOf(resumeChild)).equals(expectedResumeCargo)).toBe(true);
  });
});

describe("persisted final response recovery", () => {
  function newWorkspaceRoot(): string {
    return mkdtempSync(join(tmpdir(), "spawner-final-"));
  }

  function persistUnder(workspaceRoot: string, response: WireResponse): void {
    persistFinalResponse({
      artifactsRoot: join(workspaceRoot, ARTIFACTS_DIR_NAME),
      executionId: "e-1",
      attempt: 1,
      response,
    });
  }

  async function failureOf(promise: Promise<unknown>): Promise<RunFailedError> {
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    return caught;
  }

  it("returns the persisted result when the child exits without ever sending a final frame", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 0.37, usage: ZERO_USAGE });
        persistUnder(workspaceRoot, { ok: true, result: RESULT });
        child.exit({ code: RESULT_UNDELIVERED_EXIT_CODE, stderr: "final_frame_write_failed" });
      });
    });
    const { events, logger } = captureLogger();
    const result = await spawnerWith(runner, { workspaceRoot, logger })({ executionId: "e-1", prompt: "p" });
    expect(result).toEqual(RESULT);
    const recovered = events.filter((event) => event.event === "final_response_recovered");
    expect(recovered).toHaveLength(1);
    expect(recovered[0]?.level).toBe("warn");
    expect(recovered[0]?.fields).toMatchObject({ executionId: "e-1", ok: true });
  });

  it("throws the persisted failure under its own subtype and spend, not as a child death", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 0.37, usage: ZERO_USAGE });
        persistUnder(workspaceRoot, {
          ok: false,
          error: {
            message: "agent run failed (errored_result): API Error: Server error mid-response",
            subtype: "errored_result",
            estimatedCostUsd: 0.44,
            sessionId: "s-2",
          },
        });
        child.exit({ code: 0 });
      });
    });
    const failure = await failureOf(
      spawnerWith(runner, { workspaceRoot })({ executionId: "e-1", prompt: "p" }),
    );
    expect(failure.subtype).toBe("errored_result");
    expect(failure.estimatedCostUsd).toBe(0.44);
    expect(failure.message).toContain("Server error mid-response");
  });

  it("names the missing result when a child exits 0 with neither a final frame nor a persisted response", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 0.37, usage: ZERO_USAGE });
        child.exit({ code: 0, stderr: "EPIPE: broken pipe, write" });
      });
    });
    const failure = await failureOf(
      spawnerWith(runner, { workspaceRoot })({ executionId: "e-1", prompt: "p" }),
    );
    expect(failure.subtype).toBe("run_child_error");
    expect(failure.estimatedCostUsd).toBe(0.37);
    expect(failure.message).toMatch(/exited with code 0 and delivered no result/);
    expect(failure.message).toMatch(/no final response was persisted/);
    expect(failure.message).toContain("EPIPE: broken pipe, write");
  });

  it("names an unreadable persisted response rather than treating it as absent", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 0.37, usage: ZERO_USAGE });
        persistUnder(workspaceRoot, { ok: true, result: RESULT });
        writeFileSync(
          finalResponsePathOf({
            artifactsRoot: join(workspaceRoot, ARTIFACTS_DIR_NAME),
            executionId: "e-1",
            attempt: 1,
          }),
          '{"ok":tr',
        );
        child.exit({ code: 0 });
      });
    });
    const failure = await failureOf(
      spawnerWith(runner, { workspaceRoot })({ executionId: "e-1", prompt: "p" }),
    );
    expect(failure.subtype).toBe("run_child_error");
    expect(failure.message).toMatch(/persisted final response could not be read/);
  });

  it("keeps a spend-free exit without a result as a plain retryable error", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { runner } = makeFakeChildRun((child) => {
      queueMicrotask(() => child.exit({ code: 0 }));
    });
    let caught: unknown;
    try {
      await spawnerWith(runner, { workspaceRoot })({ executionId: "e-1", prompt: "p" });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(Error);
    expect(caught).not.toBeInstanceOf(RunFailedError);
    expect(String(caught)).toMatch(/delivered no result/);
  });

  it("does not consult the persisted response when the caller cancelled the run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { runner, children } = makeFakeChildRun((child) => {
      child.input.signal.addEventListener(
        "abort",
        () => child.exit({ code: null, signal: "SIGKILL", stderr: "killed" }),
        { once: true },
      );
      queueMicrotask(() => {
        child.emit({ kind: "cost", estimatedCostUsd: 0.37, usage: ZERO_USAGE });
        persistUnder(workspaceRoot, { ok: true, result: RESULT });
      });
    });
    const controller = new AbortController();
    const promise = spawnerWith(runner, { workspaceRoot })(
      { executionId: "e-1", prompt: "p" },
      { signal: controller.signal },
    );
    await firstSpawnOf(children);
    await new Promise<void>((resolve) => setTimeout(resolve, 10));
    controller.abort();
    const failure = await failureOf(promise);
    expect(failure.subtype).toBe("aborted");
  });
});
