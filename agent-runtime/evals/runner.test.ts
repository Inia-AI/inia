import { afterAll, describe, expect, it } from "bun:test";
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { buildTraceEnvelope } from "../src/run-child.js";
import type { RunReportFn, RunReportResult } from "../src/run-report.js";
import { parseExpectedFixture } from "./grader.js";
import {
  buildEvalPrompt,
  DEFAULT_EVAL_RUNS,
  evalPromptSha256,
  parseSavedEvalRun,
  runEvalFixture,
} from "./runner.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "eval-runner-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

function miniExpected(): ReturnType<typeof parseExpectedFixture> {
  return parseExpectedFixture({
    fixture: "mini",
    document: "mini.pdf",
    currency: "EUR",
    sourcePages: [1],
    notes: [],
    anchors: { totalAssets: { FY0: 100, FY_1: 90 } },
    judgment: {
      shortTermDebt: {
        acceptedBases: [{ id: "run1", basis: "current_borrowings", values: { FY0: 20, FY_1: 18 } }],
      },
    },
  });
}

function resultFor(executionId: string): RunReportResult {
  return {
    executionId,
    output: '{"fields":{},"citations":{}}',
    estimatedCostUsd: 0.01,
    usage: {
      inputTokens: 0,
      outputTokens: 0,
      cacheCreationInputTokens: 0,
      cacheReadInputTokens: 0,
    },
    sessionId: "s-1",
  };
}

describe("evalPromptSha256", () => {
  it("digests the prompt exactly as the trace envelope does, per citation mode", () => {
    const expected = miniExpected();
    expect(evalPromptSha256(expected)).toBe(
      buildTraceEnvelope({ prompt: buildEvalPrompt(expected), env: {} }).promptSha256,
    );
    expect(evalPromptSha256(expected, "page-and-line")).toBe(
      buildTraceEnvelope({ prompt: buildEvalPrompt(expected, "page-and-line"), env: {} }).promptSha256,
    );
    expect(evalPromptSha256(expected)).not.toBe(evalPromptSha256(expected, "page-and-line"));
  });
});

describe("buildEvalPrompt", () => {
  it("names the document and every expected field", () => {
    const prompt = buildEvalPrompt(miniExpected());
    expect(prompt).toContain("mini.pdf");
    expect(prompt).toContain("totalAssets");
    expect(prompt).toContain("shortTermDebt");
    expect(prompt).toContain("FY0");
    expect(prompt).toContain("null");
  });

  it("pins how citations quote: copy one printed row, omissions allowed, inventions banned", () => {
    for (const mode of ["line", "page-and-line"] as const) {
      const prompt = buildEvalPrompt(miniExpected(), mode);
      expect(prompt).toContain("copy one row of the statement exactly as printed");
      expect(prompt).toContain("may omit columns");
      expect(prompt).toContain("never add words, years, labels, separators, or arithmetic");
    }
  });

  it("asks for line-only citations by default", () => {
    const prompt = buildEvalPrompt(miniExpected());
    expect(prompt).toContain('"citations":{"<field>":"<the line as printed in the document>"}');
    expect(prompt).not.toContain('"page"');
  });

  it("asks for a file page beside the line in page-and-line mode, changing nothing else", () => {
    const withPages = buildEvalPrompt(miniExpected(), "page-and-line");
    expect(withPages).toContain(
      '"citations":{"<field>":{"page":<page index in the PDF file the line appears on, counting from 1 — ' +
        "the file page, not the number printed on the paper>," +
        '"as_printed":"<the line as printed in the document>"}}',
    );
    const stripCitations = (prompt: string) => prompt.replace(/,"citations":.*$/m, "");
    expect(stripCitations(withPages)).toBe(stripCitations(buildEvalPrompt(miniExpected())));
  });
});

