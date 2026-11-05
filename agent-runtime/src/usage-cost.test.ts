import { describe, expect, it } from "bun:test";
import { messageDelta, messageStart, messageStop } from "./stream-event-fixtures.js";
import {
  type ApiMessageUsage,
  createCostMeter,
  createUsageTracker,
  estimateUsageCostUsd,
  type ModelUsageTotals,
  modelUsageTotalsOf,
  subagentUsageOf,
  type TokenUsage,
} from "./usage-cost.js";

const SONNET = "us.anthropic.claude-sonnet-4-6";
const HAIKU = "us.anthropic.claude-haiku-4-5";
const SONNET_5 = "us.anthropic.claude-sonnet-5";
const SONNET_5_5_BEDROCK = "us.anthropic.claude-sonnet-5-5";
const SONNET_5_5_FOUNDRY = "claude-sonnet-5-5";
const OPUS_5 = "us.anthropic.claude-opus-5";
const OPUS_5_5_BEDROCK = "us.anthropic.claude-opus-5-5";
const OPUS_5_5_FOUNDRY = "claude-opus-5-5";
const HAIKU_5_5_BEDROCK = "us.anthropic.claude-haiku-5-5";
const HAIKU_5_5_FOUNDRY = "claude-haiku-5-5";

function usageOf(partial: Partial<TokenUsage>): TokenUsage {
  return {
    inputTokens: 0,
    outputTokens: 0,
    cacheCreationInputTokens: 0,
    cacheReadInputTokens: 0,
    ...partial,
  };
}

function assistantMessage(model: string, usage: Record<string, unknown>): unknown {
  return { type: "assistant", message: { model, usage }, session_id: "s-1" };
}

// Field for field the payload a live run produced on SDK 0.3.227; the wrapper is the
// SDKUserMessage carrying the Agent tool_result.
function subagentResultMessage(payload: Record<string, unknown>): unknown {
  return {
    type: "user",
    message: { role: "user", content: [{ type: "tool_result", tool_use_id: "toolu_agent_1" }] },
    parent_tool_use_id: null,
    session_id: "s-1",
    tool_use_result: payload,
  };
}

const MEASURED_VERIFIER_PAYLOAD: Record<string, unknown> = {
  status: "completed",
  prompt: "verify the extraction",
  agentId: "a06d88f49db90882f",
  agentType: "verifier",
  resolvedModel: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
  totalDurationMs: 10_165,
  totalTokens: 7725,
  totalToolUseCount: 5,
  usage: {
    input_tokens: 6,
    cache_creation_input_tokens: 1061,
    cache_read_input_tokens: 6490,
    output_tokens: 168,
    server_tool_use: { web_search_requests: 0, web_fetch_requests: 0 },
    service_tier: "standard",
    cache_creation: { ephemeral_1h_input_tokens: 0, ephemeral_5m_input_tokens: 1061 },
  },
};

const MEASURED_VERIFIER_USD = (6 * 1 + 168 * 5 + 1061 * 1.25 + 6490 * 0.1) / 1_000_000;

