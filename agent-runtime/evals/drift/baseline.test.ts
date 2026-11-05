import { describe, expect, it } from "bun:test";
import { DRIFT_METRIC_NAMES, metricValueOf, parseDriftBaseline, stampDriftBaseline } from "./baseline.js";
import { TEST_MODEL_PIN, TEST_PROMPT_SHA256, testRecord, testRecordSet } from "./test-records.js";

const PROMPT_IDS = ["bash-loop", "pdf-read", "subagent"];

function calibrationRecords() {
  return [1, 2, 3, 4, 5, 6].flatMap((run) =>
    testRecordSet(PROMPT_IDS, 2, (promptId, repeat) => ({
      executionId: `drift-${run}-${promptId}-${repeat}`,
      wallMs: 10_000 + run * 1000 + repeat * 100,
      numTurns: promptId === "subagent" ? 3 : 4 + (run % 2),
      tokens: { output: 200 + run * 10 },
    })),
  );
}

describe("stampDriftBaseline", () => {
  it("summarizes every metric per prompt over the twelve calibration samples, median as the reference", () => {
    const baseline = stampDriftBaseline({
      records: calibrationRecords(),
      runtimeVersion: "0.3.1011",
      stampedAt: "2026-09-04",
      calibrationRuns: 6,
    });
    expect(baseline.sdkVersion).toBe("0.3.251");
    expect(baseline.cliVersion).toBe("2.1.251");
    expect(baseline.modelPin).toBe(TEST_MODEL_PIN);
    expect(baseline.runtimeVersion).toBe("0.3.1011");
    expect(baseline.calibrationRuns).toBe(6);
    expect(Object.keys(baseline.prompts)).toEqual(PROMPT_IDS);
    const bashLoop = baseline.prompts["bash-loop"];
    expect(bashLoop?.promptSha256).toBe(TEST_PROMPT_SHA256["bash-loop"] ?? "");
    expect(bashLoop?.stagedInputsSha256).toBe("absent");
    expect(bashLoop?.samples).toBe(12);
    for (const metric of DRIFT_METRIC_NAMES) {
      expect(bashLoop?.metrics[metric]?.samples).toBe(12);
    }
    expect(bashLoop?.metrics.wallMs).toEqual({
      min: 11_100,
      p25: 12_175,
      median: 13_650,
      p75: 15_125,
      max: 16_200,
      samples: 12,
    });
    expect(bashLoop?.metrics.numTurns?.median).toBe(4.5);
    expect(bashLoop?.metrics.contextTokens?.median).toBe(400 + 3000 + 9000);
    expect(bashLoop?.metrics.sdkToFloorRatio?.median).toBeCloseTo(1.2, 9);
    expect(bashLoop?.toolCalls).toEqual({
      Bash: { min: 3, p25: 3, median: 3, p75: 3, max: 3, samples: 12 },
      StructuredOutput: { min: 1, p25: 1, median: 1, p75: 1, max: 1, samples: 12 },
    });
    expect(baseline.prompts["pdf-read"]?.stagedInputsSha256).toBe("dd".repeat(32));
  });

  it("counts a tool a run never called as 0 for that run, so an occasional extra call shows as spread", () => {
    const records = testRecordSet(["bash-loop"], 2, (_promptId, repeat) => ({
      toolCalls: repeat === 1 ? { Bash: 3, Read: 1 } : { Bash: 3 },
    }));
    const baseline = stampDriftBaseline({ records, runtimeVersion: "x", stampedAt: "d", calibrationRuns: 1 });
    expect(baseline.prompts["bash-loop"]?.toolCalls["Read"]).toEqual({
      min: 0,
      p25: 0.25,
      median: 0.5,
      p75: 0.75,
      max: 1,
      samples: 2,
    });
  });

  it("refuses to stamp over a run that was not correct — a baseline never launders a failure", () => {
    const records = [
      testRecord("bash-loop", 1),
      testRecord("bash-loop", 2, { outcome: "failed", detail: "boom" }),
    ];
    expect(() =>
      stampDriftBaseline({ records, runtimeVersion: "x", stampedAt: "d", calibrationRuns: 1 }),
    ).toThrow(/drift-bash-loop-2.*failed/);
  });

  it("refuses records whose identity disagrees — one baseline describes one SDK, CLI, pin and prompt", () => {
    const mixedSdk = [
      testRecord("bash-loop", 1),
      testRecord("bash-loop", 2, { identity: { sdkVersion: "0.3.260" } }),
    ];
    expect(() =>
      stampDriftBaseline({ records: mixedSdk, runtimeVersion: "x", stampedAt: "d", calibrationRuns: 1 }),
    ).toThrow(/sdkVersion/);
    const mixedPrompt = [
      testRecord("bash-loop", 1),
      testRecord("bash-loop", 2, { identity: { promptSha256: "ee".repeat(32) } }),
    ];
    expect(() =>
      stampDriftBaseline({ records: mixedPrompt, runtimeVersion: "x", stampedAt: "d", calibrationRuns: 1 }),
    ).toThrow(/promptSha256/);
    const missingIdentity = [testRecord("bash-loop", 1, { identity: { cliVersion: null } })];
    expect(() =>
      stampDriftBaseline({
        records: missingIdentity,
        runtimeVersion: "x",
        stampedAt: "d",
        calibrationRuns: 1,
      }),
    ).toThrow(/cliVersion/);
  });

  it("refuses an empty calibration", () => {
    expect(() =>
      stampDriftBaseline({ records: [], runtimeVersion: "x", stampedAt: "d", calibrationRuns: 0 }),
    ).toThrow(/no records/);
  });
});

