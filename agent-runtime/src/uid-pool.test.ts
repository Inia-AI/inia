import { describe, expect, it } from "bun:test";
import { RUN_CHILD_MEMORY_BUDGET_MIB, UID_POOL_SIZE } from "./config.js";
import { createUidPool, UidPoolExhaustedError } from "./uid-pool.js";

describe("pool size against the production memory budget", () => {
  it("derives exactly the advertised 32 slots from the deployed memory budget", () => {
    expect(UID_POOL_SIZE).toBe(32);
  });

  it("budgets each child above the top of the band the production OOM kills measured", () => {
    const observedChildFootprintTopMib = 250;
    expect(RUN_CHILD_MEMORY_BUDGET_MIB).toBeGreaterThanOrEqual(observedChildFootprintTopMib);
  });
});

describe("uid pool", () => {
  it("hands out distinct uids starting at the base", () => {
    const pool = createUidPool({ baseUid: 20001, size: 3 });
    expect([pool.acquire(), pool.acquire(), pool.acquire()]).toEqual([20001, 20002, 20003]);
  });

  it("throws once every uid is leased, rather than reusing a live one", () => {
    const pool = createUidPool({ baseUid: 20001, size: 2 });
    pool.acquire();
    pool.acquire();
    expect(() => pool.acquire()).toThrow(UidPoolExhaustedError);
  });

  it("reuses a uid after it is released", () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const first = pool.acquire();
    pool.release(first);
    expect(pool.acquire()).toBe(first);
  });

  it("reports how many uids remain", () => {
    const pool = createUidPool({ baseUid: 20001, size: 2 });
    expect(pool.available()).toBe(2);
    const uid = pool.acquire();
    expect(pool.available()).toBe(1);
    pool.release(uid);
    expect(pool.available()).toBe(2);
  });

  it("rejects releasing a uid that was never leased, which would corrupt the pool", () => {
    const pool = createUidPool({ baseUid: 20001, size: 2 });
    expect(() => pool.release(20001)).toThrow();
    expect(() => pool.release(999)).toThrow();
  });

  it("rejects a pool that would include root", () => {
    expect(() => createUidPool({ baseUid: 0, size: 1 })).toThrow();
    expect(() => createUidPool({ baseUid: 20001, size: 0 })).toThrow();
  });
});

describe("retained uids", () => {
  it("keeps a uid retained dirty out of circulation and names it", () => {
    const pool = createUidPool({ baseUid: 20001, size: 2 });
    const uid = pool.acquire();
    pool.retain(uid);
    expect(pool.retained()).toEqual([uid]);
    expect(pool.available()).toBe(1);
    expect(pool.acquire()).toBe(20002);
    expect(() => pool.acquire()).toThrow(UidPoolExhaustedError);
  });

  it("rejects retaining or releasing a uid that is not leased, keeping the original message", () => {
    const pool = createUidPool({ baseUid: 20001, size: 2 });
    expect(() => pool.retain(20001)).toThrow("uid 20001 was not leased from this pool");
    const uid = pool.acquire();
    pool.retain(uid);
    expect(() => pool.retain(uid)).toThrow();
    expect(() => pool.release(uid)).toThrow("uid 20001 was not leased from this pool");
  });

  it("returns a retained uid to the pool when its reclaim work reports it clean", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const uid = pool.acquire();
    pool.retain(uid);
    expect(await pool.reclaim(uid, () => Promise.resolve(false))).toBe(false);
    expect(pool.retained()).toEqual([uid]);
    expect(await pool.reclaim(uid, () => Promise.resolve(true))).toBe(true);
    expect(pool.retained()).toEqual([]);
    expect(pool.acquire()).toBe(uid);
  });

  it("keeps the uid retained when the reclaim work throws", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const uid = pool.acquire();
    pool.retain(uid);
    await expect(pool.reclaim(uid, () => Promise.reject(new Error("sweep threw")))).rejects.toThrow(
      "sweep threw",
    );
    expect(pool.retained()).toEqual([uid]);
  });

  it("reclaims only a retained uid, and only once at a time", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 2 });
    expect(await pool.reclaim(20001, () => Promise.resolve(true))).toBe(false);
    const uid = pool.acquire();
    pool.retain(uid);
    const sweep = Promise.withResolvers<boolean>();
    const first = pool.reclaim(uid, () => sweep.promise);
    expect(await pool.reclaim(uid, () => Promise.resolve(true))).toBe(false);
    sweep.resolve(true);
    expect(await first).toBe(true);
  });
});

