import { isRecord, withNullsAsAbsent } from "./narrow.js";

export type TokenUsage = {
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
};

export const ZERO_TOKEN_USAGE: TokenUsage = {
  inputTokens: 0,
  outputTokens: 0,
  cacheCreationInputTokens: 0,
  cacheReadInputTokens: 0,
};

export function addTokenUsage(left: TokenUsage, right: TokenUsage): TokenUsage {
  return {
    inputTokens: left.inputTokens + right.inputTokens,
    outputTokens: left.outputTokens + right.outputTokens,
    cacheCreationInputTokens: left.cacheCreationInputTokens + right.cacheCreationInputTokens,
    cacheReadInputTokens: left.cacheReadInputTokens + right.cacheReadInputTokens,
  };
}

export type ApiMessageUsage = {
  messageId: string;
  model: string;
  rawUsage: Record<string, unknown>;
  settled: boolean;
};

type UsdPerMillionTokens = {
  input: number;
  output: number;
  cacheWrite: number;
  cacheRead: number;
};

// A request whose prompt (input plus cache reads and writes) is over thresholdTokens pays `rates`
// on every token of that request, output included.
type LongPromptPrice = {
  thresholdTokens: number;
  rates: UsdPerMillionTokens;
};

type ModelPrice = UsdPerMillionTokens & { longPrompt?: LongPromptPrice };

const TOKENS_PER_MILLION = 1_000_000;

// cacheWrite rates are the 5-minute-TTL price (1.25x input); 1-hour-TTL writes bill 2x
// and are deliberately left out of these rates, not a rounding gap to fix.
const SONNET_RATES: UsdPerMillionTokens = { input: 3, output: 15, cacheWrite: 3.75, cacheRead: 0.3 };
// Sonnet 5 kept its $2/$10 launch price as the list price (the 2026-09-01 rise to $3/$15 was cancelled).
const SONNET_5_RATES: UsdPerMillionTokens = { input: 2, output: 10, cacheWrite: 2.5, cacheRead: 0.2 };
// Sonnet 5.5 lists at Sonnet 5's input and output rates, but its cache reads bill at 0.05x input.
const SONNET_5_5_RATES: UsdPerMillionTokens = { input: 2, output: 10, cacheWrite: 2.5, cacheRead: 0.1 };
const HAIKU_RATES: UsdPerMillionTokens = { input: 1, output: 5, cacheWrite: 1.25, cacheRead: 0.1 };
// Haiku 5.5 lists at a tenth of Haiku 4.5, and at 5x that for a prompt of over 100,000 tokens
// (platform.claude.com pricing and the AWS price list, read 2026-10-16).
const HAIKU_5_5_RATES: ModelPrice = {
  input: 0.1,
  output: 0.5,
  cacheWrite: 0.125,
  cacheRead: 0.01,
  longPrompt: {
    thresholdTokens: 100_000,
    rates: { input: 0.5, output: 2.5, cacheWrite: 0.625, cacheRead: 0.05 },
  },
};
const OPUS_RATES: UsdPerMillionTokens = { input: 5, output: 25, cacheWrite: 6.25, cacheRead: 0.5 };
// Opus 5.5 is priced below Opus 5, and its cache reads bill at 0.05x input, not 0.1x.
const OPUS_5_5_RATES: UsdPerMillionTokens = { input: 4, output: 20, cacheWrite: 5, cacheRead: 0.2 };

const MODEL_RATE_PATTERNS: readonly { pattern: RegExp; rates: ModelPrice }[] = [
  { pattern: /haiku-5-5/i, rates: HAIKU_5_5_RATES },
  { pattern: /haiku/i, rates: HAIKU_RATES },
  { pattern: /opus-5-5/i, rates: OPUS_5_5_RATES },
  { pattern: /opus/i, rates: OPUS_RATES },
  { pattern: /sonnet-5-5/i, rates: SONNET_5_5_RATES },
  { pattern: /sonnet-5/i, rates: SONNET_5_RATES },
  { pattern: /sonnet/i, rates: SONNET_RATES },
];