describe("createUsageTracker", () => {
  it("reports the final message_delta cumulative count, never the message_start placeholder", () => {
    const tracker = createUsageTracker();
    tracker.record(messageStart(SONNET, "msg-1", { input_tokens: 5, output_tokens: 1 }));
    tracker.record(messageDelta({ output_tokens: 13_343, input_tokens: null }));
    const settled = tracker.record(messageStop());
    expect(settled).toHaveLength(1);
    expect(settled[0]).toMatchObject({
      messageId: "msg-1",
      model: SONNET,
      settled: true,
      rawUsage: { input_tokens: 5, output_tokens: 13_343 },
    });
  });

  it("keeps message_start input and cache counts when the delta reports them as null", () => {
    const tracker = createUsageTracker();
    tracker.record(
      messageStart(SONNET, "msg-1", {
        input_tokens: 100,
        output_tokens: 1,
        cache_creation_input_tokens: 30,
        cache_read_input_tokens: 4000,
      }),
    );
    const updates = tracker.record(
      messageDelta({ output_tokens: 500, input_tokens: null, cache_read_input_tokens: null }),
    );
    expect(updates[0]?.rawUsage).toEqual({
      input_tokens: 100,
      output_tokens: 500,
      cache_creation_input_tokens: 30,
      cache_read_input_tokens: 4000,
    });
  });

  it("carries the whole merged usage object, including output_tokens_details", () => {
    const tracker = createUsageTracker();
    tracker.record(messageStart(SONNET, "msg-1", { input_tokens: 5, output_tokens: 1 }));
    tracker.record(messageDelta({ output_tokens: 9000, output_tokens_details: { thinking_tokens: 8000 } }));
    const settled = tracker.record(messageStop());
    expect(settled[0]?.rawUsage).toMatchObject({
      input_tokens: 5,
      output_tokens: 9000,
      output_tokens_details: { thinking_tokens: 8000 },
    });
  });

  it("settles an in-flight message when the next message_start arrives without a stop", () => {
    const tracker = createUsageTracker();
    tracker.record(messageStart(SONNET, "msg-1", { input_tokens: 5, output_tokens: 1 }));
    tracker.record(messageDelta({ output_tokens: 700 }));
    const updates = tracker.record(messageStart(SONNET, "msg-2", { input_tokens: 9, output_tokens: 1 }));
    expect(updates).toHaveLength(2);
    expect(updates[0]).toMatchObject({ messageId: "msg-1", settled: true, rawUsage: { output_tokens: 700 } });
    expect(updates[1]).toMatchObject({ messageId: "msg-2", settled: false });
  });

  it("settleInFlight flushes the pending message once and only once", () => {
    const tracker = createUsageTracker();
    tracker.record(messageStart(SONNET, "msg-1", { input_tokens: 5, output_tokens: 1 }));
    tracker.record(messageDelta({ output_tokens: 800 }));
    const flushed = tracker.settleInFlight();
    expect(flushed).toMatchObject({ messageId: "msg-1", settled: true, rawUsage: { output_tokens: 800 } });
    expect(tracker.settleInFlight()).toBeUndefined();
  });

  it("ignores assistant messages and other non-stream-event shapes", () => {
    const tracker = createUsageTracker();
    expect(tracker.record(assistantMessage(SONNET, { input_tokens: 5, output_tokens: 2 }))).toEqual([]);
    expect(tracker.record({ type: "result", subtype: "success" })).toEqual([]);
    expect(tracker.record({ type: "stream_event" })).toEqual([]);
    expect(tracker.record({ type: "stream_event", event: { type: "content_block_delta" } })).toEqual([]);
    expect(tracker.record(null)).toEqual([]);
  });

  it("ignores delta and stop events that arrive with no message in flight", () => {
    const tracker = createUsageTracker();
    expect(tracker.record(messageDelta({ output_tokens: 500 }))).toEqual([]);
    expect(tracker.record(messageStop())).toEqual([]);
  });

  it("ignores subagent stream events, which never settle or merge into the main loop's in-flight message", () => {
    const tracker = createUsageTracker();
    tracker.record(messageStart(SONNET, "main-1", { input_tokens: 5, output_tokens: 1 }));
    const subStart = tracker.record(
      messageStart(HAIKU, "sub-1", { input_tokens: 9, output_tokens: 1 }, "toolu_sub"),
    );
    expect(subStart).toEqual([]);
    expect(tracker.record(messageDelta({ output_tokens: 400 }, "toolu_sub"))).toEqual([]);
    expect(tracker.record(messageStop("toolu_sub"))).toEqual([]);
    tracker.record(messageDelta({ output_tokens: 700 }));
    const settled = tracker.record(messageStop());
    expect(settled).toHaveLength(1);
    expect(settled[0]).toMatchObject({
      messageId: "main-1",
      settled: true,
      rawUsage: { input_tokens: 5, output_tokens: 700 },
    });
  });
});

