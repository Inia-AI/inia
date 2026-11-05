import { describe, expect, it } from "bun:test";
import { type DriftBaseline, stampDriftBaseline } from "./baseline.js";
import type { DriftRunRecord } from "./record.js";
import { skippedDriftRecord } from "./record.js";
import { TEST_MODEL_PIN, type TestRecordOverrides, testRecordSet } from "./test-records.js";
import { DRIFT_REPORT_MARKER, type DriftVerdict, driftVerdict, renderDriftReport } from "./verdict.js";

const PROMPT_IDS = ["bash-loop", "pdf-read", "subagent"];
const SPEND_CAP_USD = 0.1;
const LOAD_BEARING: Record<string, readonly string[]> = {
  "bash-loop": ["Bash"],
  "pdf-read": ["Read"],
  subagent: ["Task", "Agent"],
};

const BASELINE: DriftBaseline = stampDriftBaseline({
  records: [1, 2, 3, 4, 5, 6].flatMap((run) =>
    testRecordSet(PROMPT_IDS, 2, (promptId, repeat) => ({
      executionId: `cal-${run}-${promptId}-${repeat}`,
      wallMs: 20_000 + run * 500,
    })),
  ),
  runtimeVersion: "0.3.1011",
  stampedAt: "2026-09-04",
  calibrationRuns: 6,
});

function verdictOver(
  overrides: (promptId: string, repeat: number) => TestRecordOverrides = () => ({}),
  extra: { records?: DriftRunRecord[]; baseline?: DriftBaseline } = {},
): DriftVerdict {
  const records = [...testRecordSet(PROMPT_IDS, 2, overrides), ...(extra.records ?? [])];
  return driftVerdict({
    baseline: extra.baseline ?? BASELINE,
    records,
    spendCapUsd: SPEND_CAP_USD,
    loadBearingTools: LOAD_BEARING,
  });
}

function only(promptId: string, patch: TestRecordOverrides, repeats: number[] = [1, 2]) {
  return (id: string, repeat: number): TestRecordOverrides =>
    id === promptId && repeats.includes(repeat) ? patch : {};
}

