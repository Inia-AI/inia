import { describe, expect, it } from "bun:test";
import { ASSISTANT_ERROR_TEXT_MAX_CHARS, createAssistantErrorTap } from "./assistant-error-tap.js";
import type { RunStreamMessage } from "./run-report.js";

const MID_RESPONSE_TEXT = "API Error: Server error mid-response. The response above may be incomplete.";

function assistant(
  text: string,
  extra: Partial<RunStreamMessage> & { error?: string } = {},
): RunStreamMessage {
  return {
    type: "assistant",
    parent_tool_use_id: null,
    message: { role: "assistant", content: [{ type: "text", text }] },
    ...extra,
  };
}

describe("createAssistantErrorTap", () => {
  it("records nothing until an assistant message carries the SDK's error field", () => {
    const tap = createAssistantErrorTap();
    tap.recordMessage(assistant("working on it"));
    tap.recordMessage({ type: "result", subtype: "success" });
    expect(tap.last()).toBeUndefined();
  });

  it("holds the error code and text of the main thread's latest errored assistant message", () => {
    const tap = createAssistantErrorTap();
    tap.recordMessage(assistant("partial answer"));
    tap.recordMessage(assistant(MID_RESPONSE_TEXT, { error: "server_error" }));
    expect(tap.last()).toEqual({ code: "server_error", text: MID_RESPONSE_TEXT });
  });

  it("forgets an error once a later main-thread assistant message carries none", () => {
    const tap = createAssistantErrorTap();
    tap.recordMessage(assistant(MID_RESPONSE_TEXT, { error: "server_error" }));
    tap.recordMessage(assistant("recovered and continued"));
    expect(tap.last()).toBeUndefined();
  });

  it("ignores a sub-agent's errored assistant message — it does not decide the run's verdict", () => {
    const tap = createAssistantErrorTap();
    tap.recordMessage(assistant(MID_RESPONSE_TEXT, { error: "server_error", parent_tool_use_id: "toolu_1" }));
    expect(tap.last()).toBeUndefined();
  });

  it("joins the text blocks and caps what it holds", () => {
    const tap = createAssistantErrorTap();
    const long = "x".repeat(ASSISTANT_ERROR_TEXT_MAX_CHARS + 50);
    tap.recordMessage({
      type: "assistant",
      parent_tool_use_id: null,
      error: "rate_limit",
      message: {
        content: [
          { type: "text", text: "first" },
          { type: "tool_use", id: "t", name: "Bash", input: {} },
          { type: "text", text: long },
        ],
      },
    });
    const held = tap.last();
    expect(held?.code).toBe("rate_limit");
    expect(held?.text.startsWith("first\n")).toBe(true);
    expect(held?.text.length).toBe(ASSISTANT_ERROR_TEXT_MAX_CHARS);
  });

  it("holds an empty text for an errored message with no text blocks rather than dropping the code", () => {
    const tap = createAssistantErrorTap();
    tap.recordMessage({
      type: "assistant",
      parent_tool_use_id: null,
      error: "unknown",
      message: { content: [] },
    });
    expect(tap.last()).toEqual({ code: "unknown", text: "" });
  });
});