describe("runEvalFixture", () => {
  it("defaults to three runs", () => {
    expect(DEFAULT_EVAL_RUNS).toBe(3);
  });

  it("runs k times with distinct executionIds and saves one output file per run", async () => {
    const runsDir = mkdtempSync(join(TEST_ROOT, "runs-"));
    const seen: string[] = [];
    const runReportFn: RunReportFn = (params) => {
      seen.push(params.executionId);
      return Promise.resolve(resultFor(params.executionId));
    };
    const saved = await runEvalFixture({ expected: miniExpected(), runsDir, k: 2, runReportFn });
    expect(saved).toHaveLength(2);
    expect(new Set(seen).size).toBe(2);
    for (const executionId of seen) {
      expect(executionId).toMatch(/^[A-Za-z0-9._-]{1,128}$/);
    }
    const files = readdirSync(join(runsDir, "mini")).filter((name) => name.endsWith(".json"));
    expect(files).toHaveLength(2);
    const first = parseSavedEvalRun(JSON.parse(readFileSync(saved[0] ?? "", "utf8")));
    expect(first.fixture).toBe("mini");
    expect(first.result?.output).toBe('{"fields":{},"citations":{}}');
    expect(first.error).toBeNull();
  });

  it("saves runs under a caller-named subdirectory when one is given", async () => {
    const runsDir = mkdtempSync(join(TEST_ROOT, "runs-"));
    const runReportFn: RunReportFn = (params) => Promise.resolve(resultFor(params.executionId));
    await runEvalFixture({
      expected: miniExpected(),
      runsDir,
      k: 1,
      runReportFn,
      runsSubdir: "mini-cite-pages",
    });
    const files = readdirSync(join(runsDir, "mini-cite-pages")).filter((name) => name.endsWith(".json"));
    expect(files).toHaveLength(1);
  });

  it("prompts with the page-and-line citation shape when asked", async () => {
    const runsDir = mkdtempSync(join(TEST_ROOT, "runs-"));
    const prompts: string[] = [];
    const runReportFn: RunReportFn = (params) => {
      prompts.push(params.prompt);
      return Promise.resolve(resultFor(params.executionId));
    };
    await runEvalFixture({
      expected: miniExpected(),
      runsDir,
      k: 1,
      runReportFn,
      citationMode: "page-and-line",
    });
    expect(prompts[0]).toContain('"as_printed"');
  });

  it("records a failed run as an error entry and keeps running the rest", async () => {
    const runsDir = mkdtempSync(join(TEST_ROOT, "runs-"));
    let call = 0;
    const runReportFn: RunReportFn = (params) => {
      call += 1;
      if (call === 1) {
        return Promise.reject(new Error("budget exhausted"));
      }
      return Promise.resolve(resultFor(params.executionId));
    };
    const saved = await runEvalFixture({ expected: miniExpected(), runsDir, k: 2, runReportFn });
    expect(saved).toHaveLength(2);
    const first = parseSavedEvalRun(JSON.parse(readFileSync(saved[0] ?? "", "utf8")));
    expect(first.result).toBeNull();
    expect(first.error).toContain("budget exhausted");
    const second = parseSavedEvalRun(JSON.parse(readFileSync(saved[1] ?? "", "utf8")));
    expect(second.result).not.toBeNull();
  });

  it("copies the run's trace pack next to the saved output when the result names one", async () => {
    const runsDir = mkdtempSync(join(TEST_ROOT, "runs-"));
    const tracePack = mkdtempSync(join(TEST_ROOT, "pack-"));
    writeFileSync(join(tracePack, "trace.ndjson"), '{"type":"envelope"}\n');
    const runReportFn: RunReportFn = (params) =>
      Promise.resolve({ ...resultFor(params.executionId), tracePath: tracePack });
    const saved = await runEvalFixture({ expected: miniExpected(), runsDir, k: 1, runReportFn });
    const savedPath = saved[0] ?? "";
    const traceDir = savedPath.replace(/\.json$/, "-trace");
    expect(readFileSync(join(traceDir, "trace.ndjson"), "utf8")).toBe('{"type":"envelope"}\n');
  });
});

describe("parseSavedEvalRun", () => {
  it("rejects records that lost their shape", () => {
    expect(() => parseSavedEvalRun({ fixture: "mini" })).toThrow();
  });

  it("keeps the structured output a saved run carried", async () => {
    const runsDir = mkdtempSync(join(TEST_ROOT, "runs-"));
    const structuredOutput = { fields: { revenue: 42 } };
    const runReportFn: RunReportFn = (params) =>
      Promise.resolve({ ...resultFor(params.executionId), structuredOutput });
    const saved = await runEvalFixture({ expected: miniExpected(), runsDir, k: 1, runReportFn });
    const reloaded = parseSavedEvalRun(JSON.parse(readFileSync(saved[0] ?? "", "utf8")));
    expect(reloaded.result?.structuredOutput).toEqual(structuredOutput);
  });
});
