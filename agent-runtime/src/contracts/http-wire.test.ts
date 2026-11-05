import { describe, expect, it } from "bun:test";
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { type AdmissionQueue, createAdmissionQueue } from "../admission-queue.js";
import {
  type CheckpointBundle,
  type CheckpointStore,
  createLocalCheckpointStore,
  sealCheckpoint,
  terminalCheckpointKeyOf,
} from "../checkpoint-store.js";
import { NO_SERVED_FILES } from "../checkpoint-store-test-helpers.js";
import { loadServerConfig, type ServerConfig, UID_POOL_BASE, UID_POOL_SIZE } from "../config.js";
import type { DocumentStore } from "../document-store.js";
import { sha256Hex } from "../hash.js";
import { CAPACITY_REASONS } from "../http-transport.js";
import type { Logger } from "../logger.js";
import { createNoopMetrics } from "../metrics.js";
import type { StagedInput } from "../run-documents.js";
import { createRunFilesView, publishRunArtifacts, type RunFilesView } from "../run-files.js";
import { removeOwnTree } from "../run-files-test-helpers.js";
import { RUN_STATUSES } from "../run-registry.js";
import {
  type PauseRequest,
  RUNTIME_FAILURE_SUBTYPES,
  RunFailedError,
  type RunReportFn,
  type RunReportResult,
} from "../run-report.js";
import type { SpawnRunFn } from "../run-spawner.js";
import { CONFLICT_CODES, type RunApiServer, startServer } from "../server.js";
import { createUidPool } from "../uid-pool.js";
import {
  buildWireConstraints,
  contractHeadersOf,
  normalizeWireValue,
  renderContractJson,
  UNRECORDED_CAPACITY_REASONS,
  UPDATE_FIXTURES_ENV,
  WIRE_CONSTRAINTS_PATH,
  WIRE_FIXTURE_PATH,
  type WireExchange,
} from "./http-wire.js";

// A production-shaped environment, so every cap that reaches the wire — the fuse ceilings on
// /v1/limits, the numbers inside a refusal message — is the deployed one and not a test knob.
const FIXTURE_ENV: Record<string, string> = {
  CLAUDE_CODE_USE_BEDROCK: "1",
  ANTHROPIC_MODEL: "us.anthropic.claude-sonnet-4-6",
  ANTHROPIC_ALLOWED_MODELS: "us.anthropic.claude-sonnet-4-6,us.anthropic.claude-haiku-4-5",
  AGENT_RUNTIME_AMBIENT_CREDENTIALS: "true",
  AUTH_DISABLED: "true",
  AWS_REGION: "us-west-2",
  AGENT_RUNTIME_VERSION: "0.0.0-fixture",
  AGENT_RUNTIME_REVISION: "fixture",
};

// Pinned so every timestamp on the wire is a literal in the fixture rather than a placeholder.
const FIXED_NOW_MS = Date.parse("2026-01-01T00:00:00.000Z");
const DAY_MS = 24 * 60 * 60 * 1000;

const EXECUTION_ID = "wire-contract-run";
const AUTH_TOKEN = "wire-contract-token";

const STUB_USAGE = {
  inputTokens: 1200,
  outputTokens: 340,
  cacheCreationInputTokens: 0,
  cacheReadInputTokens: 0,
};

const STUB_RESULT: RunReportResult = {
  executionId: EXECUTION_ID,
  output: "the finished report",
  estimatedCostUsd: 0.1234,
  sessionId: "session-abc",
  usage: STUB_USAGE,
};

const QUESTION = {
  id: "q-1",
  toolName: "mcp__platform__request_analyst_input",
  prompt: "Which fund does the mandate refer to?",
  input: { question: "Which fund does the mandate refer to?" },
};

function pauseRequest(): PauseRequest {
  return {
    question: QUESTION,
    sessionId: "session-abc",
    estimatedCostUsd: 0.05,
    checkpoint: sealCheckpoint(new TextEncoder().encode("serialized session state")),
  };
}

function memoryCheckpointStore(): CheckpointStore {
  const bundles = new Map<string, CheckpointBundle>();
  const keyOf = (key: { executionId: string; attempt: number; segment: number }): string =>
    `${key.executionId}.${key.attempt}.${key.segment}`;
  return {
    ...NO_SERVED_FILES,
    save(key, bundle) {
      bundles.set(keyOf(key), bundle);
      return Promise.resolve();
    },
    load(key) {
      return Promise.resolve(bundles.get(keyOf(key)));
    },
    delete(key) {
      bundles.delete(keyOf(key));
      return Promise.resolve();
    },
  };
}

