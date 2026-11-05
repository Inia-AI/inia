import { afterAll, describe, expect, it } from "bun:test";
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  type HookJSONOutput,
  SYSTEM_PROMPT_DYNAMIC_BOUNDARY,
  type SyncHookJSONOutput,
} from "@anthropic-ai/claude-agent-sdk";
import { STATIC_CREDENTIAL_VARS } from "./aws-profile.js";
import { decodeCheckpointManifest } from "./checkpoint-bundle.js";
import { EXPECTED_CLAUDE_CODE_VERSION } from "./claude-code-pin.js";
import { DEFAULT_MAX_TURNS, MAX_SYSTEM_PROMPT_CHARS } from "./config.js";
import { RUNTIME_ENVIRONMENT_SECTION } from "./environment-section.js";
import { sha256Hex } from "./hash.js";
import { MODEL_PROVIDERS } from "./model-provider.js";
import { createNarrower } from "./narrow.js";
import {
  buildQueryOptions,
  type PauseRequest,
  parseMcpServerUrl,
  parseRunEffort,
  parseRunOutputFormat,
  parseRunReportParams,
  parseRunSystemPrompt,
  type QueryFn,
  type QueryInput,
  RUN_EFFORT_LEVELS,
  RUN_GIT_IDENTITY_ENV,
  RunFailedError,
  type RunStreamMessage,
  resolveReasoningConfig,
  runEffortChoice,
  runReport,
  runtimeDefaultEffort,
  sandboxConfigId,
  toRunResult,
  ValidationError,
} from "./run-report.js";
import { SKILL_ASSETS_ENV_VAR } from "./skill-plugin.js";
import type { RunTrace } from "./trace.js";
import { RESERVED_WORKSPACE_DIRS } from "./workspace-layout.js";

function successMessage(): RunStreamMessage & { subtype: string } {
  return {
    type: "result",
    subtype: "success",
    result: "the report",
    total_cost_usd: 0.42,
    session_id: "session-1",
  };
}

function errorMessage(): RunStreamMessage & { subtype: string } {
  return {
    type: "result",
    subtype: "error_max_turns",
    total_cost_usd: 0.13,
    session_id: "session-2",
    errors: ["ran out of turns"],
  };
}

const TEST_WORKSPACE_ROOT = mkdtempSync(join(tmpdir(), "run-report-tests-"));

afterAll(() => {
  rmSync(TEST_WORKSPACE_ROOT, { recursive: true, force: true });
});

async function* streamOf(...messages: RunStreamMessage[]): AsyncGenerator<RunStreamMessage> {
  yield* messages;
}

// The given messages, then a stream that never ends — the shape every deadline and abort
// test needs, so the kill path is exercised against a genuinely live stream.
async function* hangingStreamOf(...messages: RunStreamMessage[]): AsyncGenerator<RunStreamMessage> {
  yield* messages;
  await new Promise(() => {});
}

// One message, then a teardown that settles on a real timer. An unawaited return() leaves
// onClosed uncalled when runReport resolves, which is the whole point of the pin.
const TEARDOWN_DELAY_MS = 20;

function streamClosingAfter(
  message: RunStreamMessage,
  onClosed: () => void,
): AsyncIterable<RunStreamMessage> {
  return {
    [Symbol.asyncIterator]: () => {
      let sent = false;
      return {
        next: () => {
          if (sent) {
            return Promise.resolve({ done: true as const, value: undefined });
          }
          sent = true;
          return Promise.resolve({ done: false as const, value: message });
        },
        return: () =>
          new Promise((resolve) =>
            setTimeout(() => {
              onClosed();
              resolve({ done: true, value: undefined });
            }, TEARDOWN_DELAY_MS),
          ),
      };
    },
  };
}

describe("parseRunReportParams", () => {
  it("accepts a valid body", () => {
    expect(parseRunReportParams({ executionId: "e-1", prompt: "hello" })).toEqual({
      executionId: "e-1",
      prompt: "hello",
    });
  });

  it("rejects bodies that are not plain objects", () => {
    for (const bad of [null, "text", 42, true, ["a"]]) {
      expect(() => parseRunReportParams(bad)).toThrow(ValidationError);
    }
  });

  it("rejects missing, empty, or non-string fields", () => {
    expect(() => parseRunReportParams({ prompt: "p" })).toThrow(ValidationError);
    expect(() => parseRunReportParams({ executionId: "", prompt: "p" })).toThrow(ValidationError);
    expect(() => parseRunReportParams({ executionId: 7, prompt: "p" })).toThrow(ValidationError);
    expect(() => parseRunReportParams({ executionId: "e", prompt: {} })).toThrow(ValidationError);
  });

  it("rejects unknown fields so typos surface as 400s", () => {
    expect(() => parseRunReportParams({ executionId: "e", prompt: "p", promt: "typo" })).toThrow(
      ValidationError,
    );
  });

  it("rejects the path-segment executionIds . and .., which the charset alone would admit", () => {
    for (const executionId of [".", ".."]) {
      expect(() => parseRunReportParams({ executionId, prompt: "p" })).toThrow(ValidationError);
    }
  });
});

describe("parseRunEffort", () => {
  const parse = createNarrower((message) => new ValidationError(message));

  it("accepts every SDK effort level", () => {
    for (const level of RUN_EFFORT_LEVELS) {
      expect(parseRunEffort(level, parse, "effort")).toBe(level);
    }
  });

  it("rejects anything outside the SDK union", () => {
    for (const bad of ["extreme", "HIGH", "", 3, null, {}, ["high"]]) {
      expect(() => parseRunEffort(bad, parse, "effort")).toThrow(ValidationError);
    }
  });
});

describe("resolveReasoningConfig", () => {
  it.each([
    ["us.anthropic.claude-opus-5-5", "medium"],
    ["claude-opus-5-5", "medium"],
    ["us.anthropic.claude-haiku-5-5", "medium"],
    ["claude-sonnet-5-5", "medium"],
    ["us.anthropic.claude-opus-5", "high"],
    ["us.anthropic.claude-opus-4-8", "high"],
    ["us.anthropic.claude-sonnet-5", "high"],
    ["us.anthropic.claude-sonnet-4-6", "high"],
  ] as const)("records the effort the CLI sends %s when the SDK is handed none", (model, effort) => {
    expect(resolveReasoningConfig(undefined, model)).toMatchObject({ effort, effortSource: "sdk_default" });
  });

  it.each([...MODEL_PROVIDERS])(
    "records the runtime's own high for Sonnet 5.5 in %s mode when the request sets none",
    (provider) => {
      for (const model of ["us.anthropic.claude-sonnet-5-5", "claude-sonnet-5-5"]) {
        expect(resolveReasoningConfig(runEffortChoice(undefined, model, provider), model)).toMatchObject({
          effort: "high",
          effortSource: "runtime_default",
        });
      }
    },
  );

  it.each(["foundry", "anthropic"] as const)(
    "records the runtime's own high for Haiku 5.5 in %s mode when the request sets none",
    (provider) => {
      const model = "claude-haiku-5-5";
      expect(resolveReasoningConfig(runEffortChoice(undefined, model, provider), model)).toMatchObject({
        effort: "high",
        effortSource: "runtime_default",
      });
    },
  );

  it.each([undefined, "high"] as const)(
    "records no effort for Haiku 4.5, which the CLI sends none for, when the request sets %s",
    (effort) => {
      for (const model of ["us.anthropic.claude-haiku-4-5-20251001-v1:0", "claude-haiku-4-5"]) {
        expect(resolveReasoningConfig(runEffortChoice(effort, model, "bedrock"), model)).toMatchObject({
          effort: null,
          effortSource: "model_takes_none",
        });
      }
    },
  );

  it("records the request's own effort over any model default", () => {
    expect(
      resolveReasoningConfig(runEffortChoice("max", "claude-opus-5-5", "foundry"), "claude-opus-5-5"),
    ).toMatchObject({ effort: "max", effortSource: "request" });
    expect(
      resolveReasoningConfig(runEffortChoice("low", "claude-sonnet-5-5", "anthropic"), "claude-sonnet-5-5"),
    ).toMatchObject({ effort: "low", effortSource: "request" });
  });
});

describe("runEffortChoice", () => {
  it.each([...MODEL_PROVIDERS])(
    "keeps a declared effort over the runtime's default in %s mode",
    (provider) => {
      expect(runEffortChoice("low", "claude-sonnet-5-5", provider)).toEqual({
        effort: "low",
        source: "request",
      });
    },
  );

  it("names the runtime's default as the runtime's when the request sets none", () => {
    expect(runEffortChoice(undefined, "claude-sonnet-5-5", "anthropic")).toEqual({
      effort: "high",
      source: "runtime_default",
    });
  });

  it("hands the SDK nothing for a model the runtime sets no default for", () => {
    expect(runEffortChoice(undefined, "us.anthropic.claude-sonnet-5", "bedrock")).toBeUndefined();
  });
});

describe("runtimeDefaultEffort", () => {
  it.each([...MODEL_PROVIDERS])(
    "declares high for Sonnet 5.5 in %s mode, the level CLI 2.1.281 ran it at",
    (provider) => {
      expect(runtimeDefaultEffort("us.anthropic.claude-sonnet-5-5", provider)).toBe("high");
      expect(runtimeDefaultEffort("claude-sonnet-5-5", provider)).toBe("high");
    },
  );

  it.each(["foundry", "anthropic"] as const)(
    "declares high for Haiku 5.5 in %s mode, the level CLI 2.1.281 sent there",
    (provider) => {
      expect(runtimeDefaultEffort("claude-haiku-5-5", provider)).toBe("high");
    },
  );

  it("declares nothing for Haiku 5.5 in Bedrock mode, where CLI 2.1.281 left it at the API's medium", () => {
    expect(runtimeDefaultEffort("us.anthropic.claude-haiku-5-5", "bedrock")).toBeUndefined();
  });

  it.each([
    "us.anthropic.claude-opus-5-5",
    "us.anthropic.claude-sonnet-5",
    "us.anthropic.claude-opus-4-8",
    "us.anthropic.claude-haiku-4-5-20251001-v1:0",
  ])("declares nothing for %s, whose default the bump did not move", (model) => {
    for (const provider of MODEL_PROVIDERS) {
      expect(runtimeDefaultEffort(model, provider)).toBeUndefined();
    }
  });
});

describe("parseRunOutputFormat", () => {
  const parse = createNarrower((message) => new ValidationError(message));

  it("accepts the SDK shape and keeps the schema opaque", () => {
    const schema = { type: "object", properties: { verdict: { enum: ["pass", "fail"] } } };
    expect(parseRunOutputFormat({ type: "json_schema", schema }, parse, "outputFormat")).toEqual({
      type: "json_schema",
      schema,
    });
  });

  it("rejects everything that is not {type: json_schema, schema: object}", () => {
    for (const bad of [
      "json_schema",
      { type: "json_schema" },
      { type: "json_schema", schema: "not-an-object" },
      { type: "json_schema", schema: ["array"] },
      { type: "text", schema: {} },
      { schema: {} },
      { type: "json_schema", schema: {}, extra: true },
    ]) {
      expect(() => parseRunOutputFormat(bad, parse, "outputFormat")).toThrow(ValidationError);
    }
  });
});

