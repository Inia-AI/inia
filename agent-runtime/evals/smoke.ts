// Tier 1 of the SDK safety net: one PAID Haiku-priced run — trivial prompt, one
// sandboxed Bash call, one structured output — asserting the runtime breakage types cannot
// see: the model actually answers, the sandbox actually starts, a tool result actually comes
// back, structured output actually lands. Costs pennies; run it on every SDK bump.
// Usage (needs AWS creds via dev.sh): ./scripts/dev.sh run --rm agent bun run smoke
import { mkdirSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { WORKSPACE_ROOT } from "../src/config.js";
import { messageOf } from "../src/errors.js";
import { isRecord } from "../src/narrow.js";
import { buildRunEnv, buildTraceEnvelope } from "../src/run-child.js";
import { FIRST_ATTEMPT } from "../src/run-registry.js";
import { type RunOutputFormat, runReport } from "../src/run-report.js";
import { createRunTrace, TRACE_FILE_NAME } from "../src/trace.js";
import { TRACES_DIR_NAME } from "../src/workspace-layout.js";
import { assertNoAmbientCapabilities } from "./caps.js";

const SMOKE_EXECUTION_ID = `sdk-smoke-${Date.now()}`;
const SMOKE_LEFT = 289;
const SMOKE_RIGHT = 93;
const SMOKE_PRODUCT = SMOKE_LEFT * SMOKE_RIGHT;
const SMOKE_DEADLINE_MS = 180000;
const SMOKE_MAX_BUDGET_USD = 0.25;
const SMOKE_MAX_COST_USD = 0.1;
const SMOKE_MAX_TURNS = 10;

const SMOKE_PROMPT =
  `Use the Bash tool exactly once to compute ${SMOKE_LEFT}*${SMOKE_RIGHT} ` +
  `(for example: echo $((${SMOKE_LEFT}*${SMOKE_RIGHT}))), then finish, reporting the product.`;

const SMOKE_OUTPUT_FORMAT: RunOutputFormat = {
  type: "json_schema",
  schema: {
    type: "object",
    properties: { product: { type: "number" } },
    required: ["product"],
    additionalProperties: false,
  },
};

function countBashToolCalls(packDir: string): number {
  return readFileSync(join(packDir, TRACE_FILE_NAME), "utf8")
    .split("\n")
    .filter((line) => line !== "")
    .map((line): unknown => JSON.parse(line))
    .filter((record) => isRecord(record) && record["type"] === "tool_call" && record["toolName"] === "Bash")
    .length;
}

function fail(message: string): never {
  console.error(`SMOKE FAIL: ${message}`);
  process.exit(1);
}

try {
  assertNoAmbientCapabilities(readFileSync("/proc/self/status", "utf8"));
} catch (error) {
  fail(messageOf(error));
}

console.error(`SDK smoke run (${SMOKE_EXECUTION_ID}): one PAID model run, budget $${SMOKE_MAX_BUDGET_USD}.`);

const privateHome = mkdtempSync(join(WORKSPACE_ROOT, "home-"));
const configDir = join(privateHome, ".claude");
mkdirSync(configDir, { recursive: true });
const workspacePath = mkdtempSync(join(WORKSPACE_ROOT, "run-"));
const trace = createRunTrace({
  traceRoot: join(WORKSPACE_ROOT, TRACES_DIR_NAME),
  executionId: SMOKE_EXECUTION_ID,
  attempt: FIRST_ATTEMPT,
  configDir,
  envelope: buildTraceEnvelope({
    prompt: SMOKE_PROMPT,
    env: process.env,
    outputFormat: SMOKE_OUTPUT_FORMAT,
  }),
});

try {
  const result = await runReport(
    { executionId: SMOKE_EXECUTION_ID, prompt: SMOKE_PROMPT },
    {
      subprocessEnv: buildRunEnv({ env: process.env, privateHome, executionId: SMOKE_EXECUTION_ID }),
      workspacePath,
      deadlineMs: SMOKE_DEADLINE_MS,
      maxBudgetUsd: SMOKE_MAX_BUDGET_USD,
      maxTurns: SMOKE_MAX_TURNS,
      outputFormat: SMOKE_OUTPUT_FORMAT,
      trace,
    },
  );

  const structured = result.structuredOutput;
  if (!isRecord(structured) || structured["product"] !== SMOKE_PRODUCT) {
    fail(`structured output did not carry product ${SMOKE_PRODUCT}: ${JSON.stringify(structured)}`);
  }
  const bashCalls = result.tracePath === undefined ? 0 : countBashToolCalls(result.tracePath);
  if (bashCalls < 1) {
    fail("no Bash tool_call reached the trace — the sandboxed tool round-trip did not happen");
  }
  if (result.estimatedCostUsd > SMOKE_MAX_COST_USD) {
    fail(
      `estimated cost $${result.estimatedCostUsd.toFixed(4)} exceeded the smoke bound ` +
        `$${SMOKE_MAX_COST_USD} — check the model pin before believing anything else`,
    );
  }
  console.error(
    `SMOKE PASS: product ${SMOKE_PRODUCT} via ${bashCalls} Bash call(s), ` +
      `$${result.estimatedCostUsd.toFixed(4)} estimated, ${result.usage?.outputTokens ?? 0} output tokens`,
  );
} catch (error) {
  fail(messageOf(error));
} finally {
  rmSync(workspacePath, { recursive: true, force: true });
  rmSync(privateHome, { recursive: true, force: true });
}
