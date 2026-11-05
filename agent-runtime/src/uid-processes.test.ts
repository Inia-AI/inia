import { afterAll, describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createUidIdleWait, livePidsForUid } from "./uid-processes.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "uid-processes-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

function fakeProc(entries: { pid: string; state: string; comm?: string }[]): string {
  const root = mkdtempSync(join(TEST_ROOT, "proc-"));
  for (const entry of entries) {
    const dir = join(root, entry.pid);
    mkdirSync(dir);
    const comm = entry.comm ?? "some (odd) name";
    writeFileSync(join(dir, "stat"), `${entry.pid} (${comm}) ${entry.state} 1 1 0 -1 4194304\n`);
  }
  mkdirSync(join(root, "not-a-pid"));
  return root;
}

describe("livePidsForUid", () => {
  it("lists the uid's running processes and ignores zombies and non-pid entries", () => {
    const ownUid = process.getuid?.();
    if (ownUid === undefined) {
      throw new Error("this suite requires a POSIX uid");
    }
    const root = fakeProc([
      { pid: "101", state: "S" },
      { pid: "102", state: "Z" },
      { pid: "103", state: "R" },
    ]);
    expect(livePidsForUid(ownUid, root).sort((a, b) => a - b)).toEqual([101, 103]);
  });

  it("reports nothing for a uid that owns no process", () => {
    const root = fakeProc([{ pid: "101", state: "S" }]);
    expect(livePidsForUid(999_999, root)).toEqual([]);
  });

  it("survives a comm field containing the delimiter it parses around", () => {
    const ownUid = process.getuid?.();
    if (ownUid === undefined) {
      throw new Error("this suite requires a POSIX uid");
    }
    const root = fakeProc([{ pid: "101", state: "Z", comm: "weird) Z (name" }]);
    expect(livePidsForUid(ownUid, root)).toEqual([]);
  });
});

describe("createUidIdleWait", () => {
  it("reports idle immediately when nothing runs as the uid", async () => {
    let kills = 0;
    const wait = createUidIdleWait({
      livePids: () => [],
      runAsUidFn: () => {
        kills += 1;
        return Promise.resolve();
      },
      ownUid: () => 1000,
    });
    expect(await wait(20001)).toBe(true);
    expect(kills).toBe(0);
  });

  it("kills stragglers as their own owner and reports idle once they are gone", async () => {
    let remaining = [4242];
    const killed: string[][] = [];
    const wait = createUidIdleWait({
      livePids: () => remaining,
      runAsUidFn: (_uid, argv) => {
        killed.push(argv);
        remaining = [];
        return Promise.resolve();
      },
      ownUid: () => 1000,
      settleIntervalMs: 1,
    });
    expect(await wait(20001)).toBe(true);
    expect(killed).toEqual([["kill", "-KILL", "4242"]]);
  });

  it("reports not-idle when a process refuses to die, so the uid is never re-leased", async () => {
    const wait = createUidIdleWait({
      livePids: () => [4242],
      runAsUidFn: () => Promise.resolve(),
      ownUid: () => 1000,
      settleIntervalMs: 1,
      settleAttempts: 3,
    });
    expect(await wait(20001)).toBe(false);
  });

  it("gives a slow straggler the caller's longer settle budget", async () => {
    let scans = 0;
    const slowToDie = (): number[] => {
      scans += 1;
      return scans <= 10 ? [4242] : [];
    };
    const options = {
      runAsUidFn: () => Promise.resolve(),
      ownUid: () => 1000,
      settleIntervalMs: 1,
      settleAttempts: 3,
    };
    expect(await createUidIdleWait({ ...options, livePids: slowToDie })(20001)).toBe(false);
    scans = 0;
    expect(await createUidIdleWait({ ...options, livePids: slowToDie })(20001, { settleBudgetMs: 50 })).toBe(
      true,
    );
  });

  it("never sweeps the supervisor's own uid — the pool cannot lease it", async () => {
    let kills = 0;
    const wait = createUidIdleWait({
      livePids: () => [4242],
      runAsUidFn: () => {
        kills += 1;
        return Promise.resolve();
      },
      ownUid: () => 1000,
      settleIntervalMs: 1,
    });
    expect(await wait(1000)).toBe(true);
    expect(kills).toBe(0);
  });
});
