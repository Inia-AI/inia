import { describe, expect, it } from "bun:test";
import {
  durationSecondsBetween,
  type PerformanceConfig,
  performanceVerdict,
  runTraceIdentityOf,
  sumOutputTokens,
} from "./performance.js";

const BASELINE_PROMPT_SHA256 = "ab".repeat(32);
const BASELINE_MODEL_PIN = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

const CONFIG: PerformanceConfig = {
  baseline: {
    p75DurationSeconds: 400,
    p75OutputTokens: 40000,
    promptSha256: BASELINE_PROMPT_SHA256,
    modelPin: BASELINE_MODEL_PIN,
    sdkVersion: "0.3.227",
    runtimeVersion: "0.2.8",
    measuredAt: "2026-08-11",
  },
  failAboveBaselineRatio: 1.5,
  warnAboveBaselineRatio: 1.2,
  spreadWarnRatio: 1.6,
};

function healthyRun(file: string) {
  return {
    file,
    durationSeconds: 380,
    outputTokens: 39000,
    promptSha256: BASELINE_PROMPT_SHA256,
    modelPin: BASELINE_MODEL_PIN,
  };
}

describe("durationSecondsBetween", () => {
  it("measures a normal span, rounding sub-second runs up rather than to a false 0", () => {
    expect(durationSecondsBetween("2026-08-12T10:00:00.000Z", "2026-08-12T10:06:20.000Z")).toBe(380);
    expect(durationSecondsBetween("2026-08-12T10:00:00.000Z", "2026-08-12T10:00:00.400Z")).toBe(1);
  });

  it("treats out-of-order, equal, and malformed timestamps as unmeasurable", () => {
    expect(durationSecondsBetween("2026-08-12T10:06:20.000Z", "2026-08-12T10:00:00.000Z")).toBeNull();
    expect(durationSecondsBetween("2026-08-12T10:00:00.000Z", "2026-08-12T10:00:00.000Z")).toBeNull();
    expect(durationSecondsBetween("not-a-date", "2026-08-12T10:00:00.000Z")).toBeNull();
  });
});

describe("sumOutputTokens", () => {
  it("sums output_tokens across usage records and ignores other record types", () => {
    const pack = [
      JSON.stringify({ type: "envelope", sdkVersion: "0.3.227" }),
      JSON.stringify({ type: "usage", usage: { input_tokens: 10, output_tokens: 1200 } }),
      JSON.stringify({ type: "tool_call", name: "Bash" }),
      JSON.stringify({ type: "usage", usage: { input_tokens: 12, output_tokens: 800 } }),
    ].join("\n");
    expect(sumOutputTokens(pack)).toBe(2000);
  });

  it("tolerates a torn final line from a killed run", () => {
    const pack = `${JSON.stringify({ type: "usage", usage: { output_tokens: 500 } })}\n{"type":"usa`;
    expect(sumOutputTokens(pack)).toBe(500);
  });

  it("returns null when no usage records are present", () => {
    expect(sumOutputTokens(JSON.stringify({ type: "envelope" }))).toBeNull();
  });
});

describe("runTraceIdentityOf", () => {
  it("reads the prompt digest and model pin from the pack's envelope record", () => {
    const pack = [
      JSON.stringify({ type: "usage", usage: { output_tokens: 10 } }),
      JSON.stringify({
        type: "envelope",
        promptSha256: BASELINE_PROMPT_SHA256,
        modelPin: BASELINE_MODEL_PIN,
        sdkVersion: "0.3.227",
      }),
    ].join("\n");
    expect(runTraceIdentityOf(pack)).toEqual({
      promptSha256: BASELINE_PROMPT_SHA256,
      modelPin: BASELINE_MODEL_PIN,
    });
  });

  it("answers nulls for a pack with no envelope or a torn one", () => {
    expect(runTraceIdentityOf(JSON.stringify({ type: "usage" }))).toEqual({
      promptSha256: null,
      modelPin: null,
    });
    expect(runTraceIdentityOf('{"type":"enve')).toEqual({ promptSha256: null, modelPin: null });
  });
});