function memoryDocumentStore(options: { failSave?: boolean } = {}): DocumentStore {
  const saved = new Map<string, StagedInput[]>();
  return {
    save(executionId, documents) {
      if (options.failSave === true) {
        return Promise.reject(new Error("document save refused"));
      }
      saved.set(executionId, documents);
      return Promise.resolve();
    },
    openInputs: () => Promise.resolve(undefined),
    delete(executionId) {
      saved.delete(executionId);
      return Promise.resolve();
    },
  };
}

function emptyRunFilesView(): RunFilesView {
  return {
    list: () => Promise.resolve([]),
    resolve: () => Promise.resolve(undefined),
    purge: () => Promise.resolve(),
    purgeArtifacts: () => Promise.resolve(),
    retainedBytes: () => Promise.resolve({ artifactBytes: 0, traceBytes: 0 }),
  };
}

function silentLogger(): Logger {
  const noop = (): void => {};
  const logger: Logger = { debug: noop, info: noop, warn: noop, error: noop, with: () => logger };
  return logger;
}

function fixtureConfig(overrides: Partial<ServerConfig> = {}): ServerConfig {
  return { ...loadServerConfig(FIXTURE_ENV), port: 0, ...overrides };
}

type Harness = {
  server: RunApiServer;
  baseUrl: string;
  clock: { ms: number };
};

const openServers: RunApiServer[] = [];

async function launch(input: {
  config?: ServerConfig;
  runReportFn?: SpawnRunFn;
  admission?: AdmissionQueue;
  documentStore?: DocumentStore;
  runFiles?: RunFilesView;
  checkpointStore?: CheckpointStore;
}): Promise<Harness> {
  const clock = { ms: FIXED_NOW_MS };
  const server = startServer(input.config ?? fixtureConfig(), {
    runReportFn: input.runReportFn ?? ((): Promise<RunReportResult> => Promise.resolve(STUB_RESULT)),
    now: () => clock.ms,
    random: () => 0,
    checkpointStore: input.checkpointStore ?? memoryCheckpointStore(),
    documentStore: input.documentStore ?? memoryDocumentStore(),
    runFiles: input.runFiles ?? emptyRunFilesView(),
    uidPool: createUidPool({ baseUid: UID_POOL_BASE, size: UID_POOL_SIZE }),
    logger: silentLogger(),
    metrics: createNoopMetrics(),
    ...(input.admission === undefined ? {} : { admission: input.admission }),
  });
  openServers.push(server);
  await new Promise<void>((resolve) => server.once("listening", () => resolve()));
  const address = server.address();
  if (address === null || typeof address === "string") {
    throw new Error("the fixture server did not bind a port");
  }
  return { server, baseUrl: `http://127.0.0.1:${address.port}`, clock };
}

async function closeServers(): Promise<void> {
  await Promise.all(
    openServers.splice(0).map(
      (server) =>
        new Promise<void>((resolve) => {
          server.close(() => resolve());
          server.closeAllConnections();
        }),
    ),
  );
}

type RecordInput = {
  harness: Harness;
  method: string;
  path: string;
  body?: unknown;
  rawBody?: string;
  contentType?: string;
  bearer?: string;
  configOverrides?: Record<string, number>;
  hideRequestBody?: boolean;
};

type Recorded = { status: number; body: unknown };

const exchanges = new Map<string, WireExchange>();
// Resume tokens the runtime minted during the capture; every other string on the wire is pinned.
// Substitution happens once at the end, so a token first seen in a response is still replaced in
// the earlier request that carried it.
const mintedTokens = new Set<string>();

function collectMintedTokens(value: unknown): void {
  if (Array.isArray(value)) {
    for (const entry of value) {
      collectMintedTokens(entry);
    }
    return;
  }
  if (typeof value !== "object" || value === null) {
    return;
  }
  for (const [key, entry] of Object.entries(value)) {
    if (key === "resumeToken" && typeof entry === "string") {
      mintedTokens.add(entry);
      continue;
    }
    collectMintedTokens(entry);
  }
}

function decodeBody(response: Response, text: string): unknown {
  const contentType = response.headers.get("content-type") ?? "";
  return contentType.includes("application/json") ? JSON.parse(text) : text;
}