describe("parseRunSystemPrompt", () => {
  const parse = createNarrower((message) => new ValidationError(message));

  it("accepts the SDK's custom-prompt variants and passes them through untouched", () => {
    expect(parseRunSystemPrompt("You are a financial analyst.", parse, "systemPrompt")).toBe(
      "You are a financial analyst.",
    );
    const blocks = ["static role and rules", SYSTEM_PROMPT_DYNAMIC_BOUNDARY, "per-pass context"];
    expect(parseRunSystemPrompt(blocks, parse, "systemPrompt")).toEqual(blocks);
  });

  it("rejects the SDK's preset variant — a run never carries an SDK-authored system prompt", () => {
    for (const preset of [
      { type: "preset", preset: "claude_code" },
      { type: "preset", preset: "claude_code", append: "Always explain your reasoning." },
      { type: "preset", preset: "claude_code", excludeDynamicSections: true },
    ]) {
      expect(() => parseRunSystemPrompt(preset, parse, "systemPrompt")).toThrow(ValidationError);
    }
  });

  it("rejects everything that is not a non-empty string or non-empty array of non-empty blocks", () => {
    for (const bad of [7, "", [], ["ok", ""], ["ok", 7], ["ok", ["nested"]], {}, true]) {
      expect(() => parseRunSystemPrompt(bad, parse, "systemPrompt")).toThrow(ValidationError);
    }
  });

  it("rejects a system prompt over the serialized size cap in either variant", () => {
    const oversized = "x".repeat(MAX_SYSTEM_PROMPT_CHARS);
    expect(() => parseRunSystemPrompt(oversized, parse, "systemPrompt")).toThrow(ValidationError);
    expect(() => parseRunSystemPrompt([oversized, oversized], parse, "systemPrompt")).toThrow(
      ValidationError,
    );
  });
});

describe("parseMcpServerUrl", () => {
  const parse = createNarrower((message) => new ValidationError(message));

  it("accepts absolute http and https URLs", () => {
    for (const url of ["https://platform.internal/mcp", "http://host.docker.internal:5118/mcp"]) {
      expect(parseMcpServerUrl(url, parse, "mcpServerUrl")).toBe(url);
    }
  });

  it("rejects non-http(s) and relative URLs", () => {
    for (const bad of ["", 7, "not-a-url", "/mcp", "ftp://host/mcp", "file:///etc/passwd", {}]) {
      expect(() => parseMcpServerUrl(bad, parse, "mcpServerUrl")).toThrow(ValidationError);
    }
  });
});

