import { describe, expect, it } from "bun:test";
import type { Logger } from "./logger.js";
import type { UidSweepOutcome } from "./run-spawner.js";
import { createUidPool } from "./uid-pool.js";
import { createUidReclaimer } from "./uid-reclaimer.js";

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

function poolWithRetained(size: number, retainedCount: number): ReturnType<typeof createUidPool> {
  const pool = createUidPool({ baseUid: 20001, size });
  const leased = Array.from({ length: retainedCount }, () => pool.acquire());
  for (const uid of leased) {
    pool.retain(uid);
  }
  return pool;
}

describe("uid reclaimer", () => {
  it("returns a retained uid to the pool once it is idle and swept", async () => {
    const pool = poolWithRetained(2, 1);
    const entries: LoggedEvent[] = [];
    let freed = 0;
    pool.onFreed(() => {
      freed += 1;
    });
    const reclaimer = createUidReclaimer({
      pool,
      sweepWhenIdle: () => Promise.resolve({ idle: true, swept: true }),
      log: capturingLogger(entries),
    });
    await reclaimer.reclaimNow();
    expect(pool.retained()).toEqual([]);
    expect(pool.available()).toBe(2);
    expect(freed).toBe(1);
    expect(entries.map((entry) => entry.event)).toEqual(["uid_reclaimed"]);
  });

  it("keeps a uid that will not go idle retained, counting the attempts", async () => {
    const pool = poolWithRetained(1, 1);
    const entries: LoggedEvent[] = [];
    const reclaimer = createUidReclaimer({
      pool,
      sweepWhenIdle: () => Promise.resolve({ idle: false, swept: false }),
      log: capturingLogger(entries),
    });
    await reclaimer.reclaimNow();
    await reclaimer.reclaimNow();
    expect(pool.retained()).toEqual([20001]);
    expect(pool.available()).toBe(0);
    expect(entries).toEqual([
      { event: "uid_reclaim_failed", fields: { uid: 20001, idle: false, attempts: 1 } },
      { event: "uid_reclaim_failed", fields: { uid: 20001, idle: false, attempts: 2 } },
    ]);
  });

  it("reclaims a uid on a later pass, reporting how many attempts it took", async () => {
    const pool = poolWithRetained(1, 1);
    const entries: LoggedEvent[] = [];
    const outcomes: UidSweepOutcome[] = [
      { idle: true, swept: false },
      { idle: true, swept: true },
    ];
    const reclaimer = createUidReclaimer({
      pool,
      sweepWhenIdle: () => Promise.resolve(outcomes.shift() ?? { idle: false, swept: false }),
      log: capturingLogger(entries),
    });
    await reclaimer.reclaimNow();
    await reclaimer.reclaimNow();
    expect(pool.available()).toBe(1);
    expect(entries.at(-1)).toEqual({ event: "uid_reclaimed", fields: { uid: 20001, attempts: 2 } });
  });

  it("lets a purge waiting on the retained uid run only after the reclaim returned it clean", async () => {
    const pool = poolWithRetained(1, 1);
    const order: string[] = [];
    const purge = pool.holdWhenFree(20001, () => {
      order.push("purge");
      return Promise.resolve();
    });
    const reclaimer = createUidReclaimer({
      pool,
      sweepWhenIdle: () => {
        order.push("sweep");
        return Promise.resolve({ idle: true, swept: true });
      },
      log: capturingLogger([]),
    });
    await reclaimer.reclaimNow();
    await purge;
    expect(order).toEqual(["sweep", "purge"]);
    expect(pool.available()).toBe(1);
  });

  it("runs one pass at a time when a tick arrives while a pass is still sweeping", async () => {
    const pool = poolWithRetained(1, 1);
    let sweeps = 0;
    const sweep = Promise.withResolvers<void>();
    const reclaimer = createUidReclaimer({
      pool,
      sweepWhenIdle: async () => {
        sweeps += 1;
        await sweep.promise;
        return { idle: true, swept: true };
      },
      log: capturingLogger([]),
    });
    const first = reclaimer.reclaimNow();
    const second = reclaimer.reclaimNow();
    await new Promise((resolve) => setTimeout(resolve, 10));
    sweep.resolve();
    await Promise.all([first, second]);
    expect(sweeps).toBe(1);
  });
});
