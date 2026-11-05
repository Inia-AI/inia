import { describe, expect, it } from "bun:test";
import { spawn } from "node:child_process";
import { EventEmitter } from "node:events";
import { awaitProcessEnd, isPidDeadOrZombie } from "./process-end.js";

const ABSENT_PID = 2_147_483_000;

function silentProcess(): EventEmitter & { pid: number } {
  return Object.assign(new EventEmitter(), { pid: ABSENT_PID });
}

describe("awaitProcessEnd", () => {
  it("settles on close with the real exit status", async () => {
    const child = spawn("sh", ["-c", "exit 3"]);
    expect(await awaitProcessEnd(child)).toEqual({ kind: "ended", code: 3, signal: null });
  });

  it("settles from the exit facts when close never follows", async () => {
    const child = silentProcess();
    const ending = awaitProcessEnd(child, { isDead: () => false, graceMs: 10 });
    child.emit("exit", null, "SIGTERM");
    expect(await ending).toEqual({ kind: "ended", code: null, signal: "SIGTERM" });
  });

  it("reports the end lost when the process is dead and no event ever arrives", async () => {
    const ending = awaitProcessEnd(silentProcess(), { isDead: () => true, pollMs: 1, graceMs: 10 });
    expect(await ending).toEqual({
      kind: "lost",
      reason: "died but neither its exit nor its close event arrived",
      dead: true,
    });
  });

  it("reports the end lost when a live process outlasts the timeout", async () => {
    const ending = awaitProcessEnd(silentProcess(), { isDead: () => false, pollMs: 1, timeoutMs: 20 });
    expect(await ending).toEqual({ kind: "lost", reason: "did not end within 20 ms", dead: false });
  });

  it("settles on a spawn error", async () => {
    const child = silentProcess();
    const ending = awaitProcessEnd(child, { isDead: () => false });
    child.emit("error", new Error("spawn EMFILE"));
    const end = await ending;
    expect(end.kind === "failed" ? end.error.message : end.kind).toBe("spawn EMFILE");
  });
});

describe("isPidDeadOrZombie", () => {
  it("treats an absent pid as dead", () => {
    expect(isPidDeadOrZombie(ABSENT_PID)).toBe(true);
  });

  it("treats this live process as alive", () => {
    expect(isPidDeadOrZombie(process.pid)).toBe(false);
  });
});
