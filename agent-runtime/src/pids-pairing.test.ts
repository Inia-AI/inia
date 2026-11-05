import { describe, expect, it } from "bun:test";
import {
  REQUIRED_PIDS_LIMIT,
  RUN_CHILD_TASK_LIMIT,
  SUPERVISOR_TASK_RESERVE,
  UID_POOL_SIZE,
} from "./config.js";
import {
  assertPidsFeedUidPool,
  CGROUP_V1_PIDS_MAX_PATH,
  CGROUP_V2_PIDS_MAX_PATH,
  readCgroupPidsLimit,
} from "./pids-pairing.js";

describe("the pid budget against the pool the runtime advertises", () => {
  it("covers every pool uid at its task cap plus the supervisor's reserve", () => {
    expect(REQUIRED_PIDS_LIMIT).toBe(4352);
    expect(REQUIRED_PIDS_LIMIT).toBe(SUPERVISOR_TASK_RESERVE + UID_POOL_SIZE * RUN_CHILD_TASK_LIMIT);
  });
});

describe("readCgroupPidsLimit", () => {
  it("reads the cgroup v2 limit as a task count", () => {
    const limit = readCgroupPidsLimit({
      readTextFile: (path) => {
        if (path === CGROUP_V2_PIDS_MAX_PATH) {
          return "4352\n";
        }
        throw new Error("absent");
      },
    });
    expect(limit).toEqual({ kind: "bounded", tasks: 4352 });
  });

  it("falls back to the cgroup v1 path", () => {
    const limit = readCgroupPidsLimit({
      readTextFile: (path) => {
        if (path === CGROUP_V1_PIDS_MAX_PATH) {
          return "9000";
        }
        throw new Error("absent");
      },
    });
    expect(limit).toEqual({ kind: "bounded", tasks: 9000 });
  });

  it("treats the literal max as unbounded", () => {
    expect(readCgroupPidsLimit({ readTextFile: () => "max\n" })).toEqual({ kind: "unbounded" });
  });

  it("reports an unparseable limit rather than guessing a number", () => {
    const limit = readCgroupPidsLimit({ readTextFile: () => "not-a-count" });
    expect(limit.kind).toBe("unreadable");
  });

  it("reports an entirely unreadable cgroup", () => {
    const limit = readCgroupPidsLimit({
      readTextFile: () => {
        throw new Error("absent");
      },
    });
    expect(limit.kind).toBe("unreadable");
  });
});

describe("assertPidsFeedUidPool", () => {
  it("boots when the pid limit covers the advertised concurrency", () => {
    expect(() => assertPidsFeedUidPool({ kind: "bounded", tasks: REQUIRED_PIDS_LIMIT })).not.toThrow();
  });

  it("boots when the container has no pid limit at all", () => {
    expect(() => assertPidsFeedUidPool({ kind: "unbounded" })).not.toThrow();
  });

  it("refuses to start when runs at their cap would starve the supervisor", () => {
    expect(() => assertPidsFeedUidPool({ kind: "bounded", tasks: REQUIRED_PIDS_LIMIT - 1 })).toThrow(
      /pid pairing is fail-closed/,
    );
  });

  it("names the deployed limit and the derivation's inputs so the fix is obvious", () => {
    expect(() => assertPidsFeedUidPool({ kind: "bounded", tasks: 512 })).toThrow(
      new RegExp(
        `512 tasks.*UID_POOL_SIZE=${UID_POOL_SIZE}.*RUN_CHILD_TASK_LIMIT=${RUN_CHILD_TASK_LIMIT}`,
        "s",
      ),
    );
  });

  it("is fail-closed on an unreadable limit, never assuming headroom", () => {
    expect(() => assertPidsFeedUidPool({ kind: "unreadable", detail: "no cgroup" })).toThrow(
      /pid pairing is fail-closed/,
    );
  });
});