describe("toRunResult", () => {
  it("maps a success result onto the wire shape", () => {
    expect(toRunResult(successMessage(), "e-1")).toEqual({
      executionId: "e-1",
      output: "the report",
      estimatedCostUsd: 0.42,
      sessionId: "session-1",
    });
  });

  it("carries the SDK's structured output through on success, absent when the SDK sent none", () => {
    const withStructured = { ...successMessage(), structured_output: { verdict: "pass" } };
    expect(toRunResult(withStructured, "e-1")).toEqual({
      executionId: "e-1",
      output: "the report",
      estimatedCostUsd: 0.42,
      sessionId: "session-1",
      structuredOutput: { verdict: "pass" },
    });
    expect("structuredOutput" in toRunResult(successMessage(), "e-1")).toBe(false);
  });

  it("fails a success-subtype result whose is_error flag is set, instead of delivering the error text", () => {
    const errored = {
      ...successMessage(),
      is_error: true,
      result: "API Error: Could not load credentials from any providers",
      total_cost_usd: 0,
    };
    let caught: unknown;
    try {
      toRunResult(errored, "e-1");
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("errored_result");
    expect(caught.message).toContain("Could not load credentials");
    expect(caught.estimatedCostUsd).toBe(0);
    expect(caught.sessionId).toBe("session-1");
  });

  const MID_RESPONSE_TEXT = "API Error: Server error mid-response. The response above may be incomplete.";

  function erroredResult(result = ""): RunStreamMessage & { subtype: string } {
    return { ...successMessage(), is_error: true, result, total_cost_usd: 0.4456 };
  }

  function failureOf(run: () => unknown): RunFailedError {
    let caught: unknown;
    try {
      run();
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    return caught;
  }

  it("names an in-band failure whose result text is empty from the stream's errored assistant message", () => {
    const failure = failureOf(() =>
      toRunResult(erroredResult(), "e-1", {
        assistantError: { code: "invalid_request", text: MID_RESPONSE_TEXT },
      }),
    );
    expect(failure.subtype).toBe("errored_result");
    expect(failure.message).toContain(MID_RESPONSE_TEXT);
    expect(failure.message).toContain("invalid_request");
    expect(failure.estimatedCostUsd).toBe(0.4456);
  });

  it("classifies a server-side API failure as upstream_error, with the cause and the SDK's error class", () => {
    const failure = failureOf(() =>
      toRunResult({ ...erroredResult(), api_error_status: 500 }, "e-1", {
        assistantError: { code: "server_error", text: MID_RESPONSE_TEXT },
      }),
    );
    expect(failure.subtype).toBe("upstream_error");
    expect(failure.message).toContain("agent run failed (upstream_error)");
    expect(failure.message).toContain(MID_RESPONSE_TEXT);
    expect(failure.message).toContain("server_error");
    expect(failure.message).toContain("500");
    expect(failure.estimatedCostUsd).toBe(0.4456);
    expect(failure.sessionId).toBe("session-1");
  });

  it("classifies an overloaded or rate-limited API the same way, and every other class as errored_result", () => {
    for (const code of ["overloaded", "rate_limit"]) {
      expect(
        failureOf(() => toRunResult(erroredResult(), "e-1", { assistantError: { code, text: "" } })).subtype,
      ).toBe("upstream_error");
    }
    for (const code of [
      "authentication_failed",
      "billing_error",
      "invalid_request",
      "max_output_tokens",
      "unknown",
    ]) {
      expect(
        failureOf(() => toRunResult(erroredResult(), "e-1", { assistantError: { code, text: "" } })).subtype,
      ).toBe("errored_result");
    }
  });

  it("prefers the result's own error text when it carries one, still classifying by the SDK's error class", () => {
    const failure = failureOf(() =>
      toRunResult(erroredResult("API Error: 529 overloaded"), "e-1", {
        assistantError: { code: "overloaded", text: "stale text from the assistant message" },
      }),
    );
    expect(failure.subtype).toBe("upstream_error");
    expect(failure.message).toContain("API Error: 529 overloaded");
    expect(failure.message).not.toContain("stale text");
  });

  it("says outright when neither the result nor the stream carried any error text", () => {
    const failure = failureOf(() => toRunResult(erroredResult(), "e-1"));
    expect(failure.subtype).toBe("errored_result");
    expect(failure.message).toMatch(/no error text/);
  });

  it("fails a success result that consumed zero turns — the run executed nothing", () => {
    const zeroTurn = {
      ...successMessage(),
      result: "Unknown command: /definitely-not-a-command",
      num_turns: 0,
      total_cost_usd: 0,
    };
    let caught: unknown;
    try {
      toRunResult(zeroTurn, "e-1");
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("zero_turns");
    expect(caught.message).toContain("Unknown command: /definitely-not-a-command");
    expect(caught.estimatedCostUsd).toBe(0);
    expect(caught.sessionId).toBe("session-1");
  });

  it("delivers a success that consumed at least one turn", () => {
    const oneTurn = { ...successMessage(), num_turns: 1 };
    expect(toRunResult(oneTurn, "e-1").output).toBe("the report");
  });

  it("throws RunFailedError carrying cost, session, and diagnostics for error subtypes", () => {
    let caught: unknown;
    try {
      toRunResult(errorMessage(), "e-1");
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("error_max_turns");
    expect(caught.estimatedCostUsd).toBe(0.13);
    expect(caught.sessionId).toBe("session-2");
    expect(caught.message).toContain("ran out of turns");
  });
});

describe("runReport in-band API failures", () => {
  const MID_RESPONSE_TEXT = "API Error: Server error mid-response. The response above may be incomplete.";

  function erroredAssistant(parentToolUseId: string | null = null): RunStreamMessage {
    return {
      type: "assistant",
      parent_tool_use_id: parentToolUseId,
      error: "server_error",
      session_id: "session-1",
      message: { role: "assistant", content: [{ type: "text", text: MID_RESPONSE_TEXT }] },
    };
  }

  function erroredResult(): RunStreamMessage {
    return { ...successMessage(), is_error: true, result: "", total_cost_usd: 0.4456 };
  }

  async function failureOf(promise: Promise<unknown>): Promise<RunFailedError> {
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    return caught;
  }

  it("fails upstream_error carrying the stream's error text when the SDK reported a server error mid-response", async () => {
    const failure = await failureOf(
      runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => streamOf(erroredAssistant(), erroredResult()), workspaceRoot: TEST_WORKSPACE_ROOT },
      ),
    );
    expect(failure.subtype).toBe("upstream_error");
    expect(failure.message).toContain(MID_RESPONSE_TEXT);
    expect(failure.estimatedCostUsd).toBe(0.4456);
    expect(failure.sessionId).toBe("session-1");
  });

  it("keeps errored_result when only a sub-agent reported the server error", async () => {
    const failure = await failureOf(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () => streamOf(erroredAssistant("toolu_1"), erroredResult()),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("errored_result");
    expect(failure.message).not.toContain(MID_RESPONSE_TEXT);
  });

  it("records the classified subtype on the trace's run.failed stage", async () => {
    const stages: { stage: string; fields: Record<string, unknown> }[] = [];
    const trace: RunTrace = {
      packDir: join(TEST_WORKSPACE_ROOT, "trace-upstream"),
      appendMessage: () => {},
      emitStage: (stage, fields = {}) => {
        stages.push({ stage, fields });
      },
      finalize: () => {},
    };
    await failureOf(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () => streamOf(erroredAssistant(), erroredResult()),
          workspaceRoot: TEST_WORKSPACE_ROOT,
          trace,
        },
      ),
    );
    expect(stages.find((entry) => entry.stage === "run.failed")?.fields).toMatchObject({
      subtype: "upstream_error",
    });
  });
});

describe("runReport", () => {
  it("resolves with the mapped result on success", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect(result).toEqual({
      executionId: "e-1",
      output: "the report",
      estimatedCostUsd: 0.42,
      sessionId: "session-1",
      usage: {
        inputTokens: 0,
        outputTokens: 0,
        cacheCreationInputTokens: 0,
        cacheReadInputTokens: 0,
      },
    });
  });

  it("reports the usage-derived estimate when it exceeds the SDK's own success figure", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(...usageEvents(SONNET_MODEL, { output_tokens: 1_000_000 }), successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.estimatedCostUsd).toBeCloseTo(15, 10);
  });

  it("ignores non-result messages while streaming", async () => {
    const chatter: RunStreamMessage = { type: "assistant" };
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(chatter, successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect(result.output).toBe("the report");
  });

  it("reports each errored tool result through onToolError while the stream flows", async () => {
    const toolUse = (id: string, name: string): RunStreamMessage => ({
      type: "assistant",
      message: { content: [{ type: "tool_use", id, name, input: {} }] },
    });
    const toolResult = (id: string, isError: boolean): RunStreamMessage => ({
      type: "user",
      message: { content: [{ type: "tool_result", tool_use_id: id, is_error: isError, content: [] }] },
    });
    const seen: { toolName: string; erroredCalls: number }[] = [];
    await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () =>
          streamOf(
            toolUse("tu-1", "Bash"),
            toolResult("tu-1", true),
            toolUse("tu-2", "Bash"),
            toolResult("tu-2", false),
            toolUse("tu-3", "Bash"),
            toolResult("tu-3", true),
            successMessage(),
          ),
        onToolError: (info) => seen.push(info),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(seen).toEqual([
      { toolName: "Bash", erroredCalls: 1 },
      { toolName: "Bash", erroredCalls: 2 },
    ]);
  });

  it("fails a run whose backend MCP server did not connect, with a distinct subtype", async () => {
    const initFailed: RunStreamMessage = {
      type: "system",
      subtype: "init",
      mcp_servers: [{ name: "platform", status: "failed" }],
    };
    let caught: unknown;
    try {
      await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          mcpServer: { url: "https://platform.internal/mcp", runToken: "tok" },
          queryFn: () => streamOf(initFailed, successMessage()),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      );
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("mcp_connect_failed");
  });

  it("fails when the init message names no platform server at all despite an MCP URL", async () => {
    const initWithoutPlatform: RunStreamMessage = {
      type: "system",
      subtype: "init",
      mcp_servers: [],
    };
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        mcpServer: { url: "https://platform.internal/mcp" },
        queryFn: () => streamOf(initWithoutPlatform, successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    await expect(promise).rejects.toThrow(/platform/);
  });

  it("lets a run proceed once the init message reports the platform server connected", async () => {
    const initConnected: RunStreamMessage = {
      type: "system",
      subtype: "init",
      mcp_servers: [{ name: "platform", status: "connected" }],
    };
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        mcpServer: { url: "https://platform.internal/mcp", runToken: "tok" },
        queryFn: () => streamOf(initConnected, successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.output).toBe("the report");
  });

  it("fails a run whose requested skills did not all register, with a distinct subtype", async () => {
    const initMissingSkill: RunStreamMessage = {
      type: "system",
      subtype: "init",
      skills: ["skills:huh"],
    };
    let caught: unknown;
    try {
      await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          skillPlugin: { path: "/private/home/skills", skillNames: ["skills:huh", "skills:grading"] },
          queryFn: () => streamOf(initMissingSkill, successMessage()),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      );
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("skill_register_failed");
    expect(caught.message).toContain("skills:grading");
  });

  it("fails when the init message carries no skills listing at all despite requested skills", async () => {
    const initWithoutSkills: RunStreamMessage = { type: "system", subtype: "init" };
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        skillPlugin: { path: "/private/home/skills", skillNames: ["skills:huh"] },
        queryFn: () => streamOf(initWithoutSkills, successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    await expect(promise).rejects.toThrow(/skills:huh/);
  });

  it("lets a run proceed once the init message lists every requested skill, extras tolerated", async () => {
    const initRegistered: RunStreamMessage = {
      type: "system",
      subtype: "init",
      skills: ["skills:huh", "some-bundled-skill"],
    };
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        skillPlugin: { path: "/private/home/skills", skillNames: ["skills:huh"] },
        queryFn: () => streamOf(initRegistered, successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.output).toBe("the report");
  });

  it("re-verifies skill registration on every resumed segment, not only the first", async () => {
    let segments = 0;
    const initRegistered: RunStreamMessage = { type: "system", subtype: "init", skills: ["skills:huh"] };
    const initUnregistered: RunStreamMessage = { type: "system", subtype: "init", skills: [] };
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        skillPlugin: { path: "/private/home/skills", skillNames: ["skills:huh"] },
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => Promise.resolve(new TextEncoder().encode("state")),
        pause: () => Promise.resolve({ answer: "go on", allowFurtherPause: true }),
        queryFn: () => {
          segments += 1;
          return segments === 1
            ? streamOf(initRegistered, pausedMessage())
            : streamOf(initUnregistered, successMessage());
        },
      },
    );
    await expect(promise).rejects.toThrow(/register/);
    expect(segments).toBe(2);
  });

  it("ignores skill listings on runs that requested no skills", async () => {
    const initWithSkills: RunStreamMessage = { type: "system", subtype: "init", skills: [] };
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(initWithSkills, successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect(result.output).toBe("the report");
  });

  it("ignores MCP statuses on runs that asked for no backend server", async () => {
    const initFailed: RunStreamMessage = {
      type: "system",
      subtype: "init",
      mcp_servers: [{ name: "platform", status: "failed" }],
    };
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(initFailed, successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect(result.output).toBe("the report");
  });

  it("fails a run whose init reports a CLI version other than the pin, with a distinct subtype", async () => {
    const initDrifted: RunStreamMessage = {
      type: "system",
      subtype: "init",
      claude_code_version: "2.1.220",
    };
    let caught: unknown;
    try {
      await runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => streamOf(initDrifted, successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
      );
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("cli_version_drift");
    expect(caught.message).toContain("2.1.220");
    expect(caught.message).toContain(EXPECTED_CLAUDE_CODE_VERSION);
  });

  it("gives the drift gate precedence when one init fails both drift and skills", async () => {
    const initDoublyBroken: RunStreamMessage = {
      type: "system",
      subtype: "init",
      claude_code_version: "0.0.1",
      skills: [],
    };
    let caught: unknown;
    try {
      await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          skillPlugin: { path: "/private/home/skills", skillNames: ["skills:huh"] },
          queryFn: () => streamOf(initDoublyBroken, successMessage()),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      );
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("cli_version_drift");
  });

  it("lets a run proceed when the init message reports exactly the pinned CLI version", async () => {
    const initPinned: RunStreamMessage = {
      type: "system",
      subtype: "init",
      claude_code_version: EXPECTED_CLAUDE_CODE_VERSION,
    };
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(initPinned, successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect(result.output).toBe("the report");
  });

  it("re-verifies the CLI version on every resumed segment, not only the first", async () => {
    let segments = 0;
    const initPinned: RunStreamMessage = {
      type: "system",
      subtype: "init",
      claude_code_version: EXPECTED_CLAUDE_CODE_VERSION,
    };
    const initDrifted: RunStreamMessage = {
      type: "system",
      subtype: "init",
      claude_code_version: "9.9.9",
    };
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => Promise.resolve(new TextEncoder().encode("state")),
        pause: () => Promise.resolve({ answer: "go on", allowFurtherPause: true }),
        queryFn: () => {
          segments += 1;
          return segments === 1
            ? streamOf(initPinned, pausedMessage())
            : streamOf(initDrifted, successMessage());
        },
      },
    );
    await expect(promise).rejects.toThrow(/9\.9\.9/);
    expect(segments).toBe(2);
  });

  it("returns the structured output when the run was asked for one and the SDK delivered", async () => {
    const outputFormat = { type: "json_schema" as const, schema: { type: "object" } };
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        outputFormat,
        queryFn: () => streamOf({ ...successMessage(), structured_output: { verdict: "pass" } }),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.structuredOutput).toEqual({ verdict: "pass" });
  });

  it("fails a success that carries no structured output when outputFormat was set", async () => {
    const outputFormat = { type: "json_schema" as const, schema: { type: "object" } };
    let caught: unknown;
    try {
      await runReport(
        { executionId: "e-1", prompt: "p" },
        { outputFormat, queryFn: () => streamOf(successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
      );
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("malformed_result");
    expect(caught.estimatedCostUsd).toBe(0.42);
    expect(caught.sessionId).toBe("session-1");
  });

  it("rejects with RunFailedError when the run fails, keeping the cost", async () => {
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(errorMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    await expect(promise).rejects.toBeInstanceOf(RunFailedError);
  });

  it("rejects a run whose only segment consumed zero turns instead of reporting success", async () => {
    const zeroTurn = { ...successMessage(), result: "Unknown command: /x", num_turns: 0, total_cost_usd: 0 };
    let caught: unknown;
    try {
      await runReport(
        { executionId: "e-1", prompt: "/x" },
        { queryFn: () => streamOf(zeroTurn), workspaceRoot: TEST_WORKSPACE_ROOT },
      );
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("zero_turns");
  });

  it("rejects when the stream ends without a result message instead of returning empty output", async () => {
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf({ type: "assistant" }), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    await expect(promise).rejects.toThrow(/no result/);
  });

  it("creates a per-run workspace under the root and hands it to the query", async () => {
    const root = mkdtempSync(join(tmpdir(), "ws-root-"));
    try {
      let seenWorkspace: string | undefined;
      const result = await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          workspaceRoot: root,
          queryFn: ({ workspacePath }) => {
            seenWorkspace = workspacePath;
            expect(existsSync(workspacePath)).toBe(true);
            return streamOf(successMessage());
          },
        },
      );
      expect(result.output).toBe("the report");
      expect(seenWorkspace?.startsWith(root)).toBe(true);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("removes the workspace when the run succeeds", async () => {
    const root = mkdtempSync(join(tmpdir(), "ws-root-"));
    try {
      let seenWorkspace = "";
      await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          workspaceRoot: root,
          queryFn: ({ workspacePath }) => {
            seenWorkspace = workspacePath;
            return streamOf(successMessage());
          },
        },
      );
      expect(seenWorkspace).not.toBe("");
      expect(existsSync(seenWorkspace)).toBe(false);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("removes the workspace when the run fails", async () => {
    const root = mkdtempSync(join(tmpdir(), "ws-root-"));
    try {
      let seenWorkspace = "";
      const promise = runReport(
        { executionId: "e-1", prompt: "p" },
        {
          workspaceRoot: root,
          queryFn: ({ workspacePath }) => {
            seenWorkspace = workspacePath;
            return streamOf(errorMessage());
          },
        },
      );
      await expect(promise).rejects.toBeInstanceOf(RunFailedError);
      expect(seenWorkspace).not.toBe("");
      expect(existsSync(seenWorkspace)).toBe(false);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("rejects with an aborted RunFailedError when the external signal fires mid-run", async () => {
    const controller = new AbortController();
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => hangingStreamOf(), signal: controller.signal, workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    controller.abort();
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("aborted");
  });

  it("rejects without consuming the stream when the signal is already aborted", async () => {
    const controller = new AbortController();
    controller.abort();
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(successMessage()),
        signal: controller.signal,
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    await expect(promise).rejects.toThrow(/abort/);
  });

  it("rejects when the deadline passes before the stream produces a result", async () => {
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => hangingStreamOf(), deadlineMs: 30, workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    await expect(promise).rejects.toThrow(/deadline/);
  });

  it("stamps a deadline failure with the session the stream reported while it was alive", async () => {
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () =>
            hangingStreamOf(
              { type: "system", subtype: "init", session_id: "s-live" },
              { type: "assistant", session_id: "s-live" },
            ),
          deadlineMs: 30,
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.sessionId).toBe("s-live");
  });

  it("keeps a deadline failure's session null when a resume leg's stream reported the wrong id", async () => {
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () => hangingStreamOf({ type: "assistant", session_id: "s-wrong" }),
          deadlineMs: 30,
          resumeFrom: { sessionId: "s-expected" },
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.sessionId).toBeNull();
  });

  it("keeps a caller abort's session null — the cancelled child is killed, not resumed", async () => {
    const controller = new AbortController();
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield { type: "assistant", session_id: "s-live" };
      controller.abort();
      await new Promise(() => {});
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () => stream(),
          signal: controller.signal,
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("aborted");
    expect(failure.sessionId).toBeNull();
  });

  it("uses a caller-provided workspace and leaves its lifecycle to the caller", async () => {
    const root = mkdtempSync(join(tmpdir(), "ws-root-"));
    try {
      const provided = join(root, "run-fixed");
      mkdirSync(provided, { recursive: true });
      let seenWorkspace = "";
      await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          workspacePath: provided,
          queryFn: ({ workspacePath }) => {
            seenWorkspace = workspacePath;
            return streamOf(successMessage());
          },
        },
      );
      expect(seenWorkspace).toBe(provided);
      expect(existsSync(provided)).toBe(true);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});

function pausedMessage(): RunStreamMessage {
  return {
    type: "result",
    subtype: "success",
    result: "",
    total_cost_usd: 0.2,
    session_id: "session-pause",
    terminal_reason: "tool_deferred",
    deferred_tool_use: {
      id: "tool-1",
      name: "mcp__platform__request_analyst_input",
      input: { question: "Which fund?" },
    },
  };
}

const RESOLUTION = { answer: "the second fund", allowFurtherPause: true };

// A resumed segment reports the session it was asked to resume, or the run fails exactly as a
// real silent fresh start would.
function resumedSuccessMessage(sessionId: string): RunStreamMessage & { subtype: string } {
  return { ...successMessage(), session_id: sessionId };
}

const CHECKPOINT_BYTES = new TextEncoder().encode("serialized session state");

describe("runReport pause and resume", () => {
  it("checkpoints, pauses, and resumes the same session with the answer as the verbatim next prompt", async () => {
    const queries: QueryInput[] = [];
    const pauses: PauseRequest[] = [];
    const result = await runReport(
      { executionId: "e-1", prompt: "the original prompt" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: (input) => {
          queries.push(input);
          return queries.length === 1
            ? streamOf(pausedMessage())
            : streamOf(resumedSuccessMessage("session-pause"));
        },
        pause: (request) => {
          pauses.push(request);
          return Promise.resolve(RESOLUTION);
        },
      },
    );
    expect(result.output).toBe("the report");
    expect(queries).toHaveLength(2);
    expect(queries[0]?.prompt).toBe("the original prompt");
    expect(queries[0]?.resumeSessionId).toBeUndefined();
    expect(queries[1]?.resumeSessionId).toBe("session-pause");
    expect(queries[1]?.prompt).toBe("the second fund");
    expect(queries[1]?.workspacePath).toBe(queries[0]?.workspacePath);
    expect(pauses).toHaveLength(1);
    expect(pauses[0]?.sessionId).toBe("session-pause");
    expect(pauses[0]?.estimatedCostUsd).toBe(0.2);
    expect(pauses[0]?.question).toEqual({
      id: "tool-1",
      toolName: "mcp__platform__request_analyst_input",
      prompt: "Which fund?",
      input: { question: "Which fund?" },
    });
    expect(pauses[0]?.checkpoint.sha256).toBe(sha256Hex(CHECKPOINT_BYTES));
    expect(new TextDecoder().decode(pauses[0]?.checkpoint.bytes)).toBe("serialized session state");
  });

  it("fails with pause_rejected when the run tries to park again after the analyst released it", async () => {
    let pauseCalls = 0;
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: () => streamOf(pausedMessage()),
        pause: () => {
          pauseCalls += 1;
          return Promise.resolve({ answer: "finish now", allowFurtherPause: false });
        },
      },
    );
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("pause_rejected");
    expect(pauseCalls).toBe(1);
  });

  it("starts with pausing disallowed when a cold resume carries allowFurtherPause false", async () => {
    let paused = false;
    const promise = runReport(
      { executionId: "e-1", prompt: "the answer" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        resumeFrom: { sessionId: "session-prior" },
        allowFurtherPause: false,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: () => streamOf({ ...pausedMessage(), session_id: "session-prior" }),
        pause: () => {
          paused = true;
          return Promise.resolve(RESOLUTION);
        },
      },
    );
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("pause_rejected");
    expect(paused).toBe(false);
  });

  it("checkpoints before asking: the pause request already carries the sealed bundle", async () => {
    const order: string[] = [];
    await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => {
          order.push("checkpoint");
          return Promise.resolve(CHECKPOINT_BYTES);
        },
        queryFn: () =>
          order.length === 0 ? streamOf(pausedMessage()) : streamOf(resumedSuccessMessage("session-pause")),
        pause: () => {
          order.push("pause");
          return Promise.resolve(RESOLUTION);
        },
      },
    );
    expect(order).toEqual(["checkpoint", "pause"]);
  });

  it("waits for the SDK stream's teardown before returning, so nothing is still writing the session", async () => {
    // Settles on a real timer, so an unawaited teardown would leave this false when runReport
    // resolves and the config dir would be serialized under an SDK still writing to it.
    let closed = false;
    const closingStream = (): AsyncIterable<RunStreamMessage> =>
      streamClosingAfter(successMessage(), () => {
        closed = true;
      });
    await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: closingStream, workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect(closed).toBe(true);
  });

  it("keeps a run's result when the stream's teardown fails", async () => {
    // The teardown runs in a finally, so anything it raises would otherwise replace the result
    // the run actually produced.
    const throwingStream = (): AsyncIterable<RunStreamMessage> => ({
      [Symbol.asyncIterator]: () => {
        let sent = false;
        return {
          next: () => {
            if (sent) {
              return Promise.resolve({ done: true as const, value: undefined });
            }
            sent = true;
            return Promise.resolve({ done: false as const, value: successMessage() });
          },
          return: () => {
            throw new Error("the SDK tore down badly");
          },
        };
      },
    });
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: throwingStream, workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect(result.output).toBe("the report");
  });

  it("waits for the same teardown on the pause path, which serializes the same config dir", async () => {
    let closesAwaited = 0;
    const closingStream = (message: RunStreamMessage): AsyncIterable<RunStreamMessage> =>
      streamClosingAfter(message, () => {
        closesAwaited += 1;
      });
    let segments = 0;
    await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: () => {
          segments += 1;
          return closingStream(segments === 1 ? pausedMessage() : resumedSuccessMessage("session-pause"));
        },
        pause: () => {
          // The pause segment's stream is torn down before the analyst is asked, so the bundle
          // the pause carries cannot capture a transcript still being written.
          expect(closesAwaited).toBe(1);
          return Promise.resolve(RESOLUTION);
        },
      },
    );
    expect(closesAwaited).toBe(2);
  });

  it("fails the run when the checkpoint cannot be written, instead of pausing anyway", async () => {
    let paused = false;
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => Promise.reject(new Error("disk full")),
        queryFn: () => streamOf(pausedMessage()),
        pause: () => {
          paused = true;
          return Promise.resolve(RESOLUTION);
        },
      },
    );
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("checkpoint_failed");
    expect(caught.sessionId).toBe("session-pause");
    expect(paused).toBe(false);
  });

  it("fails the run when it pauses but no pause handler is wired", async () => {
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      { workspaceRoot: TEST_WORKSPACE_ROOT, queryFn: () => streamOf(pausedMessage()) },
    );
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("pause_unsupported");
    expect(caught.estimatedCostUsd).toBe(0.2);
  });

  it("fails the run when the pause is rejected, keeping the pause cost", async () => {
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: () => streamOf(pausedMessage()),
        pause: () => Promise.reject(new Error("run cancelled")),
      },
    );
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("pause_rejected");
    expect(caught.estimatedCostUsd).toBe(0.2);
    expect(caught.sessionId).toBe("session-pause");
  });

  it("fails a pause whose message names no session or question rather than resuming blind", async () => {
    const withoutSession: RunStreamMessage = { ...pausedMessage() };
    delete withoutSession.session_id;
    const withoutQuestion: RunStreamMessage = { ...pausedMessage() };
    delete withoutQuestion.deferred_tool_use;
    for (const message of [withoutSession, withoutQuestion]) {
      const promise = runReport(
        { executionId: "e-1", prompt: "p" },
        {
          workspaceRoot: TEST_WORKSPACE_ROOT,
          checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
          queryFn: () => streamOf(message),
          pause: () => Promise.resolve(RESOLUTION),
        },
      );
      let caught: unknown;
      try {
        await promise;
      } catch (error) {
        caught = error;
      }
      expect(caught).toBeInstanceOf(RunFailedError);
      if (!(caught instanceof RunFailedError)) {
        throw new Error("unreachable");
      }
      expect(caught.subtype).toBe("malformed_pause");
    }
  });

  it("serializes the session dir named by CLAUDE_CONFIG_DIR by default", async () => {
    const root = mkdtempSync(join(tmpdir(), "default-ckpt-"));
    try {
      const configDir = join(root, "claude-config");
      mkdirSync(configDir, { recursive: true });
      writeFileSync(join(configDir, "session.jsonl"), '{"type":"user"}\n');
      const pauses: PauseRequest[] = [];
      let first = true;
      await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          workspaceRoot: root,
          subprocessEnv: { CLAUDE_CONFIG_DIR: configDir },
          queryFn: () => {
            const stream = first
              ? streamOf(pausedMessage())
              : streamOf(resumedSuccessMessage("session-pause"));
            first = false;
            return stream;
          },
          pause: (request) => {
            pauses.push(request);
            return Promise.resolve(RESOLUTION);
          },
        },
      );
      const manifest = decodeCheckpointManifest(pauses[0]?.checkpoint.bytes ?? new Uint8Array(), {
        workspaceRoot: root,
        reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      });
      expect(manifest.sessionId).toBe("session-pause");
      expect(Object.keys(manifest.files)).toContain("config/session.jsonl");
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("sums cost across segments: the pause carries its total and the result carries the run's", async () => {
    let first = true;
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: () => {
          const stream = first ? streamOf(pausedMessage()) : streamOf(resumedSuccessMessage("session-pause"));
          first = false;
          return stream;
        },
        pause: () => Promise.resolve(RESOLUTION),
      },
    );
    expect(result.estimatedCostUsd).toBeCloseTo(0.62, 10);
  });

  // Since CLI 2.1.281 a resumed session restores its transcript's cost-state, so the SDK's
  // total_cost_usd on a resumed segment carries every earlier segment's spend as well.
  describe("a resumed segment's restored cost-state", () => {
    function cumulativeSuccess(
      sessionId: string,
      totalCostUsd: number,
    ): RunStreamMessage & { subtype: string } {
      return { ...successMessage(), session_id: sessionId, total_cost_usd: totalCostUsd };
    }

    it("reports a continuation's own leg, net of the cost the CLI restored", async () => {
      const asked: string[] = [];
      const result = await runReport(
        { executionId: "e-1", prompt: "the follow-up" },
        {
          workspaceRoot: TEST_WORKSPACE_ROOT,
          resumeFrom: { sessionId: "session-1" },
          restoredSessionCostUsd: (sessionId) => {
            asked.push(sessionId);
            return 0.3;
          },
          queryFn: () => streamOf(cumulativeSuccess("session-1", 0.42)),
        },
      );
      expect(asked).toEqual(["session-1"]);
      expect(result.estimatedCostUsd).toBeCloseTo(0.12, 10);
    });

    it("adds a cold resume's own segment to its priorCostUsd once, not the restored spend again", async () => {
      const result = await runReport(
        { executionId: "e-1", prompt: "the answer" },
        {
          workspaceRoot: TEST_WORKSPACE_ROOT,
          resumeFrom: { sessionId: "session-1" },
          priorCostUsd: 0.3,
          restoredSessionCostUsd: () => 0.3,
          queryFn: () => streamOf(cumulativeSuccess("session-1", 0.42)),
        },
      );
      expect(result.estimatedCostUsd).toBeCloseTo(0.42, 10);
    });

    it("sums a warm pause's segments once each when the resumed segment reports them cumulatively", async () => {
      let first = true;
      const asked: string[] = [];
      const result = await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          workspaceRoot: TEST_WORKSPACE_ROOT,
          checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
          restoredSessionCostUsd: (sessionId) => {
            asked.push(sessionId);
            return 0.2;
          },
          queryFn: () => {
            const stream = first
              ? streamOf(pausedMessage())
              : streamOf(cumulativeSuccess("session-pause", 0.62));
            first = false;
            return stream;
          },
          pause: () => Promise.resolve(RESOLUTION),
        },
      );
      expect(asked).toEqual(["session-pause"]);
      expect(result.estimatedCostUsd).toBeCloseTo(0.62, 10);
    });

    it("reports a failed continuation's own leg, net of the cost the CLI restored", async () => {
      let caught: unknown;
      try {
        await runReport(
          { executionId: "e-1", prompt: "the follow-up" },
          {
            workspaceRoot: TEST_WORKSPACE_ROOT,
            resumeFrom: { sessionId: "session-2" },
            restoredSessionCostUsd: () => 0.1,
            queryFn: () => streamOf(errorMessage()),
          },
        );
      } catch (error) {
        caught = error;
      }
      if (!(caught instanceof RunFailedError)) {
        throw new Error("a failed result must surface as a RunFailedError");
      }
      expect(caught.estimatedCostUsd).toBeCloseTo(0.03, 10);
    });

    it("reads the restored cost from the transcript under CLAUDE_CONFIG_DIR by default", async () => {
      const root = mkdtempSync(join(tmpdir(), "restored-cost-"));
      try {
        const workspacePath = join(root, "run-a");
        mkdirSync(workspacePath, { recursive: true });
        const projectDir = join(root, "projects", workspacePath.replace(/[^a-zA-Z0-9]/g, "-"));
        mkdirSync(projectDir, { recursive: true });
        writeFileSync(
          join(projectDir, "session-1.jsonl"),
          `${JSON.stringify({ type: "cost-state", sessionId: "session-1", totalCostUSD: 0.3 })}\n`,
        );
        const result = await runReport(
          { executionId: "e-1", prompt: "the follow-up" },
          {
            workspaceRoot: TEST_WORKSPACE_ROOT,
            subprocessEnv: { CLAUDE_CONFIG_DIR: root },
            workspacePath,
            resumeFrom: { sessionId: "session-1" },
            queryFn: () => streamOf(cumulativeSuccess("session-1", 0.42)),
          },
        );
        expect(result.estimatedCostUsd).toBeCloseTo(0.12, 10);
      } finally {
        rmSync(root, { recursive: true, force: true });
      }
    });

    // A mismatched stream is a different, fresh conversation: nothing was restored into it, so
    // subtracting the resumed session's figure would undercount the spend it really bought.
    it("reports a mismatched session's whole cost, never net of the session it failed to resume", async () => {
      let caught: unknown;
      try {
        await runReport(
          { executionId: "e-1", prompt: "the follow-up" },
          {
            workspaceRoot: TEST_WORKSPACE_ROOT,
            resumeFrom: { sessionId: "session-1" },
            restoredSessionCostUsd: () => 0.3,
            queryFn: () => streamOf(cumulativeSuccess("session-fresh", 0.42)),
          },
        );
      } catch (error) {
        caught = error;
      }
      if (!(caught instanceof RunFailedError)) {
        throw new Error("a mismatched session must surface as a RunFailedError");
      }
      expect(caught.subtype).toBe("session_id_mismatch");
      expect(caught.estimatedCostUsd).toBeCloseTo(0.42, 10);
    });

    // The SDK priced Haiku 5.5 at Opus 5.5 rates; the run prices its own totals.
    it("reports a segment priced from its own per-model totals at the runtime's rates", async () => {
      const recorded: unknown[] = [];
      const result = await runReport(
        { executionId: "e-1", prompt: "the follow-up" },
        {
          workspaceRoot: TEST_WORKSPACE_ROOT,
          resumeFrom: { sessionId: "session-1" },
          restoredSessionCostUsd: () => 0.01,
          restoredSessionModelUsage: () =>
            new Map([
              [
                "us.anthropic.claude-haiku-5-5",
                { inputTokens: 1, outputTokens: 10, cacheCreationInputTokens: 0, cacheReadInputTokens: 0 },
              ],
            ]),
          trace: {
            packDir: "",
            appendMessage: () => {},
            appendModelUsage: (totals) => recorded.push(totals),
            emitStage: () => {},
            finalize: () => {},
          },
          queryFn: () =>
            streamOf({
              ...cumulativeSuccess("session-1", 0.06),
              modelUsage: {
                "us.anthropic.claude-haiku-5-5": {
                  inputTokens: 5,
                  outputTokens: 82,
                  cacheReadInputTokens: 0,
                  cacheCreationInputTokens: 9_219,
                  costUSD: 0.06,
                },
              },
            }),
        },
      );
      expect(result.estimatedCostUsd).toBeCloseTo((4 * 0.1 + 72 * 0.5 + 9_219 * 0.125) / 1_000_000, 12);
      expect(recorded).toEqual([
        new Map([
          [
            "us.anthropic.claude-haiku-5-5",
            { inputTokens: 4, outputTokens: 72, cacheCreationInputTokens: 9_219, cacheReadInputTokens: 0 },
          ],
        ]),
      ]);
    });

    it("never reads a restored cost for a fresh session, which has none to restore", async () => {
      const asked: string[] = [];
      const result = await runReport(
        { executionId: "e-1", prompt: "p" },
        {
          workspaceRoot: TEST_WORKSPACE_ROOT,
          restoredSessionCostUsd: (sessionId) => {
            asked.push(sessionId);
            return 0.3;
          },
          queryFn: () => streamOf(successMessage()),
        },
      );
      expect(asked).toEqual([]);
      expect(result.estimatedCostUsd).toBeCloseTo(0.42, 10);
    });
  });

  it("gives a resumed segment only the budget the run has not yet spent", async () => {
    const budgets: number[] = [];
    let first = true;
    await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        maxBudgetUsd: 5,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: (input) => {
          budgets.push(input.maxBudgetUsd);
          const stream = first ? streamOf(pausedMessage()) : streamOf(resumedSuccessMessage("session-pause"));
          first = false;
          return stream;
        },
        pause: () => Promise.resolve(RESOLUTION),
      },
    );
    expect(budgets[0]).toBe(5);
    expect(budgets[1]).toBeCloseTo(4.8, 10);
  });

  it("hands the caller's maxTurns and effort to every segment, resume included", async () => {
    const inputs: QueryInput[] = [];
    let first = true;
    await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        maxTurns: 40,
        effort: "medium",
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: (input) => {
          inputs.push(input);
          const stream = first ? streamOf(pausedMessage()) : streamOf(resumedSuccessMessage("session-pause"));
          first = false;
          return stream;
        },
        pause: () => Promise.resolve(RESOLUTION),
      },
    );
    expect(inputs.map((input) => input.maxTurns)).toEqual([40, 40]);
    expect(inputs.map((input) => input.effort)).toEqual(["medium", "medium"]);
  });

  it("defaults the turn cap and leaves effort unset when the caller names neither", async () => {
    const inputs: QueryInput[] = [];
    await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        queryFn: (input) => {
          inputs.push(input);
          return streamOf(successMessage());
        },
      },
    );
    expect(inputs[0]?.maxTurns).toBe(DEFAULT_MAX_TURNS);
    expect(inputs[0]?.effort).toBeUndefined();
  });

  it("fails with budget_exhausted rather than granting a fresh budget to the next segment", async () => {
    const expensivePause: RunStreamMessage = { ...pausedMessage(), total_cost_usd: 0.6 };
    let calls = 0;
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        maxBudgetUsd: 0.5,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: () => {
          calls += 1;
          if (calls > 1) {
            throw new Error("a segment started despite the exhausted budget");
          }
          return streamOf(expensivePause);
        },
        pause: () => Promise.resolve(RESOLUTION),
      },
    );
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("budget_exhausted");
    expect(caught.estimatedCostUsd).toBeCloseTo(0.6, 10);
  });

  it("counts prior spend against both the total and the first segment's budget on a cold resume", async () => {
    const budgets: number[] = [];
    const result = await runReport(
      { executionId: "e-1", prompt: "the analyst answer" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        maxBudgetUsd: 5,
        resumeFrom: { sessionId: "session-prior" },
        priorCostUsd: 0.3,
        queryFn: (input) => {
          budgets.push(input.maxBudgetUsd);
          return streamOf(resumedSuccessMessage("session-prior"));
        },
      },
    );
    expect(budgets[0]).toBeCloseTo(4.7, 10);
    expect(result.estimatedCostUsd).toBeCloseTo(0.72, 10);
  });

  it("disarms the deadline while paused: a pause longer than the deadline still completes", async () => {
    let first = true;
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        deadlineMs: 30,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: () => {
          const stream = first ? streamOf(pausedMessage()) : streamOf(resumedSuccessMessage("session-pause"));
          first = false;
          return stream;
        },
        pause: () => new Promise((resolve) => setTimeout(() => resolve(RESOLUTION), 100)),
      },
    );
    expect(result.output).toBe("the report");
  });

  it("re-arms the deadline per segment: a hung post-resume segment still times out", async () => {
    let first = true;
    const neverEnding = async function* (): AsyncGenerator<RunStreamMessage> {
      await new Promise(() => {});
      yield successMessage();
    };
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        deadlineMs: 30,
        checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
        queryFn: () => {
          const stream = first ? streamOf(pausedMessage()) : neverEnding();
          first = false;
          return stream;
        },
        pause: () => Promise.resolve(RESOLUTION),
      },
    );
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    expect(caught.subtype).toBe("deadline_exceeded");
  });

  it("starts straight into a resumed session when resumeFrom names one", async () => {
    const queries: QueryInput[] = [];
    const result = await runReport(
      { executionId: "e-1", prompt: "the analyst answer" },
      {
        workspaceRoot: TEST_WORKSPACE_ROOT,
        resumeFrom: { sessionId: "session-prior" },
        queryFn: (input) => {
          queries.push(input);
          return streamOf(resumedSuccessMessage("session-prior"));
        },
      },
    );
    expect(result.output).toBe("the report");
    expect(queries[0]?.resumeSessionId).toBe("session-prior");
    expect(queries[0]?.prompt).toBe("the analyst answer");
  });
});

