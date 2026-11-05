import { cpSync, existsSync, mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { messageOf } from "../src/errors.js";
import { sha256Hex } from "../src/hash.js";
import { createNarrower, type Narrower } from "../src/narrow.js";
import {
  parseRunReportResult,
  type RunReportFn,
  type RunReportOptions,
  type RunReportResult,
} from "../src/run-report.js";
import type { ExpectedFixture } from "./grader.js";

export class EvalRunnerError extends Error {}

export const DEFAULT_EVAL_RUNS = 3;

export type SavedEvalRun = {
  fixture: string;
  runIndex: number;
  executionId: string;
  startedAt: string;
  finishedAt: string;
  result: RunReportResult | null;
  error: string | null;
};

const parse: Narrower = createNarrower((message) => new EvalRunnerError(message));

export type CitationMode = "line" | "page-and-line";

export const CITATION_MODES: readonly CitationMode[] = ["line", "page-and-line"];

const CITATION_SHAPE_BY_MODE: Record<CitationMode, string> = {
  line: '"citations":{"<field>":"<the line as printed in the document>"}',
  "page-and-line":
    '"citations":{"<field>":{"page":<page index in the PDF file the line appears on, counting from 1 — ' +
    "the file page, not the number printed on the paper>," +
    '"as_printed":"<the line as printed in the document>"}}',
};

export function buildEvalPrompt(expected: ExpectedFixture, citationMode: CitationMode = "line"): string {
  const fieldNames = [...Object.keys(expected.anchors), ...Object.keys(expected.judgment)];
  return [
    `You are standardizing a balance sheet from the document ${expected.document} in your working directory.`,
    `Read the consolidated statement of financial position (printed page(s) ${expected.sourcePages.join(", ")}) and extract the fields below for the current period (FY0) and the prior period (FY_1), in ${expected.currency}, as exact integers with no thousands separators.`,
    "",
    `Fields: ${fieldNames.join(", ")}`,
    "",
    "Reply with a single JSON object and nothing else, in this shape:",
    `{"fields":{"<field>":{"values":{"FY0":<integer or null>,"FY_1":<integer or null>},"basis":"<optional: short label for what you included>","composition":["<optional: components you summed>"]}},${CITATION_SHAPE_BY_MODE[citationMode]}}`,
    "",
    "Use null when a figure is genuinely not in the statement. Never invent a number.",
    "Each citation must copy one row of the statement exactly as printed, left to right. You may omit " +
      "columns (such as the note number), but never add words, years, labels, separators, or arithmetic " +
      "that are not printed in that row. Quoting just the row label is acceptable.",
  ].join("\n");
}

// The same digest buildTraceEnvelope stamps into every trace pack — recomputed here so a
// baseline's promptSha256 is checkable from the repo alone, not trusted from expected.json.
export function evalPromptSha256(expected: ExpectedFixture, citationMode: CitationMode = "line"): string {
  return sha256Hex(new TextEncoder().encode(buildEvalPrompt(expected, citationMode)));
}

function timestampSlug(date: Date): string {
  return date.toISOString().replaceAll(":", "-").replace(/\..*$/, "Z");
}

export type RunEvalFixtureInput = {
  expected: ExpectedFixture;
  runsDir: string;
  runReportFn: RunReportFn;
  k?: number;
  runOptions?: RunReportOptions;
  now?: () => number;
  citationMode?: CitationMode;
  runsSubdir?: string;
};

export async function runEvalFixture(input: RunEvalFixtureInput): Promise<string[]> {
  const k = input.k ?? DEFAULT_EVAL_RUNS;
  const now = input.now ?? Date.now;
  const fixtureRunsDir = join(input.runsDir, input.runsSubdir ?? input.expected.fixture);
  mkdirSync(fixtureRunsDir, { recursive: true });
  const prompt = buildEvalPrompt(input.expected, input.citationMode ?? "line");
  const batchSlug = timestampSlug(new Date(now()));
  const savedPaths: string[] = [];
  for (let runIndex = 1; runIndex <= k; runIndex += 1) {
    const executionId = `eval-${input.expected.fixture}-${batchSlug}-${runIndex}`;
    const startedAt = new Date(now()).toISOString();
    let result: RunReportResult | null = null;
    let error: string | null = null;
    try {
      result = await input.runReportFn({ executionId, prompt }, input.runOptions ?? {});
    } catch (caught) {
      error = messageOf(caught);
    }
    const saved: SavedEvalRun = {
      fixture: input.expected.fixture,
      runIndex,
      executionId,
      startedAt,
      finishedAt: new Date(now()).toISOString(),
      result,
      error,
    };
    const savedPath = join(fixtureRunsDir, `${batchSlug}-${runIndex}.json`);
    writeFileSync(savedPath, `${JSON.stringify(saved, null, 2)}\n`);
    if (result?.tracePath !== undefined && existsSync(result.tracePath)) {
      cpSync(result.tracePath, join(fixtureRunsDir, `${batchSlug}-${runIndex}-trace`), { recursive: true });
    }
    savedPaths.push(savedPath);
  }
  return savedPaths;
}

function parseSavedResult(value: unknown): RunReportResult | null {
  return value === null ? null : parseRunReportResult(value, parse, "saved run.result");
}

export function parseSavedEvalRun(value: unknown): SavedEvalRun {
  const record = parse.asRecord(value, "saved run");
  const error = record["error"];
  if (error !== null && typeof error !== "string") {
    parse.fail("saved run.error must be a string or null");
  }
  const runIndex = parse.requireNumber(record, "runIndex", "saved run");
  return {
    fixture: parse.requireString(record, "fixture", "saved run"),
    runIndex,
    executionId: parse.requireString(record, "executionId", "saved run"),
    startedAt: parse.requireString(record, "startedAt", "saved run"),
    finishedAt: parse.requireString(record, "finishedAt", "saved run"),
    result: parseSavedResult(record["result"] ?? null),
    error,
  };
}
