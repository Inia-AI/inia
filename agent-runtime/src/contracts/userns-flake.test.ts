import { describe, expect, it } from "bun:test";
import {
  describePersistentUserNamespaceFailure,
  describeUserNamespaceFlake,
  isUserNamespaceCreationFlake,
  retryOnUserNamespaceFlake,
  USER_NAMESPACE_DIAGNOSTIC_PATHS,
  USERNS_EINVAL_SIGNATURE,
  USERNS_FLAKE_MAX_ATTEMPTS,
  UserNamespaceFlakePersistedError,
} from "./userns-flake.js";

const EINVAL_TOOL_RESULT = `Exit code 1\n${USERNS_EINVAL_SIGNATURE}`;

describe("isUserNamespaceCreationFlake", () => {
  it("detects the EINVAL signature in any tool result text", () => {
    expect(isUserNamespaceCreationFlake(["something fine", EINVAL_TOOL_RESULT])).toBe(true);
  });

  it("ignores a successful tool result", () => {
    expect(isUserNamespaceCreationFlake(["ENG232_SCRIPTED_RUN_MARKER\nENG232_TMP_WRITABLE"])).toBe(false);
  });

  it("ignores the EPERM family, which points at a missing control rather than the race", () => {
    expect(
      isUserNamespaceCreationFlake([
        "Exit code 1\nbwrap: setting up uid map: Permission denied",
        "Exit code 1\napply-seccomp: unshare(CLONE_NEWPID|CLONE_NEWNS): Operation not permitted",
      ]),
    ).toBe(false);
  });

  it("treats an empty result list as no flake", () => {
    expect(isUserNamespaceCreationFlake([])).toBe(false);
  });
});

describe("describeUserNamespaceFlake", () => {
  it("names the race, the attempt, the tool result, and every diagnostic value, and no ticket", () => {
    const message = describeUserNamespaceFlake({
      attempt: 2,
      maxAttempts: 3,
      toolResultTexts: [EINVAL_TOOL_RESULT],
      readDiagnosticFile: (path) => `value-of ${path}`,
    });
    expect(message).toContain("nested user-namespace race");
    expect(message).not.toMatch(/\b(?:ENG|INI|MEI)-\d+\b/);
    expect(message).toContain("attempt 2 of 3");
    expect(message).toContain(USERNS_EINVAL_SIGNATURE);
    expect(USER_NAMESPACE_DIAGNOSTIC_PATHS.length).toBeGreaterThan(0);
    for (const path of USER_NAMESPACE_DIAGNOSTIC_PATHS) {
      expect(message).toContain(path);
      expect(message).toContain(`value-of ${path}`);
    }
  });

  it("reports an unreadable diagnostic path instead of throwing", () => {
    const message = describeUserNamespaceFlake({
      attempt: 1,
      maxAttempts: USERNS_FLAKE_MAX_ATTEMPTS,
      toolResultTexts: [EINVAL_TOOL_RESULT],
      readDiagnosticFile: () => {
        throw new Error("no such proc entry");
      },
    });
    for (const path of USER_NAMESPACE_DIAGNOSTIC_PATHS) {
      expect(message).toContain(`${path}: unreadable (no such proc entry)`);
    }
  });
});

describe("retryOnUserNamespaceFlake", () => {
  it("returns the first clean result without reporting a flake", async () => {
    const reported: number[] = [];
    const result = await retryOnUserNamespaceFlake({
      run: () => Promise.resolve("clean"),
      isFlake: () => false,
      maxAttempts: 3,
      onFlake: (attempt) => {
        reported.push(attempt);
      },
      exhausted: () => new Error("unexpected exhaustion"),
    });
    expect(result).toBe("clean");
    expect(reported).toEqual([]);
  });

  it("reports the flaky attempt and returns the later clean result", async () => {
    const results = ["flake", "clean"];
    const reported: { attempt: number; result: string }[] = [];
    const result = await retryOnUserNamespaceFlake({
      run: () => Promise.resolve(results.shift() ?? "exhausted"),
      isFlake: (candidate) => candidate === "flake",
      maxAttempts: 3,
      onFlake: (attempt, candidate) => {
        reported.push({ attempt, result: candidate });
      },
      exhausted: () => new Error("unexpected exhaustion"),
    });
    expect(result).toBe("clean");
    expect(reported).toEqual([{ attempt: 1, result: "flake" }]);
  });

  it("stops at maxAttempts and throws the named error built from the last flaky result", async () => {
    let attempts = 0;
    const reported: number[] = [];
    const exhausted = retryOnUserNamespaceFlake({
      run: () => {
        attempts += 1;
        return Promise.resolve(`flake-${attempts}`);
      },
      isFlake: () => true,
      maxAttempts: 3,
      onFlake: (attempt) => {
        reported.push(attempt);
      },
      exhausted: (lastResult) => new UserNamespaceFlakePersistedError(`persisted: ${lastResult}`),
    });
    await expect(exhausted).rejects.toBeInstanceOf(UserNamespaceFlakePersistedError);
    await expect(exhausted).rejects.toThrow("persisted: flake-3");
    expect(attempts).toBe(3);
    expect(reported).toEqual([1, 2, 3]);
  });

  it("propagates a driver rejection instead of retrying it", async () => {
    let attempts = 0;
    const driverFailure = retryOnUserNamespaceFlake({
      run: () => {
        attempts += 1;
        return Promise.reject(new Error("driver exited 1"));
      },
      isFlake: () => true,
      maxAttempts: 3,
      onFlake: () => {},
      exhausted: () => new Error("unexpected exhaustion"),
    });
    await expect(driverFailure).rejects.toThrow("driver exited 1");
    expect(attempts).toBe(1);
  });
});

describe("describePersistentUserNamespaceFailure", () => {
  it("names the attempt count, the catalogued cause, and every diagnostic value, and no ticket", () => {
    const message = describePersistentUserNamespaceFailure({
      maxAttempts: 3,
      toolResultTexts: [EINVAL_TOOL_RESULT],
      readDiagnosticFile: (path) => `value-of ${path}`,
    });
    expect(message).not.toMatch(/\b(?:ENG|INI|MEI)-\d+\b/);
    expect(message).toContain("every one of 3 attempts");
    expect(message).toContain("sdk_helper_nested_userns_einval");
    expect(message).toContain(USERNS_EINVAL_SIGNATURE);
    for (const path of USER_NAMESPACE_DIAGNOSTIC_PATHS) {
      expect(message).toContain(`${path}: value-of ${path}`);
    }
  });
});