describe("estimateUsageCostUsd", () => {
  it("prices sonnet-family usage at $3/$15 per MTok with 1.25x cache writes and 0.1x cache reads", () => {
    expect(estimateUsageCostUsd(SONNET, usageOf({ inputTokens: 1_000_000 }))).toBeCloseTo(3, 10);
    expect(estimateUsageCostUsd(SONNET, usageOf({ outputTokens: 1_000_000 }))).toBeCloseTo(15, 10);
    expect(estimateUsageCostUsd(SONNET, usageOf({ cacheCreationInputTokens: 1_000_000 }))).toBeCloseTo(
      3.75,
      10,
    );
    expect(estimateUsageCostUsd(SONNET, usageOf({ cacheReadInputTokens: 1_000_000 }))).toBeCloseTo(0.3, 10);
  });

  it.each([
    [SONNET_5, 0.2],
    [SONNET_5_5_BEDROCK, 0.1],
    [SONNET_5_5_FOUNDRY, 0.1],
  ])("prices %s at the Sonnet 5 $2/$10 per MTok list rates with cache reads at $%s", (model, cacheRead) => {
    expect(
      estimateUsageCostUsd(model, usageOf({ inputTokens: 1_000_000, outputTokens: 1_000_000 })),
    ).toBeCloseTo(12, 10);
    expect(estimateUsageCostUsd(model, usageOf({ cacheCreationInputTokens: 1_000_000 }))).toBeCloseTo(
      2.5,
      10,
    );
    expect(estimateUsageCostUsd(model, usageOf({ cacheReadInputTokens: 1_000_000 }))).toBeCloseTo(
      cacheRead,
      10,
    );
  });

  it.each([OPUS_5_5_BEDROCK, OPUS_5_5_FOUNDRY])(
    "prices %s at Opus 5.5's own $4/$20 per MTok list rates, not the generic opus rates",
    (model) => {
      expect(
        estimateUsageCostUsd(model, usageOf({ inputTokens: 1_000_000, outputTokens: 1_000_000 })),
      ).toBeCloseTo(24, 10);
      expect(estimateUsageCostUsd(model, usageOf({ cacheCreationInputTokens: 1_000_000 }))).toBeCloseTo(
        5,
        10,
      );
      expect(estimateUsageCostUsd(model, usageOf({ cacheReadInputTokens: 1_000_000 }))).toBeCloseTo(0.2, 10);
    },
  );

  it("prices opus 5 at the generic opus $5/$25 per MTok rates", () => {
    expect(
      estimateUsageCostUsd(OPUS_5, usageOf({ inputTokens: 1_000_000, outputTokens: 1_000_000 })),
    ).toBeCloseTo(30, 10);
    expect(estimateUsageCostUsd(OPUS_5, usageOf({ cacheReadInputTokens: 1_000_000 }))).toBeCloseTo(0.5, 10);
  });

  it("prices haiku-family usage at $1/$5 per MTok", () => {
    expect(
      estimateUsageCostUsd(HAIKU, usageOf({ inputTokens: 1_000_000, outputTokens: 1_000_000 })),
    ).toBeCloseTo(6, 10);
  });

  it.each([HAIKU_5_5_BEDROCK, HAIKU_5_5_FOUNDRY])(
    "prices %s at Haiku 5.5's own $0.10/$0.50 per MTok list rates, not the haiku-family rates",
    (model) => {
      expect(
        estimateUsageCostUsd(model, usageOf({ inputTokens: 100_000, outputTokens: 1_000_000 })),
      ).toBeCloseTo(0.01 + 0.5, 10);
      expect(estimateUsageCostUsd(model, usageOf({ cacheCreationInputTokens: 100_000 }))).toBeCloseTo(
        0.0125,
        10,
      );
      expect(estimateUsageCostUsd(model, usageOf({ cacheReadInputTokens: 100_000 }))).toBeCloseTo(0.001, 10);
    },
  );

  // A Haiku 5.5 request whose prompt (input plus cache reads and writes) is over 100,000
  // tokens pays 5x on every token of that request, output included.
  it.each([HAIKU_5_5_BEDROCK, HAIKU_5_5_FOUNDRY])(
    "prices a %s request over 100,000 prompt tokens at the long-prompt rates",
    (model) => {
      const longRequest = usageOf({
        inputTokens: 1_000,
        outputTokens: 10_000,
        cacheCreationInputTokens: 20_000,
        cacheReadInputTokens: 80_000,
      });
      expect(estimateUsageCostUsd(model, longRequest)).toBeCloseTo(
        (1_000 * 0.5 + 10_000 * 2.5 + 20_000 * 0.625 + 80_000 * 0.05) / 1_000_000,
        12,
      );
      const atThreshold = usageOf({ ...longRequest, cacheReadInputTokens: 79_000 });
      expect(estimateUsageCostUsd(model, atThreshold)).toBeCloseTo(
        (1_000 * 0.1 + 10_000 * 0.5 + 20_000 * 0.125 + 79_000 * 0.01) / 1_000_000,
        12,
      );
    },
  );

  it("has no long-prompt price for other models", () => {
    expect(estimateUsageCostUsd(SONNET_5_5_BEDROCK, usageOf({ cacheReadInputTokens: 900_000 }))).toBeCloseTo(
      0.09,
      10,
    );
  });

  it("estimates unknown models at the report tier's sonnet rates rather than zero", () => {
    expect(estimateUsageCostUsd("mystery-model", usageOf({ inputTokens: 1_000_000 }))).toBeCloseTo(3, 10);
    expect(estimateUsageCostUsd("", usageOf({ outputTokens: 1_000_000 }))).toBeCloseTo(15, 10);
  });
});

