import { isRecord } from "./narrow.js";
import type { RunStreamMessage } from "./run-report.js";

// The CLI reports an API failure in-band as an assistant message carrying the SDK's
// `error` class (`SDKAssistantMessageError`) and the error text where the answer would
// be, followed by a success-subtype result with `is_error` set, whose `result` text is
// empty on a mid-response failure. This tap holds the main thread's latest such message
// so the result can be classified by the SDK's own error class and named by the text the
// stream did carry.
export const ASSISTANT_ERROR_TEXT_MAX_CHARS = 500;

export type AssistantErrorObservation = { code: string; text: string };

export type AssistantErrorTap = {
  recordMessage(message: RunStreamMessage): void;
  last(): AssistantErrorObservation | undefined;
};

const ASSISTANT_MESSAGE_TYPE = "assistant";
const TEXT_BLOCK_TYPE = "text";

function isMainThreadAssistant(message: RunStreamMessage): boolean {
  return (
    message.type === ASSISTANT_MESSAGE_TYPE &&
    (message.parent_tool_use_id === null || message.parent_tool_use_id === undefined)
  );
}

function textOf(message: RunStreamMessage): string {
  if (!isRecord(message.message)) {
    return "";
  }
  const content = message.message["content"];
  if (!Array.isArray(content)) {
    return "";
  }
  return content
    .filter(isRecord)
    .filter((block) => block["type"] === TEXT_BLOCK_TYPE && typeof block["text"] === "string")
    .map((block) => String(block["text"]))
    .join("\n")
    .slice(0, ASSISTANT_ERROR_TEXT_MAX_CHARS);
}

export function createAssistantErrorTap(): AssistantErrorTap {
  let last: AssistantErrorObservation | undefined;
  return {
    recordMessage(message) {
      if (!isMainThreadAssistant(message)) {
        return;
      }
      // Latest main-thread assistant message wins: a turn the CLI recovered from leaves no
      // error behind it, so only an error the stream ended on can classify the result.
      last = typeof message.error === "string" ? { code: message.error, text: textOf(message) } : undefined;
    },
    last: () => last,
  };
}
