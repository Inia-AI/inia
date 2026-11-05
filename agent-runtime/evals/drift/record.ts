import { createNarrower, isRecord, type Narrower } from "../../src/narrow.js";
import type { RunFailure, RunReportResult } from "../../src/run-report.js";
import { estimateUsageCostUsd, type TokenUsage } from "../../src/usage-cost.js";
import { answerOf } from "./prompts.js";

export class DriftRecordError extends Error {}

const parse: Narrower = createNarrower((message) => new DriftRecordError(message));

export const DRIFT_OUTCOMES = ["correct", "wrong", "failed", "skipped"] as const;

export type DriftOutcome = (typeof DRIFT_OUTCOMES)[number];

export type DriftTokens = { input: number; output: number; cacheWrite: number; cacheRead: number };

export const ZERO_DRIFT_TOKENS: DriftTokens = { input: 0, output: 0, cacheWrite: 0, cacheRead: 0 };

export type DriftRunIdentity = {
  sdkVersion: string | null;
  cliVersion: string | null;
  modelPin: string | null;
  promptSha256: string | null;
  stagedInputsSha256: string | null;
};

export type DriftRunRecord = {
  promptId: string;
  repeat: number;
  executionId: string;
  startedAt: string;
  wallMs: number | null;
  outcome: DriftOutcome;
  detail: string;
  numTurns: number | null;
  maxTurnsHit: boolean;
  apiMessages: number;
  toolCalls: Record<string, number>;
  tokens: DriftTokens;
  sdkCostUsd: number | null;
  floorCostUsd: number | null;
  estimatedCostUsd: number;
  identity: DriftRunIdentity;
};

export type TraceFacts = {
  identity: DriftRunIdentity;
  apiMessages: number;
  tokens: DriftTokens;
  floorCostUsd: number | null;
  toolCalls: Record<string, number>;
  numTurns: number | null;
  sdkCostUsd: number | null;
};

const MAX_TURNS_SUBTYPE = "error_max_turns";
const HARNESS_ERROR_SUBTYPE = "harness_error";
const UNPRICED_MODEL = "";

function stringOrNull(record: Record<string, unknown>, key: string): string | null {
  const value = record[key];
  return typeof value === "string" && value !== "" ? value : null;
}

