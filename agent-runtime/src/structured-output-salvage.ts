import { isRecord } from "./narrow.js";
import type { RunStreamMessage } from "./run-report.js";

// The CLI's end-turn tool for outputFormat runs: the model's final answer arrives as this
// tool call's input, so it is the one tool worth holding for salvage.
export const STRUCTURED_OUTPUT_TOOL_NAME = "StructuredOutput";

export type HeldStructuredOutput = { value: unknown };

export type StructuredOutputTap = {
  recordMessage(message: RunStreamMessage): void;
  held(): HeldStructuredOutput | undefined;
};

const TOOL_USE_BLOCK_TYPE = "tool_use";
const TOOL_RESULT_BLOCK_TYPE = "tool_result";

function mainThreadContentBlocksOf(message: RunStreamMessage): Record<string, unknown>[] {
  if (message.parent_tool_use_id !== null && message.parent_tool_use_id !== undefined) {
    return [];
  }
  if (!isRecord(message.message)) {
    return [];
  }
  const content = message.message["content"];
  if (!Array.isArray(content)) {
    return [];
  }
  return content.filter(isRecord);
}

export function createStructuredOutputTap(): StructuredOutputTap {
  let held: { toolUseId: string; value: unknown } | undefined;
  return {
    recordMessage(message) {
      for (const block of mainThreadContentBlocksOf(message)) {
        if (block["type"] === TOOL_USE_BLOCK_TYPE && block["name"] === STRUCTURED_OUTPUT_TOOL_NAME) {
          const toolUseId = block["id"];
          const value = block["input"];
          if (typeof toolUseId === "string" && value !== undefined) {
            held = { toolUseId, value };
          }
          continue;
        }
        if (
          block["type"] === TOOL_RESULT_BLOCK_TYPE &&
          held !== undefined &&
          block["tool_use_id"] === held.toolUseId &&
          block["is_error"] === true
        ) {
          held = undefined;
        }
      }
    },
    held() {
      return held === undefined ? undefined : { value: held.value };
    },
  };
}
