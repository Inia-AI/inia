import {
  type DriftBaseline,
  type DriftMetricName,
  type DriftPromptBaseline,
  metricValueOf,
} from "./baseline.js";
import type { DriftRunRecord } from "./record.js";
import { medianOf } from "./stats.js";

export const DRIFT_REPORT_MARKER = "<!-- sdk-drift -->";

export const WALL_WARN_RATIO = 1.5;
export const WALL_FAIL_RATIO = 2;
export const CONTEXT_WARN_LOW_RATIO = 0.75;
export const CONTEXT_WARN_HIGH_RATIO = 1.25;
export const CONTEXT_FAIL_RATIO = 1.5;
export const OUTPUT_WARN_LOW_RATIO = 0.5;
export const OUTPUT_WARN_HIGH_RATIO = 1.5;
export const OUTPUT_FAIL_RATIO = 2;
export const TURNS_WARN_DELTA = 2;
export const TURNS_FAIL_DELTA = 4;
export const TOOL_CALLS_WARN_DELTA = 1;
export const COST_RATIO_WARN_MOVE = 0.1;
export const COST_WARN_RATIO = 1.5;
export const COST_FAIL_RATIO = 2;

export type BandLevel = "ok" | "warn" | "fail" | "skipped";

export type BandResult = {
  metric: DriftMetricName;
  statistic: number | null;
  baselineMedian: number | null;
  ratio: number | null;
  level: BandLevel;
  label: string;
};

export type DriftVerdict = {
  tripped: boolean;
  reasons: string[];
  warnings: string[];
  notes: string[];
  bands: Record<string, BandResult[]>;
};

export type DriftVerdictInput = {
  baseline: DriftBaseline;
  records: readonly DriftRunRecord[];
  spendCapUsd: number;
  loadBearingTools: Record<string, readonly string[]>;
};

const RESTAMP_HINT =
  "baseline stale; a human re-stamps evals/drift/baseline.json from a fresh calibration set";

function formatNumber(value: number): string {
  return String(Math.round(value * 100) / 100);
}

function formatUsd(value: number): string {
  return `$${value.toFixed(4)}`;
}

function formatRatio(value: number): string {
  return `${value.toFixed(2)}x`;
}

function runLabel(record: DriftRunRecord): string {
  return `${record.promptId} #${record.repeat}`;
}

function baselineLabel(baseline: DriftBaseline): string {
  return `${baseline.modelPin}, SDK ${baseline.sdkVersion} / CLI ${baseline.cliVersion} / runtime ${baseline.runtimeVersion} @ ${baseline.stampedAt}`;
}

function outcomeReasons(
  records: readonly DriftRunRecord[],
  loadBearing: Record<string, readonly string[]>,
): string[] {
  const reasons: string[] = [];
  for (const record of records) {
    const label = runLabel(record);
    switch (record.outcome) {
      case "wrong":
        reasons.push(`${label}: wrong answer — ${record.detail}`);
        break;
      case "failed":
        reasons.push(`${label}: run failed — ${record.detail}`);
        break;
      case "skipped":
        reasons.push(`${label}: skipped — ${record.detail}`);
        break;
      default:
        break;
    }
    if (record.outcome === "skipped") {
      continue;
    }
    if (record.maxTurnsHit) {
      reasons.push(`${label}: hit maxTurns (${record.numTurns ?? "?"} turns)`);
    }
    const tools = loadBearing[record.promptId] ?? [];
    if (tools.length > 0 && !tools.some((tool) => (record.toolCalls[tool] ?? 0) > 0)) {
      reasons.push(`${label}: load-bearing tool ${tools.join("/")} never called`);
    }
  }
  return reasons;
}

function identityComplete(record: DriftRunRecord): boolean {
  return record.identity.promptSha256 !== null && record.identity.modelPin !== null;
}

type Freshness = { fresh: DriftRunRecord[]; reasons: string[]; warnings: string[] };

