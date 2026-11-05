import { isRecord } from "../src/narrow.js";

export type PerformanceBaseline = {
  p75DurationSeconds: number;
  p75OutputTokens: number;
  promptSha256: string;
  modelPin: string;
  sdkVersion: string;
  runtimeVersion: string;
  measuredAt: string;
};

export type PerformanceConfig = {
  baseline: PerformanceBaseline;
  failAboveBaselineRatio: number;
  warnAboveBaselineRatio: number;
  spreadWarnRatio: number;
};

export const DEFAULT_FAIL_ABOVE_BASELINE_RATIO = 1.5;
export const DEFAULT_WARN_ABOVE_BASELINE_RATIO = 1.2;
export const DEFAULT_SPREAD_WARN_RATIO = 1.6;

export type RunPerformanceMetrics = {
  file: string;
  durationSeconds: number | null;
  outputTokens: number | null;
  promptSha256: string | null;
  modelPin: string | null;
};

export type RunTraceIdentity = { promptSha256: string | null; modelPin: string | null };

export function runTraceIdentityOf(packText: string): RunTraceIdentity {
  for (const line of packText.split("\n")) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(line);
    } catch {
      continue;
    }
    if (!isRecord(parsed) || parsed["type"] !== "envelope") {
      continue;
    }
    return {
      promptSha256: typeof parsed["promptSha256"] === "string" ? parsed["promptSha256"] : null,
      modelPin: typeof parsed["modelPin"] === "string" ? parsed["modelPin"] : null,
    };
  }
  return { promptSha256: null, modelPin: null };
}

export type PerformanceVerdictInput = {
  runs: RunPerformanceMetrics[];
  config: PerformanceConfig;
};

export type PerformanceFindings = {
  reasons: string[];
  warnings: string[];
};

export function durationSecondsBetween(startedAt: string, finishedAt: string): number | null {
  const started = Date.parse(startedAt);
  const finished = Date.parse(finishedAt);
  if (Number.isNaN(started) || Number.isNaN(finished) || finished <= started) {
    return null;
  }
  return Math.ceil((finished - started) / 1000);
}

export function sumOutputTokens(packText: string): number | null {
  let total = 0;
  let sawUsage = false;
  for (const line of packText.split("\n")) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(line);
    } catch {
      continue;
    }
    if (typeof parsed !== "object" || parsed === null) {
      continue;
    }
    if (!("type" in parsed) || parsed.type !== "usage" || !("usage" in parsed)) {
      continue;
    }
    const usage: unknown = parsed.usage;
    if (typeof usage !== "object" || usage === null || !("output_tokens" in usage)) {
      continue;
    }
    if (typeof usage.output_tokens === "number") {
      total += usage.output_tokens;
      sawUsage = true;
    }
  }
  return sawUsage ? total : null;
}

type MetricName = "duration" | "output tokens";

function baselineLabel(baseline: PerformanceBaseline): string {
  return `${baseline.modelPin}, ${baseline.sdkVersion}/${baseline.runtimeVersion} @ ${baseline.measuredAt}`;
}

function staleRunFiles(
  runs: RunPerformanceMetrics[],
  baseline: PerformanceBaseline,
  reasons: string[],
  warnings: string[],
): Set<string> {
  const stale = new Set<string>();
  for (const run of runs) {
    const mismatches: string[] = [];
    if (run.promptSha256 !== null && run.promptSha256 !== baseline.promptSha256) {
      mismatches.push("prompt sha256");
    }
    if (run.modelPin !== null && run.modelPin !== baseline.modelPin) {
      mismatches.push("model pin");
    }
    if (mismatches.length > 0) {
      stale.add(run.file);
      const verb = mismatches.length > 1 ? "differ" : "differs";
      reasons.push(
        `${run.file}: ${mismatches.join(" and ")} ${verb} from the stamped baseline ` +
          `(${baselineLabel(baseline)}) — baseline stale; a human re-stamps expected.json ` +
          "from a fresh calibration batch",
      );
    } else if (run.promptSha256 === null || run.modelPin === null) {
      warnings.push(
        `${run.file}: trace envelope identity missing or partial — baseline staleness unchecked for it`,
      );
    }
  }
  return stale;
}

export function performanceVerdict(input: PerformanceVerdictInput): PerformanceFindings {
  const { baseline, failAboveBaselineRatio, warnAboveBaselineRatio, spreadWarnRatio } = input.config;
  const reasons: string[] = [];
  const warnings: string[] = [];
  const staleFiles = staleRunFiles(input.runs, baseline, reasons, warnings);
  const freshRuns = input.runs.filter((run) => !staleFiles.has(run.file));
  const gates: {
    name: MetricName;
    baselineValue: number;
    unit: string;
    of: (run: RunPerformanceMetrics) => number | null;
  }[] = [
    {
      name: "duration",
      baselineValue: baseline.p75DurationSeconds,
      unit: "s",
      of: (run) => run.durationSeconds,
    },
    {
      name: "output tokens",
      baselineValue: baseline.p75OutputTokens,
      unit: "",
      of: (run) => run.outputTokens,
    },
  ];
  for (const gate of gates) {
    const measured: number[] = [];
    for (const run of freshRuns) {
      const value = gate.of(run);
      if (value === null) {
        warnings.push(`${run.file}: ${gate.name} unmeasurable — performance gate skipped for it`);
        continue;
      }
      measured.push(value);
      if (value > gate.baselineValue * failAboveBaselineRatio) {
        reasons.push(
          `${run.file}: ${gate.name} ${value}${gate.unit} exceeds ${failAboveBaselineRatio}x baseline ` +
            `p75 ${gate.baselineValue}${gate.unit} (${baselineLabel(baseline)})`,
        );
      } else if (value > gate.baselineValue * warnAboveBaselineRatio) {
        warnings.push(
          `${run.file}: ${gate.name} ${value}${gate.unit} above ${warnAboveBaselineRatio}x baseline ` +
            `p75 ${gate.baselineValue}${gate.unit}`,
        );
      }
    }
    if (measured.length >= 2) {
      const max = Math.max(...measured);
      const min = Math.min(...measured);
      if (min > 0 && max > min * spreadWarnRatio) {
        const label = gate.name === "duration" ? "duration spread" : "output-token spread";
        warnings.push(
          `${label} across runs (${min}${gate.unit}..${max}${gate.unit}) exceeds ${spreadWarnRatio}x`,
        );
      }
    }
  }
  return { reasons, warnings };
}