describe("subagent spend from the Agent tool_result payload", () => {
  it("adds a completed sub-agent payload to the segment estimate at its resolved model's rates", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(subagentResultMessage(MEASURED_VERIFIER_PAYLOAD));
    expect(meter.segmentEstimateUsd()).toBeCloseTo(MEASURED_VERIFIER_USD, 10);
  });

  it("keeps the killed-run floor complete: main-loop messages plus sub-agent payloads", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(messageStart(HAIKU, "main-1", { input_tokens: 10, output_tokens: 1 }));
    meter.recordMessage(messageDelta({ output_tokens: 300 }));
    meter.recordMessage(messageStop());
    meter.recordMessage(subagentResultMessage(MEASURED_VERIFIER_PAYLOAD));
    const mainUsd = (10 * 1 + 300 * 5) / 1_000_000;
    expect(meter.bestKnownUsd()).toBeCloseTo(mainUsd + MEASURED_VERIFIER_USD, 10);
    expect(meter.noteSegmentSettled(0)).toBeCloseTo(mainUsd + MEASURED_VERIFIER_USD, 10);
  });

  it("meters each sub-agent once even when its payload is seen twice", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(subagentResultMessage(MEASURED_VERIFIER_PAYLOAD));
    meter.recordMessage(subagentResultMessage(MEASURED_VERIFIER_PAYLOAD));
    expect(meter.segmentEstimateUsd()).toBeCloseTo(MEASURED_VERIFIER_USD, 10);
  });

  it("ignores payloads that carry no usage, including the async_launched variant", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(
      subagentResultMessage({ status: "async_launched", agentId: "a-bg", agentType: "verifier" }),
    );
    meter.recordMessage(subagentResultMessage({ agentId: "a-2", usage: "not-a-record" }));
    expect(meter.segmentEstimateUsd()).toBe(0);
  });

  it("prices a payload without resolvedModel at the last main-loop model, unknown-tier when none seen", () => {
    const { resolvedModel: _dropped, ...withoutModel } = MEASURED_VERIFIER_PAYLOAD;
    const coldMeter = createCostMeter({ priorCostUsd: 0 });
    coldMeter.recordMessage(subagentResultMessage({ ...withoutModel, agentId: "a-cold" }));
    const sonnetUsd = (6 * 3 + 168 * 15 + 1061 * 3.75 + 6490 * 0.3) / 1_000_000;
    expect(coldMeter.segmentEstimateUsd()).toBeCloseTo(sonnetUsd, 10);

    const warmMeter = createCostMeter({ priorCostUsd: 0 });
    warmMeter.recordMessage(messageStart(HAIKU, "main-1", { input_tokens: 0, output_tokens: 1 }));
    warmMeter.recordMessage(messageStop());
    warmMeter.recordMessage(subagentResultMessage({ ...withoutModel, agentId: "a-warm" }));
    const settledMainUsd = (1 * 5) / 1_000_000;
    expect(warmMeter.segmentEstimateUsd()).toBeCloseTo(MEASURED_VERIFIER_USD + settledMainUsd, 10);
  });
});