// An unrecognized model estimates at the report tier's rates — never zero, which would
// silently under-price a killed run to $0.
const UNKNOWN_MODEL_RATES: ModelPrice = SONNET_RATES;

export const INIT_MESSAGE_TYPE = "system";
export const INIT_MESSAGE_SUBTYPE = "init";

const STREAM_EVENT_MESSAGE_TYPE = "stream_event";
const MESSAGE_START_EVENT = "message_start";
const MESSAGE_DELTA_EVENT = "message_delta";
const MESSAGE_STOP_EVENT = "message_stop";

export function isStreamEventMessage(message: unknown): message is Record<string, unknown> {
  return isRecord(message) && message["type"] === STREAM_EVENT_MESSAGE_TYPE;
}

function tokenCountOf(usage: Record<string, unknown>, key: string): number {
  const value = usage[key];
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? value : 0;
}

function tokenUsageOf(rawUsage: Record<string, unknown>): TokenUsage {
  return {
    inputTokens: tokenCountOf(rawUsage, "input_tokens"),
    outputTokens: tokenCountOf(rawUsage, "output_tokens"),
    cacheCreationInputTokens: tokenCountOf(rawUsage, "cache_creation_input_tokens"),
    cacheReadInputTokens: tokenCountOf(rawUsage, "cache_read_input_tokens"),
  };
}

const USER_MESSAGE_TYPE = "user";
const RESULT_MESSAGE_TYPE = "result";

export type SubagentUsagePayload = {
  agentId: string;
  sessionId: string | undefined;
  resolvedModel: string | undefined;
  rawUsage: Record<string, unknown>;
};

export type SubagentApiMessagesReader = (
  sessionId: string,
  agentId: string,
) => readonly ApiMessageUsage[] | undefined;

// The payload's `usage` is the sub-agent's LAST API call, not a sum: the CLI copies its final
// assistant message's usage. The payload alone therefore under-counts a sub-agent that made
// more than one call. The async_launched variant carries no usage and is skipped, which holds
// only because the agent gate forces foreground spawns.
export function subagentUsageOf(message: unknown): SubagentUsagePayload | undefined {
  if (!isRecord(message) || message["type"] !== USER_MESSAGE_TYPE) {
    return undefined;
  }
  const payload = message["tool_use_result"];
  if (!isRecord(payload)) {
    return undefined;
  }
  const agentId = payload["agentId"];
  const rawUsage = payload["usage"];
  if (typeof agentId !== "string" || agentId === "" || !isRecord(rawUsage)) {
    return undefined;
  }
  const resolvedModel = payload["resolvedModel"];
  const sessionId = message["session_id"];
  return {
    agentId,
    sessionId: typeof sessionId === "string" && sessionId !== "" ? sessionId : undefined,
    resolvedModel: typeof resolvedModel === "string" && resolvedModel !== "" ? resolvedModel : undefined,
    rawUsage: { ...rawUsage },
  };
}

type InFlightApiMessage = {
  messageId: string;
  model: string;
  rawUsage: Record<string, unknown>;
};

function snapshotOf(inFlight: InFlightApiMessage, settled: boolean): ApiMessageUsage {
  return {
    messageId: inFlight.messageId,
    model: inFlight.model,
    rawUsage: { ...inFlight.rawUsage },
    settled,
  };
}

const NO_UPDATES: readonly ApiMessageUsage[] = [];

// Do not read usage off the assembled assistant message: its output_tokens is a
// placeholder (measured: a call recorded as 2 output tokens had metered 13,343 on
// Bedrock's own CloudWatch) until the final message_delta.
//
// Sub-agent API calls emit no stream events into the parent stream (measured), so this meter
// never sees sub-agent spend; it is settled from the sub-agent's transcript instead. Events
// with a non-null parent_tool_use_id are excluded below in case a future SDK forwards them.
export type UsageTracker = {
  record(message: unknown): readonly ApiMessageUsage[];
  settleInFlight(): ApiMessageUsage | undefined;
};

