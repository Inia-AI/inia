import { afterAll, describe, expect, it } from "bun:test";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { messageDelta, messageStart, messageStop } from "./stream-event-fixtures.js";
import {
  buildLabelRecord,
  createRunTrace,
  ENVELOPE_ABSENT,
  readInstalledSdkVersion,
  TRACE_FORMAT_VERSION,
  type TraceEnvelopeInput,
  tracePackDirOf,
} from "./trace.js";
import { claimRetainedRoot, RETAINED_WORKSPACE_ROOTS } from "./workspace-layout.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "trace-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

function envelopeInput(): TraceEnvelopeInput {
  return {
    modelProvider: "bedrock",
    modelPin: "us.anthropic.claude-sonnet-4-6",
    promptSha256: "ab".repeat(32),
    systemPromptSha256: "ef".repeat(32),
    outputFormatSha256: "12".repeat(32),
    stagedInputsSha256: "34".repeat(32),
    mcpServerSha256: ENVELOPE_ABSENT,
    tools: ["Bash"],
    skills: ["skills:huh"],
    agents: [],
    sandboxConfigId: "cd".repeat(32),
    sdkVersion: "0.3.220",
    reasoning: {
      effort: "high",
      effortSource: "sdk_default",
      thinking: { type: "adaptive" },
      thinkingSource: "sdk_default_assumed",
    },
  };
}

function freshArea(name: string): { traceRoot: string; configDir: string } {
  const area = mkdtempSync(join(TEST_ROOT, `${name}-`));
  const configDir = join(area, ".claude");
  mkdirSync(configDir, { recursive: true });
  return { traceRoot: join(area, "traces"), configDir };
}

function readRecords(packDir: string): Record<string, unknown>[] {
  const text = readFileSync(join(packDir, "trace.ndjson"), "utf8");
  return text
    .split("\n")
    .filter((line) => line !== "")
    .map((line) => JSON.parse(line) as Record<string, unknown>);
}

const IMAGE_BYTES = Buffer.from("fake-jpeg-page-render-bytes");
const IMAGE_BASE64 = IMAGE_BYTES.toString("base64");
const IMAGE_SHA256 = createHash("sha256").update(IMAGE_BYTES).digest("hex");

function base64ImageBlock(): Record<string, unknown> {
  return {
    type: "image",
    source: { type: "base64", media_type: "image/jpeg", data: IMAGE_BASE64 },
  };
}

function messageContentOf(record: Record<string, unknown> | undefined): unknown[] {
  const sdkMessage = record?.["message"] as Record<string, unknown> | undefined;
  const inner = sdkMessage?.["message"] as Record<string, unknown> | undefined;
  return inner?.["content"] as unknown[];
}

function toolUseMessage(id: string, name: string, input: unknown): Record<string, unknown> {
  return { type: "assistant", message: { content: [{ type: "tool_use", id, name, input }] } };
}

function toolResultMessage(id: string, content: unknown, isError = false): Record<string, unknown> {
  return {
    type: "user",
    message: { content: [{ type: "tool_result", tool_use_id: id, content, is_error: isError }] },
  };
}

describe("tracePackDirOf", () => {
  it("keys the pack dir by executionId and attempt", () => {
    expect(tracePackDirOf("/workspaces/traces", "e-1", 1)).toBe("/workspaces/traces/e-1/attempt-1");
  });
});