describe("createCostMeter", () => {
  it("meters only the main loop when subagent stream events interleave", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(messageStart(SONNET, "main-1", { input_tokens: 5, output_tokens: 1 }));
    meter.recordMessage(
      messageStart(SONNET, "sub-1", { input_tokens: 900_000, output_tokens: 1 }, "toolu_sub"),
    );
    meter.recordMessage(messageDelta({ output_tokens: 800_000 }, "toolu_sub"));
    meter.recordMessage(messageStop("toolu_sub"));
    meter.recordMessage(messageDelta({ output_tokens: 700_000 }));
    meter.recordMessage(messageStop());
    expect(meter.segmentEstimateUsd()).toBeCloseTo((5 * 3 + 700_000 * 15) / 1_000_000, 10);
  });

  it("prices output from the final message_delta, never a placeholder snapshot", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 1_000_000, output_tokens: 1 }));
    meter.recordMessage(assistantMessage(SONNET, { input_tokens: 1_000_000, output_tokens: 2 }));
    meter.recordMessage(messageDelta({ output_tokens: 1_000_000, input_tokens: null }));
    meter.recordMessage(messageStop());
    expect(meter.segmentEstimateUsd()).toBeCloseTo(18, 10);
  });

  it("replaces the in-flight estimate on each cumulative delta instead of summing snapshots", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 0, output_tokens: 1 }));
    meter.recordMessage(messageDelta({ output_tokens: 400_000 }));
    meter.recordMessage(messageDelta({ output_tokens: 1_000_000 }));
    meter.recordMessage(messageStop());
    expect(meter.segmentEstimateUsd()).toBeCloseTo(15, 10);
  });

  it("accumulates settled messages and reports each rise", () => {
    const reported: number[] = [];
    const meter = createCostMeter({ priorCostUsd: 0, onCostUpdate: (cost) => reported.push(cost) });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 1_000_000 }));
    meter.recordMessage({ type: "system", subtype: "init" });
    meter.recordMessage(messageStop());
    meter.recordMessage(messageStart(SONNET, "msg-2", { input_tokens: 0 }));
    meter.recordMessage(messageDelta({ output_tokens: 1_000_000 }));
    meter.recordMessage(messageStop());
    expect(meter.segmentEstimateUsd()).toBeCloseTo(18, 10);
    expect(meter.bestKnownUsd()).toBeCloseTo(18, 10);
    expect(reported).toHaveLength(2);
    expect(reported[0]).toBeCloseTo(3, 10);
    expect(reported[1]).toBeCloseTo(18, 10);
  });

  it("treats null, absent, and malformed token counts as zero when pricing", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(
      messageStart(SONNET, "msg-1", {
        input_tokens: 1_000_000,
        output_tokens: null,
        cache_creation_input_tokens: "many",
        cache_read_input_tokens: -5,
      }),
    );
    meter.recordMessage(messageStop());
    expect(meter.segmentEstimateUsd()).toBeCloseTo(3, 10);
  });

  it("folds the larger of the SDK segment cost and the usage estimate into spend, returning the total", () => {
    const meter = createCostMeter({ priorCostUsd: 0.5 });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 1_000_000 }));
    meter.recordMessage(messageStop());
    expect(meter.noteSegmentSettled(0.2)).toBeCloseTo(3.5, 10);
    expect(meter.spentUsd()).toBeCloseTo(3.5, 10);
    expect(meter.segmentEstimateUsd()).toBe(0);
  });

  it("takes the SDK segment cost when it exceeds the usage estimate", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 1_000_000 }));
    meter.recordMessage(messageStop());
    expect(meter.noteSegmentSettled(7.5)).toBeCloseTo(7.5, 10);
    expect(meter.spentUsd()).toBeCloseTo(7.5, 10);
  });

  it("counts an in-flight message the stream never settled", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 0, output_tokens: 1 }));
    meter.recordMessage(messageDelta({ output_tokens: 1_000_000 }));
    expect(meter.segmentEstimateUsd()).toBeCloseTo(15, 10);
    expect(meter.bestKnownUsd()).toBeCloseTo(15, 10);
  });

  it("starts from the prior cost so a cold resume never under-reports", () => {
    const meter = createCostMeter({ priorCostUsd: 1.25 });
    expect(meter.spentUsd()).toBe(1.25);
    expect(meter.bestKnownUsd()).toBe(1.25);
  });
});

describe("createCostMeter token totals", () => {
  it("accumulates token totals across settled messages and reports them with each cost rise", () => {
    const reported: { cost: number; usage: TokenUsage }[] = [];
    const meter = createCostMeter({
      priorCostUsd: 0,
      onCostUpdate: (cost, usage) => reported.push({ cost, usage }),
    });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 100, cache_read_input_tokens: 40 }));
    meter.recordMessage(messageStop());
    meter.recordMessage(messageStart(SONNET, "msg-2", { input_tokens: 7, output_tokens: 1 }));
    meter.recordMessage(messageDelta({ output_tokens: 900 }));
    meter.recordMessage(messageStop());
    expect(meter.bestKnownUsage()).toEqual(
      usageOf({ inputTokens: 107, outputTokens: 900, cacheReadInputTokens: 40 }),
    );
    const last = reported[reported.length - 1];
    expect(last?.usage).toEqual(usageOf({ inputTokens: 107, outputTokens: 900, cacheReadInputTokens: 40 }));
  });

  it("counts an in-flight message the stream never settled in the token totals", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 50, output_tokens: 1 }));
    meter.recordMessage(messageDelta({ output_tokens: 500 }));
    expect(meter.bestKnownUsage()).toEqual(usageOf({ inputTokens: 50, outputTokens: 500 }));
  });

  it("folds in-flight tokens into the totals when a segment settles", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 10, output_tokens: 1 }));
    meter.recordMessage(messageDelta({ output_tokens: 200 }));
    meter.noteSegmentSettled(0);
    meter.recordMessage(messageStart(SONNET, "msg-2", { input_tokens: 5 }));
    meter.recordMessage(messageStop());
    expect(meter.bestKnownUsage()).toEqual(usageOf({ inputTokens: 15, outputTokens: 200 }));
  });

  it("starts from the prior usage so a cold resume never under-counts tokens", () => {
    const meter = createCostMeter({
      priorCostUsd: 1,
      priorUsage: usageOf({ inputTokens: 1000, outputTokens: 300 }),
    });
    meter.recordMessage(messageStart(SONNET, "msg-1", { input_tokens: 10 }));
    meter.recordMessage(messageStop());
    expect(meter.bestKnownUsage()).toEqual(usageOf({ inputTokens: 1010, outputTokens: 300 }));
  });
});