async function record(name: string, input: RecordInput): Promise<Recorded> {
  if (exchanges.has(name)) {
    throw new Error(`duplicate wire case name "${name}"`);
  }
  const sent = input.rawBody ?? (input.body === undefined ? undefined : JSON.stringify(input.body));
  const contentType = sent === undefined ? undefined : (input.contentType ?? "application/json");
  const response = await fetch(`${input.harness.baseUrl}${input.path}`, {
    method: input.method,
    headers: {
      ...(contentType === undefined ? {} : { "content-type": contentType }),
      ...(input.bearer === undefined ? {} : { authorization: `Bearer ${input.bearer}` }),
      connection: "close",
    },
    ...(sent === undefined ? {} : { body: sent }),
  });
  const text = await response.text();
  const decoded = decodeBody(response, text);
  collectMintedTokens(decoded);
  const requestBody =
    sent === undefined
      ? {}
      : input.hideRequestBody === true
        ? { bodyBytes: Buffer.byteLength(sent) }
        : { body: input.rawBody ?? input.body };
  exchanges.set(name, {
    request: {
      method: input.method,
      path: input.path,
      ...(contentType === undefined ? {} : { contentType }),
      ...(input.bearer === undefined ? {} : { auth: "bearer" }),
      ...requestBody,
    },
    ...(input.configOverrides === undefined ? {} : { configOverrides: input.configOverrides }),
    response: {
      status: response.status,
      headers: contractHeadersOf(response.headers),
      body: decoded,
    },
  });
  return { status: response.status, body: decoded };
}

async function pollUntilStatus(harness: Harness, executionId: string, status: string): Promise<void> {
  for (let attempt = 0; attempt < 400; attempt += 1) {
    const response = await fetch(`${harness.baseUrl}/v1/runs/${executionId}`, {
      headers: { connection: "close" },
    });
    if (response.status === 200) {
      const body: unknown = await response.json();
      if (typeof body === "object" && body !== null && "status" in body && body.status === status) {
        return;
      }
    } else {
      await response.text();
    }
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  throw new Error(`run ${executionId} never reached status ${status}`);
}

function resumeTokenOf(body: unknown): string {
  if (typeof body !== "object" || body === null || !("requiredInput" in body)) {
    throw new Error("monitor body carried no requiredInput");
  }
  const required = body.requiredInput;
  if (typeof required !== "object" || required === null || !("resumeToken" in required)) {
    throw new Error("requiredInput carried no resumeToken");
  }
  const token = required.resumeToken;
  if (typeof token !== "string") {
    throw new Error("resumeToken was not a string");
  }
  return token;
}

const START_BODY = { executionId: EXECUTION_ID, prompt: "Extract the fund's holdings." };

function neverSettlingRun(): SpawnRunFn {
  return () => new Promise<RunReportResult>(() => {});
}

function abortableRun(): RunReportFn {
  return (_params, options) =>
    new Promise<RunReportResult>((_resolve, reject) => {
      options?.signal?.addEventListener("abort", () => reject(new Error("aborted by cancel")), {
        once: true,
      });
    });
}

function pausingRun(): RunReportFn {
  return async (_params, options) => {
    if (options?.pause === undefined) {
      throw new Error("the pause seam was not injected");
    }
    await options.pause(pauseRequest());
    return STUB_RESULT;
  };
}

async function captureLifecycleCases(): Promise<void> {
  const harness = await launch({ runReportFn: neverSettlingRun() });

  await record("healthz.ok", { harness, method: "GET", path: "/healthz" });
  await record("healthz.method_not_allowed", { harness, method: "POST", path: "/healthz" });
  await record("limits.ok", { harness, method: "GET", path: "/v1/limits" });
  await record("environment.ok", { harness, method: "GET", path: "/v1/environment" });
  await record("unknown_path.not_found", { harness, method: "GET", path: "/v1/nothing-here" });

  await record("runs.start.accepted", { harness, method: "POST", path: "/v1/runs", body: START_BODY });
  await record("runs.start.duplicate", { harness, method: "POST", path: "/v1/runs", body: START_BODY });
  await record("runs.start.payload_conflict", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: { executionId: EXECUTION_ID, prompt: "a different prompt" },
  });

  await record("runs.monitor.running", { harness, method: "GET", path: `/v1/runs/${EXECUTION_ID}` });
  await record("runs.monitor.not_found", { harness, method: "GET", path: "/v1/runs/no-such-run" });
  await record("runs.monitor.method_not_allowed", {
    harness,
    method: "PUT",
    path: `/v1/runs/${EXECUTION_ID}`,
  });

  await record("runs.complete.not_completable", {
    harness,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/complete`,
  });
  await record("runs.complete.not_found", { harness, method: "POST", path: "/v1/runs/no-such-run/complete" });
  await record("runs.cancel.not_found", { harness, method: "POST", path: "/v1/runs/no-such-run/cancel" });
  await record("runs.input.not_found", {
    harness,
    method: "POST",
    path: "/v1/runs/no-such-run/input",
    body: { resumeToken: "0f2f0b1e-0000-4000-8000-000000000000", questionId: "q-1", answer: "a" },
  });
  await record("runs.input.not_awaiting_input", {
    harness,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/input`,
    body: { resumeToken: "0f2f0b1e-0000-4000-8000-000000000000", questionId: "q-1", answer: "a" },
  });

  // Cancel last: this run never observes its abort signal, so it parks in `cancelling`, which
  // is the only way that transient status can be captured at rest.
  await record("runs.cancel.cancelling", {
    harness,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/cancel`,
  });
  await record("runs.monitor.cancelling", { harness, method: "GET", path: `/v1/runs/${EXECUTION_ID}` });
}

async function captureValidationCases(): Promise<void> {
  const harness = await launch({ runReportFn: neverSettlingRun() });
  const rejected = { executionId: "wire-rejected", prompt: "p" };

  await record("runs.start.invalid_execution_id", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: { ...rejected, executionId: "../escape" },
  });
  await record("runs.start.missing_prompt", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: { executionId: "wire-rejected" },
  });
  await record("runs.start.unknown_field", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: { ...rejected, retries: 3 },
  });
  await record("runs.start.model_not_allowed", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: { ...rejected, model: "us.anthropic.claude-opus-4-1" },
  });
  await record("runs.start.fuse_above_ceiling", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: { ...rejected, maxTurns: 100000 },
  });
  await record("runs.start.invalid_skill_asset_name", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: {
      ...rejected,
      skillFiles: [
        {
          name: "verify.md",
          contentBase64: Buffer.from("# skill").toString("base64"),
          assets: [{ name: "../escape.py", contentBase64: Buffer.from("x").toString("base64") }],
        },
      ],
    },
  });
  await record("runs.start.preset_system_prompt_refused", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: { ...rejected, systemPrompt: { type: "preset", preset: "claude_code" } },
  });
  await record("runs.start.invalid_json", {
    harness,
    method: "POST",
    path: "/v1/runs",
    rawBody: '{"executionId":',
  });
  await record("runs.start.unsupported_media_type", {
    harness,
    method: "POST",
    path: "/v1/runs",
    rawBody: JSON.stringify(rejected),
    contentType: "text/plain",
  });
  await record("runs.input.invalid_body", {
    harness,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/input`,
    body: { questionId: "q-1" },
  });
}