export function createUsageTracker(): UsageTracker {
  let inFlight: InFlightApiMessage | undefined;

  function settle(): ApiMessageUsage | undefined {
    if (inFlight === undefined) {
      return undefined;
    }
    const settled = snapshotOf(inFlight, true);
    inFlight = undefined;
    return settled;
  }

  return {
    record(message) {
      if (!isStreamEventMessage(message)) {
        return NO_UPDATES;
      }
      const parentToolUseId = message["parent_tool_use_id"];
      if (parentToolUseId !== null && parentToolUseId !== undefined) {
        return NO_UPDATES;
      }
      const event = message["event"];
      if (!isRecord(event)) {
        return NO_UPDATES;
      }
      if (event["type"] === MESSAGE_START_EVENT) {
        const previous = settle();
        const inner = event["message"];
        if (!isRecord(inner)) {
          return previous === undefined ? NO_UPDATES : [previous];
        }
        const rawUsage = inner["usage"];
        const messageId = inner["id"];
        const model = inner["model"];
        inFlight = {
          messageId: typeof messageId === "string" ? messageId : "",
          model: typeof model === "string" ? model : "",
          rawUsage: isRecord(rawUsage) ? { ...rawUsage } : {},
        };
        const opened = snapshotOf(inFlight, false);
        return previous === undefined ? [opened] : [previous, opened];
      }
      if (inFlight === undefined) {
        return NO_UPDATES;
      }
      if (event["type"] === MESSAGE_DELTA_EVENT) {
        const deltaUsage = event["usage"];
        if (isRecord(deltaUsage)) {
          Object.assign(inFlight.rawUsage, withNullsAsAbsent(deltaUsage));
        }
        return [snapshotOf(inFlight, false)];
      }
      if (event["type"] === MESSAGE_STOP_EVENT) {
        const settled = settle();
        return settled === undefined ? NO_UPDATES : [settled];
      }
      return NO_UPDATES;
    },

    settleInFlight: settle,
  };
}

function knownRatesFor(model: string): ModelPrice | undefined {
  return MODEL_RATE_PATTERNS.find(({ pattern }) => pattern.test(model))?.rates;
}

function ratesFor(model: string): ModelPrice {
  return knownRatesFor(model) ?? UNKNOWN_MODEL_RATES;
}

function promptTokensOf(usage: TokenUsage): number {
  return usage.inputTokens + usage.cacheCreationInputTokens + usage.cacheReadInputTokens;
}

function costAt(rates: UsdPerMillionTokens, usage: TokenUsage): number {
  return (
    (usage.inputTokens * rates.input +
      usage.outputTokens * rates.output +
      usage.cacheCreationInputTokens * rates.cacheWrite +
      usage.cacheReadInputTokens * rates.cacheRead) /
    TOKENS_PER_MILLION
  );
}

// One API request's cost: a long-prompt price applies to the request whose own prompt is over it.
export function estimateUsageCostUsd(model: string, usage: TokenUsage): number {
  const rates = ratesFor(model);
  const longPrompt = rates.longPrompt;
  if (longPrompt !== undefined && promptTokensOf(usage) > longPrompt.thresholdTokens) {
    return costAt(longPrompt.rates, usage);
  }
  return costAt(rates, usage);
}

// The SDK's per-model token totals for a session (a result message's `modelUsage`, a cost-state
// record's), keyed by the model id the SDK called. They count every request the SDK made, the ones
// the message stream never shows included: context compaction and the CLI's own small-model calls.
export type ModelUsageTotals = ReadonlyMap<string, TokenUsage>;

export const NO_MODEL_USAGE: ModelUsageTotals = new Map();

const MODEL_USAGE_FIELD = "modelUsage";

function totalCountOf(entry: Record<string, unknown>, key: string): number {
  const value = entry[key];
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? value : 0;
}

