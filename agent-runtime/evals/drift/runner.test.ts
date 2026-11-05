import { describe, expect, it } from "bun:test";
import { RunFailedError, type RunReportResult } from "../../src/run-report.js";
import type { DriftPrompt } from "./prompts.js";
import type { DriftRunRecord } from "./record.js";
import {
  DRIFT_MAX_TURNS,
  DRIFT_REPEATS,
  DRIFT_RUN_BUDGET_USD,
  DRIFT_SET_SPEND_CAP_USD,
  type DriftRunOutcome,
  type ExecuteDriftPromptFn,
  runDriftSet,
} from "./runner.js";

const OUTPUT_FORMAT = { type: "json_schema" as const, schema: {} };

const PROMPTS: DriftPrompt[] = [
  {
    id: "bash-loop",
    prompt: "a",
    expectedAnswer: 1,
    loadBearingTools: ["Bash"],
    outputFormat: OUTPUT_FORMAT,
  },
  { id: "pdf-read", prompt: "b", expectedAnswer: 2, loadBearingTools: ["Read"], outputFormat: OUTPUT_FORMAT },
];

function pack(promptSha256: string): string {
  return [
    JSON.stringify({ type: "envelope", modelPin: "haiku", promptSha256, sdkVersion: "0.3.251" }),
    JSON.stringify({
      type: "message",
      message: { type: "result", subtype: "success", num_turns: 2, total_cost_usd: 0.01 },
    }),
  ].join("\n");
}

function resultFor(prompt: DriftPrompt, executionId: string, costUsd = 0.01): RunReportResult {
  return {
    executionId,
    output: String(prompt.expectedAnswer),
    estimatedCostUsd: costUsd,
    sessionId: "s",
    usage: { inputTokens: 1, outputTokens: 1, cacheCreationInputTokens: 0, cacheReadInputTokens: 0 },
    structuredOutput: { answer: prompt.expectedAnswer },
  };
}

function healthyExecute(costUsd = 0.01): ExecuteDriftPromptFn {
  return async ({ prompt, executionId }): Promise<DriftRunOutcome> => ({
    result: resultFor(prompt, executionId, costUsd),
    failure: null,
    tracePackText: pack(prompt.id),
  });
}

function tickingClock(stepMs: number): () => number {
  let now = 1_000_000;
  return () => {
    now += stepMs;
    return now;
  };
}

describe("the run-set fuses", () => {
  it("pin the owner's spend cap and per-run budget so a change is a conscious edit here too", () => {
    expect(DRIFT_SET_SPEND_CAP_USD).toBe(0.15);
    expect(DRIFT_RUN_BUDGET_USD).toBe(0.05);
    expect(DRIFT_REPEATS).toBe(2);
    expect(DRIFT_MAX_TURNS).toBe(10);
  });
});

describe("runDriftSet", () => {
  it("runs every prompt once per repeat, interleaved, and projects one record per run", async () => {
    const seen: string[] = [];
    const records = await runDriftSet({
      prompts: PROMPTS,
      repeats: 2,
      batchSlug: "batch",
      spendCapUsd: 0.1,
      maxTurns: 10,
      now: tickingClock(500),
      execute: async (input) => {
        seen.push(input.executionId);
        return healthyExecute()(input);
      },
    });
    expect(seen).toEqual([
      "drift-batch-bash-loop-1",
      "drift-batch-pdf-read-1",
      "drift-batch-bash-loop-2",
      "drift-batch-pdf-read-2",
    ]);
    expect(records.map((record) => [record.promptId, record.repeat, record.outcome])).toEqual([
      ["bash-loop", 1, "correct"],
      ["pdf-read", 1, "correct"],
      ["bash-loop", 2, "correct"],
      ["pdf-read", 2, "correct"],
    ]);
    expect(records[0]?.wallMs).toBe(500);
    expect(records[0]?.numTurns).toBe(2);
    expect(records[0]?.identity.promptSha256).toBe("bash-loop");
    expect(records[0]?.startedAt).toBe(new Date(1_000_500).toISOString());
  });

  it("hands each record to the observer as it lands", async () => {
    const landed: DriftRunRecord[] = [];
    await runDriftSet({
      prompts: PROMPTS,
      repeats: 1,
      batchSlug: "b",
      spendCapUsd: 0.1,
      maxTurns: 10,
      execute: healthyExecute(),
      onRecord: (record) => landed.push(record),
    });
    expect(landed.map((record) => record.promptId)).toEqual(["bash-loop", "pdf-read"]);
  });

  it("stops spending once the summed cost passes the cap and records the rest as skipped", async () => {
    const executed: string[] = [];
    const records = await runDriftSet({
      prompts: PROMPTS,
      repeats: 2,
      batchSlug: "b",
      spendCapUsd: 0.1,
      maxTurns: 10,
      execute: async (input) => {
        executed.push(input.executionId);
        return healthyExecute(0.06)(input);
      },
    });
    expect(executed).toEqual(["drift-b-bash-loop-1", "drift-b-pdf-read-1"]);
    expect(records.map((record) => record.outcome)).toEqual(["correct", "correct", "skipped", "skipped"]);
    expect(records[2]?.detail).toMatch(/spend cap/);
  });

  it("records a RunFailedError as a failed run with its subtype and spend, and keeps going", async () => {
    const records = await runDriftSet({
      prompts: PROMPTS,
      repeats: 1,
      batchSlug: "b",
      spendCapUsd: 0.1,
      maxTurns: 10,
      execute: async (input) => {
        if (input.prompt.id === "bash-loop") {
          throw new RunFailedError("agent run failed (error_max_turns)", {
            subtype: "error_max_turns",
            estimatedCostUsd: 0.02,
            sessionId: null,
          });
        }
        return healthyExecute()(input);
      },
    });
    expect(records[0]?.outcome).toBe("failed");
    expect(records[0]?.detail).toMatch(/^error_max_turns:/);
    expect(records[0]?.estimatedCostUsd).toBe(0.02);
    expect(records[0]?.maxTurnsHit).toBe(true);
    expect(records[1]?.outcome).toBe("correct");
  });

  it("records any other throw as a harness error rather than losing the batch", async () => {
    const records = await runDriftSet({
      prompts: PROMPTS,
      repeats: 1,
      batchSlug: "b",
      spendCapUsd: 0.1,
      maxTurns: 10,
      execute: async (input) => {
        if (input.prompt.id === "pdf-read") {
          throw new Error("mkdtemp exploded");
        }
        return healthyExecute()(input);
      },
    });
    expect(records[1]?.outcome).toBe("failed");
    expect(records[1]?.detail).toBe("harness_error: mkdtemp exploded");
    expect(records[1]?.estimatedCostUsd).toBe(0);
  });
});
