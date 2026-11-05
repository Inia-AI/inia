import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { WORKSPACE_ROOT } from "../src/config.js";
import { messageOf } from "../src/errors.js";
import { buildRunEnv, buildTraceEnvelope } from "../src/run-child.js";
import { FIRST_ATTEMPT } from "../src/run-registry.js";
import { type RunReportFn, runReport } from "../src/run-report.js";
import { createRunTrace } from "../src/trace.js";
import { TRACES_DIR_NAME } from "../src/workspace-layout.js";
import { assertNoAmbientCapabilities } from "./caps.js";
import { parseExpectedFixture } from "./grader.js";
import { DEFAULT_EVAL_RUNS, runEvalFixture } from "./runner.js";

try {
  assertNoAmbientCapabilities(readFileSync("/proc/self/status", "utf8"));
} catch (error) {
  console.error(messageOf(error));
  process.exit(1);
}

const EVALS_DIR = fileURLToPath(new URL(".", import.meta.url));
const CITE_PAGES_FLAG = "cite-pages";
const fixtureName = process.argv[2] ?? "greenvolt";
const k = process.argv[3] === undefined ? DEFAULT_EVAL_RUNS : Number(process.argv[3]);
if (!Number.isInteger(k) || k < 1) {
  console.error(`k must be a positive integer, got "${process.argv[3]}"`);
  process.exit(1);
}
const modeArg = process.argv[4];
if (modeArg !== undefined && modeArg !== CITE_PAGES_FLAG) {
  console.error(`unknown mode "${modeArg}" — the only mode is "${CITE_PAGES_FLAG}"`);
  process.exit(1);
}
const citePages = modeArg === CITE_PAGES_FLAG;
const runsSubdir = citePages ? `${fixtureName}-${CITE_PAGES_FLAG}` : fixtureName;

const expectedPath = join(EVALS_DIR, "fixtures", fixtureName, "expected.json");
if (!existsSync(expectedPath)) {
  console.error(`no fixture at ${expectedPath}`);
  process.exit(1);
}
const expected = parseExpectedFixture(JSON.parse(readFileSync(expectedPath, "utf8")));

const documentPath = join(EVALS_DIR, "fixtures", "documents", expected.document);
if (!existsSync(documentPath)) {
  console.error(
    `missing ${documentPath} — place the source document there (see evals/fixtures/documents/README.md)`,
  );
  process.exit(1);
}

console.error(
  `eval "${fixtureName}": ${k} PAID model run(s) against ${expected.document}. ` +
    "This is a regression tripwire, not an accuracy benchmark.",
);

const privateHome = mkdtempSync(join(WORKSPACE_ROOT, "home-"));
const configDir = join(privateHome, ".claude");
mkdirSync(configDir, { recursive: true });

const runReportFn: RunReportFn = async (params, options) => {
  const workspacePath = mkdtempSync(join(WORKSPACE_ROOT, "run-"));
  cpSync(documentPath, join(workspacePath, expected.document));
  const trace = createRunTrace({
    traceRoot: join(WORKSPACE_ROOT, TRACES_DIR_NAME),
    executionId: params.executionId,
    attempt: FIRST_ATTEMPT,
    configDir,
    envelope: buildTraceEnvelope({ prompt: params.prompt, env: process.env }),
  });
  try {
    const result = await runReport(params, {
      ...options,
      subprocessEnv: buildRunEnv({ env: process.env, privateHome, executionId: params.executionId }),
      workspacePath,
      trace,
    });
    console.error(`  ${params.executionId}: $${result.estimatedCostUsd.toFixed(4)} (estimate)`);
    return result;
  } finally {
    rmSync(workspacePath, { recursive: true, force: true });
  }
};

const savedPaths = await runEvalFixture({
  expected,
  runsDir: join(EVALS_DIR, "runs"),
  runReportFn,
  k,
  ...(citePages ? { citationMode: "page-and-line" as const, runsSubdir } : {}),
});
rmSync(privateHome, { recursive: true, force: true });
console.error(`saved ${savedPaths.length} run(s):`);
for (const savedPath of savedPaths) {
  console.error(`  ${savedPath}`);
}
console.error(`grade them with: bun run eval:grade ${fixtureName}${citePages ? ` ${runsSubdir}` : ""}`);