describe("buildQueryOptions", () => {
  const WORKSPACE = "/home/bun/workspaces/run-x";

  function build(): { controller: AbortController; options: ReturnType<typeof buildQueryOptions> } {
    const controller = new AbortController();
    const options = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: controller,
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
    });
    return { controller, options };
  }

  it("exposes Bash and Read as the only tools, auto-approved, with the workspace as cwd", () => {
    const { options } = build();
    expect(options.tools).toEqual(["Bash", "Read"]);
    expect(options.allowedTools).toEqual(["Bash", "Read"]);
    expect(options.disallowedTools).toBeUndefined();
    expect(options.permissionMode).toBe("dontAsk");
    expect(options.cwd).toBe(WORKSPACE);
    expect(options.settingSources).toEqual([]);
  });

  it("hard-requires the sandbox and forbids the unsandboxed escape hatch", () => {
    const { options } = build();
    expect(options.sandbox?.enabled).toBe(true);
    expect(options.sandbox?.failIfUnavailable).toBe(true);
    expect(options.sandbox?.allowUnsandboxedCommands).toBe(false);
  });

  it("gives sandboxed commands no network: an empty allowlist, enforced strictly", () => {
    const { options } = build();
    expect(options.sandbox?.network).toEqual({ allowedDomains: [], strictAllowlist: true });
  });

  it("erases the shared roots from the sandbox and re-binds only this run's workspace", () => {
    const { options } = build();
    expect(options.sandbox?.filesystem).toEqual({
      denyRead: ["/app", "/home/bun", "/root", "/workspaces", "/tmp"],
      allowRead: [WORKSPACE],
      allowWrite: [WORKSPACE],
    });
  });

  it("denies the sandbox every variable the child strips, so the two lists cannot drift apart", () => {
    const { options } = build();
    const denied = (options.sandbox?.credentials?.envVars ?? []).map((entry) => entry.name);
    for (const name of STATIC_CREDENTIAL_VARS) {
      expect(denied).toContain(name);
    }
  });

  it("strips AWS credentials and credential-source pointers from the sandboxed command environment", () => {
    const { options } = build();
    expect(options.sandbox?.credentials).toEqual({
      envVars: [
        { name: "AWS_ACCESS_KEY_ID", mode: "deny" },
        { name: "AWS_SECRET_ACCESS_KEY", mode: "deny" },
        { name: "AWS_SESSION_TOKEN", mode: "deny" },
        { name: "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", mode: "deny" },
        { name: "AWS_CONTAINER_CREDENTIALS_FULL_URI", mode: "deny" },
        { name: "AWS_EC2_METADATA_SERVICE_ENDPOINT", mode: "deny" },
      ],
    });
  });

  it("registers the Read workspace gate as a PreToolUse hook bound to this run's workspace", async () => {
    const realWorkspace = mkdtempSync(join(tmpdir(), "gate-ws-"));
    try {
      const documentBytes = Buffer.from("%PDF-1.7 staged");
      const docsDir = join(realWorkspace, "docs");
      mkdirSync(docsDir);
      const stagedPath = join(docsDir, "accounts.pdf");
      writeFileSync(stagedPath, documentBytes);
      const mirrorDir = join(realWorkspace, "..", `gate-mirror-${Date.now()}`);
      mkdirSync(mirrorDir, { recursive: true });
      const options = buildQueryOptions({
        maxBudgetUsd: 3,
        maxTurns: DEFAULT_MAX_TURNS,
        abortController: new AbortController(),
        env: { PATH: "/usr/bin" },
        workspacePath: realWorkspace,
        readMirror: {
          dir: mirrorDir,
          documents: [
            { name: "accounts.pdf", sha256: sha256Hex(documentBytes), sizeBytes: documentBytes.length },
          ],
        },
      });
      const matchers = options.hooks?.PreToolUse;
      expect(matchers).toHaveLength(1);
      const hooks = matchers?.[0]?.hooks;
      expect(hooks).toHaveLength(1);
      const gate = hooks?.[0];
      if (gate === undefined) {
        throw new Error("PreToolUse gate hook missing");
      }
      const hookContext = { signal: new AbortController().signal };
      const preToolUse = (toolInput: unknown) =>
        ({
          hook_event_name: "PreToolUse",
          tool_name: "Read",
          tool_input: toolInput,
          tool_use_id: "toolu_1",
          session_id: "session-1",
          transcript_path: "/dev/null",
          cwd: realWorkspace,
        }) as const;
      const hookSpecificOutput = (output: HookJSONOutput): SyncHookJSONOutput["hookSpecificOutput"] => {
        if ("async" in output) {
          throw new Error("expected a synchronous hook output");
        }
        return output.hookSpecificOutput;
      };
      const denied = await gate(preToolUse({ file_path: "/proc/self/environ" }), "toolu_1", hookContext);
      expect(hookSpecificOutput(denied)).toMatchObject({ permissionDecision: "deny" });
      const allowed = await gate(preToolUse({ file_path: stagedPath }), "toolu_1", hookContext);
      expect(hookSpecificOutput(allowed)).toEqual({
        hookEventName: "PreToolUse",
        permissionDecision: "allow",
        updatedInput: { file_path: join(mirrorDir, "accounts.pdf") },
      });
      const unstagedPath = join(realWorkspace, "page.pdf");
      writeFileSync(unstagedPath, "pdf");
      const unstaged = await gate(preToolUse({ file_path: unstagedPath }), "toolu_1", hookContext);
      expect(hookSpecificOutput(unstaged)).toMatchObject({ permissionDecision: "deny" });
      // Pins containment to this run's workspace, not a shared ancestor (tmpdir, cwd, /).
      const otherWorkspace = mkdtempSync(join(tmpdir(), "gate-ws-"));
      try {
        const neighbourDocs = join(otherWorkspace, "docs");
        mkdirSync(neighbourDocs);
        const neighbourPath = join(neighbourDocs, "accounts.pdf");
        writeFileSync(neighbourPath, documentBytes);
        const neighbour = await gate(preToolUse({ file_path: neighbourPath }), "toolu_1", hookContext);
        expect(hookSpecificOutput(neighbour)).toMatchObject({ permissionDecision: "deny" });
      } finally {
        rmSync(otherWorkspace, { recursive: true, force: true });
      }
      rmSync(mirrorDir, { recursive: true, force: true });
    } finally {
      rmSync(realWorkspace, { recursive: true, force: true });
    }
  });

  it("keeps the fuses: turn cap, budget cap, abort wiring, allowlisted env only", () => {
    const { controller, options } = build();
    expect(options.maxTurns).toBe(DEFAULT_MAX_TURNS);
    expect(options.maxBudgetUsd).toBe(3);
    expect(options.abortController).toBe(controller);
    expect(options.env).toEqual({ PATH: "/usr/bin", ...RUN_GIT_IDENTITY_ENV });
  });

  it("threads a per-run turn cap and effort into the SDK options", () => {
    const options = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: 40,
      effort: "medium",
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
    });
    expect(options.maxTurns).toBe(40);
    expect(options.effort).toBe("medium");
  });

  it("keeps effort off the options entirely when unset, leaving the SDK's own default", () => {
    const { options } = build();
    expect("effort" in options).toBe(false);
  });

  it("asks for partial messages so usage arrives from the final message_delta, not a placeholder", () => {
    const { options } = build();
    expect(options.includePartialMessages).toBe(true);
  });

  it("wires a backend MCP server as an http config carrying the run token, tools widened to match", () => {
    const withMcp = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      mcpServer: { url: "https://platform.internal/mcp", runToken: "run-token-1" },
    });
    expect(withMcp.mcpServers).toEqual({
      platform: {
        type: "http",
        url: "https://platform.internal/mcp",
        headers: { Authorization: "Bearer run-token-1" },
        alwaysLoad: true,
      },
    });
    expect(withMcp.tools).toEqual(["Bash", "Read"]);
    expect(withMcp.allowedTools).toEqual(["Bash", "Read", "mcp__platform__*"]);
    expect(JSON.stringify(withMcp.env)).not.toContain("run-token-1");
  });

  it("omits the Authorization header when the run carries no token", () => {
    const withMcp = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      mcpServer: { url: "http://localhost:5118/mcp" },
    });
    expect(withMcp.mcpServers).toEqual({
      platform: { type: "http", url: "http://localhost:5118/mcp", alwaysLoad: true },
    });
  });

  it("keeps the MCP surface off entirely for runs without a backend URL, and pins strictMcpConfig", () => {
    const { options } = build();
    expect("mcpServers" in options).toBe(false);
    expect(options.allowedTools).toEqual(["Bash", "Read"]);
    expect(options.strictMcpConfig).toBe(true);
  });

  it("registers requested skills: plugin loaded from its exact path, Skill tool added, names enabled", () => {
    const withSkills = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      skillPlugin: { path: "/workspaces/home-1/skills", skillNames: ["skills:huh", "skills:grading"] },
    });
    expect(withSkills.tools).toEqual(["Bash", "Read", "Skill"]);
    expect(withSkills.allowedTools).toEqual(["Bash", "Read", "Skill"]);
    expect(withSkills.skills).toEqual(["skills:huh", "skills:grading"]);
    expect(withSkills.plugins).toEqual([
      { type: "local", path: "/workspaces/home-1/skills", skipMcpDiscovery: true },
    ]);
    expect(withSkills.plugins?.[0]?.path.startsWith(WORKSPACE)).toBe(false);
    expect(withSkills.settingSources).toEqual([]);
  });

  it("ro-binds a skill assets dir into the sandbox and names it in the env, never writable", () => {
    const withAssets = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      skillPlugin: {
        path: "/workspaces/home-1/skills",
        skillNames: ["skills:verify"],
        assetsPath: "/workspaces/skill-assets-1",
      },
    });
    expect(withAssets.sandbox?.filesystem?.allowRead).toEqual([WORKSPACE, "/workspaces/skill-assets-1"]);
    expect(withAssets.sandbox?.filesystem?.allowWrite).toEqual([WORKSPACE]);
    expect(withAssets.env?.[SKILL_ASSETS_ENV_VAR]).toBe("/workspaces/skill-assets-1");
  });

  it("keeps the sandbox and env byte-identical for a skill plugin without assets", () => {
    const withSkills = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      skillPlugin: { path: "/workspaces/home-1/skills", skillNames: ["skills:huh"] },
    });
    expect(withSkills.sandbox?.filesystem).toEqual({
      denyRead: ["/app", "/home/bun", "/root", "/workspaces", "/tmp"],
      allowRead: [WORKSPACE],
      allowWrite: [WORKSPACE],
    });
    expect(withSkills.env).toEqual({ PATH: "/usr/bin", ...RUN_GIT_IDENTITY_ENV });
  });

  it("widens allowedTools for skills and MCP together without dropping either", () => {
    const withBoth = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      skillPlugin: { path: "/workspaces/home-1/skills", skillNames: ["skills:huh"] },
      mcpServer: { url: "https://platform.internal/mcp", runToken: "tok" },
    });
    expect(withBoth.tools).toEqual(["Bash", "Read", "Skill"]);
    expect(withBoth.allowedTools).toEqual(["Bash", "Read", "Skill", "mcp__platform__*"]);
  });

  it("turns skills off explicitly for a run without them: no Skill tool, no plugins, nothing enabled", () => {
    const { options } = build();
    expect(options.skills).toEqual([]);
    expect("plugins" in options).toBe(false);
    expect(options.tools).toEqual(["Bash", "Read"]);
  });

  it("passes the requested output format through to the SDK, absent otherwise", () => {
    const outputFormat = { type: "json_schema" as const, schema: { type: "object" } };
    const withFormat = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      outputFormat,
    });
    expect(withFormat.outputFormat).toEqual(outputFormat);
    const { options } = build();
    expect("outputFormat" in options).toBe(false);
  });

  it("puts the runtime environment section above the caller's block array, every block kept", () => {
    const systemPrompt = ["static role and rules", SYSTEM_PROMPT_DYNAMIC_BOUNDARY, "per-pass context"];
    const withPrompt = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      systemPrompt,
    });
    expect(withPrompt.systemPrompt).toEqual([RUNTIME_ENVIRONMENT_SECTION, ...systemPrompt]);
  });

  it("puts the runtime environment section above the caller's string prompt", () => {
    const withPrompt = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      systemPrompt: "You are a credit analyst.",
    });
    expect(withPrompt.systemPrompt).toBe(`${RUNTIME_ENVIRONMENT_SECTION}\n\nYou are a credit analyst.`);
  });

  it("sends the runtime environment section alone when the caller sends no system prompt", () => {
    const { options } = build();
    expect(options.systemPrompt).toBe(RUNTIME_ENVIRONMENT_SECTION);
  });

  it("carries the section on a verification-shaped run: structured output, resumed, with sub-agents", () => {
    const verification = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      outputFormat: { type: "json_schema", schema: { type: "object" } },
      resumeSessionId: "session-under-review",
      agents: [{ name: "checker", description: "checks", prompt: "check it", skills: [] }],
      systemPrompt: "You verify the worker's draft.",
    });
    expect(verification.systemPrompt).toBe(
      `${RUNTIME_ENVIRONMENT_SECTION}\n\nYou verify the worker's draft.`,
    );
  });

  it("resumes the named session, and omits the resume option entirely otherwise", () => {
    const resumed = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE,
      resumeSessionId: "session-prior",
    });
    expect(resumed.resume).toBe("session-prior");
    const { options } = build();
    expect("resume" in options).toBe(false);
  });
});

