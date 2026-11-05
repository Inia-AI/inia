// The SDK's apply-seccomp helper (its own native code, embedded in the CLI binary and re-exec'd
// as /proc/self/fd/3 inside the bwrap sandbox) creates a nested user namespace before applying
// its seccomp filter. unshare(CLONE_NEWUSER) implies CLONE_THREAD, which the kernel refuses with
// EINVAL unless the calling thread group holds exactly one thread, so a helper thread racing
// startup turns the whole tool call into this exact error, intermittently, on shared CI runners.
// bwrap's own user namespace was already up when the helper ran, so the four sandbox controls are
// proven present in the very run that fails: this signature is runner timing, never a missing
// control. Any other failure (the EPERM family that does point at a missing control) must stay
// red on the first attempt.
import { describeSandboxFailure, SDK_NESTED_USERNS_EINVAL_SIGNATURE } from "../sandbox-probe.js";

export const USERNS_EINVAL_SIGNATURE = SDK_NESTED_USERNS_EINVAL_SIGNATURE;
export const USERNS_FLAKE_MAX_ATTEMPTS = 3;

// Every attempt of one job has lost the race (measured twice, 3 of 3, both on kernel
// 6.17.0-1022-azure), so the exhausted retry names the cause in one annotation instead of
// leaving a stub-run transcript diff as the only evidence.
export class UserNamespaceFlakePersistedError extends Error {}

export const USER_NAMESPACE_DIAGNOSTIC_PATHS: readonly string[] = [
  "/proc/sys/user/max_user_namespaces",
  "/proc/sys/kernel/apparmor_restrict_unprivileged_userns",
  "/proc/self/uid_map",
  "/proc/version",
];

export function isUserNamespaceCreationFlake(toolResultTexts: readonly string[]): boolean {
  return toolResultTexts.some((text) => text.includes(USERNS_EINVAL_SIGNATURE));
}

type UserNamespaceFlakeDescription = {
  attempt: number;
  maxAttempts: number;
  toolResultTexts: readonly string[];
  readDiagnosticFile: (path: string) => string;
};

export function describeUserNamespaceFlake(description: UserNamespaceFlakeDescription): string {
  const diagnostics = USER_NAMESPACE_DIAGNOSTIC_PATHS.map(
    (path) => `${path}: ${readDiagnosticValue(description.readDiagnosticFile, path)}`,
  );
  return [
    `userns flake: the SDK's apply-seccomp helper lost its nested user-namespace race on ` +
      `attempt ${description.attempt} of ${description.maxAttempts} (kernel EINVAL for a ` +
      `multithreaded unshare(CLONE_NEWUSER)); retrying the scripted run.`,
    `tool result: ${description.toolResultTexts.join("\n")}`,
    ...diagnostics,
  ].join("\n");
}

type PersistentUserNamespaceFailure = {
  maxAttempts: number;
  toolResultTexts: readonly string[];
  readDiagnosticFile: (path: string) => string;
};

export function describePersistentUserNamespaceFailure(failure: PersistentUserNamespaceFailure): string {
  const diagnostics = USER_NAMESPACE_DIAGNOSTIC_PATHS.map(
    (path) => `${path}: ${readDiagnosticValue(failure.readDiagnosticFile, path)}`,
  );
  return [
    `userns flake persisted: the scripted contract run lost the SDK's nested user-namespace race on every one of ` +
      `${failure.maxAttempts} attempts on this runner — ` +
      describeSandboxFailure(failure.toolResultTexts.join("\n")),
    ...diagnostics,
  ].join("\n");
}

function readDiagnosticValue(readDiagnosticFile: (path: string) => string, path: string): string {
  try {
    return readDiagnosticFile(path).trim();
  } catch (error) {
    return `unreadable (${error instanceof Error ? error.message : String(error)})`;
  }
}

type UserNamespaceFlakeRetry<Result> = {
  run: () => Promise<Result>;
  isFlake: (result: Result) => boolean;
  maxAttempts: number;
  onFlake: (attempt: number, result: Result) => void;
  exhausted: (lastResult: Result) => Error;
};

export async function retryOnUserNamespaceFlake<Result>(
  retry: UserNamespaceFlakeRetry<Result>,
): Promise<Result> {
  let lastResult: Result | undefined;
  for (let attempt = 1; attempt <= retry.maxAttempts; attempt += 1) {
    const result = await retry.run();
    if (!retry.isFlake(result)) {
      return result;
    }
    retry.onFlake(attempt, result);
    lastResult = result;
  }
  if (lastResult === undefined) {
    throw new Error("retryOnUserNamespaceFlake requires maxAttempts of at least 1");
  }
  throw retry.exhausted(lastResult);
}
