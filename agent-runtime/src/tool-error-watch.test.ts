import { describe, expect, it } from "bun:test";
import { createToolErrorWatch, type ToolCallError } from "./tool-error-watch.js";

function toolUse(id: string, name: string): unknown {
  return { type: "assistant", message: { content: [{ type: "tool_use", id, name, input: {} }] } };
}

function toolResult(id: string, isError: boolean): unknown {
  return {
    type: "user",
    message: { content: [{ type: "tool_result", tool_use_id: id, is_error: isError, content: [] }] },
  };
}

describe("createToolErrorWatch", () => {
  it("reports each errored tool result with a per-tool running error count", () => {
    const seen: ToolCallError[] = [];
    const observe = createToolErrorWatch((info) => seen.push(info));
    observe(toolUse("tu-1", "Bash"));
    observe(toolResult("tu-1", true));
    observe(toolUse("tu-2", "Bash"));
    observe(toolResult("tu-2", false));
    observe(toolUse("tu-3", "Bash"));
    observe(toolResult("tu-3", true));
    expect(seen).toEqual([
      { toolName: "Bash", erroredCalls: 1 },
      { toolName: "Bash", erroredCalls: 2 },
    ]);
  });

  it("counts each tool's errors independently", () => {
    const seen: ToolCallError[] = [];
    const observe = createToolErrorWatch((info) => seen.push(info));
    observe(toolUse("tu-1", "Bash"));
    observe(toolUse("tu-2", "Read"));
    observe(toolResult("tu-1", true));
    observe(toolResult("tu-2", true));
    expect(seen).toEqual([
      { toolName: "Bash", erroredCalls: 1 },
      { toolName: "Read", erroredCalls: 1 },
    ]);
  });

  it("ignores results whose tool_use was never seen, and messages without content blocks", () => {
    const seen: ToolCallError[] = [];
    const observe = createToolErrorWatch((info) => seen.push(info));
    observe(toolResult("tu-unknown", true));
    observe({ type: "result", subtype: "success" });
    observe("not a record");
    expect(seen).toEqual([]);
  });

  it("resolves each tool_use at most once", () => {
    const seen: ToolCallError[] = [];
    const observe = createToolErrorWatch((info) => seen.push(info));
    observe(toolUse("tu-1", "Bash"));
    observe(toolResult("tu-1", true));
    observe(toolResult("tu-1", true));
    expect(seen).toEqual([{ toolName: "Bash", erroredCalls: 1 }]);
  });
});
