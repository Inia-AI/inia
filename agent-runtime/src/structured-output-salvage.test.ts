import { describe, expect, it } from "bun:test";
import type { RunStreamMessage } from "./run-report.js";
import { createStructuredOutputTap, STRUCTURED_OUTPUT_TOOL_NAME } from "./structured-output-salvage.js";

const TOOL_USE_ID = "tool-so-1";

function assistantToolUse(input: {
  toolName?: string;
  toolUseId?: string;
  toolInput?: unknown;
  parentToolUseId?: string | null;
}): RunStreamMessage {
  return {
    type: "assistant",
    parent_tool_use_id: input.parentToolUseId ?? null,
    message: {
      role: "assistant",
      content: [
        {
          type: "tool_use",
          id: input.toolUseId ?? TOOL_USE_ID,
          name: input.toolName ?? STRUCTURED_OUTPUT_TOOL_NAME,
          input: input.toolInput,
        },
      ],
    },
  };
}

function toolResult(input: { toolUseId?: string; isError?: boolean }): RunStreamMessage {
  return {
    type: "user",
    parent_tool_use_id: null,
    message: {
      role: "user",
      content: [
        {
          type: "tool_result",
          tool_use_id: input.toolUseId ?? TOOL_USE_ID,
          ...(input.isError === undefined ? {} : { is_error: input.isError }),
          content: [],
        },
      ],
    },
  };
}

describe("createStructuredOutputTap", () => {
  it("holds nothing before a StructuredOutput tool call appears", () => {
    const tap = createStructuredOutputTap();
    expect(tap.held()).toBeUndefined();
  });

  it("holds the input of a completed StructuredOutput tool call", () => {
    const tap = createStructuredOutputTap();
    const extraction = { status: "ok", periods: ["FY0", "FY-1"] };
    tap.recordMessage(assistantToolUse({ toolInput: extraction }));
    expect(tap.held()).toEqual({ value: extraction });
  });

  it("replaces the held input when a later StructuredOutput call arrives", () => {
    const tap = createStructuredOutputTap();
    tap.recordMessage(assistantToolUse({ toolUseId: "tool-so-1", toolInput: { attempt: 1 } }));
    tap.recordMessage(assistantToolUse({ toolUseId: "tool-so-2", toolInput: { attempt: 2 } }));
    expect(tap.held()).toEqual({ value: { attempt: 2 } });
  });

  it("drops the held input when the CLI rejects it with an error tool_result", () => {
    const tap = createStructuredOutputTap();
    tap.recordMessage(assistantToolUse({ toolInput: { status: "ok" } }));
    tap.recordMessage(toolResult({ isError: true }));
    expect(tap.held()).toBeUndefined();
  });

  it("keeps the held input when the tool_result is not an error", () => {
    const tap = createStructuredOutputTap();
    tap.recordMessage(assistantToolUse({ toolInput: { status: "ok" } }));
    tap.recordMessage(toolResult({}));
    expect(tap.held()).toEqual({ value: { status: "ok" } });
  });

  it("ignores an error tool_result for a different tool call", () => {
    const tap = createStructuredOutputTap();
    tap.recordMessage(assistantToolUse({ toolInput: { status: "ok" } }));
    tap.recordMessage(toolResult({ toolUseId: "tool-other", isError: true }));
    expect(tap.held()).toEqual({ value: { status: "ok" } });
  });

  it("ignores tool calls that are not StructuredOutput", () => {
    const tap = createStructuredOutputTap();
    tap.recordMessage(assistantToolUse({ toolName: "Bash", toolInput: { command: "ls" } }));
    expect(tap.held()).toBeUndefined();
  });

  it("ignores a StructuredOutput call made inside a sub-agent", () => {
    const tap = createStructuredOutputTap();
    tap.recordMessage(assistantToolUse({ toolInput: { status: "ok" }, parentToolUseId: "task-1" }));
    expect(tap.held()).toBeUndefined();
  });

  it("ignores a tool_use block whose input is missing", () => {
    const tap = createStructuredOutputTap();
    tap.recordMessage(assistantToolUse({ toolInput: undefined }));
    expect(tap.held()).toBeUndefined();
  });

  it("survives malformed messages without holding anything", () => {
    const tap = createStructuredOutputTap();
    tap.recordMessage({ type: "assistant" });
    tap.recordMessage({ type: "assistant", message: "not a record" });
    tap.recordMessage({ type: "assistant", message: { content: "not an array" } });
    tap.recordMessage({ type: "assistant", message: { content: [42, "text"] } });
    expect(tap.held()).toBeUndefined();
  });
});
