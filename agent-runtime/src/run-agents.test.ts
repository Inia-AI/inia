import { describe, expect, it } from "bun:test";
import { createNarrower, type Narrower } from "./narrow.js";
import {
  MAX_AGENT_DESCRIPTION_CHARS,
  MAX_RUN_AGENTS,
  parseRunAgentPayloads,
  type RunAgentPayload,
  resolveRunAgents,
} from "./run-agents.js";

class TestParseError extends Error {}

const parse: Narrower = createNarrower((message) => new TestParseError(message));

function agentPayload(overrides: Partial<RunAgentPayload> = {}): Record<string, unknown> {
  return {
    name: "verifier",
    description: "Checks extracted figures against the document.",
    prompt: "You verify figures. Report mismatches.",
    ...overrides,
  };
}

describe("parseRunAgentPayloads", () => {
  it("parses a well-formed declaration, skills list included", () => {
    const agents = parseRunAgentPayloads([agentPayload({ skills: ["beta-check.md"] })], parse);
    expect(agents).toEqual([
      {
        name: "verifier",
        description: "Checks extracted figures against the document.",
        prompt: "You verify figures. Report mismatches.",
        skills: ["beta-check.md"],
      },
    ]);
  });

  it("refuses the SDK's built-in agent names, which would let the gate bless a built-in", () => {
    for (const reserved of ["general-purpose", "claude", "statusline-setup", "explore", "plan"]) {
      expect(() => parseRunAgentPayloads([agentPayload({ name: reserved })], parse)).toThrow(/reserved/);
    }
  });

  it("refuses names outside the agent identity shape and duplicate names", () => {
    expect(() => parseRunAgentPayloads([agentPayload({ name: "Bad Name" })], parse)).toThrow(TestParseError);
    expect(() => parseRunAgentPayloads([agentPayload({ name: "general purpose!" })], parse)).toThrow(
      TestParseError,
    );
    expect(() =>
      parseRunAgentPayloads([agentPayload(), agentPayload({ description: "other" })], parse),
    ).toThrow(/duplicate/);
  });

  it("refuses unknown fields, empty declarations, and oversized lists", () => {
    expect(() => parseRunAgentPayloads([{ ...agentPayload(), model: "opus" }], parse)).toThrow(
      TestParseError,
    );
    expect(() => parseRunAgentPayloads([], parse)).toThrow(/at least one/);
    const oversized = Array.from({ length: MAX_RUN_AGENTS + 1 }, (_, index) =>
      agentPayload({ name: `agent-${index}` }),
    );
    expect(() => parseRunAgentPayloads(oversized, parse)).toThrow(/at most/);
  });

  it("caps description and prompt lengths and requires non-empty strings", () => {
    expect(() =>
      parseRunAgentPayloads(
        [agentPayload({ description: "x".repeat(MAX_AGENT_DESCRIPTION_CHARS + 1) })],
        parse,
      ),
    ).toThrow(TestParseError);
    expect(() => parseRunAgentPayloads([agentPayload({ prompt: "" })], parse)).toThrow(TestParseError);
  });

  it("refuses a skills entry that keeps no skill identity", () => {
    expect(() => parseRunAgentPayloads([agentPayload({ skills: ["!!!"] })], parse)).toThrow(TestParseError);
  });
});

describe("resolveRunAgents", () => {
  const fail = (message: string): never => {
    throw new TestParseError(message);
  };

  it("maps caller skill references to the staged plugin's qualified names", () => {
    const resolved = resolveRunAgents(
      [
        {
          name: "verifier",
          description: "d",
          prompt: "p",
          skills: ["Beta-Check.MD"],
        },
      ],
      ["skills:alpha-check", "skills:beta-check"],
      fail,
    );
    expect(resolved).toEqual([
      { name: "verifier", description: "d", prompt: "p", skills: ["skills:beta-check"] },
    ]);
  });

  it("fails closed on a reference to a skill the run does not stage", () => {
    expect(() =>
      resolveRunAgents(
        [{ name: "verifier", description: "d", prompt: "p", skills: ["missing-skill"] }],
        ["skills:alpha-check"],
        fail,
      ),
    ).toThrow(/missing-skill/);
  });

  it("resolves an agent without skills to an empty skill set", () => {
    const resolved = resolveRunAgents([{ name: "verifier", description: "d", prompt: "p" }], [], fail);
    expect(resolved).toEqual([{ name: "verifier", description: "d", prompt: "p", skills: [] }]);
  });
});
