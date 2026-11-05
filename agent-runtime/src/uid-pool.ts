export class UidPoolExhaustedError extends Error {}

export type UidPool = {
  acquire: () => number;
  release: (uid: number) => void;
  retain: (uid: number) => void;
  available: () => number;
  size: () => number;
  owns: (uid: number) => boolean;
  retained: () => readonly number[];
  holdWhenFree: <T>(uid: number, work: () => Promise<T>) => Promise<T>;
  reclaim: (uid: number, work: () => Promise<boolean>) => Promise<boolean>;
  onFreed: (listener: () => void) => void;
};

export function createUidPool(input: { baseUid: number; size: number }): UidPool {
  if (!Number.isInteger(input.baseUid) || input.baseUid < 1) {
    throw new Error(`uid pool base must be a positive integer, got ${input.baseUid}`);
  }
  if (!Number.isInteger(input.size) || input.size < 1) {
    throw new Error(`uid pool size must be at least 1, got ${input.size}`);
  }

  const free: number[] = [];
  for (let offset = 0; offset < input.size; offset += 1) {
    free.push(input.baseUid + offset);
  }
  const leased = new Set<number>();
  const retained = new Set<number>();
  const waiters = new Map<number, (() => void)[]>();
  const freedListeners: (() => void)[] = [];

  function owns(uid: number): boolean {
    return Number.isInteger(uid) && uid >= input.baseUid && uid < input.baseUid + input.size;
  }

  function endLease(uid: number): void {
    if (!leased.delete(uid)) {
      throw new Error(`uid ${uid} was not leased from this pool`);
    }
  }

  // A waiting hold takes the uid before it re-enters circulation, so work queued behind a lease
  // runs before the next run can lease the uid.
  function returnToFree(uid: number): void {
    const queue = waiters.get(uid);
    const next = queue?.shift();
    if (queue !== undefined && queue.length === 0) {
      waiters.delete(uid);
    }
    if (next !== undefined) {
      next();
      return;
    }
    free.push(uid);
    for (const listener of freedListeners) {
      listener();
    }
  }

  return {
    acquire(): number {
      const uid = free.shift();
      if (uid === undefined) {
        throw new UidPoolExhaustedError(
          `all ${input.size} run uids are in use; refuse the run rather than share one`,
        );
      }
      leased.add(uid);
      return uid;
    },
    release(uid: number): void {
      endLease(uid);
      returnToFree(uid);
    },
    retain(uid: number): void {
      endLease(uid);
      retained.add(uid);
    },
    available(): number {
      return free.length;
    },
    size(): number {
      return input.size;
    },
    owns,
    retained(): readonly number[] {
      return [...retained];
    },
    async holdWhenFree<T>(uid: number, work: () => Promise<T>): Promise<T> {
      if (!owns(uid)) {
        throw new Error(`uid ${uid} is outside this pool`);
      }
      const freeIndex = free.indexOf(uid);
      if (freeIndex === -1) {
        const woken = Promise.withResolvers<void>();
        const queue = waiters.get(uid) ?? [];
        queue.push(woken.resolve);
        waiters.set(uid, queue);
        await woken.promise;
      } else {
        free.splice(freeIndex, 1);
      }
      try {
        return await work();
      } finally {
        returnToFree(uid);
      }
    },
    async reclaim(uid: number, work: () => Promise<boolean>): Promise<boolean> {
      if (!retained.delete(uid)) {
        return false;
      }
      let clean = false;
      try {
        clean = await work();
        return clean;
      } finally {
        if (clean) {
          returnToFree(uid);
        } else {
          retained.add(uid);
        }
      }
    },
    onFreed(listener: () => void): void {
      freedListeners.push(listener);
    },
  };
}