// The continuation start shape: one accepted continuation and the refusals a platform
// integrator must be able to tell apart, all before admission.
async function captureContinuationCases(): Promise<void> {
  const harness = await launch({ runReportFn: neverSettlingRun() });
  const bundleBytes = Buffer.from("platform-held session bundle");
  const outBytes = Buffer.from("print('check')");
  const goodContinuation = {
    sessionBundle: { sha256: sha256Hex(bundleBytes), bytesBase64: bundleBytes.toString("base64") },
    outFiles: [
      {
        path: "checks/model.py",
        contentBase64: outBytes.toString("base64"),
        sha256: sha256Hex(outBytes),
      },
    ],
  };
  await record("runs.start.continuation_accepted", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: {
      executionId: "wire-continuation",
      prompt: "revise with these findings",
      continuation: goodContinuation,
    },
  });
  await record("runs.start.continuation_missing_bundle", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: {
      executionId: "wire-continuation-bad",
      prompt: "revise",
      continuation: { outFiles: [] },
    },
  });
  await record("runs.start.continuation_walking_path", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: {
      executionId: "wire-continuation-bad",
      prompt: "revise",
      continuation: {
        sessionBundle: goodContinuation.sessionBundle,
        outFiles: [{ ...goodContinuation.outFiles[0], path: "../escape.py" }],
      },
    },
  });
  await record("runs.start.continuation_missing_digest", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: {
      executionId: "wire-continuation-bad",
      prompt: "revise",
      continuation: {
        sessionBundle: goodContinuation.sessionBundle,
        outFiles: [{ path: "checks/model.py", contentBase64: outBytes.toString("base64") }],
      },
    },
  });
  await record("runs.start.continuation_digest_mismatch", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: {
      executionId: "wire-continuation-bad",
      prompt: "revise",
      continuation: {
        sessionBundle: goodContinuation.sessionBundle,
        outFiles: [{ ...goodContinuation.outFiles[0], sha256: "0".repeat(64) }],
      },
    },
  });
}

