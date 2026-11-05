import { describe, expect, it } from "bun:test";
import type { RunFailure, RunReportResult } from "../../src/run-report.js";
import {
  contextTokensOf,
  type DriftRunRecord,
  parseDriftRunRecord,
  projectDriftRecord,
  skippedDriftRecord,
  traceFactsOf,
} from "./record.js";

const HAIKU = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
const PROMPT_SHA = "ab".repeat(32);

function pack(lines: Record<string, unknown>[]): string {
  return `${lines.map((line) => JSON.stringify(line)).join("\n")}\n`;
}

function envelope(): Record<string, unknown> {
  return {
    type: "envelope",
    traceVersion: 7,
    modelPin: HAIKU,
    promptSha256: PROMPT_SHA,
    stagedInputsSha256: "absent",
    sdkVersion: "0.3.251",
  };
}

function initMessage(): Record<string, unknown> {
  return {
    type: "message",
    seq: 1,
    message: { type: "system", subtype: "init", claude_code_version: "2.1.251", model: HAIKU },
  };
}

function usage(messageId: string, tokens: Record<string, number>): Record<string, unknown> {
  return { type: "usage", messageId, model: HAIKU, usage: { output_tokens_details: null, ...tokens } };
}

function toolCall(toolName: string): Record<string, unknown> {
  return { type: "tool_call", toolUseId: `tu-${toolName}`, toolName, outputBytes: 3, durationMs: 40 };
}

function resultMessage(fields: Record<string, unknown>): Record<string, unknown> {
  return { type: "message", seq: 9, message: { type: "result", subtype: "success", ...fields } };
}

const HEALTHY_PACK = pack([
  envelope(),
  initMessage(),
  usage("m1", { input_tokens: 100, output_tokens: 40, cache_creation_input_tokens: 3000 }),
  toolCall("Bash"),
  usage("m2", { input_tokens: 120, output_tokens: 30, cache_read_input_tokens: 3000 }),
  toolCall("Bash"),
  usage("m3", { input_tokens: 130, output_tokens: 20, cache_read_input_tokens: 3000 }),
  toolCall("StructuredOutput"),
  resultMessage({ num_turns: 3, total_cost_usd: 0.0123 }),
]);

function healthyResult(structuredOutput: unknown = { answer: 339 }): RunReportResult {
  return {
    executionId: "drift-x",
    output: "339",
    estimatedCostUsd: 0.0123,
    sessionId: "s1",
    usage: { inputTokens: 350, outputTokens: 90, cacheCreationInputTokens: 3000, cacheReadInputTokens: 6000 },
    structuredOutput,
    tracePath: "/workspaces/traces/drift-x/attempt-1",
  };
}

function projected(overrides: {
  result?: RunReportResult | null;
  failure?: RunFailure | null;
  tracePackText?: string | null;
}): DriftRunRecord {
  return projectDriftRecord({
    promptId: "bash-loop",
    repeat: 1,
    executionId: "drift-x",
    startedAt: "2026-09-04T10:00:00.000Z",
    wallMs: 12345,
    expectedAnswer: 339,
    maxTurns: 10,
    result: overrides.result === undefined ? healthyResult() : overrides.result,
    failure: overrides.failure ?? null,
    tracePackText: overrides.tracePackText === undefined ? HEALTHY_PACK : overrides.tracePackText,
  });
}

describe("traceFactsOf", () => {
  it("projects identity, per-message usage, tool calls and the result message off the pack", () => {
    const facts = traceFactsOf(HEALTHY_PACK);
    expect(facts.identity).toEqual({
      sdkVersion: "0.3.251",
      cliVersion: "2.1.251",
      modelPin: HAIKU,
      promptSha256: PROMPT_SHA,
      stagedInputsSha256: "absent",
    });
    expect(facts.apiMessages).toBe(3);
    expect(facts.tokens).toEqual({ input: 350, output: 90, cacheWrite: 3000, cacheRead: 6000 });
    expect(facts.toolCalls).toEqual({ Bash: 2, StructuredOutput: 1 });
    expect(facts.numTurns).toBe(3);
    expect(facts.sdkCostUsd).toBe(0.0123);
    // Haiku list rates: 350 in @ $1, 90 out @ $5, 3000 cache write @ $1.25, 6000 cache read @ $0.10 per MTok.
    expect(facts.floorCostUsd).toBeCloseTo((350 * 1 + 90 * 5 + 3000 * 1.25 + 6000 * 0.1) / 1_000_000, 9);
  });

  it("sums a sub-agent's usage records with the main loop's and never prices the Agent payload itself", () => {
    const subagentPayload = {
      type: "message",
      seq: 5,
      message: {
        type: "user",
        session_id: "session-1",
        tool_use_result: {
          status: "completed",
          agentId: "agent-1",
          resolvedModel: HAIKU,
          usage: {
            input_tokens: 5,
            output_tokens: 4,
            cache_creation_input_tokens: 0,
            cache_read_input_tokens: 900,
          },
        },
      },
    };
    const facts = traceFactsOf(
      pack([
        envelope(),
        usage("m1", { input_tokens: 10, output_tokens: 5 }),
        {
          ...usage("sub-1", { input_tokens: 1000, output_tokens: 100, cache_creation_input_tokens: 900 }),
          agentId: "agent-1",
          source: "subagent_transcript",
        },
        {
          ...usage("sub-2", { input_tokens: 5, output_tokens: 4, cache_read_input_tokens: 900 }),
          agentId: "agent-1",
          source: "subagent_transcript",
        },
        subagentPayload,
        subagentPayload,
        resultMessage({ num_turns: 2, total_cost_usd: 0.002 }),
      ]),
    );
    expect(facts.tokens).toEqual({ input: 1015, output: 109, cacheWrite: 900, cacheRead: 900 });
    expect(facts.floorCostUsd).toBeCloseTo(
      (10 + 1000 + 5 + (5 + 100 + 4) * 5 + 900 * 1.25 + 900 * 0.1) / 1_000_000,
      9,
    );
    expect(facts.apiMessages).toBe(3);
  });

  it("tolerates a pack with no result, no init and unparsable lines", () => {
    const facts = traceFactsOf(
      `${JSON.stringify(envelope())}\nnot json\n${JSON.stringify(toolCall("Read"))}\n`,
    );
    expect(facts.identity.cliVersion).toBeNull();
    expect(facts.numTurns).toBeNull();
    expect(facts.sdkCostUsd).toBeNull();
    expect(facts.floorCostUsd).toBeNull();
    expect(facts.toolCalls).toEqual({ Read: 1 });
    expect(facts.tokens).toEqual({ input: 0, output: 0, cacheWrite: 0, cacheRead: 0 });
  });
});

