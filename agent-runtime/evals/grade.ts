import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { messageOf } from "../src/errors.js";
import { TRACE_FILE_NAME } from "../src/trace.js";
import { citationMissesOf, pageTextsOf } from "./citation.js";
import {
  type ExtractionDoc,
  type GradeReport,
  gradeExtraction,
  parseExpectedFixture,
  parseExtractionText,
  varianceAcrossRuns,
} from "./grader.js";
import {
  durationSecondsBetween,
  type RunPerformanceMetrics,
  runTraceIdentityOf,
  sumOutputTokens,
} from "./performance.js";
import { parseSavedEvalRun, type SavedEvalRun } from "./runner.js";
import { evalVerdict } from "./verdict.js";

const EVALS_DIR = fileURLToPath(new URL(".", import.meta.url));
const fixtureName = process.argv[2] ?? "greenvolt";
const runsSubdirName = process.argv[3] ?? fixtureName;

const expectedPath = join(EVALS_DIR, "fixtures", fixtureName, "expected.json");
if (!existsSync(expectedPath)) {
  console.error(`no fixture at ${expectedPath}`);
  process.exit(1);
}
const expected = parseExpectedFixture(JSON.parse(readFileSync(expectedPath, "utf8")));

const runsDir = join(EVALS_DIR, "runs", runsSubdirName);
if (!existsSync(runsDir)) {
  console.error(`no saved runs under ${runsDir} — run "bun run eval ${fixtureName}" first`);
  process.exit(1);
}
const savedFiles = readdirSync(runsDir)
  .filter((name) => name.endsWith(".json"))
  .sort();
if (savedFiles.length === 0) {
  console.error(`no saved runs under ${runsDir} — run "bun run eval ${fixtureName}" first`);
  process.exit(1);
}

const documentTextPath = join(EVALS_DIR, "fixtures", "documents", `${expected.document}.txt`);
const documentText = existsSync(documentTextPath) ? readFileSync(documentTextPath, "utf8") : null;
const documentPageTexts = documentText === null ? null : pageTextsOf(documentText);

type GradedRun = {
  file: string;
  run: SavedEvalRun;
  extraction: ExtractionDoc | null;
  report: GradeReport | null;
  extractionError: string | null;
  citationMisses: string[];
  metrics: RunPerformanceMetrics;
};

function metricsFor(file: string, run: SavedEvalRun): RunPerformanceMetrics {
  const durationSeconds = durationSecondsBetween(run.startedAt, run.finishedAt);
  const tracePackPath = join(runsDir, `${file.replace(/\.json$/, "-trace")}`, TRACE_FILE_NAME);
  const packText = existsSync(tracePackPath) ? readFileSync(tracePackPath, "utf8") : null;
  const identity = packText === null ? { promptSha256: null, modelPin: null } : runTraceIdentityOf(packText);
  return {
    file,
    durationSeconds,
    outputTokens: packText === null ? null : sumOutputTokens(packText),
    ...identity,
  };
}

const gradedRuns: GradedRun[] = [];
for (const file of savedFiles) {
  const run = parseSavedEvalRun(JSON.parse(readFileSync(join(runsDir, file), "utf8")));
  let extraction: ExtractionDoc | null = null;
  let report: GradeReport | null = null;
  let extractionError: string | null = run.error;
  const citationMisses: string[] = [];
  if (run.result !== null) {
    try {
      const parsedExtraction = parseExtractionText(run.result.output);
      extraction = parsedExtraction;
      report = gradeExtraction(expected, parsedExtraction);
      if (documentText !== null && documentPageTexts !== null) {
        const checks = Object.entries(parsedExtraction.citations).map(([field, citation]) => ({
          field,
          asPrinted: citation.asPrinted,
          page: citation.page,
          values: Object.values(parsedExtraction.fields[field]?.values ?? {}),
        }));
        citationMisses.push(...citationMissesOf(documentText, documentPageTexts, checks));
      }
    } catch (error) {
      extractionError = messageOf(error);
    }
  }
  gradedRuns.push({
    file,
    run,
    extraction,
    report,
    extractionError,
    citationMisses,
    metrics: metricsFor(file, run),
  });
}

const variance = varianceAcrossRuns(
  gradedRuns.flatMap((graded) => (graded.extraction === null ? [] : [graded.extraction])),
);

const verdict = evalVerdict({
  runs: gradedRuns.map((graded) => ({
    file: graded.file,
    report: graded.report,
    citationMisses: graded.citationMisses,
  })),
  variance,
  anchorFields: Object.keys(expected.anchors),
  ...(expected.performance === null
    ? {}
    : { performance: { config: expected.performance, runs: gradedRuns.map((graded) => graded.metrics) } }),
});

function formatMetrics(graded: GradedRun): string {
  const duration = graded.metrics.durationSeconds === null ? "?" : `${graded.metrics.durationSeconds}s`;
  const tokens = graded.metrics.outputTokens === null ? "?" : `${graded.metrics.outputTokens}`;
  const cost = graded.run.result === null ? "?" : graded.run.result.estimatedCostUsd.toFixed(2);
  return `duration=${duration} outputTokens=${tokens} estCostUsd=${cost}`;
}

console.error(`eval grade "${fixtureName}" — regression tripwire, not an accuracy benchmark`);
for (const graded of gradedRuns) {
  if (graded.report === null) {
    console.error(`  ${graded.file}: UNGRADEABLE — ${graded.extractionError ?? "no result"}`);
    continue;
  }
  const { summary } = graded.report;
  const brokenFootings = graded.report.footings.filter((footing) => footing.ok === false);
  console.error(
    `  ${graded.file}: correct=${summary.correct} wrong=${summary.wrong} miss=${summary.miss} ` +
      `hallucination=${summary.hallucination} brokenFootings=${brokenFootings.length}` +
      (documentText === null ? "" : ` citationMisses=${graded.citationMisses.length}`) +
      ` ${formatMetrics(graded)}`,
  );
}
for (const reason of verdict.reasons) {
  console.error(`  TRIP: ${reason}`);
}
for (const warning of verdict.warnings) {
  console.error(`  warn: ${warning}`);
}
if (documentText === null) {
  console.error(
    `  (citation checks skipped: no ${documentTextPath}; create it with pdftotext to enable them)`,
  );
}
if (expected.performance === null) {
  console.error(
    `  (performance checks skipped: no performance baseline in expected.json — a correct-but-2x-slower ` +
      `run exits green; stamp one from the measured metrics of a calibration batch: bun run eval ${fixtureName} 3)`,
  );
}

console.log(
  JSON.stringify(
    {
      fixture: fixtureName,
      framing: "regression tripwire, not an accuracy benchmark",
      runs: gradedRuns.map((graded) => ({
        file: graded.file,
        executionId: graded.run.executionId,
        error: graded.extractionError,
        report: graded.report,
        citationMisses: graded.citationMisses,
        metrics: graded.metrics,
        estimatedCostUsd: graded.run.result === null ? null : graded.run.result.estimatedCostUsd,
      })),
      variance,
      performance: expected.performance,
      verdict,
    },
    null,
    2,
  ),
);
process.exit(verdict.tripped ? 1 : 0);
