const TEST_SESSION_ID = "s-1";

export function messageStart(
  model: string,
  id: string,
  usage: Record<string, unknown>,
  parentToolUseId?: string,
): Record<string, unknown> {
  return {
    type: "stream_event",
    event: { type: "message_start", message: { id, model, usage } },
    parent_tool_use_id: parentToolUseId ?? null,
    session_id: TEST_SESSION_ID,
  };
}

export function messageDelta(
  usage: Record<string, unknown>,
  parentToolUseId?: string,
): Record<string, unknown> {
  return {
    type: "stream_event",
    event: { type: "message_delta", delta: { stop_reason: "end_turn" }, usage },
    parent_tool_use_id: parentToolUseId ?? null,
    session_id: TEST_SESSION_ID,
  };
}

export function messageStop(parentToolUseId?: string): Record<string, unknown> {
  return {
    type: "stream_event",
    event: { type: "message_stop" },
    parent_tool_use_id: parentToolUseId ?? null,
    session_id: TEST_SESSION_ID,
  };
}