function freshRecordsOf(
  promptId: string,
  records: readonly DriftRunRecord[],
  baseline: DriftBaseline,
  stamped: DriftPromptBaseline,
): Freshness {
  const reasons: string[] = [];
  const warnings: string[] = [];
  const mismatches = new Set<string>();
  const fresh: DriftRunRecord[] = [];
  for (const record of records) {
    if (!identityComplete(record)) {
      warnings.push(`${runLabel(record)}: trace identity missing or partial — excluded from the bands`);
      continue;
    }
    if (record.identity.promptSha256 !== stamped.promptSha256) {
      mismatches.add("prompt sha256");
    }
    if (record.identity.stagedInputsSha256 !== stamped.stagedInputsSha256) {
      mismatches.add("staged inputs sha256");
    }
    if (record.identity.modelPin !== baseline.modelPin) {
      mismatches.add("model pin");
    }
    fresh.push(record);
  }
  if (mismatches.size > 0) {
    reasons.push(
      `${promptId}: ${[...mismatches].join(" and ")} differs from the stamped baseline (${baselineLabel(baseline)}) — ${RESTAMP_HINT}`,
    );
    return { fresh: [], reasons, warnings };
  }
  return { fresh, reasons, warnings };
}

type BandSpec = {
  metric: DriftMetricName;
  name: string;
  statistic: "min" | "median";
  format: (value: number) => string;
};

const RATIO_BANDS: readonly (BandSpec & { warnLow?: number; warnHigh: number; fail: number })[] = [
  {
    metric: "wallMs",
    name: "wall time",
    statistic: "min",
    format: (v) => `${formatNumber(v)}ms`,
    warnHigh: WALL_WARN_RATIO,
    fail: WALL_FAIL_RATIO,
  },
  {
    metric: "contextTokens",
    name: "context tokens",
    statistic: "median",
    format: formatNumber,
    warnLow: CONTEXT_WARN_LOW_RATIO,
    warnHigh: CONTEXT_WARN_HIGH_RATIO,
    fail: CONTEXT_FAIL_RATIO,
  },
  {
    metric: "outputTokens",
    name: "output tokens",
    statistic: "median",
    format: formatNumber,
    warnLow: OUTPUT_WARN_LOW_RATIO,
    warnHigh: OUTPUT_WARN_HIGH_RATIO,
    fail: OUTPUT_FAIL_RATIO,
  },
  {
    metric: "estimatedCostUsd",
    name: "cost",
    statistic: "median",
    format: formatUsd,
    warnHigh: COST_WARN_RATIO,
    fail: COST_FAIL_RATIO,
  },
];

function statisticOf(
  records: readonly DriftRunRecord[],
  metric: DriftMetricName,
  which: "min" | "median",
): number | null {
  const values = records.map((record) => metricValueOf(record, metric)).filter((value) => value !== null);
  if (values.length === 0) {
    return null;
  }
  return which === "min" ? Math.min(...values) : medianOf(values);
}

type BandOutcome = { result: BandResult; reason?: string; warning?: string };

function unmeasurable(spec: { metric: DriftMetricName; name: string }, promptId: string): BandOutcome {
  return {
    result: {
      metric: spec.metric,
      statistic: null,
      baselineMedian: null,
      ratio: null,
      level: "skipped",
      label: "unmeasurable",
    },
    warning: `${promptId}: ${spec.name} unmeasurable — band skipped`,
  };
}

