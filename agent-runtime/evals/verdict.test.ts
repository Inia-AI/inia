import { describe, expect, it } from "bun:test";
import type { GradeReport } from "./grader.js";
import { evalVerdict } from "./verdict.js";

const ANCHOR_FIELDS = ["totalAssets", "totalLiabilities"];

function cleanReport(): GradeReport {
  return {
    fixture: "mini",
    fields: [
      {
        field: "totalAssets",
        period: "FY0",
        kind: "anchor",
        state: "correct",
        accepted: [100],
        actual: 100,
        matchedBasisId: null,
        compositionMismatch: false,
      },
    ],
    footings: [
      {
        name: "totalAssets=totalLiabilities+totalEquity",
        period: "FY0",
        computed: 100,
        reported: 100,
        ok: true,
      },
    ],
    summary: { correct: 1, wrong: 0, miss: 0, hallucination: 0 },
  };
}

function runOf(overrides: Partial<Parameters<typeof evalVerdict>[0]["runs"][number]> = {}) {
  return { file: "run-1.json", report: cleanReport(), citationMisses: [], ...overrides };
}

describe("evalVerdict", () => {
  it("passes a clean set of runs with no variance", () => {
    const verdict = evalVerdict({
      runs: [runOf(), runOf({ file: "run-2.json" })],
      variance: [],
      anchorFields: ANCHOR_FIELDS,
    });
    expect(verdict.tripped).toBe(false);
    expect(verdict.reasons).toEqual([]);
  });

  it("trips when any run has a wrong, missed, or hallucinated anchor", () => {
    const report = cleanReport();
    const anchor = report.fields[0];
    if (anchor === undefined) {
      throw new Error("fixture field missing");
    }
    anchor.state = "wrong";
    const verdict = evalVerdict({ runs: [runOf({ report })], variance: [], anchorFields: ANCHOR_FIELDS });
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons.join(" ")).toContain("run-1.json");
  });

  it("trips on a hallucinated unexpected field", () => {
    const report = cleanReport();
    report.fields.push({
      field: "invented",
      period: "FY0",
      kind: "unexpected",
      state: "hallucination",
      accepted: [null],
      actual: 1,
      matchedBasisId: null,
      compositionMismatch: false,
    });
    const verdict = evalVerdict({ runs: [runOf({ report })], variance: [], anchorFields: ANCHOR_FIELDS });
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons.join(" ")).toContain("hallucination");
  });

  it("trips when a judgment value matches no accepted basis", () => {
    const report = cleanReport();
    report.fields.push({
      field: "shortTermDebt",
      period: "FY0",
      kind: "judgment",
      state: "wrong",
      accepted: [20],
      actual: 21,
      matchedBasisId: null,
      compositionMismatch: false,
    });
    const verdict = evalVerdict({ runs: [runOf({ report })], variance: [], anchorFields: ANCHOR_FIELDS });
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons.join(" ")).toContain("shortTermDebt");
  });

  it("warns without tripping when a correct judgment value carries a mismatched composition", () => {
    const report = cleanReport();
    report.fields.push({
      field: "shortTermDebt",
      period: "FY0",
      kind: "judgment",
      state: "correct",
      accepted: [20],
      actual: 20,
      matchedBasisId: "run1",
      compositionMismatch: true,
    });
    const verdict = evalVerdict({ runs: [runOf({ report })], variance: [], anchorFields: ANCHOR_FIELDS });
    expect(verdict.tripped).toBe(false);
    expect(verdict.reasons).toEqual([]);
    expect(verdict.warnings.join(" ")).toContain("composition");
    expect(verdict.warnings.join(" ")).toContain("shortTermDebt");
  });

  it("trips when any recomputed footing is broken", () => {
    const report = cleanReport();
    const footing = report.footings[0];
    if (footing === undefined) {
      throw new Error("fixture footing missing");
    }
    footing.ok = false;
    const verdict = evalVerdict({ runs: [runOf({ report })], variance: [], anchorFields: ANCHOR_FIELDS });
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons.join(" ")).toContain("footing");
  });

  it("trips on an ungradeable run", () => {
    const verdict = evalVerdict({
      runs: [runOf({ report: null })],
      variance: [],
      anchorFields: ANCHOR_FIELDS,
    });
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons.join(" ")).toContain("ungradeable");
  });

  it("trips on between-run variance of an anchor field even when every run passes", () => {
    const verdict = evalVerdict({
      runs: [runOf(), runOf({ file: "run-2.json" })],
      variance: [{ field: "totalAssets", period: "FY0", valuesSeen: [100, 99] }],
      anchorFields: ANCHOR_FIELDS,
    });
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons.join(" ")).toContain("totalAssets");
  });

  it("warns without tripping on judgment-field variance between accepted bases", () => {
    const verdict = evalVerdict({
      runs: [runOf(), runOf({ file: "run-2.json" })],
      variance: [{ field: "shortTermDebt", period: "FY0", valuesSeen: [20, 18] }],
      anchorFields: ANCHOR_FIELDS,
    });
    expect(verdict.tripped).toBe(false);
    expect(verdict.warnings.join(" ")).toContain("shortTermDebt");
  });

  it("warns without tripping when a run's citations are not found as printed", () => {
    const verdict = evalVerdict({
      runs: [runOf({ citationMisses: ["totalAssets"] })],
      variance: [],
      anchorFields: ANCHOR_FIELDS,
    });
    expect(verdict.tripped).toBe(false);
    expect(verdict.reasons).toEqual([]);
    expect(verdict.warnings.join(" ")).toContain("citation");
  });

  it("trips on a performance regression even when every field is correct", () => {
    const verdict = evalVerdict({
      runs: [runOf()],
      variance: [],
      anchorFields: ANCHOR_FIELDS,
      performance: {
        config: {
          baseline: {
            p75DurationSeconds: 400,
            p75OutputTokens: 40000,
            promptSha256: "ab".repeat(32),
            modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
            sdkVersion: "0.3.227",
            runtimeVersion: "0.2.8",
            measuredAt: "2026-08-11",
          },
          failAboveBaselineRatio: 1.5,
          warnAboveBaselineRatio: 1.2,
          spreadWarnRatio: 1.6,
        },
        runs: [
          {
            file: "run-1.json",
            durationSeconds: 966,
            outputTokens: 39000,
            promptSha256: "ab".repeat(32),
            modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
          },
        ],
      },
    });
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons.join(" ")).toContain("duration");
  });

  it("trips when a graded run's identity does not match the stamped performance baseline", () => {
    const verdict = evalVerdict({
      runs: [runOf()],
      variance: [],
      anchorFields: ANCHOR_FIELDS,
      performance: {
        config: {
          baseline: {
            p75DurationSeconds: 400,
            p75OutputTokens: 40000,
            promptSha256: "ab".repeat(32),
            modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
            sdkVersion: "0.3.227",
            runtimeVersion: "0.2.8",
            measuredAt: "2026-08-11",
          },
          failAboveBaselineRatio: 1.5,
          warnAboveBaselineRatio: 1.2,
          spreadWarnRatio: 1.6,
        },
        runs: [
          {
            file: "run-1.json",
            durationSeconds: 380,
            outputTokens: 39000,
            promptSha256: "cd".repeat(32),
            modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
          },
        ],
      },
    });
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons.join(" ")).toContain("stale");
  });

  it("surfaces performance spread as a warning without tripping", () => {
    const verdict = evalVerdict({
      runs: [runOf(), runOf({ file: "run-2.json" })],
      variance: [],
      anchorFields: ANCHOR_FIELDS,
      performance: {
        config: {
          baseline: {
            p75DurationSeconds: 400,
            p75OutputTokens: 40000,
            promptSha256: "ab".repeat(32),
            modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
            sdkVersion: "0.3.227",
            runtimeVersion: "0.2.8",
            measuredAt: "2026-08-11",
          },
          failAboveBaselineRatio: 1.5,
          warnAboveBaselineRatio: 1.2,
          spreadWarnRatio: 1.6,
        },
        runs: [
          {
            file: "run-1.json",
            durationSeconds: 200,
            outputTokens: 20000,
            promptSha256: "ab".repeat(32),
            modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
          },
          {
            file: "run-2.json",
            durationSeconds: 390,
            outputTokens: 39000,
            promptSha256: "ab".repeat(32),
            modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
          },
        ],
      },
    });
    expect(verdict.tripped).toBe(false);
    expect(verdict.warnings.join(" ")).toContain("duration spread");
  });
});
