import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { hasErrorCode } from "./errors.js";
import { runAsUid } from "./setpriv.js";

export { isPidDeadOrZombie } from "./process-end.js";

const PROC_ROOT = "/proc";
const ZOMBIE_STATE = "Z";
const SETTLE_INTERVAL_MS = 25;
const SETTLE_ATTEMPTS = 40;

export function livePidsForUid(uid: number, procRoot: string = PROC_ROOT): number[] {
  const pids: number[] = [];
  for (const entry of readdirSync(procRoot)) {
    const pid = Number(entry);
    if (!Number.isInteger(pid) || pid <= 0) {
      continue;
    }
    try {
      if (statSync(join(procRoot, entry)).uid !== uid) {
        continue;
      }
      const stat = readFileSync(join(procRoot, entry, "stat"), "utf8");
      if (stat.slice(stat.lastIndexOf(") ") + 2).split(" ")[0] !== ZOMBIE_STATE) {
        pids.push(pid);
      }
    } catch (error) {
      // The process exited between the listing and the read: gone is idle.
      if (!hasErrorCode(error, "ENOENT", "ESRCH")) {
        throw error;
      }
    }
  }
  return pids;
}

// The settle budget is how long stragglers get to die after the KILL; a caller about to put
// new work on the uid can afford a longer one than teardown.
export type UidIdleWait = (uid: number, options?: { settleBudgetMs?: number }) => Promise<boolean>;

export function createUidIdleWait(input: {
  livePids?: (uid: number) => number[];
  runAsUidFn?: typeof runAsUid;
  ownUid?: () => number | undefined;
  settleIntervalMs?: number;
  settleAttempts?: number;
}): UidIdleWait {
  const livePids = input.livePids ?? ((uid: number) => livePidsForUid(uid));
  const runAsUidFn = input.runAsUidFn ?? runAsUid;
  const ownUid = input.ownUid ?? (() => process.getuid?.());
  const settleIntervalMs = input.settleIntervalMs ?? SETTLE_INTERVAL_MS;
  const settleAttempts = input.settleAttempts ?? SETTLE_ATTEMPTS;
  return async (uid, options) => {
    // Mistaking the supervisor's own uid for a pool uid would have this sweep kill the process it protects.
    if (uid === ownUid()) {
      return true;
    }
    const attempts =
      options?.settleBudgetMs === undefined
        ? settleAttempts
        : Math.max(1, Math.ceil(options.settleBudgetMs / settleIntervalMs));
    for (let attempt = 0; attempt < attempts; attempt += 1) {
      const pids = livePids(uid);
      if (pids.length === 0) {
        return true;
      }
      if (attempt === 0) {
        await runAsUidFn(uid, ["kill", "-KILL", ...pids.map(String)]).catch(() => {
          // A pid exiting before the kill makes it exit non-zero; the loop decides idleness, not this call.
        });
      }
      await new Promise((resolve) => setTimeout(resolve, settleIntervalMs));
    }
    return livePids(uid).length === 0;
  };
}

export const waitForUidIdle: UidIdleWait = createUidIdleWait({});