// Field for field the completed Agent payload one drift-calibration run produced on SDK
// 0.3.251 / CLI 2.1.251: its `usage` is the sub-agent's LAST API call (the counters sum to
// `totalTokens`, the final context size), not the sub-agent's spend.
const MEASURED_HELPER_SESSION_ID = "411c8fc3-ae63-4b79-9538-1f25d1b9e63b";
const MEASURED_HELPER_AGENT_ID = "afad00da6bccb7101";
const TRANSCRIPT_MODEL = "anthropic.claude-haiku-4-5-20251001-v1:0";

const MEASURED_HELPER_PAYLOAD: Record<string, unknown> = {
  status: "completed",
  prompt: "Run the bash command: echo $((17*23))\n\nReport the output verbatim.",
  agentId: MEASURED_HELPER_AGENT_ID,
  agentType: "helper",
  content: [{ type: "text", text: "391" }],
  resolvedModel: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
  totalDurationMs: 2686,
  totalTokens: 6350,
  totalToolUseCount: 1,
  usage: {
    input_tokens: 5,
    cache_creation_input_tokens: 207,
    cache_read_input_tokens: 6134,
    output_tokens: 4,
    output_tokens_details: { thinking_tokens: 0 },
    service_tier: "standard",
  },
};

// The same sub-agent's transcript, one settled usage per API message (2 calls: the Bash
// tool_use turn, then the final text turn).
const MEASURED_HELPER_TRANSCRIPT: readonly ApiMessageUsage[] = [
  {
    messageId: "msg_bdrk_h56yuig7gltdvvrupub62imq4446he2losnyph2326pv4a6apetq",
    model: TRANSCRIPT_MODEL,
    rawUsage: {
      input_tokens: 10,
      cache_creation_input_tokens: 6134,
      cache_read_input_tokens: 0,
      output_tokens: 128,
    },
    settled: true,
  },
  {
    messageId: "msg_bdrk_hhxls5bspuevedwb3uoui5uj7agdl7i3t22hgrxdqiegu2zptk6q",
    model: TRANSCRIPT_MODEL,
    rawUsage: {
      input_tokens: 5,
      cache_creation_input_tokens: 207,
      cache_read_input_tokens: 6134,
      output_tokens: 4,
    },
    settled: true,
  },
];

const MEASURED_HELPER_TRANSCRIPT_USD = (15 * 1 + 132 * 5 + 6341 * 1.25 + 6134 * 0.1) / 1_000_000;
const MEASURED_HELPER_LAST_CALL_USD = (5 * 1 + 4 * 5 + 207 * 1.25 + 6134 * 0.1) / 1_000_000;

function measuredHelperMessage(): unknown {
  return {
    type: "user",
    message: { role: "user", content: [{ type: "tool_result", tool_use_id: "toolu_bdrk_agent" }] },
    parent_tool_use_id: null,
    session_id: MEASURED_HELPER_SESSION_ID,
    tool_use_result: MEASURED_HELPER_PAYLOAD,
  };
}