async function captureTerminalCases(): Promise<void> {
  const succeeded = await launch({});
  await record("runs.start.accepted_for_success", {
    harness: succeeded,
    method: "POST",
    path: "/v1/runs",
    body: START_BODY,
  });
  await pollUntilStatus(succeeded, EXECUTION_ID, "succeeded");
  await record("runs.monitor.succeeded", {
    harness: succeeded,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}`,
  });
  await record("runs.complete.completed", {
    harness: succeeded,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/complete`,
  });
  await record("runs.monitor.completed", {
    harness: succeeded,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}`,
  });
  await record("runs.files.completed_gone", {
    harness: succeeded,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}/files`,
  });

  const salvaged = await launch({
    runReportFn: () =>
      Promise.resolve({
        ...STUB_RESULT,
        // A salvaged run's output is the serialized structured output, exactly as a normal
        // outputFormat success delivers it — the platform stores output as the block answer.
        output: JSON.stringify({ status: "ok", periods: ["FY0", "FY-1"] }),
        estimatedCostUsd: 2.58,
        structuredOutput: { status: "ok", periods: ["FY0", "FY-1"] },
        budgetExhausted: true as const,
      }),
  });
  await fetch(`${salvaged.baseUrl}/v1/runs`, {
    method: "POST",
    headers: { "content-type": "application/json", connection: "close" },
    body: JSON.stringify(START_BODY),
  });
  await pollUntilStatus(salvaged, EXECUTION_ID, "succeeded");
  await record("runs.monitor.succeeded_at_budget", {
    harness: salvaged,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}`,
  });

  const failed = await launch({
    runReportFn: () =>
      Promise.reject(
        new RunFailedError("the run exhausted its budget", {
          subtype: "budget_exhausted",
          estimatedCostUsd: 2.5,
          sessionId: "session-abc",
          usage: STUB_USAGE,
        }),
      ),
  });
  await record("runs.start.accepted_for_failure", {
    harness: failed,
    method: "POST",
    path: "/v1/runs",
    body: START_BODY,
  });
  await pollUntilStatus(failed, EXECUTION_ID, "failed");
  await record("runs.monitor.failed", { harness: failed, method: "GET", path: `/v1/runs/${EXECUTION_ID}` });

  const cancelled = await launch({ runReportFn: abortableRun() });
  await record("runs.start.accepted_for_cancel", {
    harness: cancelled,
    method: "POST",
    path: "/v1/runs",
    body: START_BODY,
  });
  await fetch(`${cancelled.baseUrl}/v1/runs/${EXECUTION_ID}/cancel`, {
    method: "POST",
    headers: { connection: "close" },
  });
  await pollUntilStatus(cancelled, EXECUTION_ID, "cancelled");
  await record("runs.monitor.cancelled", {
    harness: cancelled,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}`,
  });
  await record("runs.cancel.settled", {
    harness: cancelled,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/cancel`,
  });
}

async function capturePauseCases(): Promise<void> {
  const harness = await launch({ runReportFn: pausingRun() });
  await record("runs.start.accepted_for_pause", {
    harness,
    method: "POST",
    path: "/v1/runs",
    body: START_BODY,
  });
  await pollUntilStatus(harness, EXECUTION_ID, "awaiting_input");
  const awaiting = await record("runs.monitor.awaiting_input", {
    harness,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}`,
  });
  const resumeToken = resumeTokenOf(awaiting.body);

  await record("runs.input.stale_resume_token", {
    harness,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/input`,
    body: { resumeToken: "0f2f0b1e-0000-4000-8000-000000000000", questionId: "q-1", answer: "a" },
  });
  await record("runs.input.unknown_question", {
    harness,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/input`,
    body: { resumeToken, questionId: "q-unknown", answer: "a" },
  });
  await record("runs.input.accepted", {
    harness,
    method: "POST",
    path: `/v1/runs/${EXECUTION_ID}/input`,
    body: { resumeToken, questionId: "q-1", answer: "The Baltic Growth Fund.", allowFurtherPause: false },
  });

  const expiring = await launch({ runReportFn: pausingRun() });
  await fetch(`${expiring.baseUrl}/v1/runs`, {
    method: "POST",
    headers: { "content-type": "application/json", connection: "close" },
    body: JSON.stringify(START_BODY),
  });
  await pollUntilStatus(expiring, EXECUTION_ID, "awaiting_input");
  expiring.clock.ms = FIXED_NOW_MS + DAY_MS + 1000;
  await record("runs.monitor.expired", {
    harness: expiring,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}`,
  });
}

async function captureFilesCases(): Promise<void> {
  const base = mkdtempSync(join(tmpdir(), "wire-contract-files-"));
  const artifactsRoot = join(base, "artifacts");
  const traceRoot = join(base, "traces");
  const workspacePath = join(base, "workspace");
  mkdirSync(join(workspacePath, "out"), { recursive: true });
  writeFileSync(join(workspacePath, "out", "report.md"), "# report\n");
  publishRunArtifacts({ artifactsRoot, executionId: EXECUTION_ID, attempt: 1, workspacePath });
  mkdirSync(join(traceRoot, EXECUTION_ID, "attempt-1"), { recursive: true });
  writeFileSync(join(traceRoot, EXECUTION_ID, "attempt-1", "trace.ndjson"), '{"type":"envelope"}\n');

  const harness = await launch({
    runReportFn: neverSettlingRun(),
    runFiles: createRunFilesView({ artifactsRoot, traceRoot, removeTree: removeOwnTree }),
  });
  await fetch(`${harness.baseUrl}/v1/runs`, {
    method: "POST",
    headers: { "content-type": "application/json", connection: "close" },
    body: JSON.stringify(START_BODY),
  });

  await record("runs.files.listing", { harness, method: "GET", path: `/v1/runs/${EXECUTION_ID}/files` });
  await record("runs.files.artifact", {
    harness,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}/files/out/report.md`,
  });
  await record("runs.files.path_not_found", {
    harness,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}/files/out/absent.md`,
  });
  await record("runs.files.run_not_found", { harness, method: "GET", path: "/v1/runs/no-such-run/files" });
}

async function captureCheckpointCases(): Promise<void> {
  // A real on-disk store, because the route serves an open descriptor. The bytes are plain text
  // rather than a real bundle: the fixture pins the wire shape, and a gzip payload would make it
  // unreadable without proving anything more.
  const store = createLocalCheckpointStore(
    join(mkdtempSync(join(tmpdir(), "wire-contract-bundle-")), "store"),
  );
  const bundle = sealCheckpoint(new TextEncoder().encode("the finished session bundle"));
  await store.save(terminalCheckpointKeyOf(EXECUTION_ID, 1), bundle);

  // Settles, because the two cases below are either side of `complete`, which a running run refuses.
  const harness = await launch({ checkpointStore: store });
  await fetch(`${harness.baseUrl}/v1/runs`, {
    method: "POST",
    headers: { "content-type": "application/json", connection: "close" },
    body: JSON.stringify(START_BODY),
  });
  await pollUntilStatus(harness, EXECUTION_ID, "succeeded");

  await record("runs.checkpoint.bundle", {
    harness,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}/checkpoint`,
  });
  // The moment custody hands over: completing the run purges the runtime's copy, and every later
  // fetch says so rather than 404ing as if the bundle had never existed.
  await fetch(`${harness.baseUrl}/v1/runs/${EXECUTION_ID}/complete`, {
    method: "POST",
    headers: { connection: "close" },
  });
  await record("runs.checkpoint.completed_gone", {
    harness,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}/checkpoint`,
  });

  const empty = await launch({ runReportFn: neverSettlingRun() });
  await fetch(`${empty.baseUrl}/v1/runs`, {
    method: "POST",
    headers: { "content-type": "application/json", connection: "close" },
    body: JSON.stringify(START_BODY),
  });
  await record("runs.checkpoint.absent", {
    harness: empty,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}/checkpoint`,
  });
}