describe("performanceVerdict", () => {
  it("passes runs at or under the baseline with no reasons or warnings", () => {
    const { reasons, warnings } = performanceVerdict({
      runs: [healthyRun("run-1.json"), healthyRun("run-2.json")],
      config: CONFIG,
    });
    expect(reasons).toEqual([]);
    expect(warnings).toEqual([]);
  });

  it("trips a run beyond the fail ratio of the duration baseline", () => {
    const { reasons } = performanceVerdict({
      runs: [{ ...healthyRun("run-1.json"), durationSeconds: 700 }],
      config: CONFIG,
    });
    expect(reasons.length).toBe(1);
    expect(reasons[0]).toContain("run-1.json");
    expect(reasons[0]).toContain("700");
    expect(reasons[0]).toContain("duration");
  });

  it("trips a run beyond the fail ratio of the token baseline", () => {
    const { reasons } = performanceVerdict({
      runs: [{ ...healthyRun("run-1.json"), outputTokens: 89000 }],
      config: CONFIG,
    });
    expect(reasons.length).toBe(1);
    expect(reasons[0]).toContain("output tokens");
  });

  it("warns between the warn and fail ratios instead of tripping", () => {
    const { reasons, warnings } = performanceVerdict({
      runs: [{ ...healthyRun("run-1.json"), durationSeconds: 520 }],
      config: CONFIG,
    });
    expect(reasons).toEqual([]);
    expect(warnings.length).toBe(1);
    expect(warnings[0]).toContain("duration");
  });

  it("warns, never trips, on spread beyond the ratio across runs", () => {
    const { reasons, warnings } = performanceVerdict({
      runs: [
        { ...healthyRun("run-1.json"), durationSeconds: 200, outputTokens: 20000 },
        { ...healthyRun("run-2.json"), durationSeconds: 390, outputTokens: 39000 },
      ],
      config: CONFIG,
    });
    expect(reasons).toEqual([]);
    expect(warnings.join(" ")).toContain("duration spread");
    expect(warnings.join(" ")).toContain("output-token spread");
  });

  it("warns when a metric is unmeasurable instead of silently skipping it", () => {
    const { reasons, warnings } = performanceVerdict({
      runs: [{ ...healthyRun("run-1.json"), outputTokens: null }],
      config: CONFIG,
    });
    expect(reasons).toEqual([]);
    expect(warnings.join(" ")).toContain("run-1.json");
  });

  it("does not warn spread against a zero floor", () => {
    const { warnings } = performanceVerdict({
      runs: [
        { ...healthyRun("run-1.json"), outputTokens: 0 },
        { ...healthyRun("run-2.json"), durationSeconds: 385 },
      ],
      config: CONFIG,
    });
    expect(warnings.join(" ")).not.toContain("spread");
  });

  it("trips a run whose prompt does not match the baseline, without grading its numbers against it", () => {
    const { reasons, warnings } = performanceVerdict({
      runs: [{ ...healthyRun("run-1.json"), promptSha256: "cd".repeat(32), durationSeconds: 4000 }],
      config: CONFIG,
    });
    expect(reasons.length).toBe(1);
    expect(reasons[0]).toContain("run-1.json");
    expect(reasons[0]).toContain("stale");
    expect(warnings).toEqual([]);
  });

  it("trips a run measured at a different model pin as stale", () => {
    const { reasons } = performanceVerdict({
      runs: [{ ...healthyRun("run-1.json"), modelPin: "us.anthropic.claude-sonnet-4-6" }],
      config: CONFIG,
    });
    expect(reasons.length).toBe(1);
    expect(reasons[0]).toContain("stale");
    expect(reasons[0]).toContain("model pin");
  });

  it("excludes a stale run from spread, so staleness is the only finding it feeds", () => {
    const { reasons, warnings } = performanceVerdict({
      runs: [
        healthyRun("run-1.json"),
        { ...healthyRun("run-2.json"), promptSha256: "cd".repeat(32), durationSeconds: 4000 },
      ],
      config: CONFIG,
    });
    expect(reasons.length).toBe(1);
    expect(warnings.join(" ")).not.toContain("spread");
  });

  it("warns on a run with no envelope identity and still grades its numbers", () => {
    const { reasons, warnings } = performanceVerdict({
      runs: [{ ...healthyRun("run-1.json"), promptSha256: null, modelPin: null, durationSeconds: 700 }],
      config: CONFIG,
    });
    expect(warnings.join(" ")).toContain("staleness unchecked");
    expect(reasons.length).toBe(1);
    expect(reasons[0]).toContain("duration");
  });

  it("warns on a partial envelope identity instead of half-checking it", () => {
    const { reasons, warnings } = performanceVerdict({
      runs: [{ ...healthyRun("run-1.json"), modelPin: null }],
      config: CONFIG,
    });
    expect(reasons).toEqual([]);
    expect(warnings.join(" ")).toContain("staleness");
  });

  it("does not compute spread from a single measurable run", () => {
    const { warnings } = performanceVerdict({
      runs: [
        healthyRun("run-1.json"),
        { ...healthyRun("run-2.json"), durationSeconds: null, outputTokens: null },
      ],
      config: CONFIG,
    });
    expect(warnings.join(" ")).not.toContain("spread");
  });
});
