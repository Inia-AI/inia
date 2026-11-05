// Stamps evals/drift/baseline.json from saved calibration batches (bun run drift --calibrate).
// The baseline is human-owned once stamped: run this only as part of a deliberate re-stamp,
// never from an agent loop that edits prompts. Usage:
//   docker run --rm -v "$PWD/evals:/app/evals" agent-runtime:dev bun run drift:stamp [batch ...]
// With no batch names every batch under evals/runs/drift/ is used. Then `bun run fix`.
import { existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { messageOf } from "../../src/errors.js";
import { DRIFT_BASELINE_PATH, stampDriftBaseline } from "./baseline.js";
import { type DriftRunRecord, parseDriftRunRecord } from "./record.js";
import { DRIFT_RECORDS_FILE_NAME, DRIFT_RUNS_DIR, readRuntimeVersion } from "./workspace.js";

function fail(message: string): never {
  console.error(`STAMP FAIL: ${message}`);
  process.exit(1);
}

if (!existsSync(DRIFT_RUNS_DIR)) {
  fail(`no calibration batches under ${DRIFT_RUNS_DIR} — run "bun run drift --calibrate" first`);
}
const requested = process.argv.slice(2);
const batchNames =
  requested.length > 0
    ? requested
    : readdirSync(DRIFT_RUNS_DIR)
        .filter((name) => existsSync(join(DRIFT_RUNS_DIR, name, DRIFT_RECORDS_FILE_NAME)))
        .sort();
if (batchNames.length === 0) {
  fail(`no ${DRIFT_RECORDS_FILE_NAME} under ${DRIFT_RUNS_DIR} — run "bun run drift --calibrate" first`);
}

const records: DriftRunRecord[] = [];
for (const batchName of batchNames) {
  const recordsPath = join(DRIFT_RUNS_DIR, batchName, DRIFT_RECORDS_FILE_NAME);
  if (!existsSync(recordsPath)) {
    fail(`no ${recordsPath}`);
  }
  const saved: unknown = JSON.parse(readFileSync(recordsPath, "utf8"));
  if (!Array.isArray(saved)) {
    fail(`${recordsPath} is not a record array`);
  }
  for (const entry of saved) {
    try {
      records.push(parseDriftRunRecord(entry));
    } catch (error) {
      fail(`${recordsPath}: ${messageOf(error)}`);
    }
  }
}

const stampedAt = new Date().toISOString().slice(0, "YYYY-MM-DD".length);
try {
  const baseline = stampDriftBaseline({
    records,
    runtimeVersion: readRuntimeVersion(),
    stampedAt,
    calibrationRuns: batchNames.length,
  });
  writeFileSync(DRIFT_BASELINE_PATH, `${JSON.stringify(baseline, null, 2)}\n`);
  console.error(
    `stamped ${DRIFT_BASELINE_PATH} from ${batchNames.length} batch(es), ${records.length} records: ` +
      `SDK ${baseline.sdkVersion} / CLI ${baseline.cliVersion} / runtime ${baseline.runtimeVersion} / ${baseline.modelPin}`,
  );
  for (const [promptId, prompt] of Object.entries(baseline.prompts)) {
    const wall = prompt.metrics.wallMs;
    const cost = prompt.metrics.estimatedCostUsd;
    console.error(
      `  ${promptId}: ${prompt.samples} samples, wall median ${wall?.median ?? "?"}ms (${wall?.min ?? "?"}–${wall?.max ?? "?"}), ` +
        `cost median $${cost?.median.toFixed(4) ?? "?"}, turns median ${prompt.metrics.numTurns?.median ?? "?"}`,
    );
  }
  console.error("format it with `bun run fix`, review it, and commit it as a human-owned file");
} catch (error) {
  fail(messageOf(error));
}