type RecordedTrace = {
  trace: RunTrace;
  stages: { event: string; detail: Record<string, unknown> }[];
  appended: unknown[];
  finalizedWith: (string | null)[];
};

function recordingTrace(): RecordedTrace {
  const recorded: RecordedTrace = {
    stages: [],
    appended: [],
    finalizedWith: [],
    trace: {
      packDir: "/traces/e-1/attempt-1",
      appendMessage(message) {
        recorded.appended.push(message);
      },
      emitStage(event, detail = {}) {
        recorded.stages.push({ event, detail });
      },
      finalize({ sessionId }) {
        recorded.finalizedWith.push(sessionId);
      },
    },
  };
  return recorded;
}

describe("runReport trace tap", () => {
  it("appends every streamed message and stamps the trace pack path onto the result", async () => {
    const recorded = recordingTrace();
    const chatter: RunStreamMessage = { type: "assistant" };
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(chatter, successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
        trace: recorded.trace,
      },
    );
    expect(recorded.appended).toEqual([chatter, successMessage()]);
    expect(result.tracePath).toBe("/traces/e-1/attempt-1");
  });

  it("emits run.started then run.completed and finalizes with the session id on success", async () => {
    const recorded = recordingTrace();
    await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
        trace: recorded.trace,
      },
    );
    expect(recorded.stages.map((stage) => stage.event)).toEqual(["run.started", "run.completed"]);
    expect(recorded.stages[0]?.detail).toEqual({ resumed: false });
    expect(recorded.stages[1]?.detail).toMatchObject({ estimatedCostUsd: 0.42, sessionId: "session-1" });
    expect(recorded.finalizedWith).toEqual(["session-1"]);
  });

  it("emits run.failed and still finalizes when the run fails, rethrowing untouched", async () => {
    const recorded = recordingTrace();
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(errorMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
        trace: recorded.trace,
      },
    );
    await expect(promise).rejects.toBeInstanceOf(RunFailedError);
    expect(recorded.stages.map((stage) => stage.event)).toEqual(["run.started", "run.failed"]);
    expect(recorded.stages[1]?.detail).toMatchObject({ subtype: "error_max_turns" });
    expect(recorded.finalizedWith).toEqual(["session-2"]);
  });

  it("marks resumed runs in the run.started stage detail", async () => {
    const recorded = recordingTrace();
    await runReport(
      { executionId: "e-1", prompt: "answers" },
      {
        queryFn: () => streamOf(resumedSuccessMessage("session-prior")),
        workspaceRoot: TEST_WORKSPACE_ROOT,
        resumeFrom: { sessionId: "session-prior" },
        trace: recorded.trace,
      },
    );
    expect(recorded.stages[0]?.detail).toEqual({ resumed: true });
  });

  it("leaves the result free of tracePath when no trace is wired", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect("tracePath" in result).toBe(false);
  });
});