function ratioBand(
  promptId: string,
  records: readonly DriftRunRecord[],
  stamped: DriftPromptBaseline,
  spec: (typeof RATIO_BANDS)[number],
): BandOutcome {
  const statistic = statisticOf(records, spec.metric, spec.statistic);
  const baselineMedian = stamped.metrics[spec.metric]?.median ?? null;
  if (statistic === null || baselineMedian === null || baselineMedian <= 0) {
    return unmeasurable(spec, promptId);
  }
  const ratio = statistic / baselineMedian;
  const base = { metric: spec.metric, statistic, baselineMedian, ratio };
  const described = `${promptId}: ${spec.name} ${spec.statistic} ${spec.format(statistic)}`;
  const reference = `baseline median ${spec.format(baselineMedian)}`;
  if (ratio > spec.fail) {
    return {
      result: { ...base, level: "fail", label: `exceeds ${spec.fail}x` },
      reason: `${described} exceeds ${spec.fail}x ${reference}`,
    };
  }
  if (ratio > spec.warnHigh) {
    return {
      result: { ...base, level: "warn", label: `above ${spec.warnHigh}x` },
      warning: `${described} is ${spec.warnLow === undefined ? `above ${spec.warnHigh}x` : `outside ${spec.warnLow}–${spec.warnHigh}x`} ${reference}`,
    };
  }
  if (spec.warnLow !== undefined && ratio < spec.warnLow) {
    return {
      result: { ...base, level: "warn", label: `below ${spec.warnLow}x` },
      warning: `${described} is outside ${spec.warnLow}–${spec.warnHigh}x ${reference}`,
    };
  }
  return { result: { ...base, level: "ok", label: "ok" } };
}

function turnsBand(
  promptId: string,
  records: readonly DriftRunRecord[],
  stamped: DriftPromptBaseline,
): BandOutcome {
  const spec = { metric: "numTurns" as const, name: "turns" };
  const statistic = statisticOf(records, spec.metric, "median");
  const baselineMedian = stamped.metrics.numTurns?.median ?? null;
  if (statistic === null || baselineMedian === null) {
    return unmeasurable(spec, promptId);
  }
  const delta = statistic - baselineMedian;
  const base = {
    metric: spec.metric,
    statistic,
    baselineMedian,
    ratio: baselineMedian > 0 ? statistic / baselineMedian : null,
  };
  const described = `${promptId}: turns median ${formatNumber(statistic)}`;
  if (delta > TURNS_FAIL_DELTA) {
    return {
      result: { ...base, level: "fail", label: `+${formatNumber(delta)} turns` },
      reason: `${described} exceeds baseline median ${formatNumber(baselineMedian)} by more than ${TURNS_FAIL_DELTA}`,
    };
  }
  if (Math.abs(delta) >= TURNS_WARN_DELTA) {
    return {
      result: { ...base, level: "warn", label: `${delta > 0 ? "+" : ""}${formatNumber(delta)} turns` },
      warning: `${described} differs from baseline median ${formatNumber(baselineMedian)} by ${formatNumber(Math.abs(delta))}`,
    };
  }
  return { result: { ...base, level: "ok", label: "ok" } };
}

function costRatioBand(
  promptId: string,
  records: readonly DriftRunRecord[],
  stamped: DriftPromptBaseline,
): BandOutcome {
  const spec = { metric: "sdkToFloorRatio" as const, name: "SDK cost / usage floor" };
  const statistic = statisticOf(records, spec.metric, "median");
  const baselineMedian = stamped.metrics.sdkToFloorRatio?.median ?? null;
  if (statistic === null || baselineMedian === null || baselineMedian <= 0) {
    return unmeasurable(spec, promptId);
  }
  const move = statistic / baselineMedian - 1;
  const base = { metric: spec.metric, statistic, baselineMedian, ratio: statistic / baselineMedian };
  if (Math.abs(move) > COST_RATIO_WARN_MOVE) {
    const percent = `${(Math.abs(move) * 100).toFixed(1)}%`;
    return {
      result: { ...base, level: "warn", label: `moved ${percent}` },
      warning: `${promptId}: ${spec.name} median ${statistic.toFixed(2)} moved ${percent} off the stamped ${baselineMedian.toFixed(2)} — pricing-table tripwire`,
    };
  }
  return { result: { ...base, level: "ok", label: "ok" } };
}

