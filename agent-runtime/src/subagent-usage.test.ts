import { describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createSubagentTranscriptReader, subagentApiMessagesOf } from "./subagent-usage.js";

// Line for line the transcript CLI 2.1.251 wrote for one drift-calibration sub-agent: the
// first API message lands twice (a thinking-block line carrying the message_start placeholder
// output count, then the tool_use line carrying the final count) and the second lands once.
const FIRST_MESSAGE_ID = "msg_bdrk_h56yuig7gltdvvrupub62imq4446he2losnyph2326pv4a6apetq";
const SECOND_MESSAGE_ID = "msg_bdrk_hhxls5bspuevedwb3uoui5uj7agdl7i3t22hgrxdqiegu2zptk6q";
const TRANSCRIPT_MODEL = "anthropic.claude-haiku-4-5-20251001-v1:0";
const AGENT_ID = "afad00da6bccb7101";
const SESSION_ID = "411c8fc3-ae63-4b79-9538-1f25d1b9e63b";

function assistantLine(id: string, usage: Record<string, unknown>, content: unknown[]): string {
  return JSON.stringify({
    type: "assistant",
    agentId: AGENT_ID,
    isSidechain: true,
    message: { id, role: "assistant", model: TRANSCRIPT_MODEL, usage, content },
  });
}

const MEASURED_TRANSCRIPT_LINES: readonly string[] = [
  JSON.stringify({
    type: "user",
    agentId: AGENT_ID,
    message: { role: "user", content: "Run the bash command: echo $((17*23))" },
  }),
  JSON.stringify({ type: "attachment", agentId: AGENT_ID, attachment: { type: "hook_success" } }),
  assistantLine(
    FIRST_MESSAGE_ID,
    { input_tokens: 10, cache_creation_input_tokens: 6134, cache_read_input_tokens: 0, output_tokens: 8 },
    [{ type: "thinking", thinking: "run it" }],
  ),
  assistantLine(
    FIRST_MESSAGE_ID,
    {
      input_tokens: 10,
      cache_creation_input_tokens: 6134,
      cache_read_input_tokens: 0,
      output_tokens: 128,
      output_tokens_details: { thinking_tokens: 44 },
      service_tier: "standard",
    },
    [{ type: "tool_use", id: "toolu_bdrk_01Y3", name: "Bash", input: { command: "echo $((17*23))" } }],
  ),
  JSON.stringify({
    type: "user",
    agentId: AGENT_ID,
    message: {
      role: "user",
      content: [{ tool_use_id: "toolu_bdrk_01Y3", type: "tool_result", content: "391" }],
    },
  }),
  assistantLine(
    SECOND_MESSAGE_ID,
    {
      input_tokens: 5,
      cache_creation_input_tokens: 207,
      cache_read_input_tokens: 6134,
      output_tokens: 4,
      output_tokens_details: { thinking_tokens: 0 },
      service_tier: "standard",
    },
    [{ type: "text", text: "391" }],
  ),
];

const MEASURED_TRANSCRIPT = `${MEASURED_TRANSCRIPT_LINES.join("\n")}\n`;

describe("subagentApiMessagesOf", () => {
  it("settles one usage per API message, the last transcript line for a message id winning", () => {
    const messages = subagentApiMessagesOf(MEASURED_TRANSCRIPT);
    expect(messages).toHaveLength(2);
    expect(messages[0]).toEqual({
      messageId: FIRST_MESSAGE_ID,
      model: TRANSCRIPT_MODEL,
      rawUsage: {
        input_tokens: 10,
        cache_creation_input_tokens: 6134,
        cache_read_input_tokens: 0,
        output_tokens: 128,
        output_tokens_details: { thinking_tokens: 44 },
        service_tier: "standard",
      },
      settled: true,
    });
    expect(messages[1]?.messageId).toBe(SECOND_MESSAGE_ID);
    expect(messages[1]?.rawUsage["output_tokens"]).toBe(4);
  });

  it("skips lines that are not assistant API messages, lack an id or usage, or do not parse", () => {
    const text = [
      '{"type":"user","message":{"role":"user","content":"hi"}}',
      '{"type":"assistant","message":{"role":"assistant","content":[]}}',
      '{"type":"assistant","message":{"id":"msg-no-usage","model":"m","content":[]}}',
      '{"type":"assistant","message":{"id":"msg-1","usage":{"output_tokens":3},"content":[]}}',
      "not json",
      "",
    ].join("\n");
    expect(subagentApiMessagesOf(text)).toEqual([
      { messageId: "msg-1", model: "", rawUsage: { output_tokens: 3 }, settled: true },
    ]);
  });
});

describe("createSubagentTranscriptReader", () => {
  function freshConfigDir(): string {
    return mkdtempSync(join(tmpdir(), "subagent-usage-"));
  }

  function writeTranscript(configDir: string, sessionId: string, agentId: string, text: string): void {
    const dir = join(configDir, "projects", "-workspaces-run-x", sessionId, "subagents");
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, `agent-${agentId}.jsonl`), text, { mode: 0o600 });
  }

  it("reads the transcript under projects/<any>/<sessionId>/subagents/agent-<agentId>.jsonl", () => {
    const configDir = freshConfigDir();
    writeTranscript(configDir, SESSION_ID, AGENT_ID, MEASURED_TRANSCRIPT);
    const read = createSubagentTranscriptReader(configDir);
    expect(read(SESSION_ID, AGENT_ID)?.map((message) => message.messageId)).toEqual([
      FIRST_MESSAGE_ID,
      SECOND_MESSAGE_ID,
    ]);
  });

  it("answers undefined when no transcript exists for the session and agent", () => {
    const configDir = freshConfigDir();
    writeTranscript(configDir, SESSION_ID, "other-agent", MEASURED_TRANSCRIPT);
    const read = createSubagentTranscriptReader(configDir);
    expect(read(SESSION_ID, AGENT_ID)).toBeUndefined();
    expect(read("another-session", "other-agent")).toBeUndefined();
    expect(createSubagentTranscriptReader(join(configDir, "missing"))(SESSION_ID, AGENT_ID)).toBeUndefined();
  });

  it("never joins an id that is not a plain path segment into the transcript path", () => {
    const configDir = freshConfigDir();
    writeTranscript(configDir, SESSION_ID, AGENT_ID, MEASURED_TRANSCRIPT);
    const read = createSubagentTranscriptReader(configDir);
    expect(read(SESSION_ID, `../${SESSION_ID}/subagents/agent-${AGENT_ID}`)).toBeUndefined();
    expect(read(`../-workspaces-run-x/${SESSION_ID}`, AGENT_ID)).toBeUndefined();
    expect(read("", AGENT_ID)).toBeUndefined();
    expect(read(SESSION_ID, "")).toBeUndefined();
  });

  it("answers undefined rather than throwing when the transcript cannot be read", () => {
    const configDir = freshConfigDir();
    const dir = join(configDir, "projects", "-workspaces-run-x", SESSION_ID, "subagents");
    mkdirSync(join(dir, `agent-${AGENT_ID}.jsonl`), { recursive: true });
    expect(createSubagentTranscriptReader(configDir)(SESSION_ID, AGENT_ID)).toBeUndefined();
  });
});
