import { toError } from "./errors.js";
import type { Logger } from "./logger.js";
import type { UidSweepOutcome } from "./run-spawner.js";
import type { UidPool } from "./uid-pool.js";

export const UID_RECLAIM_INTERVAL_MS = 5 * 60_000;

export type UidReclaimerDeps = {
  pool: UidPool;
  sweepWhenIdle: (uid: number) => Promise<UidSweepOutcome>;
  log: Logger;
};

export type UidReclaimer = {
  reclaimNow: () => Promise<void>;
  start: () => void;
};

export function createUidReclaimer(deps: UidReclaimerDeps): UidReclaimer {
  const attemptsByUid = new Map<number, number>();
  let pass: Promise<void> | undefined;

  async function reclaim(uid: number): Promise<void> {
    await deps.pool.reclaim(uid, async () => {
      const attempts = (attemptsByUid.get(uid) ?? 0) + 1;
      attemptsByUid.set(uid, attempts);
      const outcome = await deps.sweepWhenIdle(uid);
      if (!outcome.swept) {
        deps.log.warn("uid_reclaim_failed", { uid, idle: outcome.idle, attempts });
        return false;
      }
      attemptsByUid.delete(uid);
      deps.log.info("uid_reclaimed", { uid, attempts });
      return true;
    });
  }

  async function runPass(): Promise<void> {
    for (const uid of deps.pool.retained()) {
      try {
        await reclaim(uid);
      } catch (error) {
        deps.log.error("uid_reclaim_errored", { uid, error: toError(error) });
      }
    }
  }

  // One pass at a time: a pass can outlast the interval, and a second one would re-sweep a uid
  // the first has just returned to the pool.
  function reclaimNow(): Promise<void> {
    pass ??= runPass().finally(() => {
      pass = undefined;
    });
    return pass;
  }

  return {
    reclaimNow,
    start() {
      setInterval(() => {
        void reclaimNow();
      }, UID_RECLAIM_INTERVAL_MS).unref();
    },
  };
}
