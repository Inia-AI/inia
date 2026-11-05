import { execFile } from "node:child_process";

// The SDK's own bwrap argv skeleton, as captured in src/contracts/bwrap-argv.fixture.json —
// one flag per kernel-side control the sandbox rests on, so a probe over this set fails
// exactly when a real sandboxed command would. src/toolbelt.test.ts proves the set runs on a
// healthy host; sandbox/README.md maps each failure message to the control at fault.
export const SDK_BWRAP_ARGV_PREFIX: readonly string[] = [
  "--new-session",
  "--die-with-parent",
  "--unshare-net",
  "--ro-bind",
  "/",
  "/",
];

export const SDK_BWRAP_ARGV_SUFFIX: readonly string[] = [
  "--dev",
  "/dev",
  "--unshare-pid",
  "--unshare-user",
  "--cap-drop",
  "ALL",
  "--proc",
  "/proc",
];

// The SDK's apply-seccomp helper makes a nested user namespace and writes its setgroups on every
// Bash call. A host AppArmor profile can refuse that step while bwrap itself succeeds, so the
// probe takes the same step instead of a bare no-op.
const PROBE_NESTED_USERNS_COMMAND: readonly string[] = [
  "/usr/bin/unshare",
  "--user",
  "--map-root-user",
  "/bin/true",
];

export const SANDBOX_PROBE_BWRAP_ARGV: readonly string[] = [
  ...SDK_BWRAP_ARGV_PREFIX,
  ...SDK_BWRAP_ARGV_SUFFIX,
  "--tmpfs",
  "/tmp",
  "--",
  ...PROBE_NESTED_USERNS_COMMAND,
];

export const SANDBOX_PROBE_TIMEOUT_MS = 20000;

export const SDK_NESTED_USERNS_EINVAL_SIGNATURE = "apply-seccomp: unshare(CLONE_NEWUSER): Invalid argument";

// One row per bwrap failure text sandbox/README.md maps to the control at fault, so every
// surface that meets the text (the pre-run probe, the scripted contract run) names the cause
// instead of quoting stderr alone. The last row is the one failure that is none of the four
// controls: the SDK's own apply-seccomp helper losing its nested user namespace to EINVAL,
// which bwrap's already-running namespace proves is not a missing control.
export type SandboxFailureCause = {
  readonly signature: string;
  readonly name: string;
  readonly control: string;
};

const BWRAP_APPARMOR_PROFILE_CONTROL =
  "the host AppArmor profile bwrap-userns-restrict, which Ubuntu 26.04 loads and which denies every " +
  "capability to what bwrap starts: unload it (sandbox/README.md)";

export const SANDBOX_FAILURE_CATALOGUE: readonly SandboxFailureCause[] = [
  {
    signature: "No permissions to create new namespace",
    name: "seccomp_profile_missing",
    control: "the container seccomp profile (security_opt seccomp=sandbox/seccomp-bwrap.json)",
  },
  {
    signature: "Can't mount proc",
    name: "proc_paths_masked",
    control: "masked /proc paths (security_opt systempaths=unconfined)",
  },
  {
    signature: "Failed to make / slave",
    name: "apparmor_confined",
    control: "AppArmor confinement (security_opt apparmor=unconfined)",
  },
  {
    signature: "setting up uid map: Permission denied",
    name: "unprivileged_userns_restricted",
    control: "the host sysctl kernel.apparmor_restrict_unprivileged_userns=0",
  },
  {
    signature: "loopback: Failed RTM_NEWADDR",
    name: "unprivileged_userns_restricted",
    control: "the host sysctl kernel.apparmor_restrict_unprivileged_userns=0",
  },
  {
    signature: "write failed /proc/self/uid_map",
    name: "bwrap_apparmor_profile_loaded",
    control: BWRAP_APPARMOR_PROFILE_CONTROL,
  },
  {
    signature: "/proc/self/setgroups",
    name: "bwrap_apparmor_profile_loaded",
    control: BWRAP_APPARMOR_PROFILE_CONTROL,
  },
  {
    signature: SDK_NESTED_USERNS_EINVAL_SIGNATURE,
    name: "sdk_helper_nested_userns_einval",
    control:
      "none of the four sandbox controls — the SDK's apply-seccomp helper was multithreaded at its " +
      "nested unshare(CLONE_NEWUSER), which the kernel refuses with EINVAL (ADR 0118)",
  },
];

const UNRECOGNIZED_SANDBOX_FAILURE = "unrecognized_sandbox_failure";

export function nameSandboxFailure(detail: string): SandboxFailureCause | undefined {
  return SANDBOX_FAILURE_CATALOGUE.find((cause) => detail.includes(cause.signature));
}

export function describeSandboxFailure(detail: string): string {
  const cause = nameSandboxFailure(detail);
  if (cause === undefined) {
    return `${UNRECOGNIZED_SANDBOX_FAILURE} (see sandbox/README.md): ${detail}`;
  }
  return `${cause.name} — ${cause.control}: ${detail}`;
}

const SANDBOX_BINARY = "bwrap";

export type SandboxProbeResult = { ok: true } | { ok: false; detail: string };

export type SandboxProbeFn = () => Promise<SandboxProbeResult>;

export function createSandboxProbe(binary: string = SANDBOX_BINARY): SandboxProbeFn {
  return () =>
    new Promise((resolve) => {
      execFile(
        binary,
        [...SANDBOX_PROBE_BWRAP_ARGV],
        { timeout: SANDBOX_PROBE_TIMEOUT_MS },
        (error, _stdout, stderr) => {
          if (error === null) {
            resolve({ ok: true });
            return;
          }
          const detail = stderr.trim();
          resolve({ ok: false, detail: detail === "" ? error.message : detail });
        },
      );
    });
}

export const probeSandbox: SandboxProbeFn = createSandboxProbe();
