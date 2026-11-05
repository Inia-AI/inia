import { contentBlocksOf } from "./trace.js";

export type ToolCallError = { toolName: string; erroredCalls: number };

export type ToolErrorObserver = (info: ToolCallError) => void;

const TOOL_USE_BLOCK_TYPE = "tool_use";
const TOOL_RESULT_BLOCK_TYPE = "tool_result";

export function createToolErrorWatch(onToolError: ToolErrorObserver): (message: unknown) => void {
  const toolNamesById = new Map<string, string>();
  const erroredCallsByTool = new Map<string, number>();
  return (message) => {
    for (const block of contentBlocksOf(message)) {
      if (block["type"] === TOOL_USE_BLOCK_TYPE) {
        const id = block["id"];
        const name = block["name"];
        if (typeof id === "string" && typeof name === "string") {
          toolNamesById.set(id, name);
        }
        continue;
      }
      if (block["type"] !== TOOL_RESULT_BLOCK_TYPE) {
        continue;
      }
      const toolUseId = block["tool_use_id"];
      if (typeof toolUseId !== "string") {
        continue;
      }
      const toolName = toolNamesById.get(toolUseId);
      if (toolName === undefined) {
        continue;
      }
      toolNamesById.delete(toolUseId);
      if (block["is_error"] !== true) {
        continue;
      }
      const erroredCalls = (erroredCallsByTool.get(toolName) ?? 0) + 1;
      erroredCallsByTool.set(toolName, erroredCalls);
      onToolError({ toolName, erroredCalls });
    }
  };
}