describe("holding a uid for maintenance", () => {
  it("takes a free uid out of circulation for the work, then returns it", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 2 });
    const work = Promise.withResolvers<void>();
    const held = pool.holdWhenFree(20001, () => work.promise);
    expect(pool.available()).toBe(1);
    expect(pool.acquire()).toBe(20002);
    expect(() => pool.acquire()).toThrow(UidPoolExhaustedError);
    work.resolve();
    await held;
    expect(pool.acquire()).toBe(20001);
  });

  it("waits while the uid is leased and runs before any run can lease it again", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const uid = pool.acquire();
    const order: string[] = [];
    const work = Promise.withResolvers<void>();
    const held = pool.holdWhenFree(uid, async () => {
      order.push("hold");
      await work.promise;
    });
    await Promise.resolve();
    expect(order).toEqual([]);
    pool.release(uid);
    expect(() => pool.acquire()).toThrow(UidPoolExhaustedError);
    await Promise.resolve();
    expect(order).toEqual(["hold"]);
    work.resolve();
    await held;
    expect(pool.acquire()).toBe(uid);
  });

  it("never runs on a retained uid: it waits until a reclaim returns the uid clean", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const uid = pool.acquire();
    const order: string[] = [];
    const held = pool.holdWhenFree(uid, () => {
      order.push("hold");
      return Promise.resolve();
    });
    pool.retain(uid);
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(order).toEqual([]);
    expect(await pool.reclaim(uid, () => Promise.resolve(true))).toBe(true);
    await held;
    expect(order).toEqual(["hold"]);
    expect(pool.available()).toBe(1);
  });

  it("runs holds on one uid one at a time, in arrival order", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    const order: string[] = [];
    const first = Promise.withResolvers<void>();
    const firstHold = pool.holdWhenFree(20001, async () => {
      order.push("first started");
      await first.promise;
      order.push("first ended");
    });
    const secondHold = pool.holdWhenFree(20001, () => {
      order.push("second started");
      return Promise.resolve();
    });
    await Promise.resolve();
    expect(order).toEqual(["first started"]);
    first.resolve();
    await Promise.all([firstHold, secondHold]);
    expect(order).toEqual(["first started", "first ended", "second started"]);
  });

  it("returns the uid even when the work fails", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    await expect(pool.holdWhenFree(20001, () => Promise.reject(new Error("rm failed")))).rejects.toThrow(
      "rm failed",
    );
    expect(pool.available()).toBe(1);
  });

  it("refuses a uid outside the pool", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    expect(pool.owns(20001)).toBe(true);
    expect(pool.owns(20002)).toBe(false);
    await expect(pool.holdWhenFree(20002, () => Promise.resolve())).rejects.toThrow();
    await expect(pool.holdWhenFree(1000, () => Promise.resolve())).rejects.toThrow();
  });
});

describe("freed-uid listener", () => {
  it("hears every uid that lands back in the free list, and none handed straight to a waiting hold", async () => {
    const pool = createUidPool({ baseUid: 20001, size: 1 });
    let freed = 0;
    pool.onFreed(() => {
      freed += 1;
    });
    const uid = pool.acquire();
    const held = pool.holdWhenFree(uid, () => Promise.resolve());
    pool.release(uid);
    expect(freed).toBe(0);
    await held;
    expect(freed).toBe(1);
    const again = pool.acquire();
    pool.retain(again);
    await pool.reclaim(again, () => Promise.resolve(true));
    expect(freed).toBe(2);
  });
});