function toolCallWarnings(
  promptId: string,
  records: readonly DriftRunRecord[],
  stamped: DriftPromptBaseline,
): string[] {
  const warnings: string[] = [];
  const names = [
    ...new Set([
      ...Object.keys(stamped.toolCalls),
      ...records.flatMap((record) => Object.keys(record.toolCalls)),
    ]),
  ].sort();
  for (const name of names) {
    const observed = medianOf(records.map((record) => record.toolCalls[name] ?? 0));
    const reference = stamped.toolCalls[name]?.median ?? 0;
    if (observed !== null && Math.abs(observed - reference) > TOOL_CALLS_WARN_DELTA) {
      warnings.push(
        `${promptId}: ${name} calls median ${formatNumber(observed)} vs baseline median ${formatNumber(reference)}`,
      );
    }
  }
  return warnings;
}

function provenanceNotes(baseline: DriftBaseline, records: readonly DriftRunRecord[]): string[] {
  const observedSdk = records.map((record) => record.identity.sdkVersion).find((value) => value !== null);
  const observedCli = records.map((record) => record.identity.cliVersion).find((value) => value !== null);
  const notes: string[] = [];
  if (observedSdk !== undefined && observedSdk !== baseline.sdkVersion) {
    notes.push(`SDK ${baseline.sdkVersion} → ${observedSdk} since the baseline was stamped`);
  }
  if (observedCli !== undefined && observedCli !== baseline.cliVersion) {
    notes.push(`CLI ${baseline.cliVersion} → ${observedCli} since the baseline was stamped`);
  }
  return notes;
}

export function driftVerdict(input: DriftVerdictInput): DriftVerdict {
  const reasons: string[] = [...outcomeReasons(input.records, input.loadBearingTools)];
  const warnings: string[] = [];
  const bands: Record<string, BandResult[]> = {};
  const totalSpend = input.records.reduce((sum, record) => sum + record.estimatedCostUsd, 0);
  if (totalSpend > input.spendCapUsd) {
    reasons.push(`run-set spend ${formatUsd(totalSpend)} exceeds the $${input.spendCapUsd.toFixed(2)} cap`);
  }
  const promptIds = [...new Set(input.records.map((record) => record.promptId))];
  for (const promptId of promptIds) {
    const stamped = input.baseline.prompts[promptId];
    if (stamped === undefined) {
      reasons.push(`${promptId}: no stamped baseline for this prompt — ${RESTAMP_HINT}`);
      continue;
    }
    const measurable = input.records.filter(
      (record) => record.promptId === promptId && record.outcome !== "failed" && record.outcome !== "skipped",
    );
    const freshness = freshRecordsOf(promptId, measurable, input.baseline, stamped);
    reasons.push(...freshness.reasons);
    warnings.push(...freshness.warnings);
    if (freshness.fresh.length === 0) {
      continue;
    }
    const outcomes: BandOutcome[] = [
      ...RATIO_BANDS.map((spec) => ratioBand(promptId, freshness.fresh, stamped, spec)),
      turnsBand(promptId, freshness.fresh, stamped),
      costRatioBand(promptId, freshness.fresh, stamped),
    ];
    bands[promptId] = outcomes.map((outcome) => outcome.result);
    for (const outcome of outcomes) {
      if (outcome.reason !== undefined) {
        reasons.push(outcome.reason);
      }
      if (outcome.warning !== undefined) {
        warnings.push(outcome.warning);
      }
    }
    warnings.push(...toolCallWarnings(promptId, freshness.fresh, stamped));
  }
  return {
    tripped: reasons.length > 0,
    reasons,
    warnings,
    notes: provenanceNotes(input.baseline, input.records),
    bands,
  };
}

export type RenderDriftReportInput = {
  baseline: DriftBaseline;
  records: readonly DriftRunRecord[];
  verdict: DriftVerdict;
  spendCapUsd: number;
};

const BAND_ROW_LABELS: Record<DriftMetricName, string> = {
  wallMs: "wall time (ms, min of repeats)",
  contextTokens: "context tokens (median)",
  outputTokens: "output tokens (median)",
  estimatedCostUsd: "cost (USD, median)",
  numTurns: "turns (median)",
  sdkToFloorRatio: "SDK cost / usage floor (median)",
};

function formatMetric(metric: DriftMetricName, value: number): string {
  return metric === "estimatedCostUsd" ? formatUsd(value) : formatNumber(value);
}