async function captureRefusalCases(): Promise<void> {
  const authed = await launch({ config: fixtureConfig({ auth: { token: AUTH_TOKEN } }) });
  await record("auth.unauthorized", { harness: authed, method: "GET", path: `/v1/runs/${EXECUTION_ID}` });

  const shedding = await launch({
    runReportFn: neverSettlingRun(),
    admission: createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 0, random: () => 0 }),
  });
  await record("runs.start.capacity_admission_shed", {
    harness: shedding,
    method: "POST",
    path: "/v1/runs",
    body: START_BODY,
  });

  const chatDisabled = await launch({
    runReportFn: neverSettlingRun(),
    admission: createAdmissionQueue(
      { poolSize: 32, chatReservedSlots: 0, chatMaxSlots: 0 },
      { freeSlots: () => 32, random: () => 0 },
    ),
  });
  await record("runs.start.capacity_chat_disabled", {
    harness: chatDisabled,
    method: "POST",
    path: "/v1/runs",
    body: { ...START_BODY, admissionClass: "chat" },
  });

  const draining = await launch({ runReportFn: neverSettlingRun() });
  draining.server.drainRuns();
  await record("runs.start.shutting_down", {
    harness: draining,
    method: "POST",
    path: "/v1/runs",
    body: START_BODY,
  });

  const persistFailure = await launch({ documentStore: memoryDocumentStore({ failSave: true }) });
  await record("runs.start.internal_error", {
    harness: persistFailure,
    method: "POST",
    path: "/v1/runs",
    body: {
      ...START_BODY,
      documents: [{ name: "mandate.txt", contentBase64: Buffer.from("hello").toString("base64") }],
    },
  });
}