describe("projectDriftRecord", () => {
  it("marks a run correct when the structured answer matches and carries every measured fact", () => {
    const record = projected({});
    expect(record.outcome).toBe("correct");
    expect(record.wallMs).toBe(12345);
    expect(record.numTurns).toBe(3);
    expect(record.maxTurnsHit).toBe(false);
    expect(record.toolCalls).toEqual({ Bash: 2, StructuredOutput: 1 });
    expect(record.sdkCostUsd).toBe(0.0123);
    expect(record.estimatedCostUsd).toBe(0.0123);
    expect(record.identity.cliVersion).toBe("2.1.251");
    expect(contextTokensOf(record.tokens)).toBe(350 + 3000 + 6000);
  });

  it("marks a wrong answer wrong and says what came back", () => {
    const record = projected({ result: healthyResult({ answer: 340 }) });
    expect(record.outcome).toBe("wrong");
    expect(record.detail).toContain("340");
  });

  it("marks a salvaged budget kill failed — the per-run fuse is part of what is measured", () => {
    const record = projected({ result: { ...healthyResult(), budgetExhausted: true } });
    expect(record.outcome).toBe("failed");
    expect(record.detail).toContain("budget");
  });

  it("marks a failed run failed with its subtype, keeping the spend and the trace facts", () => {
    const failure: RunFailure = {
      message: "agent run failed (error_max_turns)",
      subtype: "error_max_turns",
      estimatedCostUsd: 0.03,
      sessionId: "s1",
    };
    const record = projected({ result: null, failure });
    expect(record.outcome).toBe("failed");
    expect(record.detail).toBe("error_max_turns: agent run failed (error_max_turns)");
    expect(record.maxTurnsHit).toBe(true);
    expect(record.estimatedCostUsd).toBe(0.03);
    expect(record.toolCalls).toEqual({ Bash: 2, StructuredOutput: 1 });
  });

  it("treats reaching the turn fuse as a max-turns hit even on a success", () => {
    const record = projectDriftRecord({
      promptId: "bash-loop",
      repeat: 2,
      executionId: "drift-y",
      startedAt: "2026-09-04T10:00:00.000Z",
      wallMs: 100,
      expectedAnswer: 339,
      maxTurns: 3,
      result: healthyResult(),
      failure: null,
      tracePackText: HEALTHY_PACK,
    });
    expect(record.maxTurnsHit).toBe(true);
  });

  it("survives a missing trace pack with null facts rather than a throw", () => {
    const record = projected({ tracePackText: null });
    expect(record.outcome).toBe("correct");
    expect(record.numTurns).toBeNull();
    expect(record.identity.sdkVersion).toBeNull();
    expect(record.tokens).toEqual({ input: 0, output: 0, cacheWrite: 0, cacheRead: 0 });
  });

  it("round-trips through the saved-record parser", () => {
    const record = projected({});
    expect(parseDriftRunRecord(JSON.parse(JSON.stringify(record)))).toEqual(record);
    const skipped = skippedDriftRecord("pdf-read", 2, "spend cap reached");
    expect(skipped.outcome).toBe("skipped");
    expect(parseDriftRunRecord(JSON.parse(JSON.stringify(skipped)))).toEqual(skipped);
  });

  it("refuses a saved record with an outcome outside the vocabulary", () => {
    const record = projected({});
    expect(() => parseDriftRunRecord({ ...record, outcome: "meh" })).toThrow(/outcome/);
  });
});