describe("createRunTrace", () => {
  it("opens the trace with a version-envelope header record", () => {
    const { traceRoot, configDir } = freshArea("envelope");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-1",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    const [first] = readRecords(trace.packDir);
    expect(first).toMatchObject({
      type: "envelope",
      traceVersion: TRACE_FORMAT_VERSION,
      executionId: "e-1",
      attempt: 1,
      modelProvider: "bedrock",
      modelPin: "us.anthropic.claude-sonnet-4-6",
      promptSha256: "ab".repeat(32),
      systemPromptSha256: "ef".repeat(32),
      outputFormatSha256: "12".repeat(32),
      stagedInputsSha256: "34".repeat(32),
      mcpServerSha256: ENVELOPE_ABSENT,
      tools: ["Bash"],
      skills: ["skills:huh"],
      agents: [],
      sandboxConfigId: "cd".repeat(32),
      sdkVersion: "0.3.220",
      reasoning: {
        effort: "high",
        effortSource: "sdk_default",
        thinking: { type: "adaptive" },
        thinkingSource: "sdk_default_assumed",
      },
    });
    expect(first?.["traceVersion"]).toBe(11);
    expect(typeof first?.["uuid"]).toBe("string");
    expect(typeof first?.["at"]).toBe("string");
  });

  it("appends every message as an NDJSON record with an increasing seq", () => {
    const { traceRoot, configDir } = freshArea("messages");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-2",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage({ type: "system", subtype: "init" });
    trace.appendMessage({ type: "result", subtype: "success", result: "done" });
    const records = readRecords(trace.packDir);
    const messages = records.filter((record) => record["type"] === "message");
    expect(messages).toHaveLength(2);
    expect(messages[0]).toMatchObject({ seq: 1, message: { type: "system", subtype: "init" } });
    expect(messages[1]).toMatchObject({
      seq: 2,
      message: { type: "result", subtype: "success", result: "done" },
    });
  });

  it("derives one usage record per API message from the final delta, never the start placeholder", () => {
    const { traceRoot, configDir } = freshArea("usage");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-usage",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage(
      messageStart("us.anthropic.claude-sonnet-4-6", "msg-1", {
        input_tokens: 120,
        output_tokens: 1,
        cache_creation_input_tokens: 56,
        cache_read_input_tokens: 7800,
      }),
    );
    trace.appendMessage({
      type: "assistant",
      message: {
        model: "us.anthropic.claude-sonnet-4-6",
        usage: { input_tokens: 120, output_tokens: 2 },
      },
    });
    trace.appendMessage(
      messageDelta({
        output_tokens: 13_343,
        input_tokens: null,
        output_tokens_details: { thinking_tokens: 12_000 },
      }),
    );
    trace.appendMessage(messageStop());
    const usageRecords = readRecords(trace.packDir).filter((record) => record["type"] === "usage");
    expect(usageRecords).toHaveLength(1);
    expect(usageRecords[0]).toMatchObject({
      messageId: "msg-1",
      model: "us.anthropic.claude-sonnet-4-6",
      usage: {
        input_tokens: 120,
        output_tokens: 13_343,
        cache_creation_input_tokens: 56,
        cache_read_input_tokens: 7800,
        output_tokens_details: { thinking_tokens: 12_000 },
      },
    });
  });

  it("records output_tokens_details as explicit null when the stream never returned it", () => {
    const { traceRoot, configDir } = freshArea("usage-no-details");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-usage-nd",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage(messageStart("us.anthropic.claude-sonnet-4-6", "msg-1", { input_tokens: 10 }));
    trace.appendMessage(messageDelta({ output_tokens: 500 }));
    trace.appendMessage(messageStop());
    const [usageRecord] = readRecords(trace.packDir).filter((record) => record["type"] === "usage");
    const usage = usageRecord?.["usage"] as Record<string, unknown>;
    expect("output_tokens_details" in usage).toBe(true);
    expect(usage["output_tokens_details"]).toBeNull();
  });

  it("never records stream events as message records and burns no seq on them", () => {
    const { traceRoot, configDir } = freshArea("stream-events");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-stream",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage(messageStart("us.anthropic.claude-sonnet-4-6", "msg-1", { input_tokens: 10 }));
    trace.appendMessage({
      type: "stream_event",
      event: { type: "content_block_delta", delta: { type: "text_delta", text: "chunk" } },
    });
    trace.appendMessage(messageStop());
    trace.appendMessage({ type: "result", subtype: "success" });
    const messages = readRecords(trace.packDir).filter((record) => record["type"] === "message");
    expect(messages).toHaveLength(1);
    expect(messages[0]).toMatchObject({ seq: 1, message: { type: "result" } });
  });

  it("replaces base64 image blocks in tool results with a stub and never records the payload", () => {
    const { traceRoot, configDir } = freshArea("image-tool-result");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-img-tr",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage({
      type: "user",
      message: {
        content: [
          {
            type: "tool_result",
            tool_use_id: "tu-read",
            content: [{ type: "text", text: "Page 1 of 10" }, base64ImageBlock()],
          },
        ],
      },
    });
    trace.finalize({ sessionId: null });
    expect(readFileSync(join(trace.packDir, "trace.ndjson"), "utf8")).not.toContain(IMAGE_BASE64);
    const [message] = readRecords(trace.packDir).filter((record) => record["type"] === "message");
    const [toolResult] = messageContentOf(message) as Record<string, unknown>[];
    expect(toolResult?.["content"]).toEqual([
      { type: "text", text: "Page 1 of 10" },
      {
        type: "image_stub",
        media_type: "image/jpeg",
        decoded_bytes: IMAGE_BYTES.byteLength,
        sha256: IMAGE_SHA256,
        tool_use_id: "tu-read",
      },
    ]);
  });

  it("stubs image blocks riding directly on message content with a null tool_use_id", () => {
    const { traceRoot, configDir } = freshArea("image-direct");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-img-direct",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage({ type: "user", message: { content: [base64ImageBlock()] } });
    trace.finalize({ sessionId: null });
    const [message] = readRecords(trace.packDir).filter((record) => record["type"] === "message");
    expect(messageContentOf(message)).toEqual([
      {
        type: "image_stub",
        media_type: "image/jpeg",
        decoded_bytes: IMAGE_BYTES.byteLength,
        sha256: IMAGE_SHA256,
        tool_use_id: null,
      },
    ]);
  });

  it("leaves url-source image blocks unchanged — they carry no payload to strip", () => {
    const { traceRoot, configDir } = freshArea("image-url");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-img-url",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    const urlBlock = { type: "image", source: { type: "url", url: "https://example.test/page.png" } };
    trace.appendMessage({ type: "user", message: { content: [urlBlock] } });
    trace.finalize({ sessionId: null });
    const [message] = readRecords(trace.packDir).filter((record) => record["type"] === "message");
    expect(messageContentOf(message)).toEqual([urlBlock]);
  });

  it("drops thinking_tokens heartbeats without burning a seq while every other system subtype records", () => {
    const { traceRoot, configDir } = freshArea("thinking-tokens");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-thinking",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage({ type: "system", subtype: "init" });
    trace.appendMessage({
      type: "system",
      subtype: "thinking_tokens",
      estimated_tokens: 1200,
      estimated_tokens_delta: 100,
    });
    trace.appendMessage({ type: "system", subtype: "task_updated", task_id: "t-1", patch: {} });
    trace.appendMessage({ type: "result", subtype: "success" });
    const messages = readRecords(trace.packDir).filter((record) => record["type"] === "message");
    expect(messages.map((record) => record["seq"])).toEqual([1, 2, 3]);
    const subtypes = messages.map((record) => (record["message"] as Record<string, unknown>)["subtype"]);
    expect(subtypes).toEqual(["init", "task_updated", "success"]);
  });

  it("settles an in-flight message's usage at finalization", () => {
    const { traceRoot, configDir } = freshArea("usage-inflight");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-usage-if",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage(messageStart("us.anthropic.claude-sonnet-4-6", "msg-1", { input_tokens: 10 }));
    trace.appendMessage(messageDelta({ output_tokens: 900 }));
    trace.finalize({ sessionId: null });
    const [usageRecord] = readRecords(trace.packDir).filter((record) => record["type"] === "usage");
    expect(usageRecord).toMatchObject({ messageId: "msg-1", usage: { output_tokens: 900 } });
  });

  it("derives a model_info record from the init message with explicit nulls for absent flags", () => {
    const { traceRoot, configDir } = freshArea("model-info");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-model-info",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage({
      type: "system",
      subtype: "init",
      model: "us.anthropic.claude-sonnet-4-6",
      fast_mode_disabled_reason: "not_first_party",
    });
    const [modelInfo] = readRecords(trace.packDir).filter((record) => record["type"] === "model_info");
    expect(modelInfo).toMatchObject({
      model: "us.anthropic.claude-sonnet-4-6",
      fastModeState: null,
      fastModeDisabledReason: "not_first_party",
      supportedEffortLevels: null,
      supportsAdaptiveThinking: null,
    });
  });

  it("writes no usage record from non-stream messages, including assistant placeholder snapshots", () => {
    const { traceRoot, configDir } = freshArea("no-usage");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-no-usage",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage({ type: "system", subtype: "init" });
    trace.appendMessage(toolUseMessage("tu-1", "Bash", { command: "ls" }));
    trace.appendMessage({
      type: "assistant",
      message: { model: "us.anthropic.claude-sonnet-4-6", usage: { input_tokens: 5, output_tokens: 2 } },
    });
    trace.finalize({ sessionId: null });
    expect(readRecords(trace.packDir).filter((record) => record["type"] === "usage")).toHaveLength(0);
  });

  it("derives a tool_call record with name, truncated input, output size, duration, and error class", () => {
    const { traceRoot, configDir } = freshArea("tools");
    let clock = 1_000;
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-3",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
      now: () => clock,
    });
    trace.appendMessage(toolUseMessage("tu-1", "Bash", { command: "ls -la" }));
    clock += 250;
    trace.appendMessage(toolResultMessage("tu-1", "file-a\nfile-b"));
    const [toolCall] = readRecords(trace.packDir).filter((record) => record["type"] === "tool_call");
    expect(toolCall).toMatchObject({
      toolUseId: "tu-1",
      toolName: "Bash",
      inputTruncated: JSON.stringify({ command: "ls -la" }),
      outputBytes: Buffer.byteLength(JSON.stringify("file-a\nfile-b")),
      durationMs: 250,
      errorClass: null,
    });
  });

  it("truncates tool input to 500 characters and classifies errored results", () => {
    const { traceRoot, configDir } = freshArea("truncate");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-4",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    const longCommand = "x".repeat(2_000);
    trace.appendMessage(toolUseMessage("tu-2", "Bash", { command: longCommand }));
    trace.appendMessage(toolResultMessage("tu-2", "boom", true));
    const [toolCall] = readRecords(trace.packDir).filter((record) => record["type"] === "tool_call");
    expect(String(toolCall?.["inputTruncated"]).length).toBe(500);
    expect(toolCall?.["errorClass"]).toBe("tool_error");
  });

  it("writes a settled segment's own per-model totals as a model_usage record", () => {
    const { traceRoot, configDir } = freshArea("model-usage");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-mu",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendModelUsage?.(
      new Map([
        [
          "us.anthropic.claude-haiku-5-5",
          { inputTokens: 4, outputTokens: 72, cacheCreationInputTokens: 9_219, cacheReadInputTokens: 0 },
        ],
      ]),
    );
    trace.finalize({ sessionId: null });
    const record = readRecords(trace.packDir).find((candidate) => candidate["type"] === "model_usage");
    expect(record?.["modelUsage"]).toEqual({
      "us.anthropic.claude-haiku-5-5": {
        inputTokens: 4,
        outputTokens: 72,
        cacheCreationInputTokens: 9_219,
        cacheReadInputTokens: 0,
      },
    });
  });

  it("writes stage events into the same trace", () => {
    const { traceRoot, configDir } = freshArea("stages");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-5",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.emitStage("run.started", { resumed: false });
    trace.emitStage("run.failed", { subtype: "deadline_exceeded" });
    const stages = readRecords(trace.packDir).filter((record) => record["type"] === "stage");
    expect(stages[0]).toMatchObject({
      event: "run.started",
      executionId: "e-5",
      attempt: 1,
      detail: { resumed: false },
    });
    expect(stages[1]).toMatchObject({ event: "run.failed", detail: { subtype: "deadline_exceeded" } });
  });

  it("sweeps sub-agent transcripts into the pack under sub/ at finalization", () => {
    const { traceRoot, configDir } = freshArea("subagents");
    const subagentsDir = join(configDir, "projects", "-workspaces-run-x", "session-1", "subagents");
    mkdirSync(subagentsDir, { recursive: true });
    writeFileSync(join(subagentsDir, "agent-a1.jsonl"), '{"type":"user"}\n');
    writeFileSync(join(subagentsDir, "agent-b2.jsonl"), '{"type":"assistant"}\n');
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-6",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.finalize({ sessionId: "session-1" });
    expect(readFileSync(join(trace.packDir, "sub", "agent-a1.jsonl"), "utf8")).toBe('{"type":"user"}\n');
    expect(readFileSync(join(trace.packDir, "sub", "agent-b2.jsonl"), "utf8")).toBe('{"type":"assistant"}\n');
    const [sweep] = readRecords(trace.packDir).filter((record) => record["type"] === "subagents");
    expect(sweep).toMatchObject({ sessionId: "session-1", agents: ["a1", "b2"] });
  });

  it("applies the image stub and heartbeat filter to swept sub-agent transcripts", () => {
    const { traceRoot, configDir } = freshArea("subagent-stubbing");
    const subagentsDir = join(configDir, "projects", "-workspaces-run-s", "session-s", "subagents");
    mkdirSync(subagentsDir, { recursive: true });
    const imageLine = JSON.stringify({
      type: "user",
      message: {
        content: [{ type: "tool_result", tool_use_id: "tu-page", content: [base64ImageBlock()] }],
      },
    });
    const heartbeatLine = JSON.stringify({
      type: "system",
      subtype: "thinking_tokens",
      estimated_tokens: 300,
      estimated_tokens_delta: 50,
    });
    const textLine = '{"type":"assistant","message":{"content":[{"type":"text","text":"done"}]}}';
    const unparseableLine = "not json but still evidence";
    writeFileSync(
      join(subagentsDir, "agent-s1.jsonl"),
      `${imageLine}\n${heartbeatLine}\n${textLine}\n${unparseableLine}\n`,
    );
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-sub-stub",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.finalize({ sessionId: "session-s" });
    const swept = readFileSync(join(trace.packDir, "sub", "agent-s1.jsonl"), "utf8");
    expect(swept).not.toContain(IMAGE_BASE64);
    expect(swept).not.toContain("thinking_tokens");
    const lines = swept.split("\n").filter((line) => line !== "");
    expect(lines).toHaveLength(3);
    const sweptImageRecord = JSON.parse(lines[0] ?? "") as Record<string, unknown>;
    const [toolResult] = (sweptImageRecord["message"] as Record<string, unknown>)["content"] as Record<
      string,
      unknown
    >[];
    expect(toolResult?.["content"]).toEqual([
      {
        type: "image_stub",
        media_type: "image/jpeg",
        decoded_bytes: IMAGE_BYTES.byteLength,
        sha256: IMAGE_SHA256,
        tool_use_id: "tu-page",
      },
    ]);
    expect(lines[1]).toBe(textLine);
    expect(lines[2]).toBe(unparseableLine);
  });

  it("gives the pack supervisor-readable modes whatever the ambient umask is", () => {
    const { traceRoot, configDir } = freshArea("modes");
    const subagentsDir = join(configDir, "projects", "-workspaces-run-m", "session-m", "subagents");
    mkdirSync(subagentsDir, { recursive: true });
    writeFileSync(join(subagentsDir, "agent-m1.jsonl"), '{"type":"user"}\n', { mode: 0o600 });
    const previousUmask = process.umask(0o077);
    try {
      const trace = createRunTrace({
        traceRoot,
        executionId: "e-modes",
        attempt: 1,
        configDir,
        envelope: envelopeInput(),
      });
      trace.appendMessage({ type: "result" });
      trace.finalize({ sessionId: "session-m" });
      const modeOf = (path: string): string => (statSync(path).mode & 0o777).toString(8);
      expect(modeOf(traceRoot)).toBe("755");
      expect(modeOf(join(traceRoot, "e-modes"))).toBe("755");
      expect(modeOf(trace.packDir)).toBe("755");
      expect(modeOf(join(trace.packDir, "trace.ndjson"))).toBe("644");
      expect(modeOf(join(trace.packDir, "trace.seq"))).toBe("644");
      expect(modeOf(join(trace.packDir, "sub"))).toBe("755");
      expect(modeOf(join(trace.packDir, "sub", "agent-m1.jsonl"))).toBe("644");
    } finally {
      process.umask(previousUmask);
    }
  });

  it("flushes tool calls that never saw a result as unresolved at finalization", () => {
    const { traceRoot, configDir } = freshArea("unresolved");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-7",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage(toolUseMessage("tu-3", "Bash", { command: "sleep 999" }));
    trace.finalize({ sessionId: null });
    const [toolCall] = readRecords(trace.packDir).filter((record) => record["type"] === "tool_call");
    expect(toolCall).toMatchObject({
      toolUseId: "tu-3",
      toolName: "Bash",
      outputBytes: null,
      durationMs: null,
      errorClass: "unresolved",
    });
  });

  it("never writes a second envelope when a resume segment reopens the trace", () => {
    const { traceRoot, configDir } = freshArea("reopen");
    const first = createRunTrace({
      traceRoot,
      executionId: "e-8",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    first.appendMessage({ type: "result" });
    const second = createRunTrace({
      traceRoot,
      executionId: "e-8",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    second.appendMessage({ type: "result" });
    const records = readRecords(second.packDir);
    expect(records.filter((record) => record["type"] === "envelope")).toHaveLength(1);
    expect(records.filter((record) => record["type"] === "message")).toHaveLength(2);
  });

  it("buffers plain messages and flushes them when a segment-boundary result message arrives", () => {
    const { traceRoot, configDir } = freshArea("batching");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-13",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage({ type: "system" });
    trace.appendMessage({ type: "assistant" });
    expect(readRecords(trace.packDir).filter((record) => record["type"] === "message")).toHaveLength(0);
    trace.appendMessage({ type: "result", subtype: "success" });
    const flushed = readRecords(trace.packDir).filter((record) => record["type"] === "message");
    expect(flushed.map((record) => record["seq"])).toEqual([1, 2, 3]);
  });

  it("flushes whatever is still buffered at finalization", () => {
    const { traceRoot, configDir } = freshArea("batch-final");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-14",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage({ type: "system" });
    trace.finalize({ sessionId: null });
    expect(readRecords(trace.packDir).filter((record) => record["type"] === "message")).toHaveLength(1);
  });

  it("continues the message seq when a cold resume reopens an existing pack, seeded from the sidecar", () => {
    const { traceRoot, configDir } = freshArea("reopen-seq");
    const first = createRunTrace({
      traceRoot,
      executionId: "e-12",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    first.appendMessage({ type: "system" });
    first.appendMessage({ type: "result" });
    expect(readFileSync(join(first.packDir, "trace.seq"), "utf8").trim()).toBe("2");
    const resumed = createRunTrace({
      traceRoot,
      executionId: "e-12",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    resumed.appendMessage({ type: "user" });
    resumed.appendMessage({ type: "result" });
    const seqs = readRecords(resumed.packDir)
      .filter((record) => record["type"] === "message")
      .map((record) => record["seq"]);
    expect(seqs).toEqual([1, 2, 3, 4]);
    expect(readFileSync(join(resumed.packDir, "trace.seq"), "utf8").trim()).toBe("4");
  });

  it("swallows write failures so a broken trace can never fail the run", () => {
    const { traceRoot, configDir } = freshArea("broken");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-9",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    rmSync(trace.packDir, { recursive: true, force: true });
    rmSync(traceRoot, { recursive: true, force: true });
    writeFileSync(traceRoot, "not a directory");
    expect(() => {
      trace.appendMessage({ type: "system" });
      trace.emitStage("run.completed", {});
      trace.finalize({ sessionId: "session-9" });
    }).not.toThrow();
    expect(existsSync(trace.packDir)).toBe(false);
  });

  it("finalize is idempotent", () => {
    const { traceRoot, configDir } = freshArea("idempotent");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-10",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.finalize({ sessionId: null });
    trace.finalize({ sessionId: null });
    expect(readRecords(trace.packDir).filter((record) => record["type"] === "subagents")).toHaveLength(1);
  });
});

describe("buildLabelRecord", () => {
  it("keys late labels by executionId, attempt, and entryUuid instead of mutating the trace", () => {
    const label = buildLabelRecord({
      executionId: "e-11",
      attempt: 1,
      entryUuid: "entry-1",
      label: "eval_grade",
      value: { pass: true },
      source: "evals/grade",
    });
    expect(label).toMatchObject({
      type: "label",
      executionId: "e-11",
      attempt: 1,
      entryUuid: "entry-1",
      label: "eval_grade",
      value: { pass: true },
      source: "evals/grade",
    });
    expect(typeof label.uuid).toBe("string");
    expect(typeof label.at).toBe("string");
  });
});

describe("readInstalledSdkVersion", () => {
  it("reports the installed Agent SDK version from its package metadata", () => {
    expect(readInstalledSdkVersion()).toMatch(/^\d+\.\d+\.\d+/);
  });
});

describe("claimRetainedRoot", () => {
  it("claims every declared retained root sticky and world-writable, owned by this process, idempotently", () => {
    for (const declared of RETAINED_WORKSPACE_ROOTS) {
      const { traceRoot } = freshArea(`claim-${declared.name}`);
      const rootPath = join(traceRoot, declared.name);
      claimRetainedRoot(rootPath, declared.mode);
      claimRetainedRoot(rootPath, declared.mode);
      const rootStat = statSync(rootPath);
      expect(rootStat.mode & 0o7777).toBe(declared.mode);
      const ownUid = process.getuid?.();
      if (ownUid !== undefined) {
        expect(rootStat.uid).toBe(ownUid);
      }
    }
  });

  it("refuses to claim a path that is not a directory", () => {
    const { traceRoot } = freshArea("claim-file");
    mkdirSync(traceRoot, { recursive: true });
    const filePath = join(traceRoot, "not-a-dir");
    writeFileSync(filePath, "occupied");
    expect(() => claimRetainedRoot(filePath)).toThrow();
  });
});

describe("sub-agent usage records (format v8)", () => {
  const SUB_SESSION_ID = "session-u";
  const SUB_AGENT_ID = "afad00da6bccb7101";
  const SUB_MODEL = "anthropic.claude-haiku-4-5-20251001-v1:0";

  function transcriptAssistantLine(id: string, usage: Record<string, unknown>): string {
    return JSON.stringify({ type: "assistant", message: { id, model: SUB_MODEL, usage, content: [] } });
  }

  function completedAgentMessage(payloadUsage: Record<string, unknown>): Record<string, unknown> {
    return {
      type: "user",
      message: { role: "user", content: [{ type: "tool_result", tool_use_id: "toolu_agent" }] },
      parent_tool_use_id: null,
      session_id: SUB_SESSION_ID,
      tool_use_result: {
        status: "completed",
        agentId: SUB_AGENT_ID,
        resolvedModel: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
        totalTokens: 6350,
        usage: payloadUsage,
      },
    };
  }

  function usageRecordsOf(packDir: string): Record<string, unknown>[] {
    return readRecords(packDir).filter((record) => record["type"] === "usage");
  }

  it("labels main-loop usage records as stream-derived with no agent", () => {
    const { traceRoot, configDir } = freshArea("usage-source");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-src",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage(messageStart("m", "msg-main", { input_tokens: 3, output_tokens: 1 }));
    trace.appendMessage(messageDelta({ output_tokens: 9 }));
    trace.appendMessage(messageStop());
    expect(usageRecordsOf(trace.packDir)).toEqual([
      expect.objectContaining({ messageId: "msg-main", source: "stream", agentId: null }),
    ]);
  });

  it("distils a completed sub-agent's transcript into one usage record per API message, once per agent", () => {
    const { traceRoot, configDir } = freshArea("usage-subagent");
    const subagentsDir = join(configDir, "projects", "-workspaces-run-u", SUB_SESSION_ID, "subagents");
    mkdirSync(subagentsDir, { recursive: true });
    writeFileSync(
      join(subagentsDir, `agent-${SUB_AGENT_ID}.jsonl`),
      [
        transcriptAssistantLine("msg-sub-1", {
          input_tokens: 10,
          cache_creation_input_tokens: 6134,
          output_tokens: 8,
        }),
        transcriptAssistantLine("msg-sub-1", {
          input_tokens: 10,
          cache_creation_input_tokens: 6134,
          output_tokens: 128,
        }),
        transcriptAssistantLine("msg-sub-2", {
          input_tokens: 5,
          cache_read_input_tokens: 6134,
          output_tokens: 4,
        }),
        "",
      ].join("\n"),
    );
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-sub",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    const payload = completedAgentMessage({
      input_tokens: 5,
      cache_read_input_tokens: 6134,
      output_tokens: 4,
    });
    trace.appendMessage(payload);
    trace.appendMessage(payload);
    expect(usageRecordsOf(trace.packDir)).toEqual([
      expect.objectContaining({
        messageId: "msg-sub-1",
        model: SUB_MODEL,
        agentId: SUB_AGENT_ID,
        source: "subagent_transcript",
        usage: expect.objectContaining({ output_tokens: 128, cache_creation_input_tokens: 6134 }),
      }),
      expect.objectContaining({
        messageId: "msg-sub-2",
        model: SUB_MODEL,
        agentId: SUB_AGENT_ID,
        source: "subagent_transcript",
        usage: expect.objectContaining({ output_tokens: 4, cache_read_input_tokens: 6134 }),
      }),
    ]);
  });

  it("records the payload's own last-call snapshot, labelled as such, when the transcript is not there", () => {
    const { traceRoot, configDir } = freshArea("usage-subagent-fallback");
    const trace = createRunTrace({
      traceRoot,
      executionId: "e-fb",
      attempt: 1,
      configDir,
      envelope: envelopeInput(),
    });
    trace.appendMessage(
      completedAgentMessage({ input_tokens: 5, cache_read_input_tokens: 6134, output_tokens: 4 }),
    );
    expect(usageRecordsOf(trace.packDir)).toEqual([
      expect.objectContaining({
        messageId: null,
        model: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
        agentId: SUB_AGENT_ID,
        source: "subagent_result",
        usage: expect.objectContaining({ input_tokens: 5, cache_read_input_tokens: 6134, output_tokens: 4 }),
      }),
    ]);
  });
});
