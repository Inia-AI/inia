import { lstat, rm } from "node:fs/promises";
import { hasErrorCode } from "./errors.js";
import {
  awaitProcessEnd,
  type ProcessEndOptions,
  type SpawnStderrPiped,
  spawnStderrPiped,
} from "./process-end.js";
import type { UidPool } from "./uid-pool.js";

export function privilegeDropArgs(uid: number): string[] {
  return [`--reuid=${uid}`, `--regid=${uid}`, "--clear-groups", "--inh-caps=-all", "--ambient-caps=-all"];
}

// Signalling another uid's pid wants CAP_KILL, which the supervisor lacks — pkill -u as that uid does not.
export function signalRunArgv(uid: number, signal: "KILL" | "TERM"): string[] {
  return ["setpriv", ...privilegeDropArgs(uid), "--", "pkill", `-${signal}`, "-u", String(uid)];
}

export class RunAsUidError extends Error {
  readonly code: number | null;
  readonly signal: string | null;
  readonly stderr: string;

  constructor(message: string, end: { code: number | null; signal: string | null; stderr: string }) {
    super(message);
    this.code = end.code;
    this.signal = end.signal;
    this.stderr = end.stderr;
  }
}

export type RunAsUidOptions = {
  spawnProcess?: SpawnStderrPiped;
  processEnd?: ProcessEndOptions;
};

export async function runAsUid(uid: number, argv: string[], options: RunAsUidOptions = {}): Promise<void> {
  const spawnProcess = options.spawnProcess ?? spawnStderrPiped;
  const child = spawnProcess("setpriv", [...privilegeDropArgs(uid), "--", ...argv]);
  const ending = awaitProcessEnd(child, options.processEnd);
  let stderr = "";
  child.stderr?.setEncoding("utf8");
  child.stderr?.on("data", (chunk: string) => {
    stderr += chunk;
  });
  const end = await ending;
  const label = `setpriv uid ${uid} ${argv[0]}`;
  if (end.kind === "failed") {
    throw end.error;
  }
  if (end.kind === "lost") {
    throw new RunAsUidError(`${label} ${end.reason}: ${stderr.trim()}`, {
      code: null,
      signal: null,
      stderr,
    });
  }
  if (end.code === 0) {
    return;
  }
  const outcome = end.signal === null ? `exited with ${String(end.code)}` : `was killed by ${end.signal}`;
  throw new RunAsUidError(`${label} ${outcome}: ${stderr.trim()}`, {
    code: end.code,
    signal: end.signal,
    stderr,
  });
}

export type RemoveTreeFn = (path: string) => Promise<void>;

async function ownerUidOfPath(path: string): Promise<number | undefined> {
  try {
    return (await lstat(path)).uid;
  } catch (error) {
    if (hasErrorCode(error, "ENOENT")) {
      return undefined;
    }
    throw error;
  }
}

// The removal holds the owner uid: run as a uid leased to a live run, it would meet that run's
// pkill and idle check, and a signal would cross from one run's uid into another's work.
export function createPoolUidTreeRemover(input: {
  pool: UidPool;
  ownerUidOf?: (path: string) => Promise<number | undefined>;
  runAsUidFn?: typeof runAsUid;
}): RemoveTreeFn {
  const ownerUidOf = input.ownerUidOf ?? ownerUidOfPath;
  const runAsUidFn = input.runAsUidFn ?? runAsUid;
  return async (path) => {
    const ownerUid = await ownerUidOf(path);
    if (ownerUid === undefined) {
      return;
    }
    const ownUid = process.getuid?.();
    if (ownUid === undefined || ownerUid === ownUid) {
      await rm(path, { recursive: true, force: true });
      return;
    }
    if (!input.pool.owns(ownerUid)) {
      throw new Error(`refusing to remove ${path}: owner uid ${ownerUid} is outside the run uid pool`);
    }
    await input.pool.holdWhenFree(ownerUid, async () => {
      const ownerNow = await ownerUidOf(path);
      if (ownerNow === undefined) {
        return;
      }
      if (ownerNow !== ownerUid) {
        throw new Error(`refusing to remove ${path}: its owner changed from uid ${ownerUid} to ${ownerNow}`);
      }
      await runAsUidFn(ownerUid, ["rm", "-rf", path]);
    });
  };
}