describe("sandboxConfigId", () => {
  it("is a stable sha256 fingerprint of the run lockdown shape", () => {
    expect(sandboxConfigId()).toMatch(/^[0-9a-f]{64}$/);
    expect(sandboxConfigId()).toBe(sandboxConfigId());
  });
});

const SONNET_MODEL = "us.anthropic.claude-sonnet-4-6";
const HAIKU_MODEL = "us.anthropic.claude-haiku-4-5";

function usageEvents(model: string, usage: Record<string, number>): RunStreamMessage[] {
  const { output_tokens: outputTokens, ...startUsage } = usage;
  return [
    {
      type: "stream_event",
      event: {
        type: "message_start",
        message: { id: "msg-1", model, usage: startUsage },
      },
    },
    {
      type: "stream_event",
      event: {
        type: "message_delta",
        delta: { stop_reason: "end_turn" },
        usage: { output_tokens: outputTokens ?? 0, input_tokens: null },
      },
    },
    { type: "stream_event", event: { type: "message_stop" } },
  ];
}

async function caughtFailure(promise: Promise<unknown>): Promise<RunFailedError> {
  let caught: unknown;
  try {
    await promise;
  } catch (error) {
    caught = error;
  }
  expect(caught).toBeInstanceOf(RunFailedError);
  if (!(caught instanceof RunFailedError)) {
    throw new Error("unreachable");
  }
  return caught;
}

