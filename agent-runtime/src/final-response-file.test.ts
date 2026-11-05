import { afterAll, describe, expect, it } from "bun:test";
import { existsSync, mkdtempSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  FinalResponseUnreadableError,
  finalResponsePathOf,
  persistFinalResponse,
  readPersistedFinalResponse,
} from "./final-response-file.js";
import { artifactAttemptDirOf } from "./run-files.js";
import type { WireResponse } from "./run-wire.js";
import { FINAL_RESPONSE_FILE_NAME } from "./workspace-layout.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "final-response-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

let caseId = 0;

function newArtifactsRoot(): string {
  caseId += 1;
  const root = join(TEST_ROOT, `case-${caseId}`, "artifacts");
  return root;
}

const SUCCESS: WireResponse = {
  ok: true,
  result: {
    executionId: "e-1",
    output: "the report",
    estimatedCostUsd: 0.4,
    sessionId: "s-1",
    usage: { inputTokens: 1, outputTokens: 2, cacheCreationInputTokens: 3, cacheReadInputTokens: 4 },
    structuredOutput: { verdict: "pass" },
  },
};

const FAILURE: WireResponse = {
  ok: false,
  error: {
    message: "agent run failed (upstream_error): API Error: Server error mid-response",
    subtype: "upstream_error",
    estimatedCostUsd: 0.44,
    sessionId: "s-2",
  },
};

describe("persistFinalResponse / readPersistedFinalResponse", () => {
  it("round-trips a success response through the attempt dir, supervisor-readable", () => {
    const artifactsRoot = newArtifactsRoot();
    persistFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1, response: SUCCESS });
    const path = finalResponsePathOf({ artifactsRoot, executionId: "e-1", attempt: 1 });
    expect(path).toBe(join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), FINAL_RESPONSE_FILE_NAME));
    expect(statSync(path).mode & 0o777).toBe(0o644);
    expect(statSync(artifactAttemptDirOf(artifactsRoot, "e-1", 1)).mode & 0o777).toBe(0o755);
    expect(readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 })).toEqual(SUCCESS);
  });

  it("round-trips a failure response the same way", () => {
    const artifactsRoot = newArtifactsRoot();
    persistFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1, response: FAILURE });
    expect(readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 })).toEqual(FAILURE);
  });

  it("leaves no staging file behind and lands the response under its final name only", () => {
    const artifactsRoot = newArtifactsRoot();
    persistFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1, response: SUCCESS });
    expect(readdirSync(artifactAttemptDirOf(artifactsRoot, "e-1", 1))).toEqual([FINAL_RESPONSE_FILE_NAME]);
  });

  it("replaces an earlier persisted response wholesale", () => {
    const artifactsRoot = newArtifactsRoot();
    persistFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1, response: SUCCESS });
    persistFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1, response: FAILURE });
    expect(readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 })).toEqual(FAILURE);
  });

  it("reads undefined when nothing was persisted", () => {
    const artifactsRoot = newArtifactsRoot();
    expect(existsSync(artifactsRoot)).toBe(false);
    expect(readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 })).toBeUndefined();
  });

  it("refuses a persisted response that is not a wire response", () => {
    const artifactsRoot = newArtifactsRoot();
    persistFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1, response: SUCCESS });
    writeFileSync(finalResponsePathOf({ artifactsRoot, executionId: "e-1", attempt: 1 }), '{"ok":true}');
    expect(() => readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 })).toThrow(
      FinalResponseUnreadableError,
    );
  });

  it("refuses a persisted response that is not JSON, as a partial write would be", () => {
    const artifactsRoot = newArtifactsRoot();
    persistFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1, response: SUCCESS });
    writeFileSync(finalResponsePathOf({ artifactsRoot, executionId: "e-1", attempt: 1 }), '{"ok":tr');
    expect(() => readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 })).toThrow(
      FinalResponseUnreadableError,
    );
  });

  it("refuses a persisted response over the byte cap without reading it", () => {
    const artifactsRoot = newArtifactsRoot();
    persistFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1, response: SUCCESS });
    const size = statSync(finalResponsePathOf({ artifactsRoot, executionId: "e-1", attempt: 1 })).size;
    expect(() =>
      readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 }, size - 1),
    ).toThrow(FinalResponseUnreadableError);
    expect(readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 }, size)).toEqual(
      SUCCESS,
    );
  });
});
