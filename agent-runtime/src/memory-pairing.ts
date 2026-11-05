import { readFileSync } from "node:fs";
import { PRODUCTION_MEM_LIMIT_MIB, UID_POOL_SIZE } from "./config.js";
import { ConfigError } from "./config-error.js";

export const CGROUP_V2_MEMORY_MAX_PATH = "/sys/fs/cgroup/memory.max";
export const CGROUP_V1_MEMORY_LIMIT_PATH = "/sys/fs/cgroup/memory/memory.limit_in_bytes";
const CGROUP_UNBOUNDED_VALUE = "max";
const CGROUP_BYTE_COUNT_PATTERN = /^\d+$/;
const BYTES_PER_MIB = 1024 * 1024;
const UNPARSEABLE_PREVIEW_CHARS = 32;
const MEMORY_CHECK_OPT_OUT = "AGENT_RUNTIME_MEMORY_CHECK_DISABLED=true";

export type CgroupMemoryLimit =
  | { kind: "bounded"; mib: number }
  | { kind: "unbounded" }
  | { kind: "unreadable"; detail: string };

export type MemoryPairingDeps = { readTextFile: (path: string) => string };

const REAL_FILESYSTEM: MemoryPairingDeps = {
  readTextFile: (path) => readFileSync(path, "utf8"),
};

export function readCgroupMemoryLimit(deps: MemoryPairingDeps = REAL_FILESYSTEM): CgroupMemoryLimit {
  for (const path of [CGROUP_V2_MEMORY_MAX_PATH, CGROUP_V1_MEMORY_LIMIT_PATH]) {
    let raw: string;
    try {
      raw = deps.readTextFile(path);
    } catch {
      continue;
    }
    return parseCgroupMemoryLimit(raw, path);
  }
  return {
    kind: "unreadable",
    detail: `neither ${CGROUP_V2_MEMORY_MAX_PATH} nor ${CGROUP_V1_MEMORY_LIMIT_PATH} is readable`,
  };
}

function parseCgroupMemoryLimit(raw: string, path: string): CgroupMemoryLimit {
  const value = raw.trim();
  if (value === CGROUP_UNBOUNDED_VALUE) {
    return { kind: "unbounded" };
  }
  if (!CGROUP_BYTE_COUNT_PATTERN.test(value)) {
    return {
      kind: "unreadable",
      detail:
        `${path} holds neither a byte count nor "${CGROUP_UNBOUNDED_VALUE}": ` +
        JSON.stringify(value.slice(0, UNPARSEABLE_PREVIEW_CHARS)),
    };
  }
  return { kind: "bounded", mib: Math.floor(Number(value) / BYTES_PER_MIB) };
}

export function assertMemoryFeedsUidPool(
  limit: CgroupMemoryLimit,
  checkDisabledFlag: string | undefined,
): void {
  if (checkDisabledFlag === "true") {
    return;
  }
  if (limit.kind === "unreadable") {
    throw new ConfigError(
      `memory pairing is fail-closed: the container memory limit could not be read (${limit.detail}) — ` +
        `set ${MEMORY_CHECK_OPT_OUT} explicitly only outside a deployed environment`,
    );
  }
  if (limit.kind === "bounded" && limit.mib < PRODUCTION_MEM_LIMIT_MIB) {
    throw new ConfigError(
      `memory pairing is fail-closed: the container memory limit is ${limit.mib} MiB but ` +
        `UID_POOL_SIZE=${UID_POOL_SIZE} is derived from PRODUCTION_MEM_LIMIT_MIB=${PRODUCTION_MEM_LIMIT_MIB}, ` +
        "so the pool would advertise more children than the cgroup can feed — raise the deployment's " +
        `memory to match, or set ${MEMORY_CHECK_OPT_OUT} explicitly only outside a deployed environment`,
    );
  }
}
