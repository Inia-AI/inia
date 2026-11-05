import { describe, expect, it } from "bun:test";
import { PRODUCTION_MEM_LIMIT_MIB } from "./config.js";
import { ConfigError } from "./config-error.js";
import {
  assertMemoryFeedsUidPool,
  CGROUP_V1_MEMORY_LIMIT_PATH,
  CGROUP_V2_MEMORY_MAX_PATH,
  type CgroupMemoryLimit,
  readCgroupMemoryLimit,
} from "./memory-pairing.js";

const BYTES_PER_MIB = 1024 * 1024;
const CGROUP_V1_NO_LIMIT_SENTINEL = "9223372036854771712";

function readerOf(files: Record<string, string>): { readTextFile: (path: string) => string } {
  return {
    readTextFile: (path: string) => {
      const content = files[path];
      if (content === undefined) {
        throw new Error(`ENOENT: no such file: ${path}`);
      }
      return content;
    },
  };
}

function boundedMib(limit: CgroupMemoryLimit): number {
  if (limit.kind !== "bounded") {
    throw new Error(`expected a bounded limit, got ${limit.kind}`);
  }
  return limit.mib;
}

describe("readCgroupMemoryLimit", () => {
  it("reads the cgroup v2 memory.max byte count", () => {
    const limit = readCgroupMemoryLimit(
      readerOf({ [CGROUP_V2_MEMORY_MAX_PATH]: `${6144 * BYTES_PER_MIB}\n` }),
    );
    expect(limit).toEqual({ kind: "bounded", mib: 6144 });
  });

  it("treats the literal 'max' as unbounded", () => {
    const limit = readCgroupMemoryLimit(readerOf({ [CGROUP_V2_MEMORY_MAX_PATH]: "max\n" }));
    expect(limit).toEqual({ kind: "unbounded" });
  });

  it("falls back to the cgroup v1 limit file when the v2 file is missing", () => {
    const limit = readCgroupMemoryLimit(
      readerOf({ [CGROUP_V1_MEMORY_LIMIT_PATH]: `${12288 * BYTES_PER_MIB}\n` }),
    );
    expect(limit).toEqual({ kind: "bounded", mib: 12288 });
  });

  it("reads the cgroup v1 no-limit sentinel as a bounded value large enough to pass", () => {
    const limit = readCgroupMemoryLimit(
      readerOf({ [CGROUP_V1_MEMORY_LIMIT_PATH]: `${CGROUP_V1_NO_LIMIT_SENTINEL}\n` }),
    );
    expect(boundedMib(limit)).toBeGreaterThan(PRODUCTION_MEM_LIMIT_MIB);
  });

  it("answers unreadable when neither cgroup file can be read", () => {
    const limit = readCgroupMemoryLimit(readerOf({}));
    expect(limit.kind).toBe("unreadable");
    if (limit.kind === "unreadable") {
      expect(limit.detail).toContain(CGROUP_V2_MEMORY_MAX_PATH);
      expect(limit.detail).toContain(CGROUP_V1_MEMORY_LIMIT_PATH);
    }
  });

  it("answers unreadable for a v2 file holding neither a byte count nor 'max', without masking it with v1", () => {
    const limit = readCgroupMemoryLimit(
      readerOf({
        [CGROUP_V2_MEMORY_MAX_PATH]: "banana\n",
        [CGROUP_V1_MEMORY_LIMIT_PATH]: `${12288 * BYTES_PER_MIB}\n`,
      }),
    );
    expect(limit.kind).toBe("unreadable");
    if (limit.kind === "unreadable") {
      expect(limit.detail).toContain(CGROUP_V2_MEMORY_MAX_PATH);
    }
  });

  it("reads the real container cgroup with its default dependencies", () => {
    expect(readCgroupMemoryLimit().kind).not.toBe("unreadable");
  });
});

describe("assertMemoryFeedsUidPool", () => {
  it("refuses a limit below the production pairing the pool was derived from", () => {
    const belowMib = PRODUCTION_MEM_LIMIT_MIB - 1;
    expect(() => assertMemoryFeedsUidPool({ kind: "bounded", mib: belowMib }, undefined)).toThrow(
      ConfigError,
    );
    expect(() => assertMemoryFeedsUidPool({ kind: "bounded", mib: belowMib }, undefined)).toThrow(
      new RegExp(`${belowMib}.*${PRODUCTION_MEM_LIMIT_MIB}|${PRODUCTION_MEM_LIMIT_MIB}.*${belowMib}`),
    );
  });

  it("names the opt-out in the refusal", () => {
    expect(() => assertMemoryFeedsUidPool({ kind: "bounded", mib: 6144 }, undefined)).toThrow(
      /AGENT_RUNTIME_MEMORY_CHECK_DISABLED/,
    );
  });

  it("accepts a limit at exactly the production pairing", () => {
    expect(() =>
      assertMemoryFeedsUidPool({ kind: "bounded", mib: PRODUCTION_MEM_LIMIT_MIB }, undefined),
    ).not.toThrow();
  });

  it("accepts a higher limit as headroom", () => {
    expect(() =>
      assertMemoryFeedsUidPool({ kind: "bounded", mib: PRODUCTION_MEM_LIMIT_MIB + 4096 }, undefined),
    ).not.toThrow();
  });

  it("accepts an unbounded cgroup", () => {
    expect(() => assertMemoryFeedsUidPool({ kind: "unbounded" }, undefined)).not.toThrow();
  });

  it("refuses an unreadable limit, fail-closed", () => {
    expect(() =>
      assertMemoryFeedsUidPool({ kind: "unreadable", detail: "no cgroup files" }, undefined),
    ).toThrow(ConfigError);
    expect(() =>
      assertMemoryFeedsUidPool({ kind: "unreadable", detail: "no cgroup files" }, undefined),
    ).toThrow(/no cgroup files/);
  });

  it("boots an undersized or unreadable limit only under the explicit opt-out", () => {
    expect(() => assertMemoryFeedsUidPool({ kind: "bounded", mib: 6144 }, "true")).not.toThrow();
    expect(() =>
      assertMemoryFeedsUidPool({ kind: "unreadable", detail: "no cgroup files" }, "true"),
    ).not.toThrow();
  });

  it("treats any opt-out value other than the literal 'true' as no opt-out", () => {
    for (const flag of ["TRUE", "1", "yes", ""]) {
      expect(() => assertMemoryFeedsUidPool({ kind: "bounded", mib: 6144 }, flag)).toThrow(ConfigError);
    }
  });
});
