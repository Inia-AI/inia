import { MAX_PROMPT_CHARS } from "./config.js";
import type { Narrower } from "./narrow.js";
import { normalizeSkillName, qualifiedSkillName } from "./skill-plugin.js";

// The caller's sub-agent declaration, exactly as it rides the request and the wire.
// `skills` entries reference the run's skillFiles by the caller's own names
// ("beta-check.md"); they resolve to plugin-qualified names only where the staged
// plugin is known.
export type RunAgentPayload = {
  name: string;
  description: string;
  prompt: string;
  skills?: string[];
};

export type ResolvedRunAgent = {
  name: string;
  description: string;
  prompt: string;
  skills: string[];
};

export const MAX_RUN_AGENTS = 16;
export const MAX_AGENT_NAME_CHARS = 64;
export const MAX_AGENT_DESCRIPTION_CHARS = 1024;
export const MAX_AGENT_PROMPT_CHARS = MAX_PROMPT_CHARS;
const MAX_AGENT_SKILLS = 16;

// The subagent_type vocabulary: the same shape as a skill identity. The shape alone
// does NOT exclude the SDK's built-in agent types — three of them fit it — so the
// reserved list below refuses them explicitly: a declared built-in name would put
// that name on the gate's allowlist, and whether the caller's definition or the
// SDK's built-in one wins for a reserved key is SDK-internal precedence this
// runtime refuses to bet a lockdown on.
const AGENT_NAME_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

const RESERVED_AGENT_NAMES: readonly string[] = [
  "claude",
  "explore",
  "general-purpose",
  "plan",
  "statusline-setup",
];

const AGENT_FIELDS: readonly string[] = ["name", "description", "prompt", "skills"];

function parseAgentSkills(value: unknown, parse: Narrower, label: string): string[] | undefined {
  if (value === undefined) {
    return undefined;
  }
  const entries = parse.requireArray(value, label);
  if (entries.length > MAX_AGENT_SKILLS) {
    parse.fail(`${label} must reference at most ${MAX_AGENT_SKILLS} skills`);
  }
  return entries.map((entry, index) => {
    if (typeof entry !== "string" || entry === "") {
      parse.fail(`${label}[${index}] must be a non-empty string`);
    }
    if (normalizeSkillName(entry) === undefined) {
      parse.fail(`${label}[${index}] ("${entry}") keeps no skill identity`);
    }
    return entry;
  });
}

export function parseRunAgentPayloads(value: unknown, parse: Narrower, label = "agents"): RunAgentPayload[] {
  const entries = parse.requireArray(value, label);
  if (entries.length === 0) {
    parse.fail(`${label} must declare at least one agent when present`);
  }
  if (entries.length > MAX_RUN_AGENTS) {
    parse.fail(`${label} must declare at most ${MAX_RUN_AGENTS} agents`);
  }
  const seenNames = new Set<string>();
  return entries.map((entry, index) => {
    const entryLabel = `${label}[${index}]`;
    const record = parse.asRecord(entry, entryLabel);
    parse.rejectUnknownFields(record, AGENT_FIELDS, entryLabel);
    const name = parse.requireString(record, "name", entryLabel);
    if (name.length > MAX_AGENT_NAME_CHARS || !AGENT_NAME_PATTERN.test(name)) {
      parse.fail(
        `${entryLabel}.name must match ${AGENT_NAME_PATTERN.source} and be at most ${MAX_AGENT_NAME_CHARS} characters`,
      );
    }
    if (RESERVED_AGENT_NAMES.includes(name)) {
      parse.fail(`${entryLabel}.name "${name}" is reserved: it names one of the SDK's built-in agents`);
    }
    if (seenNames.has(name)) {
      parse.fail(`${entryLabel}.name "${name}" is a duplicate agent name`);
    }
    seenNames.add(name);
    const description = parse.requireString(record, "description", entryLabel);
    if (description.length > MAX_AGENT_DESCRIPTION_CHARS) {
      parse.fail(`${entryLabel}.description must be at most ${MAX_AGENT_DESCRIPTION_CHARS} characters`);
    }
    const prompt = parse.requireString(record, "prompt", entryLabel);
    if (prompt.length > MAX_AGENT_PROMPT_CHARS) {
      parse.fail(`${entryLabel}.prompt must be at most ${MAX_AGENT_PROMPT_CHARS} characters`);
    }
    const skills = parseAgentSkills(record["skills"], parse, `${entryLabel}.skills`);
    return { name, description, prompt, ...(skills === undefined ? {} : { skills }) };
  });
}

const QUALIFIED_SKILL_PATTERN = /^skills:[a-z0-9]+(?:-[a-z0-9]+)*$/;

export function parseResolvedRunAgents(
  value: unknown,
  parse: Narrower,
  label = "agents",
): ResolvedRunAgent[] {
  return parseRunAgentPayloads(value, parse, label).map((agent) => {
    const skills = (agent.skills ?? []).map((entry, index) => {
      if (!QUALIFIED_SKILL_PATTERN.test(entry)) {
        parse.fail(`${label} skill "${entry}" (entry ${index}) is not a plugin-qualified skill name`);
      }
      return entry;
    });
    return { name: agent.name, description: agent.description, prompt: agent.prompt, skills };
  });
}

export function resolveRunAgents(
  agents: readonly RunAgentPayload[],
  availableQualifiedSkillNames: readonly string[],
  fail: (message: string) => never,
): ResolvedRunAgent[] {
  return agents.map((agent) => {
    const skills = (agent.skills ?? []).map((reference) => {
      const identity = normalizeSkillName(reference);
      const qualified = identity === undefined ? undefined : qualifiedSkillName(identity);
      if (qualified === undefined || !availableQualifiedSkillNames.includes(qualified)) {
        fail(
          `agent "${agent.name}" references skill "${reference}", which is not among this run's skill files`,
        );
      }
      return qualified;
    });
    return { name: agent.name, description: agent.description, prompt: agent.prompt, skills };
  });
}
