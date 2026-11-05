// Shared vocabulary between the scripted-run driver (an entry point) and its gate test:
// importing this never executes a run.
import { fileURLToPath } from "node:url";
import { createNarrower, isRecord, type Narrower } from "../narrow.js";
import type { ToolResultImage } from "./probe-image.js";

export const SCRIPTED_RUN_MARKER = "ENG232_SCRIPTED_RUN_MARKER";
export const ASSET_EXECUTED_PROBE = "ENG262_ASSET_EXECUTED";
export const ASSETS_READONLY_PROBE = "ENG262_ASSETS_READONLY";
export const ASSETS_WRITABLE_MARKER = "ENG262_ASSETS_WRITABLE";
export const PLUGIN_INVISIBLE_PROBE = "ENG262_PLUGIN_INVISIBLE";
export const SCRIPTED_SKILL_NAME = "asset-probe";
export const SCRIPTED_ASSET_NAME = "probe.sh";
export const SKILL_ASSETS_SCRATCH_DIR = "skill-assets";
export const TMP_WRITABLE_PROBE = "ENG232_TMP_WRITABLE";
export const SCRATCH_INVISIBLE_PROBE = "ENG232_SCRATCH_INVISIBLE";
export const DIRECT_EGRESS_REFUSED_PROBE = "ENG232_DIRECT_EGRESS_REFUSED";
export const PROXY_EGRESS_REFUSED_PROBE = "ENG232_PROXY_EGRESS_REFUSED";
export const EGRESS_OPEN_MARKER = "ENG232_EGRESS_OPEN";
export const SCRIPTED_FINAL_TEXT = `scripted run complete ${SCRIPTED_RUN_MARKER}`;

export const BWRAP_ARGV_FIXTURE_PATH = fileURLToPath(new URL("bwrap-argv.fixture.json", import.meta.url));

export type ScriptedRunReport = {
  provider: string;
  output: string;
  sessionId: string;
  outputTokens: number;
  estimatedCostUsd: number;
  sdkCostUsd: number;
  resumedLeg: ResumedLegReport;
  costUpdates: number;
  toolResultTexts: string[];
  toolResultImages: ToolResultImage[];
  toolNamesOffered: string[];
  requestPaths: string[];
  messageRequests: number;
  bwrapInvocations: number;
  bwrapArgvRaw: string[];
  bwrapArgvNormalized: string[];
  traceInitClaudeCodeVersion: string | undefined;
  traceModelProvider: string | undefined;
  traceToolCalls: { name: string; errored: boolean }[];
  traceUsageRecords: number;
  traceRecordTypes: Record<string, number>;
};

export type ResumedLegReport = {
  sessionId: string;
  estimatedCostUsd: number;
  sdkCostUsd: number;
  restoredCostUsd: number;
};

export const SCRIPTED_PROVIDER_FLAG_PREFIX = "--provider=";
export const FOUNDRY_MODEL_PIN = "claude-sonnet-5";

export class ScriptedRunReportError extends Error {}

function stringArrayOf(value: unknown, path: string): string[] {
  if (!Array.isArray(value) || value.some((entry) => typeof entry !== "string")) {
    throw new ScriptedRunReportError(`${path} must be an array of strings`);
  }
  return value.filter((entry): entry is string => typeof entry === "string");
}

function toolResultImagesOf(value: unknown, parse: Narrower): ToolResultImage[] {
  return parse.requireArray(value, "toolResultImages").map((entry, index) => {
    const image = parse.asRecord(entry, `toolResultImages[${index}]`);
    const dimensions = image["dimensions"];
    return {
      mediaType: parse.requirePossiblyEmptyString(image, "mediaType", "toolResultImages"),
      byteLength: parse.requireNonNegativeNumber(image, "byteLength", "toolResultImages"),
      dimensions: isRecord(dimensions)
        ? {
            width: parse.requireNonNegativeNumber(dimensions, "width", "toolResultImages.dimensions"),
            height: parse.requireNonNegativeNumber(dimensions, "height", "toolResultImages.dimensions"),
          }
        : undefined,
    };
  });
}

