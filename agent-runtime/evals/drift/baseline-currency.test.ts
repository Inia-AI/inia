import { describe, expect, it } from "bun:test";
import { existsSync, readFileSync } from "node:fs";
import { DRIFT_BASELINE_PATH, parseDriftBaseline, stampDriftBaseline } from "./baseline.js";
import {
  DRIFT_PROMPT_IDS,
  DRIFT_PROMPTS,
  driftPromptSha256,
  driftStagedInputsOf,
  stagedInputsDigestOf,
} from "./prompts.js";
import { testRecordSet } from "./test-records.js";

function currentIdentityOf(
  promptId: string,
): { promptSha256: string; stagedInputsSha256: string } | undefined {
  const prompt = DRIFT_PROMPTS.find((candidate) => candidate.id === promptId);
  if (prompt === undefined) {
    return undefined;
  }
  return {
    promptSha256: driftPromptSha256(prompt),
    stagedInputsSha256: stagedInputsDigestOf(driftStagedInputsOf(prompt)),
  };
}

describe("the stamped drift baseline stays current with the prompts the drift runs actually send", () => {
  it("every stamped prompt matches the prompt and staged inputs this repo still builds", () => {
    if (!existsSync(DRIFT_BASELINE_PATH)) {
      return;
    }
    const baseline = parseDriftBaseline(JSON.parse(readFileSync(DRIFT_BASELINE_PATH, "utf8")));
    expect(Object.keys(baseline.prompts).sort()).toEqual([...DRIFT_PROMPT_IDS].sort());
    for (const [promptId, stamped] of Object.entries(baseline.prompts)) {
      const current = currentIdentityOf(promptId);
      expect(current).toBeDefined();
      expect(stamped.promptSha256).toBe(current?.promptSha256 ?? "");
      expect(stamped.stagedInputsSha256).toBe(current?.stagedInputsSha256 ?? "");
    }
  });

  it("catches a baseline stamped against a prompt that no longer exists", () => {
    const stale = stampDriftBaseline({
      records: testRecordSet(["bash-loop"], 2),
      runtimeVersion: "x",
      stampedAt: "d",
      calibrationRuns: 1,
    });
    expect(stale.prompts["bash-loop"]?.promptSha256).not.toBe(currentIdentityOf("bash-loop")?.promptSha256);
  });
});