describe("sub-agent spend on SDK 0.3.251, where the payload's usage is the last API call only", () => {
  it("exposes the session the payload rode in, so its transcript can be found", () => {
    expect(subagentUsageOf(measuredHelperMessage())?.sessionId).toBe(MEASURED_HELPER_SESSION_ID);
    expect(
      subagentUsageOf({ type: "user", tool_use_result: MEASURED_HELPER_PAYLOAD })?.sessionId,
    ).toBeUndefined();
  });

  it("prices every API message of the sub-agent's transcript, not the payload's last-call snapshot", () => {
    const asked: [string, string][] = [];
    const meter = createCostMeter({
      priorCostUsd: 0,
      subagentApiMessages: (sessionId, agentId) => {
        asked.push([sessionId, agentId]);
        return MEASURED_HELPER_TRANSCRIPT;
      },
    });
    meter.recordMessage(measuredHelperMessage());
    expect(asked).toEqual([[MEASURED_HELPER_SESSION_ID, MEASURED_HELPER_AGENT_ID]]);
    expect(meter.segmentEstimateUsd()).toBeCloseTo(MEASURED_HELPER_TRANSCRIPT_USD, 10);
    expect(meter.bestKnownUsage()).toEqual(
      usageOf({
        inputTokens: 15,
        outputTokens: 132,
        cacheCreationInputTokens: 6341,
        cacheReadInputTokens: 6134,
      }),
    );
  });

  it("falls back to the payload's own last-call snapshot when no transcript can be read — never $0", () => {
    const noReader = createCostMeter({ priorCostUsd: 0 });
    noReader.recordMessage(measuredHelperMessage());
    expect(noReader.segmentEstimateUsd()).toBeCloseTo(MEASURED_HELPER_LAST_CALL_USD, 10);

    const missing = createCostMeter({ priorCostUsd: 0, subagentApiMessages: () => undefined });
    missing.recordMessage(measuredHelperMessage());
    expect(missing.segmentEstimateUsd()).toBeCloseTo(MEASURED_HELPER_LAST_CALL_USD, 10);

    const empty = createCostMeter({ priorCostUsd: 0, subagentApiMessages: () => [] });
    empty.recordMessage(measuredHelperMessage());
    expect(empty.segmentEstimateUsd()).toBeCloseTo(MEASURED_HELPER_LAST_CALL_USD, 10);
  });

  it("meters the transcript once per agent even when the payload is seen twice", () => {
    let reads = 0;
    const meter = createCostMeter({
      priorCostUsd: 0,
      subagentApiMessages: () => {
        reads += 1;
        return MEASURED_HELPER_TRANSCRIPT;
      },
    });
    meter.recordMessage(measuredHelperMessage());
    meter.recordMessage(measuredHelperMessage());
    expect(reads).toBe(1);
    expect(meter.segmentEstimateUsd()).toBeCloseTo(MEASURED_HELPER_TRANSCRIPT_USD, 10);
  });

  it("prices each transcript message at its own model, the payload's resolvedModel when a line names none", () => {
    const meter = createCostMeter({
      priorCostUsd: 0,
      subagentApiMessages: () => [
        {
          messageId: "m-opus",
          model: "us.anthropic.claude-opus-4-8",
          rawUsage: { output_tokens: 1_000_000 },
          settled: true,
        },
        { messageId: "m-unnamed", model: "", rawUsage: { output_tokens: 1_000_000 }, settled: true },
      ],
    });
    meter.recordMessage(measuredHelperMessage());
    expect(meter.segmentEstimateUsd()).toBeCloseTo(25 + 5, 10);
  });
});

// Field for field the per-model totals of a result message on SDK 0.3.281 (a Foundry run, 2026-10-05).
function resultWithModelUsage(
  totalCostUsd: number,
  modelUsage: Record<string, Record<string, number>>,
): unknown {
  return {
    type: "result",
    subtype: "success",
    session_id: "s-1",
    total_cost_usd: totalCostUsd,
    modelUsage: Object.fromEntries(
      Object.entries(modelUsage).map(([model, counts]) => [
        model,
        { webSearchRequests: 0, costUSD: totalCostUsd, contextWindow: 200_000, costBasis: "list", ...counts },
      ]),
    ),
  };
}

function totalsOf(entries: Record<string, Partial<TokenUsage>>): ModelUsageTotals {
  return new Map(Object.entries(entries).map(([model, partial]) => [model, usageOf(partial)]));
}

