import { fileURLToPath } from "node:url";
import { createNarrower, type Narrower } from "../../src/narrow.js";
import { contextTokensOf, type DriftRunIdentity, type DriftRunRecord } from "./record.js";
import { type SampleSummary, summarizeSamples } from "./stats.js";

export class DriftBaselineError extends Error {}

const parse: Narrower = createNarrower((message) => new DriftBaselineError(message));

export const DRIFT_BASELINE_PATH = fileURLToPath(new URL("baseline.json", import.meta.url));

export const DRIFT_METRIC_NAMES = [
  "wallMs",
  "contextTokens",
  "outputTokens",
  "numTurns",
  "estimatedCostUsd",
  "sdkToFloorRatio",
] as const;

export type DriftMetricName = (typeof DRIFT_METRIC_NAMES)[number];

export type DriftPromptBaseline = {
  promptSha256: string;
  stagedInputsSha256: string;
  samples: number;
  metrics: Partial<Record<DriftMetricName, SampleSummary>>;
  toolCalls: Record<string, SampleSummary>;
};

export type DriftBaseline = {
  stampedAt: string;
  sdkVersion: string;
  cliVersion: string;
  runtimeVersion: string;
  modelPin: string;
  calibrationRuns: number;
  prompts: Record<string, DriftPromptBaseline>;
};

export function metricValueOf(record: DriftRunRecord, metric: DriftMetricName): number | null {
  switch (metric) {
    case "wallMs":
      return record.wallMs;
    case "contextTokens":
      return contextTokensOf(record.tokens);
    case "outputTokens":
      return record.tokens.output;
    case "numTurns":
      return record.numTurns;
    case "estimatedCostUsd":
      return record.estimatedCostUsd;
    case "sdkToFloorRatio":
      return record.sdkCostUsd === null || record.floorCostUsd === null || record.floorCostUsd <= 0
        ? null
        : record.sdkCostUsd / record.floorCostUsd;
    default:
      return null;
  }
}

type StampedIdentity = { [Key in keyof DriftRunIdentity]: string };

const IDENTITY_KEYS: readonly (keyof DriftRunIdentity)[] = [
  "sdkVersion",
  "cliVersion",
  "modelPin",
  "promptSha256",
  "stagedInputsSha256",
];

function requireIdentity(record: DriftRunRecord): StampedIdentity {
  const identity: Partial<StampedIdentity> = {};
  for (const key of IDENTITY_KEYS) {
    const value = record.identity[key];
    if (value === null) {
      throw new DriftBaselineError(`${record.executionId}: identity.${key} is missing — cannot stamp it`);
    }
    identity[key] = value;
  }
  return {
    sdkVersion: identity.sdkVersion ?? "",
    cliVersion: identity.cliVersion ?? "",
    modelPin: identity.modelPin ?? "",
    promptSha256: identity.promptSha256 ?? "",
    stagedInputsSha256: identity.stagedInputsSha256 ?? "",
  };
}

function assertSameIdentity(
  reference: StampedIdentity,
  candidate: StampedIdentity,
  keys: readonly (keyof DriftRunIdentity)[],
  executionId: string,
): void {
  for (const key of keys) {
    if (reference[key] !== candidate[key]) {
      throw new DriftBaselineError(
        `${executionId}: identity.${key} "${candidate[key]}" differs from "${reference[key]}" — ` +
          "one baseline describes one SDK, CLI, model pin and prompt; stamp from one calibration set",
      );
    }
  }
}

const SET_IDENTITY_KEYS: readonly (keyof DriftRunIdentity)[] = ["sdkVersion", "cliVersion", "modelPin"];
const PROMPT_IDENTITY_KEYS: readonly (keyof DriftRunIdentity)[] = ["promptSha256", "stagedInputsSha256"];

// Six decimals keep a ten-thousandth of a cent and a thousandth of a token, and keep the
// stamped file free of floating-point residue a reviewer would otherwise have to read past.
const STAMPED_DECIMALS = 6;

function roundStamped(value: number): number {
  return Number(value.toFixed(STAMPED_DECIMALS));
}

function roundedSummary(summary: SampleSummary): SampleSummary {
  return {
    min: roundStamped(summary.min),
    p25: roundStamped(summary.p25),
    median: roundStamped(summary.median),
    p75: roundStamped(summary.p75),
    max: roundStamped(summary.max),
    samples: summary.samples,
  };
}

function summariesOf(records: readonly DriftRunRecord[]): Partial<Record<DriftMetricName, SampleSummary>> {
  const metrics: Partial<Record<DriftMetricName, SampleSummary>> = {};
  for (const metric of DRIFT_METRIC_NAMES) {
    const values = records.map((record) => metricValueOf(record, metric)).filter((value) => value !== null);
    const summary = summarizeSamples(values);
    if (summary !== null) {
      metrics[metric] = roundedSummary(summary);
    }
  }
  return metrics;
}

function toolCallSummariesOf(records: readonly DriftRunRecord[]): Record<string, SampleSummary> {
  const names = [...new Set(records.flatMap((record) => Object.keys(record.toolCalls)))].sort();
  const summaries: Record<string, SampleSummary> = {};
  for (const name of names) {
    const summary = summarizeSamples(records.map((record) => record.toolCalls[name] ?? 0));
    if (summary !== null) {
      summaries[name] = summary;
    }
  }
  return summaries;
}