function finiteOrNull(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function tokenCountOf(usage: Record<string, unknown>, key: string): number {
  const value = finiteOrNull(usage[key]);
  return value === null || value < 0 ? 0 : value;
}

function driftTokensOf(rawUsage: Record<string, unknown>): DriftTokens {
  return {
    input: tokenCountOf(rawUsage, "input_tokens"),
    output: tokenCountOf(rawUsage, "output_tokens"),
    cacheWrite: tokenCountOf(rawUsage, "cache_creation_input_tokens"),
    cacheRead: tokenCountOf(rawUsage, "cache_read_input_tokens"),
  };
}

function addDriftTokens(left: DriftTokens, right: DriftTokens): DriftTokens {
  return {
    input: left.input + right.input,
    output: left.output + right.output,
    cacheWrite: left.cacheWrite + right.cacheWrite,
    cacheRead: left.cacheRead + right.cacheRead,
  };
}

function asTokenUsage(tokens: DriftTokens): TokenUsage {
  return {
    inputTokens: tokens.input,
    outputTokens: tokens.output,
    cacheCreationInputTokens: tokens.cacheWrite,
    cacheReadInputTokens: tokens.cacheRead,
  };
}

export function contextTokensOf(tokens: DriftTokens): number {
  return tokens.input + tokens.cacheWrite + tokens.cacheRead;
}

function parsedLinesOf(packText: string): Record<string, unknown>[] {
  const records: Record<string, unknown>[] = [];
  for (const line of packText.split("\n")) {
    if (line === "") {
      continue;
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(line);
    } catch {
      continue;
    }
    if (isRecord(parsed)) {
      records.push(parsed);
    }
  }
  return records;
}

export function traceFactsOf(packText: string): TraceFacts {
  const identity: DriftRunIdentity = {
    sdkVersion: null,
    cliVersion: null,
    modelPin: null,
    promptSha256: null,
    stagedInputsSha256: null,
  };
  let apiMessages = 0;
  let tokens = ZERO_DRIFT_TOKENS;
  let floorCostUsd: number | null = null;
  const toolCalls: Record<string, number> = {};
  let numTurns: number | null = null;
  let sdkCostUsd: number | null = null;
  // Sub-agent spend rides the pack as its own usage records (source subagent_transcript /
  // subagent_result, trace format v8), so the Agent payload is never priced here.
  const addSpend = (model: string, usage: DriftTokens): void => {
    tokens = addDriftTokens(tokens, usage);
    floorCostUsd = (floorCostUsd ?? 0) + estimateUsageCostUsd(model, asTokenUsage(usage));
  };
  for (const record of parsedLinesOf(packText)) {
    switch (record["type"]) {
      case "envelope": {
        identity.sdkVersion = stringOrNull(record, "sdkVersion");
        identity.modelPin = stringOrNull(record, "modelPin");
        identity.promptSha256 = stringOrNull(record, "promptSha256");
        identity.stagedInputsSha256 = stringOrNull(record, "stagedInputsSha256");
        break;
      }
      case "usage": {
        const usage = record["usage"];
        if (isRecord(usage)) {
          apiMessages += 1;
          addSpend(stringOrNull(record, "model") ?? UNPRICED_MODEL, driftTokensOf(usage));
        }
        break;
      }
      case "tool_call": {
        const toolName = stringOrNull(record, "toolName");
        if (toolName !== null) {
          toolCalls[toolName] = (toolCalls[toolName] ?? 0) + 1;
        }
        break;
      }
      case "message": {
        const message = record["message"];
        if (!isRecord(message)) {
          break;
        }
        if (message["type"] === "system" && message["subtype"] === "init") {
          identity.cliVersion = stringOrNull(message, "claude_code_version");
        }
        if (message["type"] === "result") {
          numTurns = finiteOrNull(message["num_turns"]);
          sdkCostUsd = finiteOrNull(message["total_cost_usd"]);
        }
        break;
      }
      default:
        break;
    }
  }
  return { identity, apiMessages, tokens, floorCostUsd, toolCalls, numTurns, sdkCostUsd };
}

const EMPTY_TRACE_FACTS: TraceFacts = {
  identity: {
    sdkVersion: null,
    cliVersion: null,
    modelPin: null,
    promptSha256: null,
    stagedInputsSha256: null,
  },
  apiMessages: 0,
  tokens: ZERO_DRIFT_TOKENS,
  floorCostUsd: null,
  toolCalls: {},
  numTurns: null,
  sdkCostUsd: null,
};

export type ProjectDriftRecordInput = {
  promptId: string;
  repeat: number;
  executionId: string;
  startedAt: string;
  wallMs: number | null;
  expectedAnswer: number;
  maxTurns: number;
  result: RunReportResult | null;
  failure: RunFailure | null;
  tracePackText: string | null;
};

type Verdict = { outcome: DriftOutcome; detail: string };

function outcomeOf(input: ProjectDriftRecordInput): Verdict {
  if (input.failure !== null) {
    return { outcome: "failed", detail: `${input.failure.subtype}: ${input.failure.message}` };
  }
  if (input.result === null) {
    return { outcome: "failed", detail: `${HARNESS_ERROR_SUBTYPE}: no result and no failure` };
  }
  if (input.result.budgetExhausted === true) {
    return { outcome: "failed", detail: "per-run budget exhausted (structured output salvaged)" };
  }
  const answer = answerOf(input.result.structuredOutput);
  if (answer === input.expectedAnswer) {
    return { outcome: "correct", detail: "" };
  }
  return {
    outcome: "wrong",
    detail: `expected ${input.expectedAnswer}, structured output ${JSON.stringify(input.result.structuredOutput)}`,
  };
}

export function projectDriftRecord(input: ProjectDriftRecordInput): DriftRunRecord {
  const facts = input.tracePackText === null ? EMPTY_TRACE_FACTS : traceFactsOf(input.tracePackText);
  const { outcome, detail } = outcomeOf(input);
  const estimatedCostUsd = input.result?.estimatedCostUsd ?? input.failure?.estimatedCostUsd ?? 0;
  const maxTurnsHit =
    input.failure?.subtype === MAX_TURNS_SUBTYPE ||
    (facts.numTurns !== null && facts.numTurns >= input.maxTurns);
  return {
    promptId: input.promptId,
    repeat: input.repeat,
    executionId: input.executionId,
    startedAt: input.startedAt,
    wallMs: input.wallMs,
    outcome,
    detail,
    numTurns: facts.numTurns,
    maxTurnsHit,
    apiMessages: facts.apiMessages,
    toolCalls: { ...facts.toolCalls },
    tokens: { ...facts.tokens },
    sdkCostUsd: facts.sdkCostUsd,
    floorCostUsd: facts.floorCostUsd,
    estimatedCostUsd,
    identity: { ...facts.identity },
  };
}

export function harnessFailureOf(message: string): RunFailure {
  return { message, subtype: HARNESS_ERROR_SUBTYPE, estimatedCostUsd: 0, sessionId: null };
}

export function skippedDriftRecord(promptId: string, repeat: number, why: string): DriftRunRecord {
  return {
    promptId,
    repeat,
    executionId: "",
    startedAt: "",
    wallMs: null,
    outcome: "skipped",
    detail: why,
    numTurns: null,
    maxTurnsHit: false,
    apiMessages: 0,
    toolCalls: {},
    tokens: { ...ZERO_DRIFT_TOKENS },
    sdkCostUsd: null,
    floorCostUsd: null,
    estimatedCostUsd: 0,
    identity: { ...EMPTY_TRACE_FACTS.identity },
  };
}

function nullableString(record: Record<string, unknown>, key: string, label: string): string | null {
  const value = record[key];
  if (value === null) {
    return null;
  }
  if (typeof value !== "string") {
    parse.fail(`${label}.${key} must be a string or null`);
  }
  return value;
}

function nullableNumber(record: Record<string, unknown>, key: string, label: string): number | null {
  const value = record[key];
  if (value === null) {
    return null;
  }
  if (typeof value !== "number" || !Number.isFinite(value)) {
    parse.fail(`${label}.${key} must be a finite number or null`);
  }
  return value;
}

function parseOutcome(value: unknown, label: string): DriftOutcome {
  const outcome = DRIFT_OUTCOMES.find((candidate) => candidate === value);
  if (outcome === undefined) {
    parse.fail(`${label}.outcome must be one of ${DRIFT_OUTCOMES.join(", ")}`);
  }
  return outcome;
}

function parseToolCalls(value: unknown, label: string): Record<string, number> {
  const record = parse.asRecord(value, label);
  const toolCalls: Record<string, number> = {};
  for (const name of Object.keys(record)) {
    toolCalls[name] = parse.requireNonNegativeNumber(record, name, label);
  }
  return toolCalls;
}

function parseTokens(value: unknown, label: string): DriftTokens {
  const record = parse.asRecord(value, label);
  return {
    input: parse.requireNonNegativeNumber(record, "input", label),
    output: parse.requireNonNegativeNumber(record, "output", label),
    cacheWrite: parse.requireNonNegativeNumber(record, "cacheWrite", label),
    cacheRead: parse.requireNonNegativeNumber(record, "cacheRead", label),
  };
}

function parseIdentity(value: unknown, label: string): DriftRunIdentity {
  const record = parse.asRecord(value, label);
  return {
    sdkVersion: nullableString(record, "sdkVersion", label),
    cliVersion: nullableString(record, "cliVersion", label),
    modelPin: nullableString(record, "modelPin", label),
    promptSha256: nullableString(record, "promptSha256", label),
    stagedInputsSha256: nullableString(record, "stagedInputsSha256", label),
  };
}

const RECORD_LABEL = "drift record";

export function parseDriftRunRecord(value: unknown): DriftRunRecord {
  const record = parse.asRecord(value, RECORD_LABEL);
  return {
    promptId: parse.requireString(record, "promptId", RECORD_LABEL),
    repeat: parse.requirePositiveInteger(record, "repeat", RECORD_LABEL),
    executionId: parse.requirePossiblyEmptyString(record, "executionId", RECORD_LABEL),
    startedAt: parse.requirePossiblyEmptyString(record, "startedAt", RECORD_LABEL),
    wallMs: nullableNumber(record, "wallMs", RECORD_LABEL),
    outcome: parseOutcome(record["outcome"], RECORD_LABEL),
    detail: parse.requirePossiblyEmptyString(record, "detail", RECORD_LABEL),
    numTurns: nullableNumber(record, "numTurns", RECORD_LABEL),
    maxTurnsHit: parse.requireBoolean(record, "maxTurnsHit", RECORD_LABEL),
    apiMessages: parse.requireNonNegativeNumber(record, "apiMessages", RECORD_LABEL),
    toolCalls: parseToolCalls(record["toolCalls"], `${RECORD_LABEL}.toolCalls`),
    tokens: parseTokens(record["tokens"], `${RECORD_LABEL}.tokens`),
    sdkCostUsd: nullableNumber(record, "sdkCostUsd", RECORD_LABEL),
    floorCostUsd: nullableNumber(record, "floorCostUsd", RECORD_LABEL),
    estimatedCostUsd: parse.requireNonNegativeNumber(record, "estimatedCostUsd", RECORD_LABEL),
    identity: parseIdentity(record["identity"], `${RECORD_LABEL}.identity`),
  };
}
