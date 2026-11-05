import type { DriftRunIdentity, DriftRunRecord, DriftTokens } from "./record.js";

export const TEST_MODEL_PIN = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

export const TEST_PROMPT_SHA256: Record<string, string> = {
  "bash-loop": "aa".repeat(32),
  "pdf-read": "bb".repeat(32),
  subagent: "cc".repeat(32),
};

export const TEST_STAGED_INPUTS_SHA256: Record<string, string> = {
  "bash-loop": "absent",
  "pdf-read": "dd".repeat(32),
  subagent: "absent",
};

const TEST_TOOL_CALLS: Record<string, Record<string, number>> = {
  "bash-loop": { Bash: 3, StructuredOutput: 1 },
  "pdf-read": { Read: 1, StructuredOutput: 1 },
  subagent: { Task: 1, StructuredOutput: 1 },
};

export function testIdentity(promptId: string, overrides: Partial<DriftRunIdentity> = {}): DriftRunIdentity {
  return {
    sdkVersion: "0.3.251",
    cliVersion: "2.1.251",
    modelPin: TEST_MODEL_PIN,
    promptSha256: TEST_PROMPT_SHA256[promptId] ?? null,
    stagedInputsSha256: TEST_STAGED_INPUTS_SHA256[promptId] ?? null,
    ...overrides,
  };
}

export type TestRecordOverrides = Partial<Omit<DriftRunRecord, "tokens" | "identity">> & {
  tokens?: Partial<DriftTokens>;
  identity?: Partial<DriftRunIdentity>;
};

export function testRecord(
  promptId: string,
  repeat: number,
  overrides: TestRecordOverrides = {},
): DriftRunRecord {
  const { tokens, identity, ...rest } = overrides;
  return {
    promptId,
    repeat,
    executionId: `drift-${promptId}-${repeat}`,
    startedAt: "2026-09-04T10:00:00.000Z",
    wallMs: 20_000,
    outcome: "correct",
    detail: "",
    numTurns: 4,
    maxTurnsHit: false,
    apiMessages: 4,
    toolCalls: { ...(TEST_TOOL_CALLS[promptId] ?? { Bash: 1 }) },
    tokens: { input: 400, output: 300, cacheWrite: 3000, cacheRead: 9000, ...tokens },
    sdkCostUsd: 0.012,
    floorCostUsd: 0.01,
    estimatedCostUsd: 0.012,
    identity: testIdentity(promptId, identity),
    ...rest,
  };
}

export function testRecordSet(
  promptIds: readonly string[],
  repeats: number,
  overrides: (promptId: string, repeat: number) => TestRecordOverrides = () => ({}),
): DriftRunRecord[] {
  const records: DriftRunRecord[] = [];
  for (let repeat = 1; repeat <= repeats; repeat += 1) {
    for (const promptId of promptIds) {
      records.push(testRecord(promptId, repeat, overrides(promptId, repeat)));
    }
  }
  return records;
}