export function modelUsageTotalsOf(value: unknown): ModelUsageTotals | undefined {
  if (!isRecord(value)) {
    return undefined;
  }
  const totals = new Map<string, TokenUsage>();
  for (const [model, entry] of Object.entries(value)) {
    if (model === "" || !isRecord(entry)) {
      continue;
    }
    totals.set(model, {
      inputTokens: totalCountOf(entry, "inputTokens"),
      outputTokens: totalCountOf(entry, "outputTokens"),
      cacheCreationInputTokens: totalCountOf(entry, "cacheCreationInputTokens"),
      cacheReadInputTokens: totalCountOf(entry, "cacheReadInputTokens"),
    });
  }
  return totals;
}

export function resultModelUsageOf(message: unknown): ModelUsageTotals | undefined {
  if (!isRecord(message) || message["type"] !== RESULT_MESSAGE_TYPE) {
    return undefined;
  }
  return modelUsageTotalsOf(message[MODEL_USAGE_FIELD]);
}

// A resumed session's totals carry every earlier segment it restored; only the rest is its own.
export function ownModelUsageOf(totals: ModelUsageTotals, restored: ModelUsageTotals): ModelUsageTotals {
  const own = new Map<string, TokenUsage>();
  for (const [model, usage] of totals) {
    const before = restored.get(model) ?? ZERO_TOKEN_USAGE;
    own.set(model, {
      inputTokens: Math.max(0, usage.inputTokens - before.inputTokens),
      outputTokens: Math.max(0, usage.outputTokens - before.outputTokens),
      cacheCreationInputTokens: Math.max(0, usage.cacheCreationInputTokens - before.cacheCreationInputTokens),
      cacheReadInputTokens: Math.max(0, usage.cacheReadInputTokens - before.cacheReadInputTokens),
    });
  }
  return own;
}

function hasTokens(usage: TokenUsage): boolean {
  return (
    usage.inputTokens + usage.outputTokens + usage.cacheCreationInputTokens + usage.cacheReadInputTokens > 0
  );
}

// Totals at the standard rates: a total says nothing about any one request's prompt length, so no
// long-prompt price applies. Undefined when a model that spent tokens is not on the table.
function priceOfTotals(totals: ModelUsageTotals): number | undefined {
  let usd = 0;
  for (const [model, usage] of totals) {
    if (!hasTokens(usage)) {
      continue;
    }
    const rates = knownRatesFor(model);
    if (rates === undefined) {
      return undefined;
    }
    usd += costAt(rates, usage);
  }
  return usd;
}

export type CostMeter = {
  recordMessage(message: unknown): void;
  // restoredModelUsage null: the segment resumed a session whose restored totals could not be read.
  noteSegmentSettled(sdkCostUsd: number, restoredModelUsage?: ModelUsageTotals | null): number;
  spentUsd(): number;
  segmentEstimateUsd(): number;
  bestKnownUsd(): number;
  bestKnownUsage(): TokenUsage;
};

export type CostMeterInput = {
  priorCostUsd: number;
  priorUsage?: TokenUsage;
  onCostUpdate?: (estimatedCostUsd: number, usage: TokenUsage) => void;
  subagentApiMessages?: SubagentApiMessagesReader;
  onSegmentModelUsage?: (totals: ModelUsageTotals) => void;
};

type PricedUsage = { model: string; tokens: TokenUsage };

