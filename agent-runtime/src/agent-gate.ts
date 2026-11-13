import type { HookCallback, HookJSONOutput } from "@anthropic-ai/claude-agent-sdk";
import { isRecord } from "./narrow.js";
import { deniedToolUse } from "./read-gate.js";

// The SDK names the sub-agent tool "Task" in init tool lists and "Agent" in the
// model's tool_use blocks (both measured); the gate registers under both so no rename
// can slip a spawn past it.
export const AGENT_TOOL_MATCHERS: readonly string[] = ["Agent", "Task"];

export const UNDECLARED_AGENT_REASON = "is not one of this run's declared agents";

const MALFORMED_SPAWN_REASON =
  "an agent spawn requires string description, prompt, and subagent_type — refusing a malformed request";

// Everything else the tool accepts is dropped deliberately: `model` would escape the
// per-run model pin, `isolation: "remote"` would leave the sandboxed host entirely,
// `name` opens SendMessage addressing, and `run_in_background` — the SDK default —
// lets a sub-agent outlive the run that must account for its spend (measured: a
// parent settled its final result while a background sub-agent was still running).
type ApprovedSpawnInput = {
  description: string;
  prompt: string;
  subagent_type: string;
  run_in_background: false;
};

export type AgentSpawnDecision =
  | { allowed: true; updatedInput: ApprovedSpawnInput }
  | { allowed: false; reason: string };

function requiredString(record: Record<string, unknown>, key: string): string | undefined {
  const value = record[key];
  return typeof value === "string" && value !== "" ? value : undefined;
}

export function evaluateAgentSpawnRequest(
  toolInput: unknown,
  declaredAgentNames: readonly string[],
): AgentSpawnDecision {
  if (!isRecord(toolInput)) {
    return { allowed: false, reason: MALFORMED_SPAWN_REASON };
  }
  const description = requiredString(toolInput, "description");
  const prompt = requiredString(toolInput, "prompt");
  const subagentType = requiredString(toolInput, "subagent_type");
  if (description === undefined || prompt === undefined || subagentType === undefined) {
    return { allowed: false, reason: MALFORMED_SPAWN_REASON };
  }
  if (!declaredAgentNames.includes(subagentType)) {
    return {
      allowed: false,
      reason: `subagent_type "${subagentType}" ${UNDECLARED_AGENT_REASON}`,
    };
  }
  return {
    allowed: true,
    updatedInput: {
      description,
      prompt,
      subagent_type: subagentType,
      run_in_background: false,
    },
  };
}

export function createAgentGateHook(declaredAgentNames: readonly string[]): HookCallback {
  const declared = [...declaredAgentNames];
  return (input): Promise<HookJSONOutput> => {
    if (input.hook_event_name !== "PreToolUse" || !AGENT_TOOL_MATCHERS.includes(input.tool_name)) {
      return Promise.resolve({});
    }
    const decision = evaluateAgentSpawnRequest(input.tool_input, declared);
    if (!decision.allowed) {
      return Promise.resolve(deniedToolUse(decision.reason));
    }
    return Promise.resolve({
      hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "allow",
        updatedInput: decision.updatedInput,
      },
    });
  };
}