async function captureScaledCapCases(): Promise<void> {
  const bodyLimitBytes = 1024;
  const tinyBody = await launch({ config: fixtureConfig({ bodyLimitBytes }) });
  await record("runs.start.body_too_large", {
    harness: tinyBody,
    method: "POST",
    path: "/v1/runs",
    body: { ...START_BODY, prompt: "x".repeat(2048) },
    hideRequestBody: true,
    configOverrides: { bodyLimitBytes },
  });

  const documentBudgetBytes = 4;
  const tinyDocuments = await launch({ config: fixtureConfig({ documentBudgetBytes }) });
  await record("runs.start.document_budget_exceeded", {
    harness: tinyDocuments,
    method: "POST",
    path: "/v1/runs",
    body: {
      ...START_BODY,
      documents: [{ name: "mandate.txt", contentBase64: Buffer.from("hello").toString("base64") }],
    },
    configOverrides: { documentBudgetBytes },
  });

  const uploadBudgetBytes = 8;
  const tinyUploads = await launch({
    runReportFn: neverSettlingRun(),
    config: fixtureConfig({ inflightUploadBudgetBytes: uploadBudgetBytes }),
  });
  await record("runs.start.capacity_upload_budget", {
    harness: tinyUploads,
    method: "POST",
    path: "/v1/runs",
    body: START_BODY,
    configOverrides: { inflightUploadBudgetBytes: uploadBudgetBytes },
  });

  // Wide enough to admit a start body, far under one listing's resident reservation, so the
  // run this refusal is about actually exists.
  const listingBudgetBytes = 64 * 1024;
  const tinyListings = await launch({
    runReportFn: neverSettlingRun(),
    config: fixtureConfig({ inflightUploadBudgetBytes: listingBudgetBytes }),
  });
  await fetch(`${tinyListings.baseUrl}/v1/runs`, {
    method: "POST",
    headers: { "content-type": "application/json", connection: "close" },
    body: JSON.stringify(START_BODY),
  });
  await record("runs.files.capacity_files_listing", {
    harness: tinyListings,
    method: "GET",
    path: `/v1/runs/${EXECUTION_ID}/files`,
    configOverrides: { inflightUploadBudgetBytes: listingBudgetBytes },
  });

  const retainedInputBudgetBytes = 1;
  const tinyRetainedInput = await launch({
    runReportFn: neverSettlingRun(),
    config: fixtureConfig({ retainedInputBudgetBytes }),
  });
  await record("runs.start.capacity_retained_input_budget", {
    harness: tinyRetainedInput,
    method: "POST",
    path: "/v1/runs",
    body: {
      ...START_BODY,
      documents: [{ name: "mandate.txt", contentBase64: Buffer.from("hello").toString("base64") }],
    },
    configOverrides: { retainedInputBudgetBytes },
  });

  const retainedOutputBudgetBytes = 1;
  const tinyRetainedOutput = await launch({
    runReportFn: neverSettlingRun(),
    config: fixtureConfig({ retainedOutputBudgetBytes }),
  });
  await record("runs.start.capacity_retained_output_budget", {
    harness: tinyRetainedOutput,
    method: "POST",
    path: "/v1/runs",
    body: START_BODY,
    configOverrides: { retainedOutputBudgetBytes },
  });
}