describe("runReport cost accumulation from streamed usage", () => {
  it("reports the usage-derived cost when the deadline kills a run before any result", async () => {
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield* usageEvents(SONNET_MODEL, { input_tokens: 1_000_000, output_tokens: 1_000_000 });
      await new Promise(() => {});
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => stream(), deadlineMs: 30, workspaceRoot: TEST_WORKSPACE_ROOT },
      ),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.estimatedCostUsd).toBeCloseTo(18, 10);
  });

  it("keeps a killed run's figure complete when a sub-agent tool_result payload rode the stream", async () => {
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield* usageEvents(HAIKU_MODEL, { input_tokens: 1_000_000, output_tokens: 1_000_000 });
      yield {
        type: "user",
        tool_use_result: {
          status: "completed",
          agentId: "a-live-1",
          resolvedModel: HAIKU_MODEL,
          usage: { input_tokens: 1_000_000 },
        },
      };
      await new Promise(() => {});
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => stream(), deadlineMs: 30, workspaceRoot: TEST_WORKSPACE_ROOT },
      ),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    // Haiku: $6 for the main-loop message (1M in + 1M out), $1 for the payload's 1M input.
    expect(failure.estimatedCostUsd).toBeCloseTo(7, 10);
  });

  it("prices a completed sub-agent from the injected transcript reader rather than its payload", async () => {
    const asked: [string, string][] = [];
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield* usageEvents(HAIKU_MODEL, { input_tokens: 1_000_000, output_tokens: 1_000_000 });
      yield {
        type: "user",
        session_id: "session-live",
        tool_use_result: {
          status: "completed",
          agentId: "a-live-2",
          resolvedModel: HAIKU_MODEL,
          usage: { input_tokens: 1_000_000 },
        },
      };
      await new Promise(() => {});
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () => stream(),
          deadlineMs: 30,
          workspaceRoot: TEST_WORKSPACE_ROOT,
          subagentApiMessages: (sessionId, agentId) => {
            asked.push([sessionId, agentId]);
            return [
              {
                messageId: "sub-1",
                model: HAIKU_MODEL,
                rawUsage: { output_tokens: 1_000_000 },
                settled: true,
              },
              {
                messageId: "sub-2",
                model: HAIKU_MODEL,
                rawUsage: { output_tokens: 1_000_000 },
                settled: true,
              },
            ];
          },
        },
      ),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(asked).toEqual([["session-live", "a-live-2"]]);
    // Haiku: $6 for the main-loop message, $5 per transcript message's 1M output — the payload's 1M input is not added.
    expect(failure.estimatedCostUsd).toBeCloseTo(16, 10);
  });

  it("reports the usage-derived cost when the caller cancels a run mid-stream", async () => {
    const controller = new AbortController();
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield* usageEvents(HAIKU_MODEL, { input_tokens: 1_000_000, output_tokens: 1_000_000 });
      controller.abort();
      await new Promise(() => {});
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => stream(), signal: controller.signal, workspaceRoot: TEST_WORKSPACE_ROOT },
      ),
    );
    expect(failure.subtype).toBe("aborted");
    expect(failure.estimatedCostUsd).toBeCloseTo(6, 10);
  });

  it("streams each rise of the running cost through onCostUpdate", async () => {
    const reported: number[] = [];
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () =>
          streamOf(
            ...usageEvents(SONNET_MODEL, { input_tokens: 1_000_000 }),
            ...usageEvents(SONNET_MODEL, { output_tokens: 1_000_000 }),
            successMessage(),
          ),
        onCostUpdate: (cost) => reported.push(cost),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(reported).toHaveLength(2);
    expect(reported[0]).toBeCloseTo(3, 10);
    expect(reported[1]).toBeCloseTo(18, 10);
    expect(result.estimatedCostUsd).toBeCloseTo(18, 10);
  });

  it("keeps the SDK's own success figure when it exceeds the usage estimate, without summing them", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(...usageEvents(SONNET_MODEL, { input_tokens: 1_000 }), successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.estimatedCostUsd).toBe(0.42);
  });

  it("raises a success figure to the usage estimate when the estimate is larger", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(...usageEvents(SONNET_MODEL, { input_tokens: 1_000_000 }), successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.estimatedCostUsd).toBeCloseTo(3, 10);
  });

  it("keeps an SDK-reported failure cost that exceeds the usage floor, without summing them", async () => {
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () => streamOf(...usageEvents(HAIKU_MODEL, { input_tokens: 1_000 }), errorMessage()),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("error_max_turns");
    expect(failure.estimatedCostUsd).toBe(0.13);
  });

  it("raises an SDK-reported failure cost to the usage floor when the estimate is larger", async () => {
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () =>
            streamOf(
              ...usageEvents(SONNET_MODEL, { input_tokens: 1_000_000, output_tokens: 1_000_000 }),
              errorMessage(),
            ),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.estimatedCostUsd).toBeCloseTo(18, 10);
  });

  it("settles a pause at the larger of the SDK figure and the segment estimate, then adds the killed segment", async () => {
    let segments = 0;
    const queryFn = (): AsyncGenerator<RunStreamMessage> => {
      segments += 1;
      if (segments === 1) {
        return streamOf(...usageEvents(SONNET_MODEL, { input_tokens: 1_000_000 }), pausedMessage());
      }
      return (async function* (): AsyncGenerator<RunStreamMessage> {
        yield* usageEvents(SONNET_MODEL, { input_tokens: 1_000_000 });
        await new Promise(() => {});
      })();
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn,
          deadlineMs: 200,
          maxBudgetUsd: 10,
          checkpointer: () => Promise.resolve(CHECKPOINT_BYTES),
          pause: () => Promise.resolve(RESOLUTION),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.estimatedCostUsd).toBeCloseTo(6, 10);
  });

  it("builds a cold resume's kill cost on top of the carried prior spend", async () => {
    const reported: number[] = [];
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield* usageEvents(SONNET_MODEL, { input_tokens: 1_000_000 });
      await new Promise(() => {});
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "the answer" },
        {
          queryFn: () => stream(),
          deadlineMs: 30,
          resumeFrom: { sessionId: "session-prior" },
          priorCostUsd: 1,
          onCostUpdate: (cost) => reported.push(cost),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
    expect(failure.estimatedCostUsd).toBeCloseTo(4, 10);
    expect(reported).toHaveLength(1);
    expect(reported[0]).toBeCloseTo(4, 10);
  });

  it("wraps a stream that throws mid-run into a failure carrying the accumulated cost", async () => {
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield* usageEvents(SONNET_MODEL, { input_tokens: 1_000_000 });
      throw new Error("connection reset");
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => stream(), workspaceRoot: TEST_WORKSPACE_ROOT },
      ),
    );
    expect(failure.subtype).toBe("stream_error");
    expect(failure.estimatedCostUsd).toBeCloseTo(3, 10);
    expect(failure.message).toMatch(/connection reset/);
  });

  it("propagates a stream error untouched when nothing was spent", async () => {
    const stream: AsyncIterable<RunStreamMessage> = {
      [Symbol.asyncIterator]: () => ({ next: () => Promise.reject(new Error("spawn failed")) }),
    };
    const promise = runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => stream, workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    let caught: unknown;
    try {
      await promise;
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(Error);
    expect(caught).not.toBeInstanceOf(RunFailedError);
  });
});