export function parseScriptedRunReport(value: unknown): ScriptedRunReport {
  const parse = createNarrower((message) => new ScriptedRunReportError(message));
  const record = parse.asRecord(value, "scripted run report");
  const toolCalls = parse.requireArray(record["traceToolCalls"], "traceToolCalls").map((entry, index) => {
    const call = parse.asRecord(entry, `traceToolCalls[${index}]`);
    return {
      name: parse.requireString(call, "name", "traceToolCalls"),
      errored: parse.requireBoolean(call, "errored", "traceToolCalls"),
    };
  });
  return {
    provider: parse.requireString(record, "provider", "scripted run report"),
    output: parse.requirePossiblyEmptyString(record, "output"),
    sessionId: parse.requirePossiblyEmptyString(record, "sessionId"),
    outputTokens: parse.requireNonNegativeNumber(record, "outputTokens"),
    estimatedCostUsd: parse.requireNonNegativeNumber(record, "estimatedCostUsd"),
    sdkCostUsd: parse.requireNonNegativeNumber(record, "sdkCostUsd"),
    resumedLeg: resumedLegOf(parse.asRecord(record["resumedLeg"], "resumedLeg"), parse),
    costUpdates: parse.requireNonNegativeNumber(record, "costUpdates"),
    toolResultTexts: stringArrayOf(record["toolResultTexts"], "toolResultTexts"),
    toolResultImages: toolResultImagesOf(record["toolResultImages"], parse),
    toolNamesOffered: stringArrayOf(record["toolNamesOffered"], "toolNamesOffered"),
    requestPaths: stringArrayOf(record["requestPaths"], "requestPaths"),
    messageRequests: parse.requireNonNegativeNumber(record, "messageRequests"),
    bwrapInvocations: parse.requireNonNegativeNumber(record, "bwrapInvocations"),
    bwrapArgvRaw: stringArrayOf(record["bwrapArgvRaw"], "bwrapArgvRaw"),
    bwrapArgvNormalized: stringArrayOf(record["bwrapArgvNormalized"], "bwrapArgvNormalized"),
    traceInitClaudeCodeVersion: parse.optionalString(record, "traceInitClaudeCodeVersion"),
    traceModelProvider: parse.optionalString(record, "traceModelProvider"),
    traceToolCalls: toolCalls,
    traceUsageRecords: parse.requireNonNegativeNumber(record, "traceUsageRecords"),
    traceRecordTypes: recordTypesOf(record["traceRecordTypes"]),
  };
}

function resumedLegOf(record: Record<string, unknown>, parse: Narrower): ResumedLegReport {
  return {
    sessionId: parse.requirePossiblyEmptyString(record, "sessionId", "resumedLeg"),
    estimatedCostUsd: parse.requireNonNegativeNumber(record, "estimatedCostUsd", "resumedLeg"),
    sdkCostUsd: parse.requireNonNegativeNumber(record, "sdkCostUsd", "resumedLeg"),
    restoredCostUsd: parse.requireNonNegativeNumber(record, "restoredCostUsd", "resumedLeg"),
  };
}

function recordTypesOf(value: unknown): Record<string, number> {
  if (!isRecord(value)) {
    throw new ScriptedRunReportError("traceRecordTypes must be a JSON object");
  }
  const counts: Record<string, number> = {};
  for (const [key, count] of Object.entries(value)) {
    if (typeof count === "number") {
      counts[key] = count;
    }
  }
  return counts;
}

export function parseBwrapArgvFixture(value: unknown): string[] {
  if (isRecord(value)) {
    throw new ScriptedRunReportError("bwrap argv fixture must be a JSON array of strings");
  }
  return stringArrayOf(value, "bwrap argv fixture");
}
