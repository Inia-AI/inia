import { afterAll, describe, expect, it } from "bun:test";
import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  createSandboxProbe,
  describeSandboxFailure,
  nameSandboxFailure,
  SANDBOX_FAILURE_CATALOGUE,
  SANDBOX_PROBE_BWRAP_ARGV,
  SDK_NESTED_USERNS_EINVAL_SIGNATURE,
} from "./sandbox-probe.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "sandbox-probe-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

const LOOPBACK_FAILURE = "bwrap: loopback: Failed RTM_NEWADDR: Operation not permitted";
const SDK_NESTED_SETGROUPS_REFUSED =
  "apply-seccomp: write /proc/self/setgroups (nested userns is capability-restricted; caller must provide CAP_SYS_ADMIN): Permission denied";
const PROBE_NESTED_UID_MAP_REFUSED = "unshare: write failed /proc/self/uid_map: Operation not permitted";

let scriptId = 0;

function scriptedBinary(body: string): string {
  scriptId += 1;
  const path = join(TEST_ROOT, `fake-bwrap-${scriptId}`);
  writeFileSync(path, `#!/bin/sh\n${body}\n`);
  chmodSync(path, 0o755);
  return path;
}

describe("SANDBOX_PROBE_BWRAP_ARGV", () => {
  it("exercises every namespace the SDK sandbox needs", () => {
    for (const flag of ["--unshare-user", "--unshare-net", "--unshare-pid", "--cap-drop", "--proc"]) {
      expect(SANDBOX_PROBE_BWRAP_ARGV).toContain(flag);
    }
  });

  it("ends in a nested user namespace that writes its setgroups and id maps, as the SDK helper does on every Bash call", () => {
    expect(SANDBOX_PROBE_BWRAP_ARGV.slice(-5)).toEqual([
      "--",
      "/usr/bin/unshare",
      "--user",
      "--map-root-user",
      "/bin/true",
    ]);
  });
});

describe("createSandboxProbe", () => {
  it("reports ok when bwrap runs the probe command cleanly, passing the full argv", async () => {
    const argvLog = join(TEST_ROOT, "argv.log");
    const binary = scriptedBinary(`printf '%s\\n' "$@" > ${argvLog}\nexit 0`);
    const result = await createSandboxProbe(binary)();
    expect(result).toEqual({ ok: true });
    const seenArgv = readFileSync(argvLog, "utf8").split("\n").slice(0, -1);
    expect(seenArgv).toEqual([...SANDBOX_PROBE_BWRAP_ARGV]);
  });

  it("reports bwrap's own stderr as the failure detail", async () => {
    const binary = scriptedBinary(`echo "${LOOPBACK_FAILURE}" >&2\nexit 1`);
    const result = await createSandboxProbe(binary)();
    expect(result).toEqual({ ok: false, detail: LOOPBACK_FAILURE });
  });

  it("reports the exec error when bwrap cannot be executed at all", async () => {
    const result = await createSandboxProbe(join(TEST_ROOT, "missing-binary"))();
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.detail).not.toBe("");
    }
  });
});

describe("describeSandboxFailure", () => {
  it("names no ticket id in any catalogued cause, because an operator reads it in a run failure", () => {
    for (const cause of SANDBOX_FAILURE_CATALOGUE) {
      expect(cause.control).not.toMatch(/\b(?:ENG|INI|MEI)-\d+\b/);
    }
  });

  it("names the control at fault for every catalogued bwrap failure text", () => {
    for (const cause of SANDBOX_FAILURE_CATALOGUE) {
      const detail = `bwrap: ${cause.signature}`;
      expect(nameSandboxFailure(detail)).toBe(cause);
      const described = describeSandboxFailure(detail);
      expect(described).toStartWith(`${cause.name} — `);
      expect(described).toContain(cause.control);
      expect(described).toEndWith(detail);
    }
  });

  it("names the loopback failure as the userns sysctl, the control it actually points at", () => {
    expect(nameSandboxFailure(LOOPBACK_FAILURE)?.name).toBe("unprivileged_userns_restricted");
  });

  it("names a refused setgroups or uid_map write in the nested user namespace as the host's AppArmor bwrap profile", () => {
    expect(nameSandboxFailure(`Exit code 1\n${SDK_NESTED_SETGROUPS_REFUSED}`)?.name).toBe(
      "bwrap_apparmor_profile_loaded",
    );
    expect(nameSandboxFailure(PROBE_NESTED_UID_MAP_REFUSED)?.name).toBe("bwrap_apparmor_profile_loaded");
  });

  it("names the SDK helper's nested-userns EINVAL as none of the four controls", () => {
    const cause = nameSandboxFailure(`Exit code 1\n${SDK_NESTED_USERNS_EINVAL_SIGNATURE}`);
    expect(cause?.name).toBe("sdk_helper_nested_userns_einval");
    expect(cause?.control).toContain("none of the four sandbox controls");
  });

  it("keeps an unrecognized text verbatim under an explicit unrecognized label", () => {
    const described = describeSandboxFailure("bwrap: something new");
    expect(described).toStartWith("unrecognized_sandbox_failure");
    expect(described).toContain("sandbox/README.md");
    expect(described).toEndWith("bwrap: something new");
  });
});
