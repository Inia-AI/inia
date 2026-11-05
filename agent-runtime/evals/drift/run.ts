// Tier 1.5 of the SDK safety net: three tiny PAID Haiku prompts, each run twice,
// every run projected into one record and compared with the human-stamped baseline under
// ratio bands. Usage (needs AWS creds via dev.sh; the evals mount must be writable so the
// records and trace packs land under evals/runs/drift/):
//   ./scripts/dev.sh run --rm -v "$PWD/evals:/app/evals" agent bun run drift
//   ./scripts/dev.sh run --rm -v "$PWD/evals:/app/evals" agent bun run drift --calibrate
//   docker run --rm -v "$PWD/evals:/app/evals" agent-runtime:dev bun run drift --report <batch>
// A calibration batch runs the set without a baseline so `bun run drift:stamp` can stamp one;
// --report re-reads a saved batch against the current baseline at zero cost.
import {
  chmodSync,
  copyFileSync,
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import { WORKSPACE_ROOT } from "../../src/config.js";
import { composeUserTurn } from "../../src/document-statement.js";
import { messageOf } from "../../src/errors.js";
import { PRIVATE_DIR_MODE, PRIVATE_FILE_MODE } from "../../src/private-fs.js";
import { claimReadMirror, type ReadMirror } from "../../src/read-gate.js";
import { buildRunEnv, buildTraceEnvelope } from "../../src/run-child.js";
import { FIRST_ATTEMPT } from "../../src/run-registry.js";
import { RunFailedError, runReport } from "../../src/run-report.js";
import { createRunTrace, TRACE_FILE_NAME } from "../../src/trace.js";
import { TRACES_DIR_NAME, WORKSPACE_DOCUMENTS_DIR } from "../../src/workspace-layout.js";
import { assertNoAmbientCapabilities } from "../caps.js";
import { DRIFT_BASELINE_PATH, type DriftBaseline, parseDriftBaseline } from "./baseline.js";
import { DRIFT_FIXTURES_DIR, DRIFT_PROMPTS, type DriftPrompt, driftStagedInputsOf } from "./prompts.js";
import { type DriftRunRecord, parseDriftRunRecord } from "./record.js";
import {
  DRIFT_DEADLINE_MS,
  DRIFT_MAX_TURNS,
  DRIFT_REPEATS,
  DRIFT_RUN_BUDGET_USD,
  DRIFT_SET_SPEND_CAP_USD,
  type DriftRunOutcome,
  type ExecuteDriftPromptFn,
  runDriftSet,
} from "./runner.js";
import { driftVerdict, renderDriftReport } from "./verdict.js";
import { DRIFT_RECORDS_FILE_NAME, DRIFT_RUNS_DIR, readRuntimeVersion, timestampSlug } from "./workspace.js";

const CALIBRATE_FLAG = "--calibrate";
const REPEATS_FLAG = "--repeats";
const REPORT_FLAG = "--report";
const REPORT_FILE_NAME = "report.md";

function fail(message: string): never {
  console.error(`DRIFT FAIL: ${message}`);
  process.exit(1);
}

try {
  assertNoAmbientCapabilities(readFileSync("/proc/self/status", "utf8"));
} catch (error) {
  fail(messageOf(error));
}

const args = process.argv.slice(2);
const calibrate = args.includes(CALIBRATE_FLAG);
const repeatsIndex = args.indexOf(REPEATS_FLAG);
const repeats = repeatsIndex === -1 ? DRIFT_REPEATS : Number(args[repeatsIndex + 1]);
if (!Number.isInteger(repeats) || repeats < 1) {
  fail(`${REPEATS_FLAG} must be a positive integer, got "${args[repeatsIndex + 1]}"`);
}
const reportIndex = args.indexOf(REPORT_FLAG);
const reportBatch = reportIndex === -1 ? undefined : args[reportIndex + 1];
if (reportIndex !== -1 && (reportBatch === undefined || reportBatch.startsWith("-"))) {
  fail(`${REPORT_FLAG} needs a batch name under ${DRIFT_RUNS_DIR}`);
}
if (reportBatch !== undefined && calibrate) {
  fail(
    `${REPORT_FLAG} re-reads a saved batch against the baseline; it cannot be combined with ${CALIBRATE_FLAG}`,
  );
}
for (const arg of args) {
  const known = [CALIBRATE_FLAG, REPEATS_FLAG, REPORT_FLAG, String(repeats), reportBatch];
  if (!known.includes(arg)) {
    fail(
      `unknown argument "${arg}" — the flags are ${CALIBRATE_FLAG}, ${REPEATS_FLAG} <n> and ${REPORT_FLAG} <batch>`,
    );
  }
}

function verdictAndReport(
  baseline: DriftBaseline,
  records: readonly DriftRunRecord[],
  batchDir: string,
): never {
  const loadBearingTools = Object.fromEntries(
    DRIFT_PROMPTS.map((prompt) => [prompt.id, prompt.loadBearingTools]),
  );
  const verdict = driftVerdict({ baseline, records, spendCapUsd: DRIFT_SET_SPEND_CAP_USD, loadBearingTools });
  const report = renderDriftReport({ baseline, records, verdict, spendCapUsd: DRIFT_SET_SPEND_CAP_USD });
  const reportPath = join(batchDir, REPORT_FILE_NAME);
  writeFileSync(reportPath, report);
  console.error(report);
  console.error(`report saved to ${reportPath}`);
  if (verdict.tripped) {
    fail(`${verdict.reasons.length} hard finding(s) — see the report above`);
  }
  console.error(
    `DRIFT ${verdict.warnings.length > 0 ? "WARN" : "PASS"}: ${verdict.warnings.length} warning(s), exit 0`,
  );
  process.exit(0);
}

let baseline: DriftBaseline | null = null;
if (!calibrate) {
  if (!existsSync(DRIFT_BASELINE_PATH)) {
    fail(
      `no baseline stamped at ${DRIFT_BASELINE_PATH} — run "bun run drift ${CALIBRATE_FLAG}" for each ` +
        'calibration batch, then "bun run drift:stamp" to stamp one (docs/operations/sdk-bumps.md)',
    );
  }
  try {
    baseline = parseDriftBaseline(JSON.parse(readFileSync(DRIFT_BASELINE_PATH, "utf8")));
  } catch (error) {
    fail(`baseline unreadable: ${messageOf(error)}`);
  }
}

if (reportBatch !== undefined && baseline !== null) {
  const savedBatchDir = join(DRIFT_RUNS_DIR, reportBatch);
  const savedRecordsPath = join(savedBatchDir, DRIFT_RECORDS_FILE_NAME);
  if (!existsSync(savedRecordsPath)) {
    fail(`no ${savedRecordsPath} to report on`);
  }
  const saved: unknown = JSON.parse(readFileSync(savedRecordsPath, "utf8"));
  if (!Array.isArray(saved)) {
    fail(`${savedRecordsPath} is not a record array`);
  }
  try {
    verdictAndReport(baseline, saved.map(parseDriftRunRecord), savedBatchDir);
  } catch (error) {
    fail(`${savedRecordsPath}: ${messageOf(error)}`);
  }
}

const runtimeVersion = readRuntimeVersion();
const batchSlug = timestampSlug(new Date());
const batchDir = join(DRIFT_RUNS_DIR, batchSlug);
mkdirSync(batchDir, { recursive: true });
const recordsPath = join(batchDir, DRIFT_RECORDS_FILE_NAME);

console.error(
  `SDK drift ${calibrate ? "calibration" : "run set"} (${batchSlug}): ${DRIFT_PROMPTS.length} prompts x ${repeats} ` +
    `PAID model runs on ${process.env["ANTHROPIC_MODEL"] ?? "an unpinned model"}, ` +
    `$${DRIFT_RUN_BUDGET_USD} per run, $${DRIFT_SET_SPEND_CAP_USD} cap on the set; runtime ${runtimeVersion}.`,
);

function stageDocuments(
  prompt: DriftPrompt,
  workspacePath: string,
  privateHome: string,
): ReadMirror | undefined {
  if (prompt.document === undefined) {
    return undefined;
  }
  const docsDir = join(workspacePath, WORKSPACE_DOCUMENTS_DIR);
  mkdirSync(docsDir, { mode: PRIVATE_DIR_MODE });
  const stagedPath = join(docsDir, prompt.document.name);
  copyFileSync(join(DRIFT_FIXTURES_DIR, prompt.document.sourceFile), stagedPath);
  chmodSync(stagedPath, PRIVATE_FILE_MODE);
  const digests = driftStagedInputsOf(prompt).map((input) => ({
    name: input.name,
    sha256: input.sha256,
    sizeBytes: input.sizeBytes,
  }));
  return claimReadMirror(privateHome, digests);
}

function readTracePack(packDir: string): string | null {
  const packPath = join(packDir, TRACE_FILE_NAME);
  return existsSync(packPath) ? readFileSync(packPath, "utf8") : null;
}

const execute: ExecuteDriftPromptFn = async ({ prompt, executionId }): Promise<DriftRunOutcome> => {
  const privateHome = mkdtempSync(join(WORKSPACE_ROOT, "home-"));
  const configDir = join(privateHome, ".claude");
  mkdirSync(configDir, { recursive: true });
  const workspacePath = mkdtempSync(join(WORKSPACE_ROOT, "run-"));
  const stagedInputs = driftStagedInputsOf(prompt);
  const trace = createRunTrace({
    traceRoot: join(WORKSPACE_ROOT, TRACES_DIR_NAME),
    executionId,
    attempt: FIRST_ATTEMPT,
    configDir,
    envelope: buildTraceEnvelope({
      prompt: prompt.prompt,
      env: process.env,
      outputFormat: prompt.outputFormat,
      stagedInputs,
      agents: prompt.agents?.map((agent) => agent.name),
    }),
  });
  try {
    const readMirror = stageDocuments(prompt, workspacePath, privateHome);
    const result = await runReport(
      { executionId, prompt: composeUserTurn(prompt.prompt, workspacePath, stagedInputs) },
      {
        subprocessEnv: buildRunEnv({ env: process.env, privateHome, executionId }),
        workspacePath,
        ...(readMirror === undefined ? {} : { readMirror }),
        ...(prompt.agents === undefined ? {} : { agents: [...prompt.agents] }),
        deadlineMs: DRIFT_DEADLINE_MS,
        maxBudgetUsd: DRIFT_RUN_BUDGET_USD,
        maxTurns: DRIFT_MAX_TURNS,
        outputFormat: prompt.outputFormat,
        trace,
      },
    );
    return { result, failure: null, tracePackText: readTracePack(trace.packDir) };
  } catch (error) {
    if (error instanceof RunFailedError) {
      return { result: null, failure: error.failure, tracePackText: readTracePack(trace.packDir) };
    }
    throw error;
  } finally {
    if (existsSync(trace.packDir)) {
      cpSync(trace.packDir, join(batchDir, `${executionId}-trace`), { recursive: true });
    }
    rmSync(workspacePath, { recursive: true, force: true });
    rmSync(privateHome, { recursive: true, force: true });
  }
};

const landed: DriftRunRecord[] = [];
const records = await runDriftSet({
  prompts: DRIFT_PROMPTS,
  repeats,
  batchSlug,
  spendCapUsd: DRIFT_SET_SPEND_CAP_USD,
  maxTurns: DRIFT_MAX_TURNS,
  execute,
  onRecord: (record) => {
    landed.push(record);
    writeFileSync(recordsPath, `${JSON.stringify(landed, null, 2)}\n`);
    console.error(
      `  ${record.promptId} #${record.repeat}: ${record.outcome}${record.detail === "" ? "" : ` (${record.detail})`} — ` +
        `${record.wallMs ?? "?"}ms, ${record.numTurns ?? "?"} turns, $${record.estimatedCostUsd.toFixed(4)}`,
    );
  },
});

const totalSpend = records.reduce((sum, record) => sum + record.estimatedCostUsd, 0);
console.error(`records saved to ${recordsPath}; run-set spend $${totalSpend.toFixed(4)}`);

if (baseline === null) {
  const offNominal = records.filter((record) => record.outcome !== "correct");
  if (offNominal.length > 0) {
    fail(
      `calibration batch has ${offNominal.length} run(s) that were not correct — ` +
        `${offNominal.map((record) => `${record.promptId} #${record.repeat} ${record.outcome}`).join(", ")}; ` +
        "drift:stamp refuses such a batch",
    );
  }
  console.error(`DRIFT CALIBRATION OK: stamp with "bun run drift:stamp" once every calibration batch is in`);
  process.exit(0);
}

verdictAndReport(baseline, records, batchDir);
