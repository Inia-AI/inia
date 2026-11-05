import { spawn } from "node:child_process";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { Readable } from "node:stream";
import { hasErrorCode } from "./errors.js";

const PROC_ROOT = "/proc";
const ZOMBIE_STATE = "Z";
const DEATH_POLL_MS = 250;

// 'close' follows 'exit' within milliseconds on a healthy process. Bun has been seen to drop
// both (ADR 0121), so a death the /proc poll sees, or an 'exit' with no 'close', settles the
// wait from what it has once this long has passed.
export const LOST_EVENT_GRACE_MS = 2_000;

export function isPidDeadOrZombie(pid: number, procRoot: string = PROC_ROOT): boolean {
  try {
    const stat = readFileSync(join(procRoot, String(pid), "stat"), "utf8");
    return stat.slice(stat.lastIndexOf(") ") + 2).split(" ")[0] === ZOMBIE_STATE;
  } catch (error) {
    if (hasErrorCode(error, "ENOENT", "ESRCH")) {
      return true;
    }
    throw error;
  }
}

export type WatchedProcess = {
  readonly pid?: number | undefined;
  on(event: string, listener: (...args: unknown[]) => void): unknown;
};

// Only stderr is piped: every caller reports it, and none reads stdout.
export type SpawnStderrPiped = (
  command: string,
  args: string[],
) => WatchedProcess & { readonly stderr?: Readable | null | undefined };

export const spawnStderrPiped: SpawnStderrPiped = (command, args) =>
  spawn(command, args, { stdio: ["ignore", "ignore", "pipe"] });

export type ProcessEnd =
  | { kind: "ended"; code: number | null; signal: string | null }
  | { kind: "failed"; error: Error }
  // `dead`: the /proc poll saw the process gone or a zombie, so only its events were lost.
  | { kind: "lost"; reason: string; dead: boolean };

export type ProcessEndOptions = {
  pollMs?: number;
  graceMs?: number;
  // Absent: a live process is waited on for as long as it lives.
  timeoutMs?: number;
  isDead?: (pid: number) => boolean;
};

function exitCodeOf(value: unknown): number | null {
  return typeof value === "number" ? value : null;
}

function signalOf(value: unknown): string | null {
  return typeof value === "string" ? value : null;
}

// Settles exactly once and never rejects: on 'close' (the full story, stdio drained), on
// 'error', on 'exit' or an observed death followed by a grace with no 'close', or on the
// timeout. Listeners attach before the caller touches the process's streams, so an 'error'
// emitted while it does is never unhandled.
export function awaitProcessEnd(child: WatchedProcess, options: ProcessEndOptions = {}): Promise<ProcessEnd> {
  const pollMs = options.pollMs ?? DEATH_POLL_MS;
  const graceMs = options.graceMs ?? LOST_EVENT_GRACE_MS;
  const isDead = options.isDead ?? ((pid: number) => isPidDeadOrZombie(pid));
  return new Promise((resolve) => {
    let settled = false;
    let exitFacts: { code: number | null; signal: string | null } | undefined;
    let graceTimer: ReturnType<typeof setTimeout> | undefined;
    const timeoutTimer =
      options.timeoutMs === undefined
        ? undefined
        : setTimeout(
            () =>
              settle({
                kind: "lost",
                reason: `did not end within ${String(options.timeoutMs)} ms`,
                dead: false,
              }),
            options.timeoutMs,
          );
    const poll = setInterval(() => {
      const pid = child.pid;
      if (pid === undefined) {
        return;
      }
      try {
        if (isDead(pid)) {
          armGrace();
        }
      } catch {
        clearInterval(poll);
      }
    }, pollMs);
    function settle(end: ProcessEnd): void {
      if (settled) {
        return;
      }
      settled = true;
      clearInterval(poll);
      clearTimeout(graceTimer);
      clearTimeout(timeoutTimer);
      resolve(end);
    }
    function armGrace(): void {
      if (graceTimer !== undefined) {
        return;
      }
      graceTimer = setTimeout(() => {
        settle(
          exitFacts === undefined
            ? { kind: "lost", reason: "died but neither its exit nor its close event arrived", dead: true }
            : { kind: "ended", ...exitFacts },
        );
      }, graceMs);
    }
    child.on("error", (error) => {
      settle({ kind: "failed", error: error instanceof Error ? error : new Error(String(error)) });
    });
    child.on("exit", (code, signal) => {
      exitFacts = { code: exitCodeOf(code), signal: signalOf(signal) };
      armGrace();
    });
    child.on("close", (code, signal) => {
      settle({ kind: "ended", code: exitCodeOf(code), signal: signalOf(signal) });
    });
  });
}
