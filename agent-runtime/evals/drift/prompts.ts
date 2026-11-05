import { readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { sha256Hex, sha256HexOfJson } from "../../src/hash.js";
import { isRecord } from "../../src/narrow.js";
import type { ResolvedRunAgent } from "../../src/run-agents.js";
import { DOCUMENTS_AREA, type StagedInputSummary } from "../../src/run-documents.js";
import type { RunOutputFormat } from "../../src/run-report.js";
import { ENVELOPE_ABSENT } from "../../src/trace.js";

export const DRIFT_FIXTURES_DIR = fileURLToPath(new URL("fixtures", import.meta.url));

export const DRIFT_PROMPT_IDS = ["bash-loop", "pdf-read", "subagent"] as const;

export type DriftPromptId = (typeof DRIFT_PROMPT_IDS)[number];

export type DriftDocument = { name: string; sourceFile: string; mediaType: string };

export type DriftPrompt = {
  id: DriftPromptId;
  prompt: string;
  expectedAnswer: number;
  loadBearingTools: readonly string[];
  outputFormat: RunOutputFormat;
  document?: DriftDocument;
  agents?: readonly ResolvedRunAgent[];
};

const ANSWER_FIELD = "answer";

export const DRIFT_OUTPUT_FORMAT: RunOutputFormat = {
  type: "json_schema",
  schema: {
    type: "object",
    properties: { [ANSWER_FIELD]: { type: "number" } },
    required: [ANSWER_FIELD],
    additionalProperties: false,
  },
};

const BASH_LOOP_NUMBERS = [7, 11, 13];
const BASH_LOOP_ANSWER = BASH_LOOP_NUMBERS.reduce((sum, value) => sum + value * value, 0);

const DRIFT_PAGE_NUMBER = 583274;

const SUBAGENT_LEFT = 17;
const SUBAGENT_RIGHT = 23;
const SUBAGENT_ANSWER = SUBAGENT_LEFT * SUBAGENT_RIGHT;
const SUBAGENT_NAME = "helper";

const BASH_TOOL = "Bash";
const READ_TOOL = "Read";
const TASK_TOOL = "Task";
const AGENT_TOOL = "Agent";

export const DRIFT_PROMPTS: readonly DriftPrompt[] = [
  {
    id: "bash-loop",
    prompt: [
      "Use the Bash tool for three separate, dependent commands, one tool call each, in this order:",
      `1. printf '${BASH_LOOP_NUMBERS.join("\\n")}\\n' > numbers.txt`,
      "2. awk '{ s += $1 * $1 } END { print s }' numbers.txt > sum.txt",
      "3. cat sum.txt",
      "Do not combine them into one command and do not compute the result yourself.",
      "Then finish, reporting the number the third command printed as the answer.",
    ].join("\n"),
    expectedAnswer: BASH_LOOP_ANSWER,
    loadBearingTools: [BASH_TOOL],
    outputFormat: DRIFT_OUTPUT_FORMAT,
  },
  {
    id: "pdf-read",
    prompt: [
      "A one-page PDF document is staged for this run; its absolute path is stated above.",
      'Use the Read tool on that path with pages "1" to view the page.',
      "Do not use Bash and do not run any text-extraction command.",
      "Then finish, reporting the six-digit number printed on the page as the answer.",
    ].join("\n"),
    expectedAnswer: DRIFT_PAGE_NUMBER,
    loadBearingTools: [READ_TOOL],
    outputFormat: DRIFT_OUTPUT_FORMAT,
    document: { name: "drift-page.pdf", sourceFile: "drift-page.pdf", mediaType: "application/pdf" },
  },
  {
    id: "subagent",
    prompt: [
      `Delegate exactly one task to the "${SUBAGENT_NAME}" sub-agent through the Task tool:`,
      `run the Bash command echo $((${SUBAGENT_LEFT}*${SUBAGENT_RIGHT})) and report its output.`,
      "Do not use the Bash tool yourself and do not compute the result yourself.",
      "Then finish, reporting the number the sub-agent returned as the answer.",
    ].join("\n"),
    expectedAnswer: SUBAGENT_ANSWER,
    loadBearingTools: [TASK_TOOL, AGENT_TOOL],
    outputFormat: DRIFT_OUTPUT_FORMAT,
    agents: [
      {
        name: SUBAGENT_NAME,
        description: "Runs the one Bash command it is given and reports the output verbatim.",
        prompt:
          "You are a helper sub-agent. Run exactly the Bash command you are asked to run, once, " +
          "using the Bash tool, and reply with its output only.",
        skills: [],
      },
    ],
  },
];

export function driftPromptById(id: string): DriftPrompt | undefined {
  return DRIFT_PROMPTS.find((prompt) => prompt.id === id);
}

export function driftPromptSha256(prompt: DriftPrompt): string {
  return sha256Hex(new TextEncoder().encode(prompt.prompt));
}

export function driftStagedInputsOf(
  prompt: DriftPrompt,
  fixturesDir = DRIFT_FIXTURES_DIR,
): StagedInputSummary[] {
  if (prompt.document === undefined) {
    return [];
  }
  const bytes = readFileSync(join(fixturesDir, prompt.document.sourceFile));
  return [
    {
      area: DOCUMENTS_AREA,
      name: prompt.document.name,
      sha256: sha256Hex(bytes),
      sizeBytes: bytes.byteLength,
      mediaType: prompt.document.mediaType,
    },
  ];
}

// Mirrors the envelope's digest of the staged-input summaries, so a changed fixture reads as stale.
export function stagedInputsDigestOf(stagedInputs: readonly StagedInputSummary[]): string {
  return stagedInputs.length === 0 ? ENVELOPE_ABSENT : sha256HexOfJson(stagedInputs);
}

export function answerOf(structuredOutput: unknown): number | null {
  if (!isRecord(structuredOutput)) {
    return null;
  }
  const answer = structuredOutput[ANSWER_FIELD];
  return typeof answer === "number" && Number.isFinite(answer) ? answer : null;
}