function sortedExchanges(): unknown {
  const sorted = Object.fromEntries([...exchanges.entries()].sort(([a], [b]) => a.localeCompare(b)));
  return normalizeWireValue(sorted, mintedTokens);
}

const SUBTYPE_LITERAL_PATTERN = /subtype:\s*"([a-z_]+)"/g;

function subtypeLiteralsInSources(): Set<string> {
  const sourceRoot = join(import.meta.dir, "..");
  const found = new Set<string>();
  const walk = (dir: string): void => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) {
        walk(path);
        continue;
      }
      if (!entry.name.endsWith(".ts") || entry.name.endsWith(".test.ts")) {
        continue;
      }
      for (const match of readFileSync(path, "utf8").matchAll(SUBTYPE_LITERAL_PATTERN)) {
        const subtype = match[1];
        if (subtype !== undefined) {
          found.add(subtype);
        }
      }
    }
  };
  walk(sourceRoot);
  return found;
}

describe("the published HTTP wire contract", () => {
  it("matches the fixtures generated from the real handlers", async () => {
    try {
      await captureLifecycleCases();
      await captureValidationCases();
      await captureContinuationCases();
      await captureTerminalCases();
      await capturePauseCases();
      await captureFilesCases();
      await captureCheckpointCases();
      await captureRefusalCases();
      await captureScaledCapCases();
    } finally {
      await closeServers();
    }

    const generated = sortedExchanges();
    const statuses = [...new Set([...exchanges.values()].map((exchange) => exchange.response.status))];
    const constraints = buildWireConstraints(statuses);

    if (process.env[UPDATE_FIXTURES_ENV] === "1") {
      writeFileSync(WIRE_FIXTURE_PATH, renderContractJson(generated));
      writeFileSync(WIRE_CONSTRAINTS_PATH, renderContractJson(constraints));
      throw new Error(
        `${UPDATE_FIXTURES_ENV}=1 rewrote the wire contract artifacts — review the diff and re-run without it`,
      );
    }

    expect(existsSync(WIRE_FIXTURE_PATH)).toBe(true);
    expect(existsSync(WIRE_CONSTRAINTS_PATH)).toBe(true);
    expect(generated).toEqual(JSON.parse(readFileSync(WIRE_FIXTURE_PATH, "utf8")));
    expect(constraints).toEqual(JSON.parse(readFileSync(WIRE_CONSTRAINTS_PATH, "utf8")));
  }, 120_000);

  // Without this, the fixture is complete only by accident: a new conflict code or capacity
  // reason would join the constraints file, answer on the wire, and never gain a recorded body.
  it("records a real response for every member of every closed wire vocabulary", () => {
    const fixture: unknown = JSON.parse(readFileSync(WIRE_FIXTURE_PATH, "utf8"));
    const valuesAtKey = (key: string): Set<string> => {
      const found = new Set<string>();
      const walk = (value: unknown): void => {
        if (Array.isArray(value)) {
          for (const entry of value) {
            walk(entry);
          }
          return;
        }
        if (typeof value !== "object" || value === null) {
          return;
        }
        for (const [name, entry] of Object.entries(value)) {
          if (name === key && typeof entry === "string") {
            found.add(entry);
          }
          walk(entry);
        }
      };
      walk(fixture);
      return found;
    };
    const missing = (published: readonly string[], recorded: Set<string>): string[] =>
      published.filter((member) => !recorded.has(member)).sort();

    expect(missing(RUN_STATUSES, valuesAtKey("status"))).toEqual([]);
    expect(missing(CONFLICT_CODES, valuesAtKey("code"))).toEqual([]);
    expect(
      missing(
        CAPACITY_REASONS.filter((reason) => !UNRECORDED_CAPACITY_REASONS.includes(reason)),
        valuesAtKey("reason"),
      ),
    ).toEqual([]);
  });

  // The published subtype list is only worth diffing if it is exhaustive, and the union it
  // derives from is deliberately open, so nothing but a source sweep can prove that.
  it("publishes every failure subtype the sources actually mint", () => {
    const published = new Set<string>(RUNTIME_FAILURE_SUBTYPES);
    const inSources = subtypeLiteralsInSources();
    expect(inSources.size).toBeGreaterThan(0);
    expect([...inSources].filter((subtype) => !published.has(subtype)).sort()).toEqual([]);
    expect([...published].filter((subtype) => !inSources.has(subtype)).sort()).toEqual([]);
  });
});
