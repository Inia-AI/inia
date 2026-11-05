import { describe, expect, it } from "bun:test";
import { existsSync, mkdtempSync, readdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  assertCliTmpDirExecutable,
  cliTmpDirOf,
  DEFAULT_TMP_DIR,
  type ExecCapability,
  probeExecCapability,
  TMP_EXEC_CHECK_OPT_OUT,
} from "./cli-tmp-exec.js";
import { ConfigError } from "./config-error.js";

const UNWRITABLE_DIR = "/proc";

function probeDepsAnswering(answer: { status: number | null; errorCode: string | undefined }) {
  return { runProbe: () => answer };
}

describe("cliTmpDirOf", () => {
  it("falls back to /tmp when the child env names no tmp dir, which is the deployed shape", () => {
    expect(cliTmpDirOf({})).toBe(DEFAULT_TMP_DIR);
  });

  it("honours TMPDIR first, then TMP, then TEMP, as Bun's os.tmpdir() does", () => {
    expect(cliTmpDirOf({ TMPDIR: "/a", TMP: "/b", TEMP: "/c" })).toBe("/a");
    expect(cliTmpDirOf({ TMP: "/b", TEMP: "/c" })).toBe("/b");
    expect(cliTmpDirOf({ TEMP: "/c" })).toBe("/c");
  });

  it("ignores an empty value", () => {
    expect(cliTmpDirOf({ TMPDIR: "" })).toBe(DEFAULT_TMP_DIR);
  });
});

describe("probeExecCapability", () => {
  it("finds this container's tmp dir executable and leaves no probe residue behind", () => {
    const dir = mkdtempSync(join(tmpdir(), "cli-tmp-exec-test-"));
    try {
      expect(probeExecCapability(dir)).toEqual({ kind: "executable" });
      expect(readdirSync(dir)).toEqual([]);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it("reads an EACCES from executing the staged probe as a noexec mount", () => {
    const dir = mkdtempSync(join(tmpdir(), "cli-tmp-exec-test-"));
    try {
      const capability = probeExecCapability(dir, probeDepsAnswering({ status: null, errorCode: "EACCES" }));
      expect(capability.kind).toBe("noexec");
      expect(readdirSync(dir)).toEqual([]);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it("reads any other execution failure as unprobeable rather than as noexec", () => {
    const dir = mkdtempSync(join(tmpdir(), "cli-tmp-exec-test-"));
    try {
      expect(probeExecCapability(dir, probeDepsAnswering({ status: null, errorCode: "ENOENT" })).kind).toBe(
        "unprobeable",
      );
      expect(probeExecCapability(dir, probeDepsAnswering({ status: 1, errorCode: undefined })).kind).toBe(
        "unprobeable",
      );
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it("answers unprobeable for a directory the probe cannot be staged in", () => {
    expect(existsSync(UNWRITABLE_DIR)).toBe(true);
    const capability = probeExecCapability(UNWRITABLE_DIR);
    expect(capability.kind).toBe("unprobeable");
  });
});

describe("assertCliTmpDirExecutable", () => {
  const noexec: ExecCapability = { kind: "noexec", detail: "executing the probe failed EACCES" };

  it("passes an executable tmp dir", () => {
    expect(() => assertCliTmpDirExecutable(DEFAULT_TMP_DIR, { kind: "executable" }, undefined)).not.toThrow();
  });

  it("refuses to boot on a noexec tmp dir, naming the mount, the consequence, the fix and the opt-out", () => {
    let thrown: unknown;
    try {
      assertCliTmpDirExecutable(DEFAULT_TMP_DIR, noexec, undefined);
    } catch (error) {
      thrown = error;
    }
    expect(thrown).toBeInstanceOf(ConfigError);
    const message = thrown instanceof Error ? thrown.message : "";
    expect(message).toContain(DEFAULT_TMP_DIR);
    expect(message).toContain("noexec");
    expect(message).toContain("Unable to resize image");
    expect(message).not.toMatch(/\b(?:ENG|INI|MEI)-\d+\b/);
    expect(message).toContain("exec");
    expect(message).toContain(TMP_EXEC_CHECK_OPT_OUT);
  });

  it("refuses to boot when the probe could not run at all, fail-closed", () => {
    expect(() =>
      assertCliTmpDirExecutable(DEFAULT_TMP_DIR, { kind: "unprobeable", detail: "no probe dir" }, undefined),
    ).toThrow(ConfigError);
  });

  it("boots on the explicit opt-out and on nothing looser", () => {
    expect(() => assertCliTmpDirExecutable(DEFAULT_TMP_DIR, noexec, "true")).not.toThrow();
    expect(() => assertCliTmpDirExecutable(DEFAULT_TMP_DIR, noexec, "TRUE")).toThrow(ConfigError);
    expect(() => assertCliTmpDirExecutable(DEFAULT_TMP_DIR, noexec, "1")).toThrow(ConfigError);
  });
});