export function createCostMeter(input: CostMeterInput): CostMeter {
  const tracker = createUsageTracker();
  let spentUsd = input.priorCostUsd;
  let settledMessagesUsd = 0;
  let inFlightMessageUsd = 0;
  let lastReportedUsd = 0;
  let lastMainLoopModel = "";
  const meteredAgentIds = new Set<string>();
  let settledUsage = input.priorUsage ?? ZERO_TOKEN_USAGE;
  let inFlightUsage = ZERO_TOKEN_USAGE;
  let segmentModelUsage: ModelUsageTotals | undefined;

  function segmentEstimateUsd(): number {
    return settledMessagesUsd + inFlightMessageUsd;
  }

  function bestKnownUsage(): TokenUsage {
    return addTokenUsage(settledUsage, inFlightUsage);
  }

  function reportIfRisen(): void {
    const bestKnown = spentUsd + segmentEstimateUsd();
    if (bestKnown > lastReportedUsd) {
      lastReportedUsd = bestKnown;
      input.onCostUpdate?.(bestKnown, bestKnownUsage());
    }
  }

  // Settled once per agentId from the sub-agent's own transcript, each call priced at the model
  // the transcript names; the payload's last-call snapshot is the fallback, never $0. A message
  // naming no model prices at the payload's resolvedModel, then the main loop's. The tokens join
  // the usage so a killed run's reported usage includes completed sub-agents.
  function subagentSpendOf(payload: SubagentUsagePayload): readonly PricedUsage[] {
    const fallbackModel = payload.resolvedModel ?? lastMainLoopModel;
    const transcript =
      payload.sessionId === undefined
        ? undefined
        : input.subagentApiMessages?.(payload.sessionId, payload.agentId);
    if (transcript === undefined || transcript.length === 0) {
      return [{ model: fallbackModel, tokens: tokenUsageOf(payload.rawUsage) }];
    }
    return transcript.map((message) => ({
      model: message.model === "" ? fallbackModel : message.model,
      tokens: tokenUsageOf(message.rawUsage),
    }));
  }

  function recordSubagentPayload(message: unknown): void {
    const payload = subagentUsageOf(message);
    if (payload === undefined || meteredAgentIds.has(payload.agentId)) {
      return;
    }
    meteredAgentIds.add(payload.agentId);
    for (const priced of subagentSpendOf(payload)) {
      settledMessagesUsd += estimateUsageCostUsd(priced.model, priced.tokens);
      settledUsage = addTokenUsage(settledUsage, priced.tokens);
    }
    reportIfRisen();
  }

  return {
    recordMessage(message) {
      segmentModelUsage = resultModelUsageOf(message) ?? segmentModelUsage;
      const updates = tracker.record(message);
      if (updates.length === 0) {
        recordSubagentPayload(message);
        return;
      }
      for (const update of updates) {
        if (update.model !== "") {
          lastMainLoopModel = update.model;
        }
        const messageTokens = tokenUsageOf(update.rawUsage);
        const messageUsd = estimateUsageCostUsd(update.model, messageTokens);
        if (update.settled) {
          settledMessagesUsd += messageUsd;
          inFlightMessageUsd = 0;
          settledUsage = addTokenUsage(settledUsage, messageTokens);
          inFlightUsage = ZERO_TOKEN_USAGE;
        } else {
          inFlightMessageUsd = messageUsd;
          inFlightUsage = messageTokens;
        }
      }
      reportIfRisen();
    },
    noteSegmentSettled(sdkCostUsd, restoredModelUsage = NO_MODEL_USAGE) {
      // The snapshot is discarded: its tokens and USD already sit in the in-flight accumulators
      // being folded below — settling only stops a later message_start re-emitting it.
      tracker.settleInFlight();
      // The SDK's own figure comes from its price table, which priced a model it did not know at
      // Opus rates (ADR 0152); its token totals priced at this table replace it when they can. Totals
      // of a resumed session whose restored part is unknown would count earlier segments again.
      const ownTotals =
        segmentModelUsage === undefined || restoredModelUsage === null
          ? undefined
          : ownModelUsageOf(segmentModelUsage, restoredModelUsage);
      segmentModelUsage = undefined;
      const ownTotalsUsd = ownTotals === undefined ? undefined : priceOfTotals(ownTotals);
      if (ownTotals !== undefined) {
        input.onSegmentModelUsage?.(ownTotals);
      }
      spentUsd += Math.max(ownTotalsUsd ?? sdkCostUsd, segmentEstimateUsd());
      settledMessagesUsd = 0;
      inFlightMessageUsd = 0;
      settledUsage = addTokenUsage(settledUsage, inFlightUsage);
      inFlightUsage = ZERO_TOKEN_USAGE;
      return spentUsd;
    },
    spentUsd: () => spentUsd,
    segmentEstimateUsd,
    bestKnownUsd: () => spentUsd + segmentEstimateUsd(),
    bestKnownUsage,
  };
}
