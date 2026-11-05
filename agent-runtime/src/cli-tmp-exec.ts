import { spawnSync } from "node:child_process";
import { chmodSync, copyFileSync, mkdtempSync, rmSync } from "node:fs";
import { join } from "node:path";
import { ConfigError } from "./config-error.js";

// The child env carries no tmp variable, so the deployed answer is /tmp.
const TMP_DIR_ENV_VARS: readonly string[] = ["TMPDIR", "TMP", "TEMP"];
export const DEFAULT_TMP_DIR = "/tmp";
export const TMP_EXEC_CHECK_OPT_OUT = "AGENT_RUNTIME_TMP_EXEC_CHECK_DISABLED=true";
const CHECK_DISABLED_VALUE = "true";
const PROBE_SOURCE_BINARY = "/bin/true";
const PROBE_DIR_PREFIX = ".exec-probe-";
const PROBE_BINARY_NAME = "probe";
const PROBE_BINARY_MODE = 0o700;
const PROBE_SUCCESS_STATUS = 0;
const NOEXEC_ERROR_CODES: ReadonlySet<string> = new Set(["EACCES", "EPERM"]);
const COMPOSE_TMP_MOUNT_EXAMPLE = '"/tmp:size=512m,exec"';

export type ExecCapability =
  | { kind: "executable" }
  | { kind: "noexec"; detail: string }
  | { kind: "unprobeable"; detail: string };

export type ProbeRun = { status: number | null; errorCode: string | undefined };

export type ExecProbeDeps = { runProbe: (path: string) => ProbeRun };

function errorCodeOf(error: Error | undefined): string | undefined {
  if (error !== undefined && "code" in error && typeof error.code === "string") {
    return error.code;
  }
  return undefined;
}

const REAL_PROBE: ExecProbeDeps = {
  runProbe: (path) => {
    const result = spawnSync(path, [], { stdio: "ignore" });
    return { status: result.status, errorCode: errorCodeOf(result.error) };
  },
};

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export function cliTmpDirOf(childEnv: Record<string, string | undefined>): string {
  for (const name of TMP_DIR_ENV_VARS) {
    const value = childEnv[name];
    if (value !== undefined && value !== "") {
      return value;
    }
  }
  return DEFAULT_TMP_DIR;
}

export function probeExecCapability(dir: string, deps: ExecProbeDeps = REAL_PROBE): ExecCapability {
  let probeDir: string;
  try {
    probeDir = mkdtempSync(join(dir, PROBE_DIR_PREFIX));
  } catch (error) {
    return {
      kind: "unprobeable",
      detail: `could not create a probe directory under ${dir}: ${messageOf(error)}`,
    };
  }
  try {
    const probePath = join(probeDir, PROBE_BINARY_NAME);
    try {
      copyFileSync(PROBE_SOURCE_BINARY, probePath);
      chmodSync(probePath, PROBE_BINARY_MODE);
    } catch (error) {
      return {
        kind: "unprobeable",
        detail: `could not stage ${PROBE_SOURCE_BINARY} at ${probePath}: ${messageOf(error)}`,
      };
    }
    const run = deps.runProbe(probePath);
    if (run.errorCode !== undefined && NOEXEC_ERROR_CODES.has(run.errorCode)) {
      return { kind: "noexec", detail: `executing ${probePath} failed ${run.errorCode}` };
    }
    if (run.errorCode !== undefined) {
      return { kind: "unprobeable", detail: `executing ${probePath} failed ${run.errorCode}` };
    }
    if (run.status !== PROBE_SUCCESS_STATUS) {
      return { kind: "unprobeable", detail: `executing ${probePath} exited ${String(run.status)}` };
    }
    return { kind: "executable" };
  } finally {
    rmSync(probeDir, { recursive: true, force: true });
  }
}

export function assertCliTmpDirExecutable(
  dir: string,
  capability: ExecCapability,
  checkDisabledFlag: string | undefined,
): void {
  if (checkDisabledFlag === CHECK_DISABLED_VALUE || capability.kind === "executable") {
    return;
  }
  const cause =
    capability.kind === "noexec"
      ? `${dir} is mounted noexec (${capability.detail})`
      : `the exec probe under ${dir} could not run (${capability.detail})`;
  throw new ConfigError(
    `tmp exec check is fail-closed: ${cause}. The Claude Code CLI extracts its embedded native image ` +
      "processor under that directory and dlopens it from there, so every Read of an image over the " +
      'model\'s pixel ceiling would fail "Unable to resize image" instead of being downscaled (ADR 0138) — ' +
      `mount it with exec (compose tmpfs entry ${COMPOSE_TMP_MOUNT_EXAMPLE}), or set ` +
      `${TMP_EXEC_CHECK_OPT_OUT} explicitly only outside a deployed environment`,
  );
}
