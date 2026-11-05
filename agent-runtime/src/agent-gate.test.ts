import { describe, expect, it } from "bun:test";
import {
  AGENT_TOOL_MATCHERS,
  createAgentGateHook,
  evaluateAgentSpawnRequest,
  UNDECLARED_AGENT_REASON,
} from "./agent-gate.js";

const DECLARED = ["verifier", "cross-checker"];

function spawnInput(overrides: Record<string, unknown>): Record<string, unknown> {
  return {
    description: "verify totals",
    prompt: "Check every figure.",
    subagent_type: "verifier",
    ...overrides,
  };
}

describe("evaluateAgentSpawnRequest", () => {
  it("approves a declared agent with the input rewritten wholesale: foreground forced, params allowlisted", () => {
    const decision = evaluateAgentSpawnRequest(
      spawnInput({ run_in_background: true, model: "opus", isolation: "remote", name: "sneaky" }),
      DECLARED,
    );
    if (!decision.allowed) {
      throw new Error(`expected allow, got: ${decision.reason}`);
    }
    expect(decision.updatedInput).toEqual({
      description: "verify totals",
      prompt: "Check every figure.",
      subagent_type: "verifier",
      run_in_background: false,
    });
  });

  it("forces foreground even when the model omits run_in_background, whose SDK default is background", () => {
    const decision = evaluateAgentSpawnRequest(spawnInput({}), DECLARED);
    if (!decision.allowed) {
      throw new Error(`expected allow, got: ${decision.reason}`);
    }
    expect(decision.updatedInput["run_in_background"]).toBe(false);
  });

  it("denies a subagent_type outside the run's declared agents — the built-ins stay unreachable", () => {
    for (const builtin of ["general-purpose", "Explore", "Plan", "claude", "statusline-setup"]) {
      const decision = evaluateAgentSpawnRequest(spawnInput({ subagent_type: builtin }), DECLARED);
      expect(decision.allowed).toBe(false);
      if (!decision.allowed) {
        expect(decision.reason).toContain(UNDECLARED_AGENT_REASON);
      }
    }
  });

  it("denies a spawn without a subagent_type rather than letting the SDK pick a default agent", () => {
    const decision = evaluateAgentSpawnRequest({ description: "d", prompt: "p" }, DECLARED);
    expect(decision.allowed).toBe(false);
  });

  it("denies unparseable input and missing required strings, fail closed", () => {
    expect(evaluateAgentSpawnRequest("not-a-record", DECLARED).allowed).toBe(false);
    expect(evaluateAgentSpawnRequest(spawnInput({ prompt: 7 }), DECLARED).allowed).toBe(false);
    expect(evaluateAgentSpawnRequest(spawnInput({ description: "" }), DECLARED).allowed).toBe(false);
  });

  it("denies every spawn when the run declared no agents", () => {
    const decision = evaluateAgentSpawnRequest(spawnInput({}), []);
    expect(decision.allowed).toBe(false);
  });
});

describe("createAgentGateHook", () => {
  it("matches both names the SDK uses for the tool", () => {
    expect(AGENT_TOOL_MATCHERS).toEqual(["Agent", "Task"]);
  });

  it("denies through the hook shape and allows with updatedInput through the hook shape", async () => {
    const hook = createAgentGateHook(DECLARED);
    const denied = await hook(
      {
        hook_event_name: "PreToolUse",
        tool_use_id: "tool-1",
        tool_name: "Agent",
        tool_input: spawnInput({ subagent_type: "general-purpose" }),
        session_id: "s",
        transcript_path: "t",
        cwd: "/w",
      },
      "tool-1",
      { signal: new AbortController().signal },
    );
    expect(denied).toMatchObject({
      hookSpecificOutput: { hookEventName: "PreToolUse", permissionDecision: "deny" },
    });
    const allowed = await hook(
      {
        hook_event_name: "PreToolUse",
        tool_use_id: "tool-2",
        tool_name: "Task",
        tool_input: spawnInput({ run_in_background: true }),
        session_id: "s",
        transcript_path: "t",
        cwd: "/w",
      },
      "tool-2",
      { signal: new AbortController().signal },
    );
    expect(allowed).toMatchObject({
      hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "allow",
        updatedInput: { run_in_background: false, subagent_type: "verifier" },
      },
    });
  });

  it("ignores events for other tools", async () => {
    const hook = createAgentGateHook(DECLARED);
    const result = await hook(
      {
        hook_event_name: "PreToolUse",
        tool_use_id: "tool-3",
        tool_name: "Bash",
        tool_input: { command: "ls" },
        session_id: "s",
        transcript_path: "t",
        cwd: "/w",
      },
      "tool-3",
      { signal: new AbortController().signal },
    );
    expect(result).toEqual({});
  });
});