describe("buildQueryOptions with declared agents", () => {
  const WORKSPACE_WITH_AGENTS = "/workspaces/run-agents";
  const RESOLVED_AGENTS = [
    {
      name: "verifier",
      description: "Checks extracted figures.",
      prompt: "You verify figures.",
      skills: ["skills:beta-check"],
    },
    { name: "cross-checker", description: "Cross-checks totals.", prompt: "You cross-check.", skills: [] },
  ];

  function buildWithAgents(): ReturnType<typeof buildQueryOptions> {
    return buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: 44,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE_WITH_AGENTS,
      agents: RESOLVED_AGENTS,
    });
  }

  it("widens the tool surface to exactly Bash, Read, and Task — an exact list, never a glob", () => {
    const options = buildWithAgents();
    expect(options.tools).toEqual(["Bash", "Read", "Task"]);
    expect(options.allowedTools).toEqual(["Bash", "Read", "Task"]);
  });

  it("opens every declared agent's prompt with the runtime environment section, its own prompt verbatim after", () => {
    const options = buildWithAgents();
    for (const agent of RESOLVED_AGENTS) {
      expect(options.agents?.[agent.name]?.prompt).toBe(`${RUNTIME_ENVIRONMENT_SECTION}\n\n${agent.prompt}`);
    }
  });

  it("declares exactly the run's agents, each locked to the run tools and its declared skill subset", () => {
    const options = buildWithAgents();
    expect(options.agents).toEqual({
      verifier: {
        description: "Checks extracted figures.",
        prompt: `${RUNTIME_ENVIRONMENT_SECTION}\n\nYou verify figures.`,
        tools: ["Bash", "Read"],
        maxTurns: 44,
        skills: ["skills:beta-check"],
      },
      "cross-checker": {
        description: "Cross-checks totals.",
        prompt: `${RUNTIME_ENVIRONMENT_SECTION}\n\nYou cross-check.`,
        tools: ["Bash", "Read"],
        maxTurns: 44,
      },
    });
  });

  it("turn accounting: every declared agent is bounded by the run's own maxTurns fuse", () => {
    const options = buildWithAgents();
    for (const definition of Object.values(options.agents ?? {})) {
      expect(definition.maxTurns).toBe(44);
    }
    expect(Object.keys(options.agents ?? {})).toHaveLength(RESOLVED_AGENTS.length);
  });

  it("registers the agent gate for both tool names beside the read gate", () => {
    const options = buildWithAgents();
    const matchers = (options.hooks?.PreToolUse ?? []).map((entry) => entry.matcher);
    expect(matchers).toEqual(["Read", "Agent", "Task"]);
  });

  it("keeps a run without agents at exactly today's surface: no Task tool, no agents, no agent gate", () => {
    const options = buildQueryOptions({
      maxBudgetUsd: 3,
      maxTurns: DEFAULT_MAX_TURNS,
      abortController: new AbortController(),
      env: { PATH: "/usr/bin" },
      workspacePath: WORKSPACE_WITH_AGENTS,
    });
    expect(options.tools).toEqual(["Bash", "Read"]);
    expect("agents" in options).toBe(false);
    expect((options.hooks?.PreToolUse ?? []).map((entry) => entry.matcher)).toEqual(["Read"]);
  });

  it("the agent gate hook denies an undeclared subagent_type and forces foreground on a declared one", async () => {
    const options = buildWithAgents();
    const agentMatcher = (options.hooks?.PreToolUse ?? []).find((entry) => entry.matcher === "Agent");
    const hook = agentMatcher?.hooks[0];
    if (hook === undefined) {
      throw new Error("no agent gate hook registered");
    }
    const context = { signal: new AbortController().signal };
    const base = {
      hook_event_name: "PreToolUse" as const,
      session_id: "s",
      transcript_path: "t",
      cwd: WORKSPACE_WITH_AGENTS,
    };
    const denied = (await hook(
      {
        ...base,
        tool_use_id: "tool-1",
        tool_name: "Agent",
        tool_input: { description: "d", prompt: "p", subagent_type: "general-purpose" },
      },
      "tool-1",
      context,
    )) as SyncHookJSONOutput;
    expect(denied.hookSpecificOutput).toMatchObject({ permissionDecision: "deny" });
    const allowed = (await hook(
      {
        ...base,
        tool_use_id: "tool-2",
        tool_name: "Agent",
        tool_input: { description: "d", prompt: "p", subagent_type: "verifier", run_in_background: true },
      },
      "tool-2",
      context,
    )) as SyncHookJSONOutput;
    expect(allowed.hookSpecificOutput).toMatchObject({
      permissionDecision: "allow",
      updatedInput: { subagent_type: "verifier", run_in_background: false },
    });
  });
});

describe("run token usage totals", () => {
  it("carries the accumulated token totals on a successful result", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () =>
          streamOf(
            ...usageEvents(SONNET_MODEL, {
              input_tokens: 120,
              output_tokens: 900,
              cache_creation_input_tokens: 40,
              cache_read_input_tokens: 3_000,
            }),
            successMessage(),
          ),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.usage).toEqual({
      inputTokens: 120,
      outputTokens: 900,
      cacheCreationInputTokens: 40,
      cacheReadInputTokens: 3_000,
    });
  });

  it("reports zeroed usage for a stream that carried no usage events", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect(result.usage).toEqual({
      inputTokens: 0,
      outputTokens: 0,
      cacheCreationInputTokens: 0,
      cacheReadInputTokens: 0,
    });
  });

  it("attaches the best-known usage to a failed run's error", async () => {
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () =>
            streamOf(...usageEvents(SONNET_MODEL, { input_tokens: 50, output_tokens: 700 }), errorMessage()),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("error_max_turns");
    expect(failure.usage).toEqual({
      inputTokens: 50,
      outputTokens: 700,
      cacheCreationInputTokens: 0,
      cacheReadInputTokens: 0,
    });
  });

  it("hands the running usage to onCostUpdate alongside the cost", async () => {
    const usages: unknown[] = [];
    await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () =>
          streamOf(...usageEvents(SONNET_MODEL, { input_tokens: 10, output_tokens: 500 }), successMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
        onCostUpdate: (_cost, usage) => usages.push(usage),
      },
    );
    expect(usages.length).toBeGreaterThan(0);
    expect(usages[usages.length - 1]).toEqual({
      inputTokens: 10,
      outputTokens: 500,
      cacheCreationInputTokens: 0,
      cacheReadInputTokens: 0,
    });
  });
});

describe("budget-kill salvage", () => {
  const EXTRACTION = { status: "ok", periods: ["FY0", "FY-1"], currency_and_units: "USD millions" };

  function structuredOutputToolUse(toolInput: unknown, toolUseId = "tool-so-1"): RunStreamMessage {
    return {
      type: "assistant",
      parent_tool_use_id: null,
      message: {
        role: "assistant",
        content: [{ type: "tool_use", id: toolUseId, name: "StructuredOutput", input: toolInput }],
      },
    };
  }

  function structuredOutputRejection(toolUseId = "tool-so-1"): RunStreamMessage {
    return {
      type: "user",
      parent_tool_use_id: null,
      message: {
        role: "user",
        content: [{ type: "tool_result", tool_use_id: toolUseId, is_error: true, content: [] }],
      },
    };
  }

  function budgetKillMessage(): RunStreamMessage {
    return {
      type: "result",
      subtype: "error_max_budget_usd",
      total_cost_usd: 2.58,
      session_id: "session-budget",
      errors: ["Reached maximum budget ($2.5)"],
    };
  }

  it("settles a budget-killed run as a success on the completed structured output", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(structuredOutputToolUse(EXTRACTION), budgetKillMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.structuredOutput).toEqual(EXTRACTION);
    expect(result.budgetExhausted).toBe(true);
    expect(result.estimatedCostUsd).toBe(2.58);
    expect(result.sessionId).toBe("session-budget");
  });

  // The platform stores result.output as the block answer, so the salvage must serialize
  // structured_output into the result text exactly as a normal outputFormat success does
  // (measured on CLI 2.1.235).
  it("serializes the salvaged output into the text result the same way a normal success does", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(structuredOutputToolUse(EXTRACTION), budgetKillMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
      },
    );
    expect(result.output).toBe(JSON.stringify(EXTRACTION));
  });

  it("still fails a budget kill that produced no structured output", async () => {
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => streamOf(budgetKillMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
      ),
    );
    expect(failure.subtype).toBe("error_max_budget_usd");
  });

  it("does not salvage a structured output the CLI rejected before the kill", async () => {
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        {
          queryFn: () =>
            streamOf(structuredOutputToolUse(EXTRACTION), structuredOutputRejection(), budgetKillMessage()),
          workspaceRoot: TEST_WORKSPACE_ROOT,
        },
      ),
    );
    expect(failure.subtype).toBe("error_max_budget_usd");
  });

  it("passes the outputFormat structured-output requirement when the run settles on a salvage", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      {
        queryFn: () => streamOf(structuredOutputToolUse(EXTRACTION), budgetKillMessage()),
        workspaceRoot: TEST_WORKSPACE_ROOT,
        outputFormat: { type: "json_schema", schema: { type: "object" } },
      },
    );
    expect(result.structuredOutput).toEqual(EXTRACTION);
    expect(result.budgetExhausted).toBe(true);
  });

  it("keeps a deadline kill a failure even when a structured output was seen", async () => {
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield structuredOutputToolUse(EXTRACTION);
      await new Promise(() => {});
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => stream(), deadlineMs: 30, workspaceRoot: TEST_WORKSPACE_ROOT },
      ),
    );
    expect(failure.subtype).toBe("deadline_exceeded");
  });

  it("keeps a caller cancellation a failure even when a structured output was seen", async () => {
    const controller = new AbortController();
    const stream = async function* (): AsyncGenerator<RunStreamMessage> {
      yield structuredOutputToolUse(EXTRACTION);
      controller.abort();
      await new Promise(() => {});
    };
    const failure = await caughtFailure(
      runReport(
        { executionId: "e-1", prompt: "p" },
        { queryFn: () => stream(), signal: controller.signal, workspaceRoot: TEST_WORKSPACE_ROOT },
      ),
    );
    expect(failure.subtype).toBe("aborted");
  });

  it("leaves an ordinary success without the budget-exhausted flag", async () => {
    const result = await runReport(
      { executionId: "e-1", prompt: "p" },
      { queryFn: () => streamOf(successMessage()), workspaceRoot: TEST_WORKSPACE_ROOT },
    );
    expect("budgetExhausted" in result).toBe(false);
  });
});

describe("session-id self-verification", () => {
  const resumedOptions = (queryFn: QueryFn): Parameters<typeof runReport>[1] => ({
    workspaceRoot: TEST_WORKSPACE_ROOT,
    resumeFrom: { sessionId: "session-prior" },
    queryFn,
  });

  async function caughtFailure(queryFn: QueryFn): Promise<RunFailedError> {
    let caught: unknown;
    try {
      await runReport({ executionId: "e-1", prompt: "revise" }, resumedOptions(queryFn));
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(RunFailedError);
    if (!(caught instanceof RunFailedError)) {
      throw new Error("unreachable");
    }
    return caught;
  }

  it("fails session_id_mismatch at $0 when the init message reports a different session", async () => {
    const freshInit: RunStreamMessage = { type: "system", subtype: "init", session_id: "session-fresh" };
    let streamAborted = false;
    const failure = await caughtFailure(() => {
      return {
        [Symbol.asyncIterator]: () => {
          let sent = false;
          return {
            next: () => {
              if (sent) {
                return new Promise<IteratorResult<RunStreamMessage>>(() => {});
              }
              sent = true;
              return Promise.resolve({ done: false as const, value: freshInit });
            },
            return: () => {
              streamAborted = true;
              return Promise.resolve({ done: true as const, value: undefined });
            },
          };
        },
      };
    });
    expect(failure.subtype).toBe("session_id_mismatch");
    expect(failure.estimatedCostUsd).toBe(0);
    expect(failure.sessionId).toBeNull();
    expect(failure.message).toContain("session-prior");
    expect(failure.message).toContain("session-fresh");
    expect(streamAborted).toBe(true);
  });

  it("runs on when the init message reports the session it was asked to resume", async () => {
    const matchingInit: RunStreamMessage = { type: "system", subtype: "init", session_id: "session-prior" };
    const result = await runReport(
      { executionId: "e-1", prompt: "revise" },
      resumedOptions(() => streamOf(matchingInit, { ...successMessage(), session_id: "session-prior" })),
    );
    expect(result.output).toBe("the report");
  });

  it("lets an init carrying no session id pass, and the result message backstops the check", async () => {
    const bareInit: RunStreamMessage = { type: "system", subtype: "init" };
    const result = await runReport(
      { executionId: "e-1", prompt: "revise" },
      resumedOptions(() => streamOf(bareInit, { ...successMessage(), session_id: "session-prior" })),
    );
    expect(result.output).toBe("the report");
  });

  it("reports the real spend when the mismatch is only caught at the result message", async () => {
    const bareInit: RunStreamMessage = { type: "system", subtype: "init" };
    const failure = await caughtFailure(() =>
      streamOf(bareInit, { ...successMessage(), session_id: "session-fresh", total_cost_usd: 0.42 }),
    );
    expect(failure.subtype).toBe("session_id_mismatch");
    expect(failure.estimatedCostUsd).toBe(0.42);
    expect(failure.sessionId).toBeNull();
  });

  it("catches a pause frame that carries the wrong session before any checkpoint lands", async () => {
    const pauseFrame: RunStreamMessage = {
      type: "result",
      subtype: "success",
      terminal_reason: "tool_deferred",
      session_id: "session-fresh",
      total_cost_usd: 0.05,
      deferred_tool_use: {
        id: "q-1",
        name: "mcp__platform__request_analyst_input",
        input: { question: "Which fund?" },
      },
    };
    const failure = await caughtFailure(() => streamOf(pauseFrame));
    expect(failure.subtype).toBe("session_id_mismatch");
    expect(failure.estimatedCostUsd).toBe(0.05);
  });
});