describe("parseDriftBaseline", () => {
  it("round-trips a stamped baseline through JSON", () => {
    const baseline = stampDriftBaseline({
      records: calibrationRecords(),
      runtimeVersion: "0.3.1011",
      stampedAt: "2026-09-04",
      calibrationRuns: 6,
    });
    expect(parseDriftBaseline(JSON.parse(JSON.stringify(baseline)))).toEqual(baseline);
  });

  it("refuses a baseline missing a metric summary or carrying a malformed one", () => {
    const baseline = stampDriftBaseline({
      records: calibrationRecords(),
      runtimeVersion: "0.3.1011",
      stampedAt: "2026-09-04",
      calibrationRuns: 6,
    });
    const serialized: unknown = JSON.parse(JSON.stringify(baseline));
    if (typeof serialized !== "object" || serialized === null || !("prompts" in serialized)) {
      throw new Error("unexpected shape");
    }
    const prompts: unknown = serialized.prompts;
    if (typeof prompts !== "object" || prompts === null || !("bash-loop" in prompts)) {
      throw new Error("unexpected shape");
    }
    const bashLoop: unknown = prompts["bash-loop"];
    if (typeof bashLoop !== "object" || bashLoop === null || !("metrics" in bashLoop)) {
      throw new Error("unexpected shape");
    }
    const metrics: unknown = bashLoop.metrics;
    if (typeof metrics !== "object" || metrics === null) {
      throw new Error("unexpected shape");
    }
    const broken = { ...metrics, wallMs: { min: 1, median: "2" } };
    expect(() =>
      parseDriftBaseline({
        ...serialized,
        prompts: { ...prompts, "bash-loop": { ...bashLoop, metrics: broken } },
      }),
    ).toThrow(/wallMs/);
    expect(() => parseDriftBaseline({ ...serialized, prompts: {} })).not.toThrow();
    expect(() => parseDriftBaseline({ ...serialized, modelPin: 7 })).toThrow(/modelPin/);
  });
});

describe("metricValueOf", () => {
  it("reads each band's statistic off a record, null where unmeasured", () => {
    const record = testRecord("bash-loop", 1, { wallMs: null, sdkCostUsd: 0.02, floorCostUsd: 0.01 });
    expect(metricValueOf(record, "wallMs")).toBeNull();
    expect(metricValueOf(record, "contextTokens")).toBe(12_400);
    expect(metricValueOf(record, "outputTokens")).toBe(300);
    expect(metricValueOf(record, "numTurns")).toBe(4);
    expect(metricValueOf(record, "estimatedCostUsd")).toBe(0.012);
    expect(metricValueOf(record, "sdkToFloorRatio")).toBe(2);
    expect(metricValueOf(testRecord("bash-loop", 1, { floorCostUsd: 0 }), "sdkToFloorRatio")).toBeNull();
  });
});