export type StampDriftBaselineInput = {
  records: readonly DriftRunRecord[];
  runtimeVersion: string;
  stampedAt: string;
  calibrationRuns: number;
};

export function stampDriftBaseline(input: StampDriftBaselineInput): DriftBaseline {
  const first = input.records[0];
  if (first === undefined) {
    throw new DriftBaselineError("no records to stamp a baseline from");
  }
  for (const record of input.records) {
    if (record.outcome !== "correct") {
      throw new DriftBaselineError(
        `${record.executionId} (${record.promptId} #${record.repeat}) is ${record.outcome}: ${record.detail} — ` +
          "a baseline is stamped from correct runs only",
      );
    }
  }
  const reference = requireIdentity(first);
  const prompts: Record<string, DriftPromptBaseline> = {};
  const promptIds = [...new Set(input.records.map((record) => record.promptId))];
  for (const promptId of promptIds) {
    const records = input.records.filter((record) => record.promptId === promptId);
    const promptReference = requireIdentity(records[0] ?? first);
    for (const record of records) {
      const identity = requireIdentity(record);
      assertSameIdentity(reference, identity, SET_IDENTITY_KEYS, record.executionId);
      assertSameIdentity(promptReference, identity, PROMPT_IDENTITY_KEYS, record.executionId);
    }
    prompts[promptId] = {
      promptSha256: promptReference.promptSha256,
      stagedInputsSha256: promptReference.stagedInputsSha256,
      samples: records.length,
      metrics: summariesOf(records),
      toolCalls: toolCallSummariesOf(records),
    };
  }
  return {
    stampedAt: input.stampedAt,
    sdkVersion: reference.sdkVersion,
    cliVersion: reference.cliVersion,
    runtimeVersion: input.runtimeVersion,
    modelPin: reference.modelPin,
    calibrationRuns: input.calibrationRuns,
    prompts,
  };
}

function parseSummary(value: unknown, label: string): SampleSummary {
  const record = parse.asRecord(value, label);
  return {
    min: parse.requireNumber(record, "min", label),
    p25: parse.requireNumber(record, "p25", label),
    median: parse.requireNumber(record, "median", label),
    p75: parse.requireNumber(record, "p75", label),
    max: parse.requireNumber(record, "max", label),
    samples: parse.requirePositiveInteger(record, "samples", label),
  };
}

function parseMetrics(value: unknown, label: string): Partial<Record<DriftMetricName, SampleSummary>> {
  const record = parse.asRecord(value, label);
  parse.rejectUnknownFields(record, DRIFT_METRIC_NAMES, label);
  const metrics: Partial<Record<DriftMetricName, SampleSummary>> = {};
  for (const metric of DRIFT_METRIC_NAMES) {
    if (record[metric] !== undefined) {
      metrics[metric] = parseSummary(record[metric], `${label}.${metric}`);
    }
  }
  return metrics;
}

function parseToolCallSummaries(value: unknown, label: string): Record<string, SampleSummary> {
  const record = parse.asRecord(value, label);
  const summaries: Record<string, SampleSummary> = {};
  for (const name of Object.keys(record)) {
    summaries[name] = parseSummary(record[name], `${label}.${name}`);
  }
  return summaries;
}

function parsePromptBaseline(value: unknown, label: string): DriftPromptBaseline {
  const record = parse.asRecord(value, label);
  return {
    promptSha256: parse.requireString(record, "promptSha256", label),
    stagedInputsSha256: parse.requireString(record, "stagedInputsSha256", label),
    samples: parse.requirePositiveInteger(record, "samples", label),
    metrics: parseMetrics(record["metrics"], `${label}.metrics`),
    toolCalls: parseToolCallSummaries(record["toolCalls"], `${label}.toolCalls`),
  };
}

const BASELINE_LABEL = "drift baseline";

export function parseDriftBaseline(value: unknown): DriftBaseline {
  const record = parse.asRecord(value, BASELINE_LABEL);
  const promptsRecord = parse.asRecord(record["prompts"], `${BASELINE_LABEL}.prompts`);
  const prompts: Record<string, DriftPromptBaseline> = {};
  for (const promptId of Object.keys(promptsRecord)) {
    prompts[promptId] = parsePromptBaseline(promptsRecord[promptId], `${BASELINE_LABEL}.prompts.${promptId}`);
  }
  return {
    stampedAt: parse.requireString(record, "stampedAt", BASELINE_LABEL),
    sdkVersion: parse.requireString(record, "sdkVersion", BASELINE_LABEL),
    cliVersion: parse.requireString(record, "cliVersion", BASELINE_LABEL),
    runtimeVersion: parse.requireString(record, "runtimeVersion", BASELINE_LABEL),
    modelPin: parse.requireString(record, "modelPin", BASELINE_LABEL),
    calibrationRuns: parse.requirePositiveInteger(record, "calibrationRuns", BASELINE_LABEL),
    prompts,
  };
}
