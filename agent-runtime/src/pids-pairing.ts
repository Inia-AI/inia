import { readFileSync } from "node:fs";
import { REQUIRED_PIDS_LIMIT, RUN_CHILD_TASK_LIMIT, UID_POOL_SIZE } from "./config.js";
import { ConfigError } from "./config-error.js";

export const CGROUP_V2_PIDS_MAX_PATH = "/sys/fs/cgroup/pids.max";
export const CGROUP_V1_PIDS_MAX_PATH = "/sys/fs/cgroup/pids/pids.max";
const CGROUP_UNBOUNDED_VALUE = "max";
const CGROUP_TASK_COUNT_PATTERN = /^\d+$/;
const UNPARSEABLE_PREVIEW_CHARS = 32;

export type CgroupPidsLimit =
  | { kind: "bounded"; tasks: number }
  | { kind: "unbounded" }
  | { kind: "unreadable"; detail: string };

export type PidsPairingDeps = { readTextFile: (path: string) => string };

const REAL_FILESYSTEM: PidsPairingDeps = {
  readTextFile: (path) => readFileSync(path, "utf8"),
};

export function readCgroupPidsLimit(deps: PidsPairingDeps = REAL_FILESYSTEM): CgroupPidsLimit {
  for (const path of [CGROUP_V2_PIDS_MAX_PATH, CGROUP_V1_PIDS_MAX_PATH]) {
    let raw: string;
    try {
      raw = deps.readTextFile(path);
    } catch {
      continue;
    }
    return parseCgroupPidsLimit(raw, path);
  }
  return {
    kind: "unreadable",
    detail: `neither ${CGROUP_V2_PIDS_MAX_PATH} nor ${CGROUP_V1_PIDS_MAX_PATH} is readable`,
  };
}

function parseCgroupPidsLimit(raw: string, path: string): CgroupPidsLimit {
  const value = raw.trim();
  if (value === CGROUP_UNBOUNDED_VALUE) {
    return { kind: "unbounded" };
  }
  if (!CGROUP_TASK_COUNT_PATTERN.test(value)) {
    return {
      kind: "unreadable",
      detail:
        `${path} holds neither a task count nor "${CGROUP_UNBOUNDED_VALUE}": ` +
        JSON.stringify(value.slice(0, UNPARSEABLE_PREVIEW_CHARS)),
    };
  }
  return { kind: "bounded", tasks: Number(value) };
}

export function assertPidsFeedUidPool(limit: CgroupPidsLimit): void {
  if (limit.kind === "unreadable") {
    throw new ConfigError(
      `pid pairing is fail-closed: the container pid limit could not be read (${limit.detail})`,
    );
  }
  if (limit.kind === "bounded" && limit.tasks < REQUIRED_PIDS_LIMIT) {
    throw new ConfigError(
      `pid pairing is fail-closed: the container pid limit is ${limit.tasks} tasks but ` +
        `UID_POOL_SIZE=${UID_POOL_SIZE} runs at RUN_CHILD_TASK_LIMIT=${RUN_CHILD_TASK_LIMIT} tasks each ` +
        `need ${REQUIRED_PIDS_LIMIT} — below it, runs at their cap starve the supervisor's own kill ` +
        "and sweep spawns, which is the outcome the per-uid cap exists to prevent. Raise the " +
        "deployment's pids_limit to match",
    );
  }
}