function summaryCell(stamped: DriftPromptBaseline, metric: DriftMetricName): string {
  const summary = stamped.metrics[metric];
  if (summary === undefined) {
    return "—";
  }
  const format = (value: number): string => formatMetric(metric, value);
  return `${format(summary.median)} (${format(summary.min)}–${format(summary.max)})`;
}

function repeatsCell(records: readonly DriftRunRecord[], metric: DriftMetricName): string {
  return records
    .map((record) => {
      const value = metricValueOf(record, metric);
      return value === null ? "—" : formatMetric(metric, value);
    })
    .join(", ");
}

function toolCallsCell(toolCalls: Record<string, number>): string {
  const entries = Object.entries(toolCalls).sort(([left], [right]) => left.localeCompare(right));
  return entries.length === 0 ? "none" : entries.map(([name, count]) => `${name} ${count}`).join(", ");
}

function promptSection(promptId: string, input: RenderDriftReportInput): string[] {
  const records = input.records.filter((record) => record.promptId === promptId);
  const stamped = input.baseline.prompts[promptId];
  const lines = [`### ${promptId}`, ""];
  if (stamped === undefined) {
    lines.push("_no stamped baseline for this prompt_", "");
    return lines;
  }
  lines.push(
    "| metric | baseline median (min–max) | repeats | statistic | ratio | band |",
    "|---|---|---|---|---|---|",
  );
  const bands = input.verdict.bands[promptId] ?? [];
  for (const band of bands) {
    lines.push(
      `| ${BAND_ROW_LABELS[band.metric]} | ${summaryCell(stamped, band.metric)} | ${repeatsCell(records, band.metric)} | ` +
        `${band.statistic === null ? "—" : formatMetric(band.metric, band.statistic)} | ${band.ratio === null ? "—" : formatRatio(band.ratio)} | ${band.label} |`,
    );
  }
  const stampedTools = Object.entries(stamped.toolCalls)
    .map(([name, summary]) => `${name} ${formatNumber(summary.median)}`)
    .join(", ");
  lines.push(
    `| tool calls | ${stampedTools === "" ? "none" : stampedTools} | ${records.map((record) => toolCallsCell(record.toolCalls)).join(" / ")} | — | — | — |`,
    `| outcome | correct | ${records.map((record) => record.outcome).join(", ")} | — | — | — |`,
    "",
  );
  return lines;
}

export function renderDriftReport(input: RenderDriftReportInput): string {
  const { baseline, verdict } = input;
  const status = verdict.tripped ? "FAIL" : verdict.warnings.length > 0 ? "WARN" : "PASS";
  const totalSpend = input.records.reduce((sum, record) => sum + record.estimatedCostUsd, 0);
  const lines = [
    DRIFT_REPORT_MARKER,
    `## SDK drift runs: ${status}`,
    "",
    `Baseline: ${baselineLabel(baseline)}, stamped from ${baseline.calibrationRuns} calibration run(s). ` +
      `Run-set spend ${formatUsd(totalSpend)} of the $${input.spendCapUsd.toFixed(2)} cap.`,
    "",
  ];
  for (const note of verdict.notes) {
    lines.push(`- ${note}`);
  }
  if (verdict.notes.length > 0) {
    lines.push("");
  }
  for (const promptId of Object.keys(baseline.prompts)) {
    lines.push(...promptSection(promptId, input));
  }
  for (const promptId of [...new Set(input.records.map((record) => record.promptId))]) {
    if (baseline.prompts[promptId] === undefined) {
      lines.push(...promptSection(promptId, input));
    }
  }
  if (verdict.reasons.length > 0) {
    lines.push("**Fails**", "", ...verdict.reasons.map((reason) => `- ${reason}`), "");
  }
  if (verdict.warnings.length > 0) {
    lines.push(
      "**Warnings** (advisory, exit 0)",
      "",
      ...verdict.warnings.map((warning) => `- ${warning}`),
      "",
    );
  }
  return `${lines.join("\n")}\n`;
}