describe("driftVerdict — outcomes", () => {
  it("is quiet over a run set that matches its baseline", () => {
    const verdict = verdictOver();
    expect(verdict.tripped).toBe(false);
    expect(verdict.reasons).toEqual([]);
    expect(verdict.warnings).toEqual([]);
  });

  it("fails a wrong answer, a failed run and a skipped run, naming each", () => {
    const verdict = verdictOver(
      (promptId, repeat) => {
        if (promptId === "bash-loop" && repeat === 1) {
          return { outcome: "wrong", detail: '{"answer":340}' };
        }
        if (promptId === "pdf-read" && repeat === 2) {
          return { outcome: "failed", detail: "cli_version_drift: 2.1.260" };
        }
        return {};
      },
      { records: [skippedDriftRecord("subagent", 3, "spend cap reached")] },
    );
    expect(verdict.tripped).toBe(true);
    expect(verdict.reasons).toEqual(
      expect.arrayContaining([
        expect.stringMatching(/bash-loop #1: wrong answer.*340/),
        expect.stringMatching(/pdf-read #2: run failed.*cli_version_drift/),
        expect.stringMatching(/subagent #3: skipped.*spend cap/),
      ]),
    );
  });

  it("fails the set when the summed spend passes the cap", () => {
    const verdict = verdictOver(() => ({ estimatedCostUsd: 0.02 }));
    expect(verdict.reasons).toEqual([
      expect.stringMatching(/run-set spend \$0\.1200 exceeds the \$0\.10 cap/),
    ]);
  });

  it("fails a run that never called its load-bearing tool, even with the right answer", () => {
    const verdict = verdictOver(only("subagent", { toolCalls: { Bash: 1, StructuredOutput: 1 } }, [2]));
    expect(verdict.reasons).toEqual([expect.stringMatching(/subagent #2: .*Task.*Agent.*never called/)]);
    expect(verdictOver(only("subagent", { toolCalls: { Agent: 1, StructuredOutput: 1 } })).reasons).toEqual(
      [],
    );
  });

  it("fails a run that hit the turn fuse", () => {
    const verdict = verdictOver(only("bash-loop", { maxTurnsHit: true, numTurns: 10 }, [1]));
    expect(verdict.reasons).toEqual(
      expect.arrayContaining([expect.stringMatching(/bash-loop #1: hit maxTurns/)]),
    );
  });
});

describe("driftVerdict — staleness", () => {
  it("fails loud as baseline stale on a prompt sha mismatch and excludes that prompt from the bands", () => {
    const verdict = verdictOver(
      only("bash-loop", { identity: { promptSha256: "ff".repeat(32) }, wallMs: 90_000 }),
    );
    expect(verdict.reasons).toEqual([
      expect.stringMatching(/bash-loop: prompt sha256 differs.*baseline stale/),
    ]);
    expect(verdict.reasons.some((reason) => /wall time/.test(reason))).toBe(false);
  });

  it("treats a staged-input digest or model pin mismatch the same way", () => {
    expect(
      verdictOver(only("pdf-read", { identity: { stagedInputsSha256: "ee".repeat(32) } })).reasons,
    ).toEqual([expect.stringMatching(/pdf-read: staged inputs sha256 differs.*baseline stale/)]);
    expect(
      verdictOver(only("subagent", { identity: { modelPin: "us.anthropic.claude-sonnet-5" } })).reasons,
    ).toEqual([expect.stringMatching(/subagent: model pin differs.*baseline stale/)]);
  });

  it("fails a prompt the baseline never stamped", () => {
    const stamped = stampDriftBaseline({
      records: testRecordSet(["bash-loop", "pdf-read"], 2),
      runtimeVersion: "x",
      stampedAt: "d",
      calibrationRuns: 1,
    });
    const verdict = verdictOver(() => ({}), { baseline: stamped });
    expect(verdict.reasons).toEqual([expect.stringMatching(/subagent: no stamped baseline/)]);
  });

  it("warns, not fails, when a run's identity is unreadable", () => {
    const verdict = verdictOver(only("bash-loop", { identity: { promptSha256: null } }, [1]));
    expect(verdict.reasons).toEqual([]);
    expect(verdict.warnings).toEqual([expect.stringMatching(/bash-loop #1: trace identity missing/)]);
  });

  it("notes a moved SDK, CLI or runtime version as provenance, never as a finding", () => {
    const verdict = verdictOver(() => ({ identity: { sdkVersion: "0.3.260", cliVersion: "2.1.260" } }));
    expect(verdict.reasons).toEqual([]);
    expect(verdict.warnings).toEqual([]);
    expect(verdict.notes).toEqual(
      expect.arrayContaining([
        expect.stringMatching(/SDK 0\.3\.251 → 0\.3\.260/),
        expect.stringMatching(/CLI 2\.1\.251 → 2\.1\.260/),
      ]),
    );
  });
});

describe("driftVerdict — bands", () => {
  it("compares wall time on the min of the repeats: one slow repeat is noise, two are drift", () => {
    expect(verdictOver(only("bash-loop", { wallMs: 90_000 }, [1])).warnings).toEqual([]);
    expect(verdictOver(only("bash-loop", { wallMs: 36_000 })).warnings).toEqual([
      expect.stringMatching(/bash-loop: wall time min 36000ms is above 1\.5x baseline median 21750ms/),
    ]);
    const failing = verdictOver(only("bash-loop", { wallMs: 50_000 }));
    expect(failing.reasons).toEqual([
      expect.stringMatching(/bash-loop: wall time min 50000ms exceeds 2x baseline median 21750ms/),
    ]);
    expect(failing.warnings).toEqual([]);
  });

  it("compares context tokens on the median, warning outside 0.75–1.25x and failing above 1.5x", () => {
    expect(verdictOver(only("pdf-read", { tokens: { cacheRead: 13_000 } })).warnings).toEqual([
      expect.stringMatching(
        /pdf-read: context tokens median 16400 is outside 0\.75–1\.25x baseline median 12400/,
      ),
    ]);
    expect(verdictOver(only("pdf-read", { tokens: { cacheRead: 4000 } })).warnings).toEqual([
      expect.stringMatching(/pdf-read: context tokens median 7400 is outside/),
    ]);
    expect(verdictOver(only("pdf-read", { tokens: { cacheRead: 16_000 } })).reasons).toEqual([
      expect.stringMatching(/pdf-read: context tokens median 19400 exceeds 1\.5x baseline median 12400/),
    ]);
  });

  it("compares output tokens on the median, warning outside 0.5–1.5x and failing above 2x", () => {
    expect(verdictOver(only("subagent", { tokens: { output: 480 } })).warnings).toEqual([
      expect.stringMatching(/subagent: output tokens median 480 is outside 0\.5–1\.5x baseline median 300/),
    ]);
    expect(verdictOver(only("subagent", { tokens: { output: 700 } })).reasons).toEqual([
      expect.stringMatching(/subagent: output tokens median 700 exceeds 2x baseline median 300/),
    ]);
  });

  it("compares turns on the median: 2 away warns, more than 4 above fails", () => {
    expect(verdictOver(only("bash-loop", { numTurns: 5 })).warnings).toEqual([]);
    expect(verdictOver(only("bash-loop", { numTurns: 6 })).warnings).toEqual([
      expect.stringMatching(/bash-loop: turns median 6 differs from baseline median 4 by 2/),
    ]);
    expect(verdictOver(only("bash-loop", { numTurns: 2 })).warnings).toEqual([
      expect.stringMatching(/bash-loop: turns median 2 differs from baseline median 4 by 2/),
    ]);
    expect(verdictOver(only("bash-loop", { numTurns: 9 })).reasons).toEqual([
      expect.stringMatching(/bash-loop: turns median 9 exceeds baseline median 4 by more than 4/),
    ]);
  });

  it("warns when a tool's median call count drifts by more than 1, including a tool the baseline never saw", () => {
    expect(verdictOver(only("bash-loop", { toolCalls: { Bash: 4, StructuredOutput: 1 } })).warnings).toEqual(
      [],
    );
    expect(verdictOver(only("bash-loop", { toolCalls: { Bash: 5, StructuredOutput: 1 } })).warnings).toEqual([
      expect.stringMatching(/bash-loop: Bash calls median 5 vs baseline median 3/),
    ]);
    expect(
      verdictOver(only("bash-loop", { toolCalls: { Bash: 3, Read: 2, StructuredOutput: 1 } })).warnings,
    ).toEqual([expect.stringMatching(/bash-loop: Read calls median 2 vs baseline median 0/)]);
  });

  it("warns when the SDK-to-floor cost ratio moves more than 10% off the stamped ratio", () => {
    expect(verdictOver(only("bash-loop", { sdkCostUsd: 0.0128 })).warnings).toEqual([]);
    expect(verdictOver(only("bash-loop", { sdkCostUsd: 0.0135 })).warnings).toEqual([
      expect.stringMatching(
        /bash-loop: SDK cost \/ usage floor median 1\.35 moved 12\.5% off the stamped 1\.20/,
      ),
    ]);
  });

  it("warns above 1.5x and fails above 2x the stamped median cost", () => {
    expect(verdictOver(only("subagent", { estimatedCostUsd: 0.019 })).warnings).toEqual([
      expect.stringMatching(/subagent: cost median \$0\.0190 is above 1\.5x baseline median \$0\.0120/),
    ]);
    expect(verdictOver(only("subagent", { estimatedCostUsd: 0.025 })).reasons).toEqual([
      expect.stringMatching(/subagent: cost median \$0\.0250 exceeds 2x baseline median \$0\.0120/),
    ]);
  });

  it("keeps a failed run out of the bands — its failure is the finding", () => {
    const verdict = verdictOver(
      only("bash-loop", { outcome: "failed", detail: "boom", wallMs: 90_000, numTurns: 10 }),
    );
    expect(verdict.reasons).toEqual([
      expect.stringMatching(/bash-loop #1: run failed/),
      expect.stringMatching(/bash-loop #2: run failed/),
    ]);
    expect(verdict.warnings).toEqual([]);
  });

  it("warns when a band's statistic is unmeasurable rather than comparing against nothing", () => {
    const verdict = verdictOver(only("bash-loop", { wallMs: null }));
    expect(verdict.reasons).toEqual([]);
    expect(verdict.warnings).toEqual([expect.stringMatching(/bash-loop: wall time unmeasurable/)]);
  });
});

describe("renderDriftReport", () => {
  it("renders a sticky-comment body with the marker, the verdict, one table per prompt and the findings", () => {
    const records = testRecordSet(PROMPT_IDS, 2, only("bash-loop", { wallMs: 36_000 }));
    const verdict = driftVerdict({
      baseline: BASELINE,
      records,
      spendCapUsd: SPEND_CAP_USD,
      loadBearingTools: LOAD_BEARING,
    });
    const report = renderDriftReport({ baseline: BASELINE, records, verdict, spendCapUsd: SPEND_CAP_USD });
    expect(report.startsWith(DRIFT_REPORT_MARKER)).toBe(true);
    expect(report).toContain("SDK drift runs: WARN");
    expect(report).toContain("| wall time (ms, min of repeats) |");
    expect(report).toContain("### bash-loop");
    expect(report).toContain("### subagent");
    expect(report).toContain("36000");
    expect(report).toContain("above 1.5x");
    expect(report).toContain(TEST_MODEL_PIN);
    expect(report).toContain("0.3.1011");
  });

  it("says PASS when nothing is found and FAIL when tripped", () => {
    const quiet = testRecordSet(PROMPT_IDS, 2);
    const quietVerdict = driftVerdict({
      baseline: BASELINE,
      records: quiet,
      spendCapUsd: SPEND_CAP_USD,
      loadBearingTools: LOAD_BEARING,
    });
    expect(
      renderDriftReport({
        baseline: BASELINE,
        records: quiet,
        verdict: quietVerdict,
        spendCapUsd: SPEND_CAP_USD,
      }),
    ).toContain("SDK drift runs: PASS");
    const broken = testRecordSet(PROMPT_IDS, 2, only("pdf-read", { outcome: "wrong", detail: "x" }, [1]));
    const brokenVerdict = driftVerdict({
      baseline: BASELINE,
      records: broken,
      spendCapUsd: SPEND_CAP_USD,
      loadBearingTools: LOAD_BEARING,
    });
    expect(
      renderDriftReport({
        baseline: BASELINE,
        records: broken,
        verdict: brokenVerdict,
        spendCapUsd: SPEND_CAP_USD,
      }),
    ).toContain("SDK drift runs: FAIL");
  });
});