describe("a settled segment priced from the SDK's per-model totals", () => {
  // The SDK 0.3.281 price table does not know Haiku 5.5 and priced it at Opus 5.5 rates:
  // 4 / 72 / 9,219 cache-write tokens reported as $0.047551.
  it("prices the segment from its per-model totals at the runtime's own rates, not the SDK figure", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(
      resultWithModelUsage(0.047551, {
        [HAIKU_5_5_BEDROCK]: {
          inputTokens: 4,
          outputTokens: 72,
          cacheCreationInputTokens: 9_219,
          cacheReadInputTokens: 0,
        },
      }),
    );
    expect(meter.noteSegmentSettled(0.047551)).toBeCloseTo(
      (4 * 0.1 + 72 * 0.5 + 9_219 * 0.125) / 1_000_000,
      12,
    );
  });

  it("counts every model in the totals, the CLI's own small-model calls included", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(
      resultWithModelUsage(0.6, {
        [OPUS_5_5_BEDROCK]: {
          inputTokens: 32,
          outputTokens: 11_707,
          cacheCreationInputTokens: 38_715,
          cacheReadInputTokens: 434_582,
        },
        "us.anthropic.claude-haiku-4-5-20251001-v1:0": { inputTokens: 1_001, outputTokens: 25 },
      }),
    );
    expect(meter.noteSegmentSettled(0.5158854)).toBeCloseTo(0.5147594 + 0.001126, 10);
  });

  it("keeps the SDK figure when a model in the totals is not on the runtime's table", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(resultWithModelUsage(0.75, { "mystery-model": { outputTokens: 1_000 } }));
    expect(meter.noteSegmentSettled(0.75)).toBeCloseTo(0.75, 10);
  });

  it("subtracts the totals the resumed session restored, so earlier segments are not priced again", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(
      resultWithModelUsage(0.2133425, {
        [SONNET_5]: {
          inputTokens: 22,
          outputTokens: 3_748,
          cacheCreationInputTokens: 42_679,
          cacheReadInputTokens: 340_605,
        },
      }),
    );
    const restored = totalsOf({
      [SONNET_5]: {
        inputTokens: 10,
        outputTokens: 1_527,
        cacheCreationInputTokens: 27_982,
        cacheReadInputTokens: 102_023,
      },
    });
    expect(meter.noteSegmentSettled(0.1066929, restored)).toBeCloseTo(
      (12 * 2 + 2_221 * 10 + 14_697 * 2.5 + 238_582 * 0.2) / 1_000_000,
      10,
    );
  });

  it("still takes the per-request estimate when it is higher, as for a long Haiku 5.5 prompt", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    meter.recordMessage(
      messageStart(HAIKU_5_5_BEDROCK, "msg-1", { input_tokens: 0, cache_read_input_tokens: 200_000 }),
    );
    meter.recordMessage(messageStop());
    meter.recordMessage(resultWithModelUsage(1, { [HAIKU_5_5_BEDROCK]: { cacheReadInputTokens: 200_000 } }));
    expect(meter.noteSegmentSettled(1)).toBeCloseTo((200_000 * 0.05) / 1_000_000, 12);
  });

  it("keeps the SDK figure and writes no totals when a resumed session's restored totals are unknown", () => {
    const seen: ModelUsageTotals[] = [];
    const meter = createCostMeter({ priorCostUsd: 0, onSegmentModelUsage: (totals) => seen.push(totals) });
    meter.recordMessage(resultWithModelUsage(0.3, { [SONNET_5]: { outputTokens: 90_000 } }));
    expect(meter.noteSegmentSettled(0.3, null)).toBeCloseTo(0.3, 10);
    expect(seen).toEqual([]);
  });

  it("falls back to the SDK figure for a segment whose stream carried no result totals", () => {
    const meter = createCostMeter({ priorCostUsd: 0 });
    expect(meter.noteSegmentSettled(0.4)).toBeCloseTo(0.4, 10);
  });

  it("hands each settled segment's own totals to the observer, once", () => {
    const seen: ModelUsageTotals[] = [];
    const meter = createCostMeter({ priorCostUsd: 0, onSegmentModelUsage: (totals) => seen.push(totals) });
    meter.recordMessage(resultWithModelUsage(0.5, { [SONNET_5]: { inputTokens: 30, outputTokens: 7 } }));
    meter.noteSegmentSettled(0.5, totalsOf({ [SONNET_5]: { inputTokens: 10, outputTokens: 2 } }));
    meter.noteSegmentSettled(0);
    expect(seen).toEqual([totalsOf({ [SONNET_5]: { inputTokens: 20, outputTokens: 5 } })]);
  });
});

describe("modelUsageTotalsOf", () => {
  it("reads each model's four token counts and nothing else", () => {
    expect(
      modelUsageTotalsOf({
        [SONNET_5]: {
          inputTokens: 1,
          outputTokens: 2,
          cacheCreationInputTokens: 3,
          cacheReadInputTokens: 4,
          costUSD: 9,
        },
      }),
    ).toEqual(
      totalsOf({
        [SONNET_5]: { inputTokens: 1, outputTokens: 2, cacheCreationInputTokens: 3, cacheReadInputTokens: 4 },
      }),
    );
  });

  it("is undefined for anything that is not a per-model record", () => {
    expect(modelUsageTotalsOf(undefined)).toBeUndefined();
    expect(modelUsageTotalsOf("x")).toBeUndefined();
    expect(modelUsageTotalsOf({ [SONNET_5]: "x" })).toEqual(new Map());
  });
});
