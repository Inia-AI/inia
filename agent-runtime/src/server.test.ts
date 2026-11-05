import { afterEach, describe, expect, it } from "bun:test";
import {
  appendFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import type { Server } from "node:http";
import { connect, Socket } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Writable } from "node:stream";
import {
  type AdmissionClass,
  type AdmissionQueue,
  COLD_START_RUN_DURATION_MS,
  createAdmissionQueue,
} from "./admission-queue.js";
import {
  type CheckpointBundle,
  type CheckpointStore,
  createLocalCheckpointStore,
  sealCheckpoint,
  terminalCheckpointKeyOf,
} from "./checkpoint-store.js";
import { NO_SERVED_FILES } from "./checkpoint-store-test-helpers.js";
import {
  DEFAULT_CHAT_BORROW_HEADROOM_SLOTS,
  DEFAULT_CHAT_MAX_SLOTS,
  DEFAULT_CHAT_RESERVED_SLOTS,
  DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
  FILES_LISTING_RESIDENT_BYTES,
  MAX_ANSWER_CHARS,
  MAX_FILES_LISTING_BYTES,
  MAX_OUTPUT_FORMAT_CHARS,
  MAX_RETAINED_OUTPUT_BYTES,
  MAX_SYSTEM_PROMPT_CHARS,
  RETAINED_OUTPUT_WORST_CASE_BYTES,
  type ServerConfig,
  UID_POOL_BASE,
  UID_POOL_SIZE,
} from "./config.js";
import type { DocumentStore } from "./document-store.js";
import { composeSystemPrompt, RUNTIME_ENVIRONMENT_SECTION } from "./environment-section.js";
import { openSizedFile } from "./fs-read.js";
import { sha256Hex } from "./hash.js";
import type { LogEventName, Logger } from "./logger.js";
import type { Metrics } from "./metrics.js";
import type { StagedInput } from "./run-documents.js";
import {
  createRunFilesView,
  publishRunArtifacts,
  type RunFileEntry,
  type RunFilesView,
} from "./run-files.js";
import { CHECKPOINT_SHA256_HEADER } from "./run-files-routes.js";
import { removeOwnTree } from "./run-files-test-helpers.js";
import {
  type PauseRequest,
  type PauseResolution,
  RunFailedError,
  type RunReportFn,
  type RunReportParams,
  type RunReportResult,
} from "./run-report.js";
import {
  type ChildHandle,
  type ChildRun,
  createRunSpawner,
  type SpawnRunFn,
  type SpawnRunOptions,
} from "./run-spawner.js";
import {
  bodyReadBudgetMs,
  monitorPollHeaders,
  type RunApiServer,
  type ServerDeps,
  socketAbandonment,
  startServer,
} from "./server.js";
import { createUidPool } from "./uid-pool.js";

// retainedBytes derives from the same list the double supplies, so a stub can never disagree with itself.
function stubRunFilesView(overrides: Partial<RunFilesView> = {}): RunFilesView {
  const list = overrides.list ?? ((): Promise<RunFileEntry[]> => Promise.resolve([]));
  const bytesOf = (files: RunFileEntry[], kind: string): number =>
    files.filter((file) => file.kind === kind).reduce((total, file) => total + file.sizeBytes, 0);
  return {
    resolve: () => Promise.resolve(undefined),
    purge: () => Promise.resolve(),
    purgeArtifacts: () => Promise.resolve(),
    retainedBytes: async (executionId, attempt) => {
      const files = await list(executionId, attempt);
      return { artifactBytes: bytesOf(files, "artifact"), traceBytes: bytesOf(files, "trace") };
    },
    ...overrides,
    list,
  };
}

function toRunFileEntries(entries: { path: string; sizeBytes: number }[]): RunFileEntry[] {
  return entries.map((entry) => ({
    ...entry,
    sha256: "0".repeat(64),
    kind: entry.path.startsWith("trace/") ? "trace" : "artifact",
  }));
}

function entriesRunFilesView(entries: { path: string; sizeBytes: number }[]): RunFilesView {
  return stubRunFilesView({ list: () => Promise.resolve(toRunFileEntries(entries)) });
}

async function retryUntilAdmitted(baseUrl: string, body: string): Promise<Response> {
  let response = await postRuns(baseUrl, body);
  for (let attempt = 0; attempt < 100 && response.status !== 202; attempt += 1) {
    await new Promise((resolve) => setTimeout(resolve, 10));
    response = await postRuns(baseUrl, body);
  }
  return response;
}

const servers: Server[] = [];
const TEST_STATE_ROOT = mkdtempSync(join(tmpdir(), "server-test-"));

afterEach(async () => {
  await Promise.all(
    servers.splice(0).map(
      (server) =>
        new Promise<void>((resolve) => {
          server.close(() => resolve());
          server.closeAllConnections();
        }),
    ),
  );
});

function testConfig(overrides: Partial<ServerConfig> = {}): ServerConfig {
  return {
    port: 0,
    logLevel: "info",
    auth: { disabled: true },
    modelProvider: { provider: "bedrock", identity: { ambient: true } },
    bodyLimitBytes: 1024,
    // Mirrors production: the in-flight upload budget is exactly one maximum body.
    inflightUploadBudgetBytes: 384 * 1024 * 1024,
    retainedInputBudgetBytes: 64 * 1024 * 1024,
    retainedOutputBudgetBytes: Number.MAX_SAFE_INTEGER,
    documentBudgetBytes: 64 * 1024,
    requestPhaseTimeoutMs: 1000,
    fuses: {
      deadlineMs: { defaultValue: 5000, ceiling: 5000 },
      maxBudgetUsd: { defaultValue: 1, ceiling: 1 },
      maxTurns: { defaultValue: 10, ceiling: 10 },
      maxRetainedOutputBytes: {
        defaultValue: DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
        ceiling: RETAINED_OUTPUT_WORST_CASE_BYTES,
      },
    },
    pauseGraceMinutes: 0,
    admissionClasses: {
      chatReservedSlots: DEFAULT_CHAT_RESERVED_SLOTS,
      chatMaxSlots: DEFAULT_CHAT_MAX_SLOTS,
      chatBorrowHeadroomSlots: DEFAULT_CHAT_BORROW_HEADROOM_SLOTS,
    },
    checkpointRoot: join(TEST_STATE_ROOT, "checkpoints"),
    documentRoot: join(TEST_STATE_ROOT, "documents"),
    subprocessEnv: { PATH: "/usr/bin" },
    allowedModels: ["default-pin"],
    pinnedModel: "default-pin",
    buildIdentity: { version: "0.0.0-test", revision: "test-revision" },
    ...overrides,
  };
}

const stubResult: RunReportResult = {
  executionId: "e-1",
  output: "report",
  estimatedCostUsd: 0.1,
  sessionId: "s-1",
  usage: {
    inputTokens: 0,
    outputTokens: 0,
    cacheCreationInputTokens: 0,
    cacheReadInputTokens: 0,
  },
};

const stubResultView = { output: "report", sessionId: "s-1", structuredOutput: null };

const QUESTION = {
  id: "q-1",
  toolName: "mcp__platform__request_analyst_input",
  prompt: "Which fund?",
  input: { question: "Which fund?" },
};

const DAY_MS = 24 * 60 * 60 * 1000;

const LONG_RUN_MS = 900_000;

const PAUSE_CHECKPOINT = sealCheckpoint(new TextEncoder().encode("serialized session state"));

function pauseRequest(): PauseRequest {
  return {
    question: QUESTION,
    sessionId: "s-pause",
    estimatedCostUsd: 0.05,
    checkpoint: PAUSE_CHECKPOINT,
  };
}

type StoreLog = {
  saves: string[];
  deletes: string[];
  releaseSaves: () => void;
};

function memoryStore(options: { failSave?: boolean; holdSaves?: boolean } = {}): CheckpointStore & StoreLog {
  const bundles = new Map<string, CheckpointBundle>();
  const saves: string[] = [];
  const deletes: string[] = [];
  const pendingSaves: (() => void)[] = [];
  const keyOf = (key: { executionId: string; attempt: number; segment: number }): string =>
    `${key.executionId}.${key.attempt}.${key.segment}`;
  return {
    saves,
    deletes,
    releaseSaves() {
      for (const complete of pendingSaves.splice(0)) {
        complete();
      }
    },
    ...NO_SERVED_FILES,
    save(key, bundle) {
      if (options.failSave === true) {
        return Promise.reject(new Error("save refused"));
      }
      saves.push(keyOf(key));
      if (options.holdSaves === true) {
        return new Promise((resolve) => {
          pendingSaves.push(() => {
            bundles.set(keyOf(key), bundle);
            resolve();
          });
        });
      }
      bundles.set(keyOf(key), bundle);
      return Promise.resolve();
    },
    load(key) {
      return Promise.resolve(bundles.get(keyOf(key)));
    },
    delete(key) {
      deletes.push(keyOf(key));
      bundles.delete(keyOf(key));
      return Promise.resolve();
    },
  };
}

type DocumentStoreLog = {
  saved: Map<string, StagedInput[]>;
  deletes: string[];
};

function memoryDocumentStore(
  options: { failSave?: boolean; holdSaves?: boolean } = {},
): DocumentStore & DocumentStoreLog & { releaseSaves: () => void; heldSaves: () => number } {
  const saved = new Map<string, StagedInput[]>();
  const deletes: string[] = [];
  const pendingSaves: (() => void)[] = [];
  return {
    saved,
    deletes,
    releaseSaves() {
      for (const complete of pendingSaves.splice(0)) {
        complete();
      }
    },
    heldSaves: () => pendingSaves.length,
    save(executionId, documents) {
      if (options.failSave === true) {
        return Promise.reject(new Error("document save refused"));
      }
      if (options.holdSaves === true) {
        return new Promise((resolve) => {
          pendingSaves.push(() => {
            saved.set(executionId, documents);
            resolve();
          });
        });
      }
      saved.set(executionId, documents);
      return Promise.resolve();
    },
    openInputs: () => Promise.resolve(undefined),
    delete(executionId) {
      deletes.push(executionId);
      saved.delete(executionId);
      return Promise.resolve();
    },
  };
}

type CapturedFields = Record<string, unknown>;

function eventsLogger(): {
  events: { level: string; event: string; fields: CapturedFields }[];
  logger: Logger;
} {
  const events: { level: string; event: string; fields: CapturedFields }[] = [];
  const boundLogger = (bound: CapturedFields): Logger => ({
    debug: (event, fields) => events.push({ level: "debug", event, fields: { ...bound, ...fields } }),
    info: (event, fields) => events.push({ level: "info", event, fields: { ...bound, ...fields } }),
    warn: (event, fields) => events.push({ level: "warn", event, fields: { ...bound, ...fields } }),
    error: (event, fields) => events.push({ level: "error", event, fields: { ...bound, ...fields } }),
    with: (fields) => boundLogger({ ...bound, ...fields }),
  });
  return { events, logger: boundLogger({}) };
}

async function launch(
  config: ServerConfig = testConfig(),
  runReportFn: SpawnRunFn = () => Promise.resolve(stubResult),
  extraDeps: Partial<Omit<ServerDeps, "runReportFn">> = {},
): Promise<{ server: RunApiServer; baseUrl: string }> {
  const server = startServer(config, {
    runReportFn,
    uidPool: createUidPool({ baseUid: UID_POOL_BASE, size: UID_POOL_SIZE }),
    ...extraDeps,
  });
  servers.push(server);
  await new Promise<void>((resolve) => server.once("listening", () => resolve()));
  const address = server.address();
  if (address === null || typeof address === "string") {
    throw new Error("server did not bind to a port");
  }
  return { server, baseUrl: `http://127.0.0.1:${address.port}` };
}

// Spread this last in header objects — a caller's own headers spread after it could re-enable pooling.
const NO_REUSE_HEADERS: Record<string, string> = { connection: "close" };

function postRuns(baseUrl: string, body: string, headers: Record<string, string> = {}): Promise<Response> {
  return fetch(`${baseUrl}/v1/runs`, {
    method: "POST",
    headers: { "content-type": "application/json", ...headers, ...NO_REUSE_HEADERS },
    body,
  });
}

function postInput(
  baseUrl: string,
  executionId: string,
  body: string,
  headers: Record<string, string> = {},
): Promise<Response> {
  return fetch(`${baseUrl}/v1/runs/${executionId}/input`, {
    method: "POST",
    headers: { "content-type": "application/json", ...headers, ...NO_REUSE_HEADERS },
    body,
  });
}

function asRecord(value: unknown): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error("expected a JSON object body");
  }
  return { ...value };
}

async function monitorOf(response: Response): Promise<Record<string, unknown>> {
  return asRecord(await response.json());
}

async function waitFor(condition: () => boolean): Promise<void> {
  for (let i = 0; i < 200; i += 1) {
    if (condition()) {
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  throw new Error("condition never became true");
}

async function waitForAsync(condition: () => Promise<boolean>): Promise<void> {
  for (let i = 0; i < 200; i += 1) {
    if (await condition()) {
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  throw new Error("condition never became true");
}

async function pollRunUntil(
  baseUrl: string,
  executionId: string,
  predicate: (body: Record<string, unknown>) => boolean,
): Promise<Record<string, unknown>> {
  for (let i = 0; i < 200; i += 1) {
    const response = await fetch(`${baseUrl}/v1/runs/${executionId}`);
    if (response.status === 200) {
      const body = await monitorOf(response);
      if (predicate(body)) {
        return body;
      }
    }
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  throw new Error(`run ${executionId} never satisfied the condition`);
}

function pollRunUntilStatus(
  baseUrl: string,
  executionId: string,
  status: string,
): Promise<Record<string, unknown>> {
  return pollRunUntil(baseUrl, executionId, (body) => body["status"] === status);
}

function signalAwareHangingFake(): RunReportFn {
  return (_params, options) =>
    new Promise((_, reject) => {
      options?.signal?.addEventListener("abort", () => reject(new Error("aborted by cancel")), {
        once: true,
      });
    });
}

describe("healthz", () => {
  it("responds ok with the build identity, including with probe query strings", async () => {
    const { baseUrl } = await launch(
      testConfig({ buildIdentity: { version: "0.2.4", revision: "45d7d7e" } }),
    );
    for (const path of ["/healthz", "/healthz?probe=1"]) {
      const response = await fetch(`${baseUrl}${path}`);
      expect(response.status).toBe(200);
      expect(await response.json()).toEqual({ status: "ok", version: "0.2.4", revision: "45d7d7e" });
    }
  });

  it("names the build identity of an unstamped image rather than omitting the field", async () => {
    const { baseUrl } = await launch(
      testConfig({ buildIdentity: { version: "unknown", revision: "unknown" } }),
    );
    const response = await fetch(`${baseUrl}/healthz`);
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ status: "ok", version: "unknown", revision: "unknown" });
  });

  it("rejects non-GET methods with 405 naming the allowed method", async () => {
    const { baseUrl } = await launch();
    const response = await fetch(`${baseUrl}/healthz`, { method: "POST" });
    expect(response.status).toBe(405);
    expect(response.headers.get("allow")).toBe("GET");
  });
});

describe("path versioning", () => {
  it("serves the run API only under /v1 — the unprefixed paths are a clean 404 break", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    const started = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(started.status).toBe(202);
    for (const unprefixed of ["/runs", "/runs/e-1", "/runs/e-1/cancel", "/v2/runs"]) {
      const method = unprefixed.endsWith("cancel") || unprefixed.endsWith("runs") ? "POST" : "GET";
      const response = await fetch(`${baseUrl}${unprefixed}`, {
        method,
        headers: { "content-type": "application/json", ...NO_REUSE_HEADERS },
        ...(method === "POST" ? { body: "{}" } : {}),
      });
      expect(response.status).toBe(404);
      expect(await response.json()).toEqual({ error: "not found" });
    }
    expect((await fetch(`${baseUrl}/v1/runs/e-1`)).status).toBe(200);
  });

  it("keeps /healthz unprefixed for infra probes", async () => {
    const { baseUrl } = await launch();
    expect((await fetch(`${baseUrl}/healthz`)).status).toBe(200);
    expect((await fetch(`${baseUrl}/v1/healthz`)).status).toBe(404);
  });
});

describe("run action lookup", () => {
  it("answers 404 for prototype-named actions — toString, constructor, and __proto__ are not routes", async () => {
    const { baseUrl } = await launch();
    for (const action of ["toString", "constructor", "__proto__", "hasOwnProperty", "valueOf"]) {
      const response = await fetch(`${baseUrl}/v1/runs/e-1/${action}`);
      expect(response.status).toBe(404);
      expect(await response.json()).toEqual({ error: "not found" });
    }
  });

  it("rejects a __proto__ body field as unknown before any key copying touches it", async () => {
    const { baseUrl } = await launch();
    const response = await postRuns(
      baseUrl,
      '{"executionId":"e-1","prompt":"p","__proto__":{"model":"smuggled"}}',
    );
    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ error: 'unknown field "__proto__"' });
  });
});

describe("starting runs", () => {
  it("accepts a new run with 202, a Retry-After hint, and a monitor body — no Operation-Location", async () => {
    const { baseUrl } = await launch();
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(response.status).toBe(202);
    expect(response.headers.get("operation-location")).toBeNull();
    expect(response.headers.get("retry-after")).toBe("2");
    expect(response.headers.get("connection")).toBe("close");
    const body = await monitorOf(response);
    expect(body["executionId"]).toBe("e-1");
    expect(body["status"]).toBe("running");
    expect(body["completed"]).toBe(false);
    expect(body["attempt"]).toBe(1);
    expect(body["segment"]).toBe(0);
    expect(body["error"]).toBeNull();
    expect(body["result"]).toBeNull();
  });

  it("answers a same-payload duplicate with 200 and the existing monitor", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    const startBody = JSON.stringify({ executionId: "e-1", prompt: "p" });
    expect((await postRuns(baseUrl, startBody)).status).toBe(202);
    const duplicate = await postRuns(baseUrl, startBody);
    expect(duplicate.status).toBe(200);
    expect(duplicate.headers.get("retry-after")).toBe("2");
    const body = await monitorOf(duplicate);
    expect(body["executionId"]).toBe("e-1");
    expect(body["status"]).toBe("running");
  });

  it("lets an identical retry re-run an executionId whose run failed for an infrastructure reason", async () => {
    const documentStore = memoryDocumentStore();
    let starts = 0;
    const { baseUrl } = await launch(
      testConfig(),
      () => {
        starts += 1;
        return starts === 1
          ? Promise.reject(
              new RunFailedError("all run uids are in use", {
                subtype: "uid_pool_exhausted",
                estimatedCostUsd: 0,
                sessionId: null,
              }),
            )
          : Promise.resolve(stubResult);
      },
      { documentStore },
    );
    const startBody = JSON.stringify({
      executionId: "e-1",
      prompt: "p",
      documents: [{ name: "a.txt", contentBase64: Buffer.from("hello").toString("base64") }],
    });
    expect((await postRuns(baseUrl, startBody)).status).toBe(202);
    const failed = await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect(failed["error"]).toEqual({ subtype: "uid_pool_exhausted" });
    await waitFor(() => documentStore.deletes.includes("e-1"));
    const retry = await postRuns(baseUrl, startBody);
    expect(retry.status).toBe(202);
    expect((await pollRunUntilStatus(baseUrl, "e-1", "succeeded"))["status"]).toBe("succeeded");
    expect(starts).toBe(2);
  });

  it("keeps a model-side failure terminal for its executionId", async () => {
    const { baseUrl } = await launch(testConfig(), () =>
      Promise.reject(
        new RunFailedError("the model gave up", {
          subtype: "max_turns_exceeded",
          estimatedCostUsd: 0.4,
          sessionId: "s-1",
        }),
      ),
    );
    const startBody = JSON.stringify({ executionId: "e-1", prompt: "p" });
    expect((await postRuns(baseUrl, startBody)).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "failed");
    const retry = await postRuns(baseUrl, startBody);
    expect(retry.status).toBe(200);
    expect((await monitorOf(retry))["status"]).toBe("failed");
  });

  it("rejects a same-executionId different-payload start with a machine-readable 409", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const conflict = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "different" }));
    expect(conflict.status).toBe(409);
    expect(await conflict.json()).toEqual({ code: "payload_conflict" });
  });

  it("hands a run without an MCP URL only the spawner-honoured seams — the runToken stays behind", async () => {
    const seen: { params?: RunReportParams; options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig(), (params, options) => {
      seen.params = params;
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-9", prompt: "p", runToken: "secret-run-token" }),
    );
    expect(response.status).toBe(202);
    expect(seen.params).toEqual({ executionId: "e-9", prompt: "p" });
    expect(Object.keys(seen.params ?? {})).not.toContain("runToken");
    expect(Object.keys(seen.options ?? {}).sort()).toEqual(["onTerminalCheckpoint", "pause", "signal"]);
    expect(seen.options?.signal).toBeDefined();
    expect(typeof seen.options?.pause).toBe("function");
  });

  it("hands the MCP URL and the runToken to the run together — the token is the MCP credential", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig(), (_params, options) => {
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({
        executionId: "e-9",
        prompt: "p",
        runToken: "secret-run-token",
        mcpServerUrl: "https://platform.internal/mcp",
      }),
    );
    expect(response.status).toBe(202);
    expect(seen.options?.mcpServerUrl).toBe("https://platform.internal/mcp");
    expect(seen.options?.runToken).toBe("secret-run-token");
    const monitor = await monitorOf(await fetch(`${baseUrl}/v1/runs/e-9`));
    expect(monitor["mcpServerUrl"]).toBe("https://platform.internal/mcp");
    expect(Object.keys(monitor)).not.toContain("runToken");
  });

  it("folds the MCP URL into the payload identity and validates it before any run starts", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    const startBody = JSON.stringify({
      executionId: "e-1",
      prompt: "p",
      mcpServerUrl: "https://platform.internal/mcp",
    });
    expect((await postRuns(baseUrl, startBody)).status).toBe(202);
    expect((await postRuns(baseUrl, startBody)).status).toBe(200);
    const conflicting = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", mcpServerUrl: "https://other.internal/mcp" }),
    );
    expect(conflicting.status).toBe(409);
    for (const mcpServerUrl of [7, "", "not-a-url", "ftp://host/mcp"]) {
      const bad = await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p", mcpServerUrl }));
      expect(bad.status).toBe(400);
    }
  });

  it("folds the agents declaration into the payload identity", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    const agentsBody = (prompt: string): string =>
      JSON.stringify({
        executionId: "e-1",
        prompt: "p",
        agents: [{ name: "verifier", description: "d", prompt }],
      });
    expect((await postRuns(baseUrl, agentsBody("verify"))).status).toBe(202);
    expect((await postRuns(baseUrl, agentsBody("verify"))).status).toBe(200);
    expect((await postRuns(baseUrl, agentsBody("verify differently"))).status).toBe(409);
    const undeclared = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(undeclared.status).toBe(409);
  });

  it("rejects non-POST methods with 405 naming the allowed method", async () => {
    const { baseUrl } = await launch();
    const response = await fetch(`${baseUrl}/v1/runs`);
    expect(response.status).toBe(405);
    expect(response.headers.get("allow")).toBe("POST");
  });

  it("hands the requested outputFormat to the run and folds it into the payload identity", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig(), (_params, options) => {
      seen.options = options;
      return new Promise(() => {});
    });
    const outputFormat = { type: "json_schema", schema: { type: "object" } };
    const startBody = JSON.stringify({ executionId: "e-1", prompt: "p", outputFormat });
    expect((await postRuns(baseUrl, startBody)).status).toBe(202);
    expect(seen.options?.outputFormat).toEqual({ type: "json_schema", schema: { type: "object" } });
    expect((await postRuns(baseUrl, startBody)).status).toBe(200);
    const conflicting = await postRuns(
      baseUrl,
      JSON.stringify({
        executionId: "e-1",
        prompt: "p",
        outputFormat: { type: "json_schema", schema: { type: "array" } },
      }),
    );
    expect(conflicting.status).toBe(409);
    const unformatted = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(unformatted.status).toBe(409);
  });

  it("rejects a malformed outputFormat with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    for (const outputFormat of [7, "json_schema", { type: "json_schema" }, { type: "text", schema: {} }]) {
      const response = await postRuns(
        baseUrl,
        JSON.stringify({ executionId: "e-1", prompt: "p", outputFormat }),
      );
      expect(response.status).toBe(400);
    }
    expect(called).toBe(false);
  });

  it("rejects an outputFormat schema over the serialized size cap with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 2 * 1024 * 1024 }), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    const outputFormat = {
      type: "json_schema",
      schema: { description: "x".repeat(MAX_OUTPUT_FORMAT_CHARS) },
    };
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", outputFormat }),
    );
    expect(response.status).toBe(400);
    const message = asRecord(await response.json())["error"];
    if (typeof message !== "string") {
      throw new Error("expected an error message");
    }
    expect(message).toContain(String(MAX_OUTPUT_FORMAT_CHARS));
    expect(called).toBe(false);
    expect((await fetch(`${baseUrl}/v1/runs/e-1`)).status).toBe(404);
  });

  it("hands the requested systemPrompt to the run and folds it into the payload identity", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig(), (_params, options) => {
      seen.options = options;
      return new Promise(() => {});
    });
    const systemPrompt = ["static role and rules", "per-pass context"];
    const startBody = JSON.stringify({ executionId: "e-1", prompt: "p", systemPrompt });
    expect((await postRuns(baseUrl, startBody)).status).toBe(202);
    expect(seen.options?.systemPrompt).toEqual(systemPrompt);
    expect((await postRuns(baseUrl, startBody)).status).toBe(200);
    const conflicting = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", systemPrompt: ["different rules"] }),
    );
    expect(conflicting.status).toBe(409);
    const unprompted = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(unprompted.status).toBe(409);
  });

  it("keeps the caller-supplied systemPrompt off the monitor view", async () => {
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult));
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", systemPrompt: "you are an analyst" }),
    );
    expect(response.status).toBe(202);
    expect(await monitorOf(response)).not.toHaveProperty("systemPrompt");
    const terminal = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(terminal).not.toHaveProperty("systemPrompt");
  });

  it("rejects a malformed systemPrompt with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    for (const systemPrompt of [7, "", [], ["ok", 7], { type: "preset", preset: "claude_code" }]) {
      const response = await postRuns(
        baseUrl,
        JSON.stringify({ executionId: "e-1", prompt: "p", systemPrompt }),
      );
      expect(response.status).toBe(400);
    }
    expect(called).toBe(false);
  });

  it("rejects a systemPrompt over the serialized size cap with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 8 * 1024 * 1024 }), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({
        executionId: "e-1",
        prompt: "p",
        systemPrompt: "x".repeat(MAX_SYSTEM_PROMPT_CHARS),
      }),
    );
    expect(response.status).toBe(400);
    const message = asRecord(await response.json())["error"];
    if (typeof message !== "string") {
      throw new Error("expected an error message");
    }
    expect(message).toContain(String(MAX_SYSTEM_PROMPT_CHARS));
    expect(called).toBe(false);
    expect((await fetch(`${baseUrl}/v1/runs/e-1`)).status).toBe(404);
  });

  it("serializes a run started with outputFormat under structuredOutput, never the SDK spelling", async () => {
    const { baseUrl } = await launch(testConfig(), () =>
      Promise.resolve({ ...stubResult, structuredOutput: { verdict: "pass", notes: ["ok"] } }),
    );
    await postRuns(
      baseUrl,
      JSON.stringify({
        executionId: "e-1",
        prompt: "p",
        outputFormat: { type: "json_schema", schema: { type: "object" } },
      }),
    );
    const body = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(asRecord(body["result"])["structuredOutput"]).toEqual({ verdict: "pass", notes: ["ok"] });
    expect(body["result"]).not.toHaveProperty("structured_output");
  });

  it("reports structuredOutput as null for a run started without outputFormat", async () => {
    const { baseUrl } = await launch();
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const body = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(body["result"]).toEqual({ ...stubResultView, structuredOutput: null });
  });

  it("returns 400 with the message for a validation failure", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "", prompt: "p" }));
    expect(response.status).toBe(400);
    const body = await response.json();
    expect(body).toEqual({ error: "executionId must be a non-empty string" });
    expect(called).toBe(false);
  });

  it("rejects unknown fields and malformed runTokens with 400", async () => {
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 64 * 1024 }));
    for (const body of [
      { executionId: "e", prompt: "p", promt: "typo" },
      { executionId: "e", prompt: "p", runToken: "" },
      { executionId: "e", prompt: "p", runToken: 7 },
      // The token lands in an Authorization header value: CR/LF, spaces, non-ASCII, unbounded length.
      { executionId: "e", prompt: "p", runToken: "tok\n" },
      { executionId: "e", prompt: "p", runToken: "tok\r\nx-injected: y" },
      { executionId: "e", prompt: "p", runToken: "tok en" },
      { executionId: "e", prompt: "p", runToken: "töken" },
      { executionId: "e", prompt: "p", runToken: "a".repeat(4097) },
    ]) {
      const response = await postRuns(baseUrl, JSON.stringify(body));
      expect(response.status).toBe(400);
    }
  });

  it("rejects executionIds outside the safe charset with 400 naming the rule", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    for (const executionId of ["tenant/1", "a b", "a%2Fb", "x".repeat(129), "ünïcode", "a\n"]) {
      const response = await postRuns(baseUrl, JSON.stringify({ executionId, prompt: "p" }));
      expect(response.status).toBe(400);
      const message = asRecord(await response.json())["error"];
      if (typeof message !== "string") {
        throw new Error("expected an error message");
      }
      expect(message).toContain("A-Za-z0-9");
    }
    expect(called).toBe(false);
  });

  it("returns 400 for a JSON null body", async () => {
    const { baseUrl } = await launch();
    const response = await postRuns(baseUrl, "null");
    expect(response.status).toBe(400);
  });

  it("returns 400 with a generic message for a malformed JSON body", async () => {
    const { baseUrl } = await launch();
    const response = await postRuns(baseUrl, "{not json");
    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ error: "invalid JSON body" });
  });

  it("returns 415 when the content type is not JSON", async () => {
    const { baseUrl } = await launch();
    const response = await fetch(`${baseUrl}/v1/runs`, {
      method: "POST",
      headers: { "content-type": "text/plain", ...NO_REUSE_HEADERS },
      body: "hello",
    });
    expect(response.status).toBe(415);
  });

  it("returns 413 when the body exceeds the limit", async () => {
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 64 }));
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e", prompt: "x".repeat(200) }));
    expect(response.status).toBe(413);
  });

  it("returns 413 for an oversized chunked body that declares no length", async () => {
    const { server } = await launch(testConfig({ bodyLimitBytes: 64 }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const rawResponse = await new Promise<string>((resolve, reject) => {
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write(
          "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
            "transfer-encoding: chunked\r\n\r\n",
        );
        const piece = "x".repeat(50);
        for (let i = 0; i < 3; i += 1) {
          socket.write(`${piece.length.toString(16)}\r\n${piece}\r\n`);
        }
        socket.write("0\r\n\r\n");
      });
      let data = "";
      socket.on("data", (chunk) => {
        data += chunk.toString("utf8");
        if (data.includes("\r\n\r\n")) {
          socket.destroy();
          resolve(data);
        }
      });
      socket.on("close", () => resolve(data));
      socket.on("error", reject);
      setTimeout(() => reject(new Error("no response to chunked request")), 3000);
    });
    expect(rawResponse).toContain("413");
  });
});

function docOf(
  text: string,
  name = "report.pdf",
  extra: Record<string, unknown> = {},
): Record<string, unknown> {
  return { name, contentBase64: Buffer.from(text).toString("base64"), ...extra };
}

function startBodyWith(documents: unknown[]): string {
  return JSON.stringify({ executionId: "e-1", prompt: "p", documents });
}

describe("starting runs with documents", () => {
  it("accepts a start request carrying documents with 202", async () => {
    const { baseUrl } = await launch();
    const response = await postRuns(
      baseUrl,
      startBodyWith([docOf("hello", "report.pdf", { mediaType: "application/pdf" })]),
    );
    expect(response.status).toBe(202);
    expect((await monitorOf(response))["status"]).toBe("running");
  });

  it("rejects malformed documents with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    for (const documents of [
      "not-an-array",
      [docOf("x", "../escape")],
      [docOf("x", "a.pdf"), docOf("y", "a.pdf")],
      [{ name: "a.pdf", contentBase64: "not base64!!" }],
      [docOf("x", "a.pdf", { sha256: "0".repeat(64) })],
      [docOf("x", "a.pdf", { unexpected: true })],
    ]) {
      const response = await postRuns(
        baseUrl,
        JSON.stringify({ executionId: "e-1", prompt: "p", documents }),
      );
      expect(response.status).toBe(400);
    }
    expect(called).toBe(false);
  });

  it("rejects a document set over the decoded budget with 413 and a clear message", async () => {
    const { baseUrl } = await launch(testConfig({ documentBudgetBytes: 16 }));
    const response = await postRuns(baseUrl, startBodyWith([docOf("x".repeat(32))]));
    expect(response.status).toBe(413);
    const message = asRecord(await response.json())["error"];
    if (typeof message !== "string") {
      throw new Error("expected an error message");
    }
    expect(message).toContain("budget");
  });

  it("treats a repeat with different documents as a payload conflict", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    expect((await postRuns(baseUrl, startBodyWith([docOf("one")]))).status).toBe(202);
    const differentBytes = await postRuns(baseUrl, startBodyWith([docOf("two")]));
    expect(differentBytes.status).toBe(409);
    expect(await differentBytes.json()).toEqual({ code: "payload_conflict" });
    const differentName = await postRuns(baseUrl, startBodyWith([docOf("one", "other.pdf")]));
    expect(differentName.status).toBe(409);
    const missingDocs = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(missingDocs.status).toBe(409);
  });

  it("answers an identical repeat, documents included, with the existing monitor", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    const body = startBodyWith([docOf("same bytes")]);
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    const repeat = await postRuns(baseUrl, body);
    expect(repeat.status).toBe(200);
    expect((await monitorOf(repeat))["status"]).toBe("running");
  });

  it("persists the decoded documents before the run starts, and only for the first accepted start", async () => {
    const documentStore = memoryDocumentStore();
    let storedAtRunTime = false;
    const fake: RunReportFn = (params, options) => {
      storedAtRunTime = documentStore.saved.has("e-1");
      return signalAwareHangingFake()(params, options);
    };
    const { baseUrl } = await launch(testConfig(), fake, { documentStore });
    const body = startBodyWith([docOf("the body", "report.pdf", { documentClass: "AnnualReport" })]);
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    await waitFor(() => storedAtRunTime);
    const stored = documentStore.saved.get("e-1");
    expect(stored).toHaveLength(1);
    expect(stored?.[0]?.name).toBe("report.pdf");
    expect(stored?.[0]?.documentClass).toBe("AnnualReport");
    expect(Buffer.from(stored?.[0]?.bytes ?? new Uint8Array()).toString("utf8")).toBe("the body");
    await postRuns(baseUrl, body);
    expect(documentStore.saved.size).toBe(1);
  });

  it("saves nothing for a run that carries no documents", async () => {
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { documentStore });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(documentStore.saved.size).toBe(0);
  });

  it("persists the decoded documents for the spawner to stream — the run options carry no bytes", async () => {
    const documentStore = memoryDocumentStore();
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(
      testConfig(),
      (_params, options) => {
        seen.options = options;
        return Promise.resolve(stubResult);
      },
      { documentStore },
    );
    await postRuns(baseUrl, startBodyWith([docOf("threaded body")]));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const persisted = documentStore.saved.get("e-1");
    expect(persisted).toHaveLength(1);
    expect(Buffer.from(persisted?.[0]?.bytes ?? new Uint8Array()).toString("utf8")).toBe("threaded body");
    expect(Object.keys(seen.options ?? {})).not.toContain("documents");
    expect(Object.keys(seen.options ?? {})).not.toContain("skillFiles");
  });

  it("fails the run with 500 when the document store cannot persist the bytes", async () => {
    const documentStore = memoryDocumentStore({ failSave: true });
    let runStarted = false;
    const { baseUrl } = await launch(
      testConfig(),
      () => {
        runStarted = true;
        return Promise.resolve(stubResult);
      },
      { documentStore },
    );
    const response = await postRuns(baseUrl, startBodyWith([docOf("x")]));
    expect(response.status).toBe(500);
    expect(await response.json()).toEqual({ error: "internal error" });
    expect((await pollRunUntilStatus(baseUrl, "e-1", "failed"))["status"]).toBe("failed");
    expect(runStarted).toBe(false);
  });

  it("keeps stored documents when the run goes terminal — deletion waits for complete or the reap", async () => {
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(testConfig(), undefined, { documentStore });
    await postRuns(baseUrl, startBodyWith([docOf("retained")]));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(documentStore.saved.has("e-1")).toBe(true);
    expect(documentStore.deletes).toEqual([]);
  });
});

describe("starting runs with skill files", () => {
  function startBody(fields: Record<string, unknown>): string {
    return JSON.stringify({ executionId: "e-1", prompt: "p", ...fields });
  }

  it("accepts skillFiles with 202 and persists the decoded files for the spawner to stream", async () => {
    const documentStore = memoryDocumentStore();
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(
      testConfig(),
      (_params, options) => {
        seen.options = options;
        return Promise.resolve(stubResult);
      },
      { documentStore },
    );
    const response = await postRuns(baseUrl, startBody({ skillFiles: [docOf("# skill", "grading.md")] }));
    expect(response.status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const persisted = documentStore.saved.get("e-1");
    expect(persisted).toHaveLength(1);
    expect(persisted?.[0]?.name).toBe("grading.md");
    expect(persisted?.[0]?.area).toBe("skills");
    expect(Buffer.from(persisted?.[0]?.bytes ?? new Uint8Array()).toString("utf8")).toBe("# skill");
    expect(Object.keys(seen.options ?? {})).not.toContain("skillFiles");
  });

  it("accepts an agents declaration with 202 and threads it to the run", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig(), (_params, options) => {
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      startBody({
        skillFiles: [docOf("# skill", "grading.md")],
        agents: [{ name: "verifier", description: "checks", prompt: "verify", skills: ["grading.md"] }],
      }),
    );
    expect(response.status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(seen.options?.agents).toEqual([
      { name: "verifier", description: "checks", prompt: "verify", skills: ["skills:grading"] },
    ]);
  });

  it("rejects an agent referencing a skill the request does not carry, 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      startBody({
        agents: [{ name: "verifier", description: "d", prompt: "p", skills: ["missing.md"] }],
      }),
    );
    expect(response.status).toBe(400);
    expect(called).toBe(false);
  });

  it("rejects malformed agent declarations with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    for (const agents of [[{ name: "Bad Name", description: "d", prompt: "p" }], [], "verifier"]) {
      const response = await postRuns(baseUrl, startBody({ agents }));
      expect(response.status).toBe(400);
    }
    expect(called).toBe(false);
  });

  it("accepts skill assets with 202 and persists decoded asset bytes for the spawner to stream", async () => {
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(testConfig(), undefined, { documentStore });
    const response = await postRuns(
      baseUrl,
      startBody({
        skillFiles: [{ ...docOf("# skill", "grading.md"), assets: [docOf("print('check')", "verify.py")] }],
      }),
    );
    expect(response.status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const assets = documentStore.saved.get("e-1")?.[0]?.assets;
    expect(assets).toHaveLength(1);
    expect(assets?.[0]?.name).toBe("verify.py");
    expect(Buffer.from(assets?.[0]?.bytes ?? new Uint8Array()).toString("utf8")).toBe("print('check')");
  });

  it("rejects a traversal-shaped skill asset name with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      startBody({
        skillFiles: [{ ...docOf("# skill", "grading.md"), assets: [docOf("x", "../escape.py")] }],
      }),
    );
    expect(response.status).toBe(400);
    expect(called).toBe(false);
  });

  it("rejects skill files carrying document metadata with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    for (const skillFiles of [
      "not-an-array",
      [docOf("x", "a.md", { mediaType: "text/markdown" })],
      [docOf("x", "a.md", { documentClass: "Skill" })],
      [docOf("x", "../escape")],
    ]) {
      const response = await postRuns(baseUrl, startBody({ skillFiles }));
      expect(response.status).toBe(400);
    }
    expect(called).toBe(false);
  });

  it("charges documents and skill files against the one decoded budget", async () => {
    const { baseUrl } = await launch(testConfig({ documentBudgetBytes: 20 }));
    const response = await postRuns(
      baseUrl,
      startBody({
        documents: [docOf("x".repeat(16), "doc.bin")],
        skillFiles: [docOf("y".repeat(16), "skill.md")],
      }),
    );
    expect(response.status).toBe(413);
  });

  it("persists skill files area-tagged beside the documents before the run starts", async () => {
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { documentStore });
    await postRuns(
      baseUrl,
      startBody({ documents: [docOf("doc body")], skillFiles: [docOf("# skill", "grading.md")] }),
    );
    await waitFor(() => documentStore.saved.has("e-1"));
    const stored = documentStore.saved.get("e-1");
    expect(stored?.map((input) => [input.name, input.area])).toEqual([
      ["report.pdf", "documents"],
      ["grading.md", "skills"],
    ]);
  });

  it("treats a repeat with different skill files as a payload conflict, an identical one as a repeat", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    const body = startBody({ skillFiles: [docOf("# skill", "grading.md")] });
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    const different = await postRuns(baseUrl, startBody({ skillFiles: [docOf("# other", "grading.md")] }));
    expect(different.status).toBe(409);
    const missing = await postRuns(baseUrl, startBody({}));
    expect(missing.status).toBe(409);
    const repeat = await postRuns(baseUrl, body);
    expect(repeat.status).toBe(200);
  });

  it("accepts a skill description, persists it with the file, and treats a changed one as a new payload", async () => {
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(testConfig(), undefined, { documentStore });
    const body = startBody({
      skillFiles: [docOf("# skill", "grading.md", { description: "How to grade" })],
    });
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(documentStore.saved.get("e-1")?.[0]?.description).toBe("How to grade");
    const reworded = await postRuns(
      baseUrl,
      startBody({ skillFiles: [docOf("# skill", "grading.md", { description: "Something else" })] }),
    );
    expect(reworded.status).toBe(409);
  });

  it("rejects skill files whose names keep no identity or collide after normalization with 400", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    for (const skillFiles of [
      [docOf("x", "---")],
      [docOf("a", "huh.md"), docOf("b", "HUH.md")],
      [docOf("x", "s.md", { description: 7 })],
    ]) {
      const response = await postRuns(baseUrl, startBody({ skillFiles }));
      expect(response.status).toBe(400);
    }
    expect(called).toBe(false);
  });
});

describe("starting runs with a model", () => {
  const ALLOWED = ["default-pin", "us.anthropic.claude-sonnet-4-6"];

  it("accepts an allowlisted model with 202, threads it to the run, and shows it on the monitor", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig({ allowedModels: ALLOWED }), (_params, options) => {
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", model: "us.anthropic.claude-sonnet-4-6" }),
    );
    expect(response.status).toBe(202);
    expect((await monitorOf(response))["model"]).toBe("us.anthropic.claude-sonnet-4-6");
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(seen.options?.model).toBe("us.anthropic.claude-sonnet-4-6");
  });

  it("rejects a model outside the allowlist with 400 and a clear message, before any registration", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig({ allowedModels: ALLOWED }), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", model: "us.anthropic.claude-opus-4-1" }),
    );
    expect(response.status).toBe(400);
    const message = asRecord(await response.json())["error"];
    if (typeof message !== "string") {
      throw new Error("expected an error message");
    }
    expect(message).toContain("us.anthropic.claude-opus-4-1");
    expect(message).toContain("allowed");
    expect(called).toBe(false);
    expect((await fetch(`${baseUrl}/v1/runs/e-1`)).status).toBe(404);
  });

  it("threads no model when the request names none, keeping the pinned default", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig({ allowedModels: ALLOWED }), (_params, options) => {
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(response.status).toBe(202);
    expect((await monitorOf(response))["model"]).toBeNull();
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(seen.options?.model).toBeUndefined();
  });

  it("rejects a model that is not a non-empty string with 400", async () => {
    const { baseUrl } = await launch(testConfig({ allowedModels: ALLOWED }));
    for (const model of [7, "", ["default-pin"]]) {
      const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p", model }));
      expect(response.status).toBe(400);
    }
  });

  it("treats an explicit null on every optional start field as absence, exactly like runToken", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig({ allowedModels: ALLOWED }), (_params, options) => {
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({
        executionId: "e-1",
        prompt: "p",
        runToken: null,
        documents: null,
        skillFiles: null,
        model: null,
        outputFormat: null,
        systemPrompt: null,
        mcpServerUrl: null,
        maxTurns: null,
        maxBudgetUsd: null,
        deadlineMs: null,
        effort: null,
      }),
    );
    expect(response.status).toBe(202);
    const monitor = await monitorOf(response);
    expect(monitor["model"]).toBeNull();
    expect(monitor["mcpServerUrl"]).toBeNull();
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(seen.options?.model).toBeUndefined();
    expect(seen.options?.mcpServerUrl).toBeUndefined();
    expect(seen.options?.systemPrompt).toBeUndefined();
    expect(seen.options?.maxTurns).toBeUndefined();
    expect(seen.options?.maxBudgetUsd).toBeUndefined();
    expect(seen.options?.deadlineMs).toBeUndefined();
    expect(seen.options?.effort).toBeUndefined();
  });

  it("accepts a model exactly equal to the pinned default, threading and reporting it explicitly", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig({ allowedModels: ALLOWED }), (_params, options) => {
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const body = JSON.stringify({ executionId: "e-1", prompt: "p", model: "default-pin" });
    const response = await postRuns(baseUrl, body);
    expect(response.status).toBe(202);
    expect((await monitorOf(response))["model"]).toBe("default-pin");
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(seen.options?.model).toBe("default-pin");
    // Deliberately no normalization: naming the pin explicitly is a different payload identity from
    // omitting it, so a cross-form retry conflicts while the same-form repeat stays idempotent.
    expect((await postRuns(baseUrl, body)).status).toBe(200);
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(409);
  });

  it("treats a repeat with a different model as a payload conflict", async () => {
    const { baseUrl } = await launch(testConfig({ allowedModels: ALLOWED }), signalAwareHangingFake());
    const body = JSON.stringify({ executionId: "e-1", prompt: "p", model: "us.anthropic.claude-sonnet-4-6" });
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(409);
    expect((await postRuns(baseUrl, body)).status).toBe(200);
  });
});

describe("starting runs with per-request fuses", () => {
  // testConfig ceilings: maxTurns 10, maxBudgetUsd 1, runDeadlineMs 5000.
  it("accepts tightened fuses with 202, threads all four to the run, and folds them into identity", async () => {
    const seen: { options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig(), (_params, options) => {
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const body = JSON.stringify({
      executionId: "e-1",
      prompt: "p",
      maxTurns: 5,
      maxBudgetUsd: 0.5,
      deadlineMs: 2500,
      effort: "medium",
    });
    const response = await postRuns(baseUrl, body);
    expect(response.status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(seen.options?.maxTurns).toBe(5);
    expect(seen.options?.maxBudgetUsd).toBe(0.5);
    expect(seen.options?.deadlineMs).toBe(2500);
    expect(seen.options?.effort).toBe("medium");
    expect((await postRuns(baseUrl, body)).status).toBe(200);
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(409);
  });

  it("accepts fuse values exactly at the runtime ceilings", async () => {
    const { baseUrl } = await launch();
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", maxTurns: 10, maxBudgetUsd: 1, deadlineMs: 5000 }),
    );
    expect(response.status).toBe(202);
  });

  it("refuses any fuse above its runtime ceiling with 400 before any registration — never clamps", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    for (const widened of [{ maxTurns: 11 }, { maxBudgetUsd: 1.01 }, { deadlineMs: 5001 }]) {
      const response = await postRuns(
        baseUrl,
        JSON.stringify({ executionId: "e-1", prompt: "p", ...widened }),
      );
      expect(response.status).toBe(400);
      const message = asRecord(await response.json())["error"];
      if (typeof message !== "string") {
        throw new Error("expected an error message");
      }
      expect(message).toContain("ceiling");
    }
    expect(called).toBe(false);
    expect((await fetch(`${baseUrl}/v1/runs/e-1`)).status).toBe(404);
  });

  it("measures a request against the ceiling, so tightening the deployed default refuses nothing", async () => {
    const seen: SpawnRunOptions[] = [];
    const tightenedDefault = testConfig();
    tightenedDefault.fuses = {
      ...tightenedDefault.fuses,
      maxBudgetUsd: { defaultValue: 0.4, ceiling: 1 },
    };
    const { baseUrl } = await launch(tightenedDefault, (_params, options = {}) => {
      seen.push(options);
      return Promise.resolve(stubResult);
    });
    expect(
      (await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p", maxBudgetUsd: 0.5 })))
        .status,
    ).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(
      (await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p", maxBudgetUsd: 1 }))).status,
    ).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-2", "succeeded");
    expect(
      (await postRuns(baseUrl, JSON.stringify({ executionId: "e-3", prompt: "p", maxBudgetUsd: 1.01 })))
        .status,
    ).toBe(400);
    expect(seen.map((options) => options.maxBudgetUsd)).toEqual([0.5, 1]);
  });

  it("rejects malformed fuse values with 400", async () => {
    const { baseUrl } = await launch();
    for (const bad of [
      { maxTurns: 0 },
      { maxTurns: -1 },
      { maxTurns: 2.5 },
      { maxTurns: "5" },
      { maxBudgetUsd: 0 },
      { maxBudgetUsd: -0.5 },
      { maxBudgetUsd: "0.5" },
      { deadlineMs: 0 },
      { deadlineMs: -1 },
      { deadlineMs: 99.5 },
      { deadlineMs: "2500" },
    ]) {
      const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p", ...bad }));
      expect(response.status).toBe(400);
    }
  });

  it("accepts every SDK effort level and threads it to the run", async () => {
    const seen: SpawnRunOptions[] = [];
    const { baseUrl } = await launch(testConfig(), (_params, options = {}) => {
      seen.push(options);
      return Promise.resolve(stubResult);
    });
    const levels = ["low", "medium", "high", "xhigh", "max"];
    for (const [index, effort] of levels.entries()) {
      const executionId = `e-${index}`;
      const response = await postRuns(baseUrl, JSON.stringify({ executionId, prompt: "p", effort }));
      expect(response.status).toBe(202);
      await pollRunUntilStatus(baseUrl, executionId, "succeeded");
    }
    expect(seen.map((options) => options.effort)).toEqual(["low", "medium", "high", "xhigh", "max"]);
  });

  it("echoes all four effective fuse values on a fully silent start", async () => {
    const { baseUrl } = await launch();
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(response.status).toBe(202);
    const body = asRecord(await response.json());
    expect(body["fuses"]).toEqual({
      deadlineMs: 5000,
      maxBudgetUsd: 1,
      maxTurns: 10,
      maxRetainedOutputBytes: DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
    });
  });

  it("echoes a declared fuse and the runtime defaults for the rest", async () => {
    const { baseUrl } = await launch();
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", maxTurns: 5 }),
    );
    expect(response.status).toBe(202);
    const body = asRecord(await response.json());
    expect(body["fuses"]).toEqual({
      deadlineMs: 5000,
      maxBudgetUsd: 1,
      maxTurns: 5,
      maxRetainedOutputBytes: DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
    });
  });

  it("echoes a tightened deployed default for a silent start", async () => {
    const tightenedDefault = testConfig();
    tightenedDefault.fuses = {
      ...tightenedDefault.fuses,
      maxBudgetUsd: { defaultValue: 0.4, ceiling: 1 },
    };
    const { baseUrl } = await launch(tightenedDefault);
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(response.status).toBe(202);
    const body = asRecord(await response.json());
    expect(asRecord(body["fuses"])["maxBudgetUsd"]).toBe(0.4);
  });

  it("echoes the same effective values on the idempotent replay and the monitor poll", async () => {
    const { baseUrl } = await launch();
    const body = JSON.stringify({ executionId: "e-1", prompt: "p", deadlineMs: 2500 });
    const first = await postRuns(baseUrl, body);
    expect(first.status).toBe(202);
    const registered = asRecord(await first.json());
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const replay = await postRuns(baseUrl, body);
    expect(replay.status).toBe(200);
    const replayed = asRecord(await replay.json());
    expect(replayed["fuses"]).toEqual(registered["fuses"]);
    const monitor = asRecord(await (await fetch(`${baseUrl}/v1/runs/e-1`)).json());
    expect(monitor["fuses"]).toEqual(registered["fuses"]);
    expect(asRecord(registered["fuses"])["deadlineMs"]).toBe(2500);
  });

  it("rejects an effort outside the SDK union with 400", async () => {
    const { baseUrl } = await launch();
    for (const effort of ["extreme", "HIGH", "", 3, ["high"]]) {
      const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p", effort }));
      expect(response.status).toBe(400);
    }
  });

  it("treats a repeat with different fuses as a payload conflict", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    const body = JSON.stringify({ executionId: "e-1", prompt: "p", maxTurns: 5, effort: "medium" });
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    expect(
      (
        await postRuns(
          baseUrl,
          JSON.stringify({ executionId: "e-1", prompt: "p", maxTurns: 6, effort: "medium" }),
        )
      ).status,
    ).toBe(409);
    expect(
      (
        await postRuns(
          baseUrl,
          JSON.stringify({ executionId: "e-1", prompt: "p", maxTurns: 5, effort: "low" }),
        )
      ).status,
    ).toBe(409);
    expect((await postRuns(baseUrl, body)).status).toBe(200);
  });
});

describe("monitoring runs", () => {
  it("shows the run progressing to succeeded with the result and no more polling hint", async () => {
    let resolveRun: ((result: RunReportResult) => void) | undefined;
    const { baseUrl } = await launch(
      testConfig(),
      () =>
        new Promise((resolve) => {
          resolveRun = resolve;
        }),
    );
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const runningResponse = await fetch(`${baseUrl}/v1/runs/e-1`);
    expect(runningResponse.status).toBe(200);
    expect(runningResponse.headers.get("retry-after")).toBe("2");
    expect((await monitorOf(runningResponse))["status"]).toBe("running");
    await waitFor(() => resolveRun !== undefined);
    resolveRun?.(stubResult);
    const body = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(body["result"]).toEqual(stubResultView);
    expect(body["estimatedCostUsd"]).toBe(0.1);
    expect(body["error"]).toBeNull();
    const terminalResponse = await fetch(`${baseUrl}/v1/runs/e-1`);
    expect(terminalResponse.headers.get("retry-after")).toBeNull();
  });

  it("surfaces a failed run through the monitor as a bare subtype with its cost", async () => {
    const { baseUrl } = await launch(testConfig(), () =>
      Promise.reject(
        new RunFailedError("agent run failed (error_max_turns): secret detail", {
          subtype: "error_max_turns",
          estimatedCostUsd: 0.2,
          sessionId: "s",
        }),
      ),
    );
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const body = await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect(asRecord(body["error"])).toEqual({ subtype: "error_max_turns" });
    expect(body["estimatedCostUsd"]).toBe(0.2);
    expect(body["result"]).toBeNull();
  });

  it("maps an unexpected run error to a failed monitor without leaking details", async () => {
    const { baseUrl } = await launch(testConfig(), () =>
      Promise.reject(new Error("stack trace with internals")),
    );
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const body = await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect(asRecord(body["error"])).toEqual({ subtype: "internal_error" });
  });

  it("keeps executionId, cost, and the container-internal tracePath off the monitor result", async () => {
    const { baseUrl } = await launch(testConfig(), () =>
      Promise.resolve({ ...stubResult, tracePath: "/workspaces/traces/e-1/attempt-1" }),
    );
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const body = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(body["result"]).toEqual(stubResultView);
    expect(body["estimatedCostUsd"]).toBe(stubResult.estimatedCostUsd);
  });

  it("returns 404 for an unknown run", async () => {
    const { baseUrl } = await launch();
    const response = await fetch(`${baseUrl}/v1/runs/missing`);
    expect(response.status).toBe(404);
  });

  it("rejects non-GET methods on the monitor with 405 naming the allowed method", async () => {
    const { baseUrl } = await launch();
    const response = await fetch(`${baseUrl}/v1/runs/e-1`, { method: "PUT" });
    expect(response.status).toBe(405);
    expect(response.headers.get("allow")).toBe("GET");
  });
});

describe("polling headers", () => {
  it("hints a fast Retry-After on active statuses, a slow one while parked, none once terminal", () => {
    for (const status of ["running", "cancelling"] as const) {
      expect(monitorPollHeaders(status)).toEqual({ "retry-after": "2" });
    }
    expect(monitorPollHeaders("awaiting_input")).toEqual({ "retry-after": "15" });
    for (const status of ["succeeded", "failed", "cancelled", "expired"] as const) {
      expect(monitorPollHeaders(status)).toEqual({});
    }
  });
});

describe("cancelling runs", () => {
  it("moves a hung run through cancelling to cancelled by aborting its signal", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const cancelResponse = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect(cancelResponse.status).toBe(202);
    expect(cancelResponse.headers.get("retry-after")).toBe("2");
    expect((await monitorOf(cancelResponse))["status"]).toBe("cancelling");
    const body = await pollRunUntilStatus(baseUrl, "e-1", "cancelled");
    expect(body["status"]).toBe("cancelled");
  });

  it("keeps the cost reported by an aborted run on the cancelled monitor", async () => {
    const fake: RunReportFn = (_params, options) =>
      new Promise((_, reject) => {
        options?.signal?.addEventListener(
          "abort",
          () =>
            reject(
              new RunFailedError("killed mid-run", {
                subtype: "aborted",
                estimatedCostUsd: 0.42,
                sessionId: null,
              }),
            ),
          { once: true },
        );
      });
    const { baseUrl } = await launch(testConfig(), fake);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    const body = await pollRunUntilStatus(baseUrl, "e-1", "cancelled");
    expect(body["estimatedCostUsd"]).toBe(0.42);
  });

  it("keeps cost that surfaces after an immediate cancel of a paused run", async () => {
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      try {
        await options.pause(pauseRequest());
      } catch {
        throw new RunFailedError("unwound after cancel", {
          subtype: "aborted",
          estimatedCostUsd: 0.3,
          sessionId: null,
        });
      }
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const cancelResponse = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect(cancelResponse.status).toBe(200);
    const body = await pollRunUntil(
      baseUrl,
      "e-1",
      (monitor) => monitor["status"] === "cancelled" && monitor["estimatedCostUsd"] === 0.3,
    );
    expect(body["estimatedCostUsd"]).toBe(0.3);
  });

  it("lands a run cancelled when it tries to pause during cancellation", async () => {
    let pauseRefused = false;
    const fake: RunReportFn = async (_params, options) => {
      const signal = options?.signal;
      const pause = options?.pause;
      if (signal === undefined || pause === undefined) {
        throw new Error("seams missing");
      }
      await new Promise<void>((resolve) => signal.addEventListener("abort", () => resolve(), { once: true }));
      try {
        await pause(pauseRequest());
      } catch {
        pauseRefused = true;
        throw new Error("pause refused after cancel");
      }
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const cancelResponse = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect(cancelResponse.status).toBe(202);
    const body = await pollRunUntilStatus(baseUrl, "e-1", "cancelled");
    expect(body["status"]).toBe("cancelled");
    expect(pauseRefused).toBe(true);
  });

  it("lets completion win over a best-effort cancel, keeping the result and cost", async () => {
    let resolveRun: ((result: RunReportResult) => void) | undefined;
    const { baseUrl } = await launch(
      testConfig(),
      () =>
        new Promise((resolve) => {
          resolveRun = resolve;
        }),
    );
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const cancelResponse = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect((await monitorOf(cancelResponse))["status"]).toBe("cancelling");
    await waitFor(() => resolveRun !== undefined);
    resolveRun?.(stubResult);
    const body = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(body["result"]).toEqual(stubResultView);
    expect(body["estimatedCostUsd"]).toBe(0.1);
  });

  it("settles a paused run when a cancel meets an expired pause", async () => {
    let nowMs = Date.now();
    let pauseRejected = false;
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      try {
        await options.pause(pauseRequest());
      } catch {
        pauseRejected = true;
        throw new Error("expired while paused");
      }
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { now: () => nowMs });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    nowMs += DAY_MS + 1000;
    const cancelResponse = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect(cancelResponse.status).toBe(200);
    expect((await monitorOf(cancelResponse))["status"]).toBe("expired");
    await waitFor(() => pauseRejected);
    const terminalResponse = await fetch(`${baseUrl}/v1/runs/e-1`);
    expect((await monitorOf(terminalResponse))["status"]).toBe("expired");
    expect(terminalResponse.headers.get("retry-after")).toBeNull();
  });

  it("cancels an awaiting_input run immediately and settles the paused runner", async () => {
    let pauseRejected = false;
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      try {
        await options.pause(pauseRequest());
      } catch {
        pauseRejected = true;
        throw new Error("cancelled while paused");
      }
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const cancelResponse = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect(cancelResponse.status).toBe(200);
    expect((await monitorOf(cancelResponse))["status"]).toBe("cancelled");
    await waitFor(() => pauseRejected);
    expect((await pollRunUntilStatus(baseUrl, "e-1", "cancelled"))["status"]).toBe("cancelled");
  });

  it("answers 200 with the monitor when cancelling an already-finished run", async () => {
    const { baseUrl } = await launch();
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const cancelResponse = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect(cancelResponse.status).toBe(200);
    expect((await monitorOf(cancelResponse))["status"]).toBe("succeeded");
  });

  it("returns 404 when cancelling an unknown run", async () => {
    const { baseUrl } = await launch();
    const response = await fetch(`${baseUrl}/v1/runs/missing/cancel`, { method: "POST" });
    expect(response.status).toBe(404);
  });
});

describe("run input", () => {
  it("round-trips a pause: awaiting_input, stale token 409, then resume with the answer", async () => {
    let receivedResolution: PauseResolution | undefined;
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      receivedResolution = await options.pause(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));

    const awaiting = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const awaitingResponse = await fetch(`${baseUrl}/v1/runs/e-1`);
    expect(awaitingResponse.headers.get("retry-after")).toBe("15");
    const requiredInput = asRecord(awaiting["requiredInput"]);
    expect(requiredInput["question"]).toEqual(QUESTION);
    expect(typeof requiredInput["expiresAt"]).toBe("string");
    const resumeToken = requiredInput["resumeToken"];
    if (typeof resumeToken !== "string") {
      throw new Error("resumeToken missing from the monitor");
    }

    const wrongToken = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: "not-the-token", questionId: "q-1", answer: "the second fund" }),
    );
    expect(wrongToken.status).toBe(409);
    expect(await wrongToken.json()).toEqual({ code: "stale_resume_token", status: "awaiting_input" });

    const unknownQuestion = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken, questionId: "q-other", answer: "the second fund" }),
    );
    expect(unknownQuestion.status).toBe(409);
    expect(await unknownQuestion.json()).toEqual({ code: "unknown_question", status: "awaiting_input" });

    const resumed = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken, questionId: "q-1", answer: "the second fund" }),
    );
    expect(resumed.status).toBe(200);
    expect(resumed.headers.get("retry-after")).toBe("2");
    const resumedBody = await monitorOf(resumed);
    expect(resumedBody["status"]).toBe("running");
    expect(resumedBody["segment"]).toBe(1);

    const done = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(done["result"]).toEqual(stubResultView);
    expect(receivedResolution).toEqual({ answer: "the second fund", allowFurtherPause: true });
  });

  it("carries allowFurtherPause false through to the paused run's resolution", async () => {
    let receivedResolution: PauseResolution | undefined;
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      receivedResolution = await options.pause(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const awaiting = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const resumeToken = asRecord(awaiting["requiredInput"])["resumeToken"];
    if (typeof resumeToken !== "string") {
      throw new Error("resumeToken missing from the monitor");
    }
    const resumed = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken, questionId: "q-1", answer: "finish", allowFurtherPause: false }),
    );
    expect(resumed.status).toBe(200);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(receivedResolution).toEqual({ answer: "finish", allowFurtherPause: false });
  });

  it("settles a paused run when input arrives after the pause expired", async () => {
    let nowMs = Date.now();
    let pauseRejected = false;
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      try {
        await options.pause(pauseRequest());
      } catch {
        pauseRejected = true;
        throw new Error("expired while paused");
      }
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { now: () => nowMs });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const awaiting = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const resumeToken = asRecord(awaiting["requiredInput"])["resumeToken"];
    if (typeof resumeToken !== "string") {
      throw new Error("resumeToken missing from the monitor");
    }
    nowMs += DAY_MS + 1000;
    const response = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken, questionId: "q-1", answer: "" }),
    );
    expect(response.status).toBe(409);
    expect(await response.json()).toEqual({ code: "not_awaiting_input", status: "expired" });
    await waitFor(() => pauseRejected);
    expect((await pollRunUntilStatus(baseUrl, "e-1", "expired"))["status"]).toBe("expired");
  });

  it("refuses a second input after a successful resume and never double-delivers", async () => {
    let deliveries = 0;
    let releaseRun: (() => void) | undefined;
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      await options.pause(pauseRequest());
      deliveries += 1;
      await new Promise<void>((resolve) => {
        releaseRun = resolve;
      });
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const awaiting = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const resumeToken = asRecord(awaiting["requiredInput"])["resumeToken"];
    if (typeof resumeToken !== "string") {
      throw new Error("resumeToken missing from the monitor");
    }
    const inputBody = JSON.stringify({ resumeToken, questionId: "q-1", answer: "a" });
    const first = await postInput(baseUrl, "e-1", inputBody);
    expect(first.status).toBe(200);
    const second = await postInput(baseUrl, "e-1", inputBody);
    expect(second.status).toBe(409);
    expect(await second.json()).toEqual({ code: "not_awaiting_input", status: "running" });
    await waitFor(() => releaseRun !== undefined);
    releaseRun?.();
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(deliveries).toBe(1);
  });

  it("answers 409 with the code and current status when the run is not awaiting input", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: "t", questionId: "q-1", answer: "" }),
    );
    expect(response.status).toBe(409);
    expect(await response.json()).toEqual({ code: "not_awaiting_input", status: "running" });
  });

  it("returns 404 for input to an unknown run", async () => {
    const { baseUrl } = await launch();
    const response = await postInput(
      baseUrl,
      "missing",
      JSON.stringify({ resumeToken: "t", questionId: "q-1", answer: "" }),
    );
    expect(response.status).toBe(404);
  });

  it("rejects malformed input bodies with 400", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    for (const body of [
      {},
      { resumeToken: "" },
      { resumeToken: "t" },
      { resumeToken: "t", questionId: "q-1" },
      { resumeToken: "t", answer: "a" },
      { resumeToken: "t", questionId: "", answer: "a" },
      { resumeToken: "t", questionId: "q-1", answer: 7 },
      { resumeToken: "t", questionId: "q-1", answer: "a", allowFurtherPause: "yes" },
      { resumeToken: "t", questionId: "q-1", answer: "a", extra: true },
      { resumeToken: "t", answers: [{ questionId: "q-1", answer: "a" }] },
    ]) {
      const response = await postInput(baseUrl, "e-1", JSON.stringify(body));
      expect(response.status).toBe(400);
    }
  });
});

describe("the terminal bundle fetch route", () => {
  function realStore(): CheckpointStore {
    return createLocalCheckpointStore(join(mkdtempSync(join(tmpdir(), "server-bundle-")), "store"));
  }

  // A run that reaches its terminal after handing the supervisor a bundle, which is what puts one
  // in the store for the route to serve.
  function publishingRun(bundle: CheckpointBundle): SpawnRunFn {
    return async (_params, options) => {
      await options?.onTerminalCheckpoint?.({ sessionId: "s-1", checkpoint: bundle });
      return stubResult;
    };
  }

  function publishingFailedRun(bundle: CheckpointBundle, failure: RunFailedError): SpawnRunFn {
    return async (_params, options) => {
      await options?.onTerminalCheckpoint?.({ sessionId: "s-1", checkpoint: bundle });
      throw failure;
    };
  }

  async function launchWithBundle(
    bundle: CheckpointBundle,
  ): Promise<{ baseUrl: string; store: CheckpointStore }> {
    const store = realStore();
    const { baseUrl } = await launch(testConfig(), publishingRun(bundle), { checkpointStore: store });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    return { baseUrl, store };
  }

  // The failing sibling of launchWithBundle: the run publishes its bundle, then dies with
  // the given failure, and the poll returns the settled failure body.
  async function launchWithFailedBundle(
    bundle: CheckpointBundle,
    failure: RunFailedError,
  ): Promise<{ baseUrl: string; failed: Record<string, unknown> }> {
    const { baseUrl } = await launch(testConfig(), publishingFailedRun(bundle, failure), {
      checkpointStore: realStore(),
    });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const failed = await pollRunUntilStatus(baseUrl, "e-1", "failed");
    return { baseUrl, failed };
  }

  it("serves the stored bytes with the digest the store wrote, while the run is terminal but not completed", async () => {
    const bundle = sealCheckpoint(new TextEncoder().encode("finished state"));
    const { baseUrl } = await launchWithBundle(bundle);
    const response = await fetch(`${baseUrl}/v1/runs/e-1/checkpoint`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(200);
    expect(response.headers.get(CHECKPOINT_SHA256_HEADER)).toBe(bundle.sha256);
    expect(response.headers.get("content-length")).toBe(String(bundle.bytes.byteLength));
    const received = new Uint8Array(await response.arrayBuffer());
    expect(sha256Hex(received)).toBe(bundle.sha256);
  });

  it("answers gone once the run is completed, and completing has taken the bytes with it", async () => {
    const bundle = sealCheckpoint(new TextEncoder().encode("finished state"));
    const { baseUrl, store } = await launchWithBundle(bundle);
    const completed = await fetch(`${baseUrl}/v1/runs/e-1/complete`, {
      method: "POST",
      headers: NO_REUSE_HEADERS,
    });
    expect(completed.status).toBe(200);
    const response = await fetch(`${baseUrl}/v1/runs/e-1/checkpoint`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(410);
    // The 410 alone comes from the completed gate and would hold whether or not the purge ran.
    // The purge runs in the background, and a load between its two file removals throws.
    await waitForAsync(async () => {
      try {
        return (await store.load(terminalCheckpointKeyOf("e-1", 1))) === undefined;
      } catch {
        return false;
      }
    });
  });

  it("serves the bundle of a run that failed, which is the leg most worth resuming", async () => {
    const bundle = sealCheckpoint(new TextEncoder().encode("the dead session"));
    const { baseUrl } = await launchWithFailedBundle(
      bundle,
      new RunFailedError("success result carried no structured output despite outputFormat", {
        subtype: "malformed_result",
        estimatedCostUsd: 0.2,
        sessionId: "s-1",
      }),
    );
    const response = await fetch(`${baseUrl}/v1/runs/e-1/checkpoint`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(200);
    expect(response.headers.get(CHECKPOINT_SHA256_HEADER)).toBe(bundle.sha256);
  });

  it("serves the bundle of a run killed on its deadline", async () => {
    const bundle = sealCheckpoint(new TextEncoder().encode("the deadline session"));
    const { baseUrl, failed } = await launchWithFailedBundle(
      bundle,
      new RunFailedError("run deadline exceeded after 900ms", {
        subtype: "deadline_exceeded",
        estimatedCostUsd: 0.4,
        sessionId: "s-1",
      }),
    );
    expect((failed["error"] as Record<string, unknown>)["subtype"]).toBe("deadline_exceeded");
    const response = await fetch(`${baseUrl}/v1/runs/e-1/checkpoint`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(200);
    expect(response.headers.get(CHECKPOINT_SHA256_HEADER)).toBe(bundle.sha256);
  });

  it("answers not found for a run that published no bundle", async () => {
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), {
      checkpointStore: realStore(),
    });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const response = await fetch(`${baseUrl}/v1/runs/e-1/checkpoint`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(404);
  });

  it("answers not found for a run the supervisor never heard of", async () => {
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), {
      checkpointStore: realStore(),
    });
    const response = await fetch(`${baseUrl}/v1/runs/absent/checkpoint`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(404);
  });

  it("refuses a method other than GET, so the route cannot be written through", async () => {
    const { baseUrl } = await launchWithBundle(sealCheckpoint(new TextEncoder().encode("finished state")));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/checkpoint`, {
      method: "POST",
      headers: NO_REUSE_HEADERS,
    });
    expect(response.status).toBe(405);
  });
});

describe("the orphaned-bundle reaper", () => {
  it("removes a bundle no live run owns, once it is older than the stated age", async () => {
    const root = join(mkdtempSync(join(tmpdir(), "server-reaper-")), "store");
    const store = createLocalCheckpointStore(root);
    await store.save(terminalCheckpointKeyOf("stranded", 1), sealCheckpoint(new TextEncoder().encode("x")));
    await launch(testConfig(), () => Promise.resolve(stubResult), {
      checkpointStore: store,
      checkpointReapIntervalMs: 10,
      checkpointOrphanMaxAgeMs: 0,
    });
    await waitFor(() => readdirSync(root).length === 0);
  });

  it("spares a bundle belonging to a run the supervisor still holds", async () => {
    const root = join(mkdtempSync(join(tmpdir(), "server-reaper-live-")), "store");
    const store = createLocalCheckpointStore(root);
    const { baseUrl } = await launch(testConfig(), () => new Promise(() => {}), {
      checkpointStore: store,
      checkpointReapIntervalMs: 10,
      checkpointOrphanMaxAgeMs: 0,
    });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await store.save(terminalCheckpointKeyOf("e-1", 1), sealCheckpoint(new TextEncoder().encode("x")));
    // Long enough for several sweeps at the 10ms interval above.
    await new Promise((resolve) => setTimeout(resolve, 60));
    expect(readdirSync(root)).toHaveLength(2);
  });
});

describe("checkpoint lifecycle", () => {
  it("saves the checkpoint before awaiting_input appears, keyed by executionId, attempt, and segment", async () => {
    const store = memoryStore();
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      await options.pause(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { checkpointStore: store });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    expect(store.saves).toEqual(["e-1.1.0"]);
  });

  it("never shows awaiting_input while the checkpoint save is still in flight", async () => {
    const store = memoryStore({ holdSaves: true });
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      await options.pause(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { checkpointStore: store });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await waitFor(() => store.saves.length === 1);
    // Deterministic: the save is held, so an awaiting_input here would mean the status flipped early.
    const during = await monitorOf(await fetch(`${baseUrl}/v1/runs/e-1`));
    expect(during["status"]).toBe("running");
    expect(during["requiredInput"]).toBeNull();
    store.releaseSaves();
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
  });

  it("deletes a checkpoint whose save only completed after the run went terminal", async () => {
    const store = memoryStore({ holdSaves: true });
    const fake: RunReportFn = (_params, options) =>
      new Promise((_, reject) => {
        if (options?.pause === undefined) {
          reject(new Error("pause seam missing"));
          return;
        }
        void options.pause(pauseRequest()).catch(() => {});
        options.signal?.addEventListener(
          "abort",
          () =>
            reject(
              new RunFailedError("killed mid-pause", {
                subtype: "aborted",
                estimatedCostUsd: 0,
                sessionId: null,
              }),
            ),
          { once: true },
        );
      });
    const { baseUrl } = await launch(testConfig(), fake, { checkpointStore: store });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await waitFor(() => store.saves.length === 1);
    await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    // The run is fully terminal — teardown done — while the save is still held.
    await pollRunUntilStatus(baseUrl, "e-1", "cancelled");
    store.releaseSaves();
    await waitFor(() => store.deletes.includes("e-1.1.0"));
    expect(await store.load({ executionId: "e-1", attempt: 1, segment: 0 })).toBeUndefined();
  });

  it("fails the run outright when the checkpoint save fails, never landing awaiting_input", async () => {
    const store = memoryStore({ failSave: true });
    let pauseRejected = false;
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      try {
        await options.pause(pauseRequest());
      } catch {
        pauseRejected = true;
        throw new RunFailedError("checkpoint could not be persisted", {
          subtype: "pause_rejected",
          estimatedCostUsd: 0,
          sessionId: null,
        });
      }
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { checkpointStore: store });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const seenStatuses = new Set<string>();
    const body = await pollRunUntil(baseUrl, "e-1", (monitor) => {
      const status = monitor["status"];
      if (typeof status === "string") {
        seenStatuses.add(status);
      }
      return status === "failed";
    });
    expect(pauseRejected).toBe(true);
    expect(seenStatuses.has("awaiting_input")).toBe(false);
    expect(body["requiredInput"]).toBeNull();
  });

  it("deletes the checkpoint once a resumed run succeeds", async () => {
    const store = memoryStore();
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      await options.pause(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { checkpointStore: store });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const awaiting = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const resumeToken = asRecord(awaiting["requiredInput"])["resumeToken"];
    if (typeof resumeToken !== "string") {
      throw new Error("resumeToken missing from the monitor");
    }
    await postInput(baseUrl, "e-1", JSON.stringify({ resumeToken, questionId: "q-1", answer: "" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    await waitFor(() => store.deletes.includes("e-1.1.0"));
    expect(store.deletes).toContain("e-1.1.0");
  });

  it("deletes the checkpoint when a paused run is cancelled in its sleep", async () => {
    const store = memoryStore();
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      await options.pause(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { checkpointStore: store });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    await pollRunUntilStatus(baseUrl, "e-1", "cancelled");
    await waitFor(() => store.deletes.includes("e-1.1.0"));
    expect(store.deletes).toContain("e-1.1.0");
  });

  it("deletes the checkpoint when the pause expires unanswered", async () => {
    let nowMs = Date.now();
    const store = memoryStore();
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      await options.pause(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { now: () => nowMs, checkpointStore: store });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    nowMs += DAY_MS + 1000;
    await pollRunUntilStatus(baseUrl, "e-1", "expired");
    await waitFor(() => store.deletes.includes("e-1.1.0"));
    expect(store.deletes).toContain("e-1.1.0");
  });
});

type HttpFakeChild = {
  sent: string[];
  killed: boolean;
  isAlive: () => boolean;
  emit: (frame: unknown) => void;
  exit: () => void;
  onSend?: (line: string) => void;
};

function httpFakeChildRun(script: (child: HttpFakeChild, index: number) => void): {
  childRun: ChildRun;
  children: HttpFakeChild[];
} {
  const children: HttpFakeChild[] = [];
  const childRun: ChildRun = (input) => {
    let alive = true;
    let resolveExit: (exit: { code: number | null; signal: string | null; stderr: string }) => void =
      () => {};
    const exited = new Promise<{ code: number | null; signal: string | null; stderr: string }>((resolve) => {
      resolveExit = resolve;
    });
    const child: HttpFakeChild = {
      sent: [],
      killed: false,
      isAlive: () => alive,
      emit: (frame) => input.onFrame(JSON.stringify(frame)),
      exit: () => {
        if (!alive) {
          return;
        }
        alive = false;
        resolveExit({ code: 0, signal: null, stderr: "" });
      },
    };
    const handle: ChildHandle = {
      send: (line) => {
        child.sent.push(line);
        child.onSend?.(line);
      },
      cargo: new Writable({
        write(_chunk, _encoding, callback) {
          callback();
        },
      }),
      cargoTeardown: new AbortController().signal,
      // Models the SIGTERM-to-close gap of a real child: kill only requests termination.
      kill: () => {
        child.killed = true;
        setTimeout(() => {
          if (!alive) {
            return;
          }
          alive = false;
          resolveExit({ code: null, signal: "SIGTERM", stderr: "" });
        }, 5);
      },
      isAlive: () => alive,
      exited,
      signalsSettled: () => Promise.resolve(true),
    };
    children.push(child);
    script(child, children.length - 1);
    return handle;
  };
  return { childRun, children };
}

function pausedWireFrame(): unknown {
  return {
    kind: "paused",
    question: QUESTION,
    sessionId: "s-pause",
    estimatedCostUsd: 0.05,
    checkpoint: {
      sha256: PAUSE_CHECKPOINT.sha256,
      bytesBase64: Buffer.from(PAUSE_CHECKPOINT.bytes).toString("base64"),
    },
  };
}

function spawnerBackedLaunch(input: {
  childRun: ChildRun;
  store: CheckpointStore;
  pauseGraceMs: number;
}): Promise<{ server: RunApiServer; baseUrl: string }> {
  const runReportFn = createRunSpawner({
    pool: createUidPool({ baseUid: 20001, size: 2 }),
    childRun: input.childRun,
    workspaceRoot: "/workspaces",
    subprocessEnv: { PATH: "/usr/bin" },
    modelProvider: { provider: "bedrock", identity: { ambient: true } },
    buildIdentity: { version: "0.0.0-test", revision: "test-revision" },
    deadlineMs: 5000,
    maxBudgetUsd: 1,
    checkpointStore: input.store,
    documentStore: memoryDocumentStore(),
    pauseGraceMs: input.pauseGraceMs,
    sweepUidResidue: () => Promise.resolve(),
    claimRunSubtrees: () => {},
  });
  return launch(testConfig(), runReportFn, { checkpointStore: input.store });
}

describe("pause and resume over HTTP", () => {
  it("cold-resumes: pause, checkpoint, child killed, input spawns a fresh child from the bundle", async () => {
    const store = memoryStore();
    const { childRun, children } = httpFakeChildRun((child, index) => {
      if (index === 0) {
        queueMicrotask(() => child.emit(pausedWireFrame()));
      } else {
        queueMicrotask(() => {
          child.emit({ kind: "final", response: { ok: true, result: stubResult } });
          child.exit();
        });
      }
    });
    const { baseUrl } = await spawnerBackedLaunch({ childRun, store, pauseGraceMs: 0 });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));

    const awaiting = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    expect(asRecord(awaiting["requiredInput"])["question"]).toEqual(QUESTION);
    expect(typeof awaiting["pausedAt"]).toBe("string");
    expect(children).toHaveLength(1);
    expect(children[0]?.killed).toBe(true);
    await waitFor(() => children[0]?.isAlive() === false);

    const resumeToken = asRecord(awaiting["requiredInput"])["resumeToken"];
    if (typeof resumeToken !== "string") {
      throw new Error("resumeToken missing from the monitor");
    }
    const resumed = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken, questionId: "q-1", answer: "the second fund" }),
    );
    expect(resumed.status).toBe(200);

    const done = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(done["result"]).toEqual(stubResultView);
    expect(typeof done["resumedAt"]).toBe("string");
    expect(typeof done["lastPauseMs"]).toBe("number");
    expect(children).toHaveLength(2);
    const resumeRequest = JSON.parse(children[1]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(resumeRequest["kind"]).toBe("resume");
    expect(resumeRequest["answer"]).toBe("the second fund");
    expect(resumeRequest["allowFurtherPause"]).toBe(true);
    expect(asRecord(resumeRequest["checkpoint"])["sha256"]).toBe(PAUSE_CHECKPOINT.sha256);
    await waitFor(() => store.deletes.includes("e-1.1.0"));
  });

  it("keys a second pause at segment 1 on both sides: distinct saves, the later bundle, both reaped", async () => {
    const store = memoryStore();
    const secondCheckpoint = sealCheckpoint(new TextEncoder().encode("second segment state"));
    const { childRun, children } = httpFakeChildRun((child, index) => {
      if (index === 0) {
        queueMicrotask(() => child.emit(pausedWireFrame()));
      } else if (index === 1) {
        queueMicrotask(() =>
          child.emit({
            kind: "paused",
            question: QUESTION,
            sessionId: "s-pause-2",
            estimatedCostUsd: 0.07,
            checkpoint: {
              sha256: secondCheckpoint.sha256,
              bytesBase64: Buffer.from(secondCheckpoint.bytes).toString("base64"),
            },
          }),
        );
      } else {
        queueMicrotask(() => {
          child.emit({ kind: "final", response: { ok: true, result: stubResult } });
          child.exit();
        });
      }
    });
    const { baseUrl } = await spawnerBackedLaunch({ childRun, store, pauseGraceMs: 0 });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));

    const awaitingFirst = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    expect(awaitingFirst["segment"]).toBe(0);
    const firstToken = asRecord(awaitingFirst["requiredInput"])["resumeToken"];
    if (typeof firstToken !== "string") {
      throw new Error("resumeToken missing from the first pause");
    }
    await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: firstToken, questionId: "q-1", answer: "first" }),
    );

    const awaitingSecond = await pollRunUntil(
      baseUrl,
      "e-1",
      (body) => body["status"] === "awaiting_input" && body["segment"] === 1,
    );
    const secondToken = asRecord(awaitingSecond["requiredInput"])["resumeToken"];
    if (typeof secondToken !== "string") {
      throw new Error("resumeToken missing from the second pause");
    }
    expect(secondToken).not.toBe(firstToken);
    await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: secondToken, questionId: "q-1", answer: "second" }),
    );

    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(store.saves).toEqual(["e-1.1.0", "e-1.1.1"]);
    expect(children).toHaveLength(3);
    const secondResume = JSON.parse(children[2]?.sent[0] ?? "{}") as Record<string, unknown>;
    expect(secondResume["kind"]).toBe("resume");
    expect(asRecord(secondResume["checkpoint"])["sha256"]).toBe(secondCheckpoint.sha256);
    await waitFor(() => store.deletes.includes("e-1.1.0") && store.deletes.includes("e-1.1.1"));
  });

  it("deletes a superseded checkpoint as soon as the next pause's bundle is durable", async () => {
    const store = memoryStore();
    const secondCheckpoint = sealCheckpoint(new TextEncoder().encode("second segment state"));
    const { childRun } = httpFakeChildRun((child, index) => {
      if (index === 0) {
        queueMicrotask(() => child.emit(pausedWireFrame()));
      } else if (index === 1) {
        queueMicrotask(() =>
          child.emit({
            kind: "paused",
            question: QUESTION,
            sessionId: "s-pause-2",
            estimatedCostUsd: 0.07,
            checkpoint: {
              sha256: secondCheckpoint.sha256,
              bytesBase64: Buffer.from(secondCheckpoint.bytes).toString("base64"),
            },
          }),
        );
      }
    });
    const { baseUrl } = await spawnerBackedLaunch({ childRun, store, pauseGraceMs: 0 });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const awaitingFirst = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const firstToken = asRecord(awaitingFirst["requiredInput"])["resumeToken"];
    if (typeof firstToken !== "string") {
      throw new Error("resumeToken missing from the first pause");
    }
    await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: firstToken, questionId: "q-1", answer: "first" }),
    );
    await pollRunUntil(
      baseUrl,
      "e-1",
      (body) => body["status"] === "awaiting_input" && body["segment"] === 1,
    );
    await waitFor(() => store.deletes.includes("e-1.1.0"));
    expect(store.deletes).not.toContain("e-1.1.1");
  });

  it("warm-resumes: with a grace window the answer reaches the still-alive child over its stdin", async () => {
    const store = memoryStore();
    const { childRun, children } = httpFakeChildRun((child) => {
      child.onSend = (line) => {
        if ((JSON.parse(line) as Record<string, unknown>)["kind"] === "answer") {
          child.emit({ kind: "final", response: { ok: true, result: stubResult } });
          child.exit();
        }
      };
      queueMicrotask(() => child.emit(pausedWireFrame()));
    });
    const { baseUrl } = await spawnerBackedLaunch({ childRun, store, pauseGraceMs: 60_000 });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const awaiting = await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    expect(children[0]?.killed).toBe(false);
    const resumeToken = asRecord(awaiting["requiredInput"])["resumeToken"];
    if (typeof resumeToken !== "string") {
      throw new Error("resumeToken missing from the monitor");
    }
    await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken, questionId: "q-1", answer: "warm answer" }),
    );
    const done = await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(done["result"]).toEqual(stubResultView);
    expect(children).toHaveLength(1);
    const answerFrame = JSON.parse(children[0]?.sent[1] ?? "{}") as Record<string, unknown>;
    expect(answerFrame["kind"]).toBe("answer");
    expect(answerFrame["answer"]).toBe("warm answer");
    expect(answerFrame["allowFurtherPause"]).toBe(true);
  });
});

describe("run files endpoints", () => {
  function seedRunFiles(): { runFiles: RunFilesView } {
    const base = mkdtempSync(join(tmpdir(), "server-files-"));
    const artifactsRoot = join(base, "artifacts");
    const traceRoot = join(base, "traces");
    const workspacePath = join(base, "run-x");
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "# report");
    writeFileSync(join(workspacePath, "out", "my table.csv"), "a,b\n");
    publishRunArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1, workspacePath });
    mkdirSync(join(traceRoot, "e-1", "attempt-1"), { recursive: true });
    writeFileSync(join(traceRoot, "e-1", "attempt-1", "trace.ndjson"), '{"type":"envelope"}\n');
    return { runFiles: createRunFilesView({ artifactsRoot, traceRoot, removeTree: removeOwnTree }) };
  }

  async function launchWithRun(): Promise<{ baseUrl: string }> {
    const { runFiles } = seedRunFiles();
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    return { baseUrl };
  }

  it("lists a run's retained files with sizes, hashes, and kinds", async () => {
    const { baseUrl } = await launchWithRun();
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(response.status).toBe(200);
    const body = asRecord(await response.json());
    expect(body["files"]).toEqual([
      {
        path: "out/my table.csv",
        sizeBytes: 4,
        sha256: sha256Hex(Buffer.from("a,b\n")),
        kind: "artifact",
      },
      {
        path: "out/report.md",
        sizeBytes: 8,
        sha256: sha256Hex(Buffer.from("# report")),
        kind: "artifact",
      },
      {
        path: "trace/trace.ndjson",
        sizeBytes: 20,
        sha256: sha256Hex(Buffer.from('{"type":"envelope"}\n')),
        kind: "trace",
      },
    ]);
  });

  it("serves raw file bytes with an extension-derived content type and an exact content length", async () => {
    const { baseUrl } = await launchWithRun();
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files/out/report.md`);
    expect(response.status).toBe(200);
    expect(response.headers.get("content-type")).toBe("text/markdown");
    expect(response.headers.get("content-length")).toBe("8");
    expect(await response.text()).toBe("# report");
  });

  it("never lets a browser sniff a served file or an error message into markup", async () => {
    const { baseUrl } = await launchWithRun();
    const file = await fetch(`${baseUrl}/v1/runs/e-1/files/out/report.md`);
    expect(file.headers.get("x-content-type-options")).toBe("nosniff");
    expect(file.headers.get("content-disposition")).toBe("attachment");
    const listing = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(listing.headers.get("x-content-type-options")).toBe("nosniff");
    const rejected = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "" }));
    expect(rejected.status).toBe(400);
    expect(rejected.headers.get("x-content-type-options")).toBe("nosniff");
    const health = await fetch(`${baseUrl}/healthz`);
    expect(health.headers.get("x-content-type-options")).toBe("nosniff");
  });

  it("returns a caller's own string in an error body with no markup a parser could act on", async () => {
    const { baseUrl } = await launch(testConfig({ allowedModels: ["default-pin"] }));
    const hostile = '<script>alert("x")</script>&';
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", model: hostile }),
    );
    expect(response.status).toBe(400);
    const raw = await response.text();
    // Escaped on the wire (no raw markup chars) but identical once JSON-parsed — nothing is lost by it.
    expect(raw).not.toContain("<");
    expect(raw).not.toContain(">");
    expect(raw).not.toContain("&");
    expect(String(asRecord(JSON.parse(raw))["error"])).toContain(hostile);
  });

  it("decodes percent-encoded path segments", async () => {
    const { baseUrl } = await launchWithRun();
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files/out/my%20table.csv`);
    expect(response.status).toBe(200);
    expect(response.headers.get("content-type")).toBe("text/csv");
    expect(await response.text()).toBe("a,b\n");
  });

  it("404s an unknown run, an unknown path, and a path outside the manifest", async () => {
    const { baseUrl } = await launchWithRun();
    const unknownRun = await fetch(`${baseUrl}/v1/runs/ghost/files`);
    expect(unknownRun.status).toBe(404);
    expect(await unknownRun.json()).toEqual({ error: "not found" });
    expect((await fetch(`${baseUrl}/v1/runs/ghost/files/out/report.md`)).status).toBe(404);
    const unknownPath = await fetch(`${baseUrl}/v1/runs/e-1/files/out/missing.md`);
    expect(unknownPath.status).toBe(404);
    expect(await unknownPath.json()).toEqual({ error: "not found" });
    expect((await fetch(`${baseUrl}/v1/runs/e-1/files/out/..%2F..%2Fetc%2Fpasswd`)).status).toBe(404);
    expect((await fetch(`${baseUrl}/v1/runs/e-1/files/trace/trace.seq`)).status).toBe(404);
  });

  it("serves the list and file fetch while the run is still deterministically running", async () => {
    const { runFiles } = seedRunFiles();
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect((await monitorOf(await fetch(`${baseUrl}/v1/runs/e-1`)))["status"]).toBe("running");
    const list = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(list.status).toBe(200);
    const listed = asRecord(await list.json())["files"];
    if (!Array.isArray(listed)) {
      throw new Error("expected a files array");
    }
    expect(listed.length).toBeGreaterThan(0);
    const file = await fetch(`${baseUrl}/v1/runs/e-1/files/out/report.md`);
    expect(file.status).toBe(200);
    expect(await file.text()).toBe("# report");
  });

  it("streams the descriptor resolve opened even when the path is swapped right after the verdict", async () => {
    const base = mkdtempSync(join(tmpdir(), "server-files-swap-"));
    const secret = join(base, "supervisor-only");
    writeFileSync(secret, "supervisor-only bytes");
    mkdirSync(join(base, "out"));
    const served = join(base, "out", "report.md");
    writeFileSync(served, "# report");
    const runFiles = stubRunFilesView({
      resolve: () => {
        const file = openSizedFile(served);
        rmSync(served);
        symlinkSync(secret, served);
        return Promise.resolve({ file, contentType: "text/markdown" });
      },
    });
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files/out/report.md`);
    expect(response.status).toBe(200);
    expect(await response.text()).toBe("# report");
  });

  it("serves an empty deliverable as an empty 200, not a stalled response", async () => {
    const base = mkdtempSync(join(tmpdir(), "server-files-empty-file-"));
    mkdirSync(join(base, "out"));
    const served = join(base, "out", "report.md");
    writeFileSync(served, "");
    const runFiles = stubRunFilesView({
      resolve: () => Promise.resolve({ file: openSizedFile(served), contentType: "text/markdown" }),
    });
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files/out/report.md`);
    expect(response.status).toBe(200);
    expect(response.headers.get("content-length")).toBe("0");
    expect(await response.text()).toBe("");
  });

  it("serves the size the resolve-time descriptor reported, not bytes appended after it", async () => {
    const base = mkdtempSync(join(tmpdir(), "server-files-grown-"));
    mkdirSync(join(base, "out"));
    const served = join(base, "out", "report.md");
    writeFileSync(served, "# report");
    const runFiles = stubRunFilesView({
      resolve: () => {
        const file = openSizedFile(served);
        appendFileSync(served, " and bytes appended after the open");
        return Promise.resolve({ file, contentType: "text/markdown" });
      },
    });
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files/out/report.md`);
    expect(response.status).toBe(200);
    expect(response.headers.get("content-length")).toBe("8");
    expect(await response.text()).toBe("# report");
  });

  it("refuses the listing with 503 when its reservation does not fit the in-flight budget", async () => {
    const { runFiles } = seedRunFiles();
    const config = testConfig({ inflightUploadBudgetBytes: 4096 });
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(response.status).toBe(503);
    expect(await response.json()).toEqual({ error: "capacity", reason: "files_listing" });
    expect(response.headers.get("retry-after")).not.toBeNull();
  });

  it("reserves what a listing holds resident, not the bytes it finally serves", async () => {
    // A budget one byte under the resident peak (manifest + parsed entries + rendering) must refuse it.
    const { runFiles } = seedRunFiles();
    const config = testConfig({ inflightUploadBudgetBytes: FILES_LISTING_RESIDENT_BYTES - 1 });
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(response.status).toBe(503);
    expect(await response.json()).toEqual({ error: "capacity", reason: "files_listing" });
  });

  it("releases a listing's charge with its response, so listings do not accumulate", async () => {
    const { runFiles } = seedRunFiles();
    const config = testConfig({ inflightUploadBudgetBytes: 2 * FILES_LISTING_RESIDENT_BYTES });
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    for (let i = 0; i < 5; i += 1) {
      const response = await fetch(`${baseUrl}/v1/runs/e-1/files`, { headers: NO_REUSE_HEADERS });
      expect(response.status).toBe(200);
      await response.text();
    }
  });

  it("refuses a listing past the byte cap for capacity, whatever a manifest smuggled in", async () => {
    const pathBloat = "x".repeat(4096);
    const entries = Array.from({ length: 4200 }, (_, i) => ({
      path: `out/${i}-${pathBloat}`,
      sizeBytes: 1,
    }));
    const runFiles = entriesRunFilesView(entries);
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(response.status).toBe(503);
    expect(await response.json()).toEqual({ error: "capacity", reason: "files_listing_bytes" });
    expect(response.headers.get("retry-after")).not.toBeNull();
  });

  it("serves a listing of escapable filenames published through the real publish path", async () => {
    const base = mkdtempSync(join(tmpdir(), "server-files-escapes-"));
    const artifactsRoot = join(base, "artifacts");
    const workspacePath = join(base, "run-x");
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    const name = "q1 <draft> & final.csv";
    writeFileSync(join(workspacePath, "out", name), "a,b\n");
    publishRunArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1, workspacePath });
    const runFiles = createRunFilesView({
      artifactsRoot,
      traceRoot: join(base, "traces"),
      removeTree: removeOwnTree,
    });
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(response.status).toBe(200);
    const body = await response.text();
    expect(Buffer.byteLength(body)).toBeLessThanOrEqual(MAX_FILES_LISTING_BYTES);
    expect(body).toContain("\\u003c");
    expect(body).not.toContain("<");
    expect(asRecord(JSON.parse(body))["files"]).toEqual([
      {
        path: `out/${name}`,
        sizeBytes: 4,
        sha256: sha256Hex(Buffer.from("a,b\n")),
        kind: "artifact",
      },
    ]);
  });

  it("lists an empty manifest for a registered run with nothing retained yet", async () => {
    const base = mkdtempSync(join(tmpdir(), "server-files-empty-"));
    const runFiles = createRunFilesView({
      artifactsRoot: join(base, "artifacts"),
      traceRoot: join(base, "traces"),
      removeTree: removeOwnTree,
    });
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(response.status).toBe(200);
    expect(asRecord(await response.json())["files"]).toEqual([]);
  });
});

describe("run complete", () => {
  function seedRetainedData(): {
    runFiles: RunFilesView;
    artifactsRunDir: string;
    traceRunDir: string;
  } {
    const base = mkdtempSync(join(tmpdir(), "server-complete-"));
    const artifactsRoot = join(base, "artifacts");
    const traceRoot = join(base, "traces");
    mkdirSync(join(artifactsRoot, "e-1", "attempt-1", "out"), { recursive: true });
    writeFileSync(join(artifactsRoot, "e-1", "attempt-1", "out", "report.md"), "# report");
    mkdirSync(join(traceRoot, "e-1", "attempt-1"), { recursive: true });
    writeFileSync(join(traceRoot, "e-1", "attempt-1", "trace.ndjson"), '{"type":"envelope"}\n');
    return {
      runFiles: createRunFilesView({ artifactsRoot, traceRoot, removeTree: removeOwnTree }),
      artifactsRunDir: join(artifactsRoot, "e-1"),
      traceRunDir: join(traceRoot, "e-1"),
    };
  }

  it("completes a terminal run: 200 monitor, everything local purged, files gone with 410", async () => {
    const seeded = seedRetainedData();
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), {
      documentStore,
      runFiles: seeded.runFiles,
    });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const complete = await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" });
    expect(complete.status).toBe(200);
    const completeBody = await monitorOf(complete);
    expect(completeBody["status"]).toBe("succeeded");
    expect(completeBody["completed"]).toBe(true);
    await waitFor(
      () =>
        documentStore.deletes.includes("e-1") &&
        !existsSync(seeded.artifactsRunDir) &&
        !existsSync(seeded.traceRunDir),
    );
    const files = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(files.status).toBe(410);
    expect(await files.json()).toEqual({ code: "completed" });
    const file = await fetch(`${baseUrl}/v1/runs/e-1/files/out/report.md`);
    expect(file.status).toBe(410);
    const monitor = await fetch(`${baseUrl}/v1/runs/e-1`);
    expect(monitor.status).toBe(200);
  });

  it("pins resume-after-complete: input answers 409 not_awaiting_input on the terminal record", async () => {
    const { baseUrl } = await launch();
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" });
    const input = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: "t", questionId: "q-1", answer: "" }),
    );
    expect(input.status).toBe(409);
    expect(await input.json()).toEqual({ code: "not_awaiting_input", status: "succeeded" });
  });

  it("is idempotent: completing an already-completed run answers 200 again", async () => {
    const { baseUrl } = await launch();
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect((await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" })).status).toBe(200);
    const again = await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" });
    expect(again.status).toBe(200);
    expect((await monitorOf(again))["status"]).toBe("succeeded");
  });

  it("winds a parked run down to cancelled, killing the waiter, and purges", async () => {
    const seeded = seedRetainedData();
    let pauseRejected = false;
    const fake: RunReportFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      try {
        await options.pause(pauseRequest());
      } catch {
        pauseRejected = true;
        throw new Error("completed while parked");
      }
      return stubResult;
    };
    const { baseUrl } = await launch(testConfig(), fake, { runFiles: seeded.runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
    const complete = await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" });
    expect(complete.status).toBe(200);
    expect((await monitorOf(complete))["status"]).toBe("cancelled");
    await waitFor(() => pauseRejected);
    expect((await pollRunUntilStatus(baseUrl, "e-1", "cancelled"))["status"]).toBe("cancelled");
    await waitFor(() => !existsSync(seeded.artifactsRunDir));
    expect((await fetch(`${baseUrl}/v1/runs/e-1/files`)).status).toBe(410);
  });

  it("refuses to complete a run that is still working, with the machine-readable code", async () => {
    const { baseUrl } = await launch(testConfig(), () => new Promise<RunReportResult>(() => {}));
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const complete = await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" });
    expect(complete.status).toBe(409);
    expect(await complete.json()).toEqual({ code: "not_completable", status: "running" });
  });

  it("returns 404 when completing an unknown run", async () => {
    const { baseUrl } = await launch();
    const response = await fetch(`${baseUrl}/v1/runs/ghost/complete`, { method: "POST" });
    expect(response.status).toBe(404);
  });

  it("purges local run data when retention prunes a never-completed run", async () => {
    const seeded = seedRetainedData();
    const documentStore = memoryDocumentStore();
    let nowMs = Date.now();
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), {
      now: () => nowMs,
      documentStore,
      runFiles: seeded.runFiles,
    });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    nowMs += DAY_MS + 1000;
    expect((await fetch(`${baseUrl}/v1/runs/e-1`)).status).toBe(404);
    await waitFor(() => documentStore.deletes.includes("e-1"));
    await waitFor(() => !existsSync(seeded.artifactsRunDir));
    await waitFor(() => !existsSync(seeded.traceRunDir));
  });

  it("runs a release's own purge even when the predecessor release rejected", async () => {
    let openGate: () => void = () => {};
    const gate = new Promise<void>((resolve) => {
      openGate = resolve;
    });
    const purges: string[] = [];
    let firstPurge = true;
    const runFiles = stubRunFilesView({
      purge: async (executionId) => {
        purges.push(executionId);
        if (firstPurge) {
          firstPurge = false;
          await gate;
          throw new Error("purge refused");
        }
      },
    });
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { runFiles });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect((await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" })).status).toBe(200);
    await waitFor(() => purges.length === 1);
    expect((await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" })).status).toBe(200);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(purges).toEqual(["e-1"]);
    openGate();
    await waitFor(() => purges.length === 2);
    expect(purges).toEqual(["e-1", "e-1"]);
  });

  it("serializes a reaped predecessor's purge before a document-less restart begins", async () => {
    let releasePurge: () => void = () => {};
    const purgeGate = new Promise<void>((resolve) => {
      releasePurge = resolve;
    });
    const purged: string[] = [];
    const runFiles = stubRunFilesView({
      purge: async (executionId) => {
        await purgeGate;
        purged.push(executionId);
      },
    });
    const purgesSeenAtStart: number[] = [];
    let nowMs = Date.now();
    const { baseUrl } = await launch(
      testConfig(),
      () => {
        purgesSeenAtStart.push(purged.length);
        return Promise.resolve(stubResult);
      },
      { now: () => nowMs, runFiles },
    );
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    nowMs += DAY_MS + 1000;
    const restarting = postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(purgesSeenAtStart).toEqual([0]);
    releasePurge();
    const restarted = await restarting;
    expect(restarted.status).toBe(202);
    await waitFor(() => purgesSeenAtStart.length === 2);
    expect(purgesSeenAtStart[1]).toBe(1);
  });
});

describe("auth", () => {
  const authedConfig = (): ServerConfig => testConfig({ auth: { token: "expected-token" } });

  it("rejects missing, malformed, and wrong tokens with 401 naming the Bearer scheme", async () => {
    const { baseUrl } = await launch(authedConfig());
    const body = JSON.stringify({ executionId: "e", prompt: "p" });
    for (const headers of [
      {},
      { authorization: "expected-token" },
      { authorization: "Bearer wrong" },
      { authorization: "Bearer " },
    ]) {
      const response = await postRuns(baseUrl, body, headers);
      expect(response.status).toBe(401);
      expect(response.headers.get("www-authenticate")).toBe("Bearer");
    }
  });

  it("guards the monitor, cancel, input, and files routes too", async () => {
    const { baseUrl } = await launch(authedConfig());
    const monitor = await fetch(`${baseUrl}/v1/runs/e-1`);
    expect(monitor.status).toBe(401);
    const cancel = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect(cancel.status).toBe(401);
    const input = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: "t", questionId: "q-1", answer: "" }),
    );
    expect(input.status).toBe(401);
    const files = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(files.status).toBe(401);
    const file = await fetch(`${baseUrl}/v1/runs/e-1/files/out/report.md`);
    expect(file.status).toBe(401);
    const complete = await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" });
    expect(complete.status).toBe(401);
  });

  it("accepts the correct bearer token", async () => {
    const { baseUrl } = await launch(authedConfig());
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e", prompt: "p" }), {
      authorization: "Bearer expected-token",
    });
    expect(response.status).toBe(202);
  });

  it("keeps healthz open without a token", async () => {
    const { baseUrl } = await launch(authedConfig());
    const response = await fetch(`${baseUrl}/healthz`);
    expect(response.status).toBe(200);
  });
});

describe("GET /v1/limits", () => {
  it("serves default, ceiling, and unit per fuse from the deployed configuration", async () => {
    const tightened = testConfig();
    tightened.fuses = {
      ...tightened.fuses,
      deadlineMs: { defaultValue: 3000, ceiling: 5000 },
    };
    const { baseUrl } = await launch(tightened);
    const response = await fetch(`${baseUrl}/v1/limits`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({
      deadlineMs: { default: 3000, ceiling: 5000, unit: "milliseconds" },
      maxBudgetUsd: { default: 1, ceiling: 1, unit: "usd" },
      maxTurns: { default: 10, ceiling: 10, unit: "turns" },
      maxRetainedOutputBytes: {
        default: DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
        ceiling: RETAINED_OUTPUT_WORST_CASE_BYTES,
        unit: "bytes",
      },
    });
  });

  it("sits behind the same bearer auth as the run routes", async () => {
    const { baseUrl } = await launch(testConfig({ auth: { token: "expected-token" } }));
    const unauthenticated = await fetch(`${baseUrl}/v1/limits`, { headers: NO_REUSE_HEADERS });
    expect(unauthenticated.status).toBe(401);
    expect(unauthenticated.headers.get("www-authenticate")).toBe("Bearer");
    const authenticated = await fetch(`${baseUrl}/v1/limits`, {
      headers: { authorization: "Bearer expected-token", ...NO_REUSE_HEADERS },
    });
    expect(authenticated.status).toBe(200);
  });

  it("reserves nothing: hammering it leaves the budgets able to admit a run", async () => {
    const { baseUrl } = await launch();
    const responses = await Promise.all(
      Array.from({ length: 25 }, () => fetch(`${baseUrl}/v1/limits`, { headers: NO_REUSE_HEADERS })),
    );
    for (const response of responses) {
      expect(response.status).toBe(200);
    }
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);
  });
});

describe("GET /v1/environment", () => {
  it("serves the runtime version and the exact environment section every run's system prompt opens with", async () => {
    const { baseUrl } = await launch(
      testConfig({ buildIdentity: { version: "0.4.1001", revision: "abc1234" } }),
    );
    const response = await fetch(`${baseUrl}/v1/environment`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(200);
    expect(response.headers.get("content-type")).toBe("application/json");
    expect(await response.json()).toEqual({ runtimeVersion: "0.4.1001", text: RUNTIME_ENVIRONMENT_SECTION });
    expect(composeSystemPrompt(undefined)).toBe(RUNTIME_ENVIRONMENT_SECTION);
  });

  it("sits behind the same bearer auth as the run routes", async () => {
    const { baseUrl } = await launch(testConfig({ auth: { token: "expected-token" } }));
    const unauthenticated = await fetch(`${baseUrl}/v1/environment`, { headers: NO_REUSE_HEADERS });
    expect(unauthenticated.status).toBe(401);
    const authenticated = await fetch(`${baseUrl}/v1/environment`, {
      headers: { authorization: "Bearer expected-token", ...NO_REUSE_HEADERS },
    });
    expect(authenticated.status).toBe(200);
  });

  it("is read-only and has no sub-paths", async () => {
    const { baseUrl } = await launch();
    const posted = await fetch(`${baseUrl}/v1/environment`, { method: "POST", headers: NO_REUSE_HEADERS });
    expect(posted.status).toBe(405);
    const nested = await fetch(`${baseUrl}/v1/environment/tools`, { headers: NO_REUSE_HEADERS });
    expect(nested.status).toBe(404);
  });
});

describe("SIGTERM drain", () => {
  it("fails an in-flight run with runtime_shutdown and aborts its work", async () => {
    let aborted = false;
    const hangingWithSignal: SpawnRunFn = (_params, options) =>
      new Promise((_, reject) => {
        options?.signal?.addEventListener(
          "abort",
          () => {
            aborted = true;
            reject(new Error("aborted by drain"));
          },
          { once: true },
        );
      });
    const { server, baseUrl } = await launch(testConfig(), hangingWithSignal);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-running", prompt: "p" }));
    expect(server.drainRuns()).toBe(1);
    const running = await pollRunUntilStatus(baseUrl, "e-running", "failed");
    expect(running["error"]).toEqual({ subtype: "runtime_shutdown" });
    await waitFor(() => aborted);
  });

  it("fails a parked run too — a checkpoint cannot survive the restart, so a zombie would lie", async () => {
    let pauseRejected = false;
    const fake: SpawnRunFn = async (_params, options) => {
      if (options?.pause === undefined) {
        throw new Error("pause seam missing");
      }
      try {
        await options.pause(pauseRequest());
      } catch {
        pauseRejected = true;
        throw new Error("unwound by drain");
      }
      return stubResult;
    };
    const { server, baseUrl } = await launch(testConfig(), fake);
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-parked", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-parked", "awaiting_input");
    expect(server.drainRuns()).toBe(1);
    const parkedMonitor = await pollRunUntilStatus(baseUrl, "e-parked", "failed");
    expect(parkedMonitor["error"]).toEqual({ subtype: "runtime_shutdown" });
    expect(parkedMonitor["requiredInput"]).toBeNull();
    await waitFor(() => pauseRejected);
  });

  it("drains nothing when every run is already terminal", async () => {
    const { server, baseUrl } = await launch();
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect(server.drainRuns()).toBe(0);
  });

  it("refuses a start that lands after the drain with 503 — an accepted run would never settle", async () => {
    let called = false;
    const { server, baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    server.drainRuns();
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-late", prompt: "p" }));
    expect(response.status).toBe(503);
    expect(called).toBe(false);
    expect((await fetch(`${baseUrl}/v1/runs/e-late`)).status).toBe(404);
  });
});

describe("body read stall timer", () => {
  it("bounds the whole read by the route's own body cap at the floor throughput", () => {
    expect(bodyReadBudgetMs(1024 * 1024, 30_000)).toBe(31_000);
    // 30s base plus 384s at the 1 MiB/s floor for a 384 MiB route — inside the ALB's 960s idle timeout.
    expect(bodyReadBudgetMs(384 * 1024 * 1024, 30_000)).toBe(414_000);
  });

  it("tears down an upload that stalls, however large its declared Content-Length", async () => {
    const { server } = await launch(
      testConfig({ bodyLimitBytes: 384 * 1024 * 1024, requestPhaseTimeoutMs: 150 }),
    );
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const startedAt = Date.now();
    const closedInMs = await new Promise<number>((resolve, reject) => {
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write(
          "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
            "content-length: 402653184\r\n\r\npartial",
        );
      });
      socket.on("close", () => resolve(Date.now() - startedAt));
      socket.on("error", () => resolve(Date.now() - startedAt));
      setTimeout(() => reject(new Error("stalled giant upload was never torn down")), 5000);
    });
    expect(closedInMs).toBeLessThan(2000);
  });

  it("keeps a chunked upload alive as long as it makes progress, past the base window", async () => {
    const { server } = await launch(testConfig({ bodyLimitBytes: 1024 * 1024, requestPhaseTimeoutMs: 250 }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    // Each chunk is well inside the 250ms stall window but the whole body takes ~2x it — only a
    // progress-reset timer accepts this; keep chunk count low or CI timer drift fires the teardown early.
    const body = JSON.stringify({ executionId: "e-slow", prompt: "p".repeat(2048) });
    const chunks = 10;
    const pieceLength = Math.ceil(body.length / chunks);
    const rawResponse = await new Promise<string>((resolve, reject) => {
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write(
          "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
            "transfer-encoding: chunked\r\n\r\n",
        );
        let sent = 0;
        const timer = setInterval(() => {
          const piece = body.slice(sent * pieceLength, (sent + 1) * pieceLength);
          sent += 1;
          if (piece === "") {
            clearInterval(timer);
            socket.write("0\r\n\r\n");
            return;
          }
          socket.write(`${Buffer.byteLength(piece).toString(16)}\r\n${piece}\r\n`);
        }, 50);
        socket.once("close", () => clearInterval(timer));
      });
      let data = "";
      socket.on("data", (chunk) => {
        data += chunk.toString("utf8");
        if (data.includes("\r\n\r\n")) {
          socket.destroy();
          resolve(data);
        }
      });
      socket.on("close", () => resolve(data));
      socket.on("error", reject);
      setTimeout(() => reject(new Error("no response to the trickled request")), 5000);
    });
    expect(rawResponse).toContain("202");
  });

  it("tears down a file download once its client stops reading — the response phase is policed too", async () => {
    const fileBytes = 8 * 1024 * 1024;
    const base = mkdtempSync(join(tmpdir(), "server-download-stall-"));
    const artifactsRoot = join(base, "artifacts");
    const workspacePath = join(base, "run-x");
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "big.bin"), Buffer.alloc(fileBytes, 7));
    publishRunArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1, workspacePath });
    const runFiles = createRunFilesView({
      artifactsRoot,
      traceRoot: join(base, "traces"),
      removeTree: removeOwnTree,
    });
    const { server, baseUrl } = await launch(
      testConfig({ requestPhaseTimeoutMs: 250 }),
      signalAwareHangingFake(),
      { runFiles },
    );
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    let received = 0;
    const socket = connect(address.port, "127.0.0.1");
    socket.on("data", (chunk) => {
      received += chunk.length;
    });
    socket.on("error", () => {});
    await new Promise<void>((resolve) => socket.once("connect", () => resolve()));
    // Stop reading immediately: the kernel buffers fill, progress stops, and the stall timer destroys it.
    socket.pause();
    socket.write("GET /v1/runs/e-1/files/out/big.bin HTTP/1.1\r\nhost: localhost\r\n\r\n");
    await new Promise((resolve) => setTimeout(resolve, 1500));
    socket.resume();
    await new Promise<void>((resolve) => socket.once("close", () => resolve()));
    // Without the teardown the whole 8 MiB (plus headers) would drain here.
    expect(received).toBeLessThan(fileBytes);
  });
});

describe("per-route body limits", () => {
  it("caps the input route far below the documents route", async () => {
    const { baseUrl } = await launch(
      testConfig({ bodyLimitBytes: 8 * 1024 * 1024, documentBudgetBytes: 8 * 1024 * 1024 }),
      signalAwareHangingFake(),
    );
    const sixMiB = 6 * 1024 * 1024;
    const startResponse = await postRuns(
      baseUrl,
      JSON.stringify({
        executionId: "e-1",
        prompt: "p",
        documents: [1, 2, 3].map((part) => ({
          name: `part-${part}.bin`,
          contentBase64: "A".repeat(sixMiB / 3),
        })),
      }),
    );
    expect(startResponse.status).toBe(202);
    const oversized = JSON.stringify({ resumeToken: "t", questionId: "q-1", answer: "x".repeat(sixMiB) });
    const inputResponse = await postInput(baseUrl, "e-1", oversized, { connection: "close" });
    expect(inputResponse.status).toBe(413);
  });

  it("rejects an answer over the 1 MiB cap with 400 and accepts one at the cap", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const overCap = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: "t", questionId: "q-1", answer: "x".repeat(MAX_ANSWER_CHARS + 1) }),
    );
    expect(overCap.status).toBe(400);
    const message = asRecord(await overCap.json())["error"];
    if (typeof message !== "string") {
      throw new Error("expected an error message");
    }
    expect(message).toContain("answer");
    // At the cap the request passes validation and reaches the state machine.
    const atCap = await postInput(
      baseUrl,
      "e-1",
      JSON.stringify({ resumeToken: "t", questionId: "q-1", answer: "x".repeat(MAX_ANSWER_CHARS) }),
    );
    expect(atCap.status).toBe(409);
  });
});

describe("cancel racing the document save", () => {
  it("never starts a run whose cancel arrived while its documents were persisting", async () => {
    const documentStore = memoryDocumentStore({ holdSaves: true });
    let runStarted = false;
    const { baseUrl } = await launch(
      testConfig(),
      (params, options) => {
        runStarted = true;
        return signalAwareHangingFake()(params, options);
      },
      { documentStore },
    );
    const startPending = postRuns(baseUrl, startBodyWith([docOf("held")]));
    await waitFor(() => documentStore.heldSaves() === 1);
    const cancelResponse = await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    expect(cancelResponse.status).toBe(202);
    documentStore.releaseSaves();
    await startPending;
    const body = await pollRunUntilStatus(baseUrl, "e-1", "cancelled");
    expect(body["status"]).toBe("cancelled");
    expect(runStarted).toBe(false);
  });
});

function retryAfterOf(response: Response): number {
  return Number(response.headers.get("retry-after"));
}

function portOf(server: Server): number {
  const address = server.address();
  if (address === null || typeof address === "string") {
    throw new Error("no port");
  }
  return address.port;
}

type RawClient = {
  write: (data: string) => void;
  received: () => string;
  status: () => Promise<number>;
  destroy: () => void;
};

function openRawClient(port: number): Promise<RawClient> {
  return new Promise((resolve, reject) => {
    let data = "";
    let connected = false;
    const socket = connect(port, "127.0.0.1", () => {
      connected = true;
      resolve({
        write: (piece) => socket.write(piece),
        received: () => data,
        status: async () => {
          for (let i = 0; i < 600; i += 1) {
            const match = /^HTTP\/1\.1 (\d{3})/.exec(data);
            if (match?.[1] !== undefined) {
              return Number(match[1]);
            }
            await new Promise((tick) => setTimeout(tick, 5));
          }
          throw new Error(`no response arrived; received: ${data}`);
        },
        destroy: () => socket.destroy(),
      });
    });
    socket.on("data", (chunk) => {
      data += chunk.toString("utf8");
    });
    socket.on("error", (error) => {
      if (!connected) {
        reject(error);
      }
    });
  });
}

function chunkOf(piece: string): string {
  return `${Buffer.byteLength(piece).toString(16)}\r\n${piece}\r\n`;
}

const CHUNKED_START_HEAD =
  "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
  "transfer-encoding: chunked\r\n\r\n";

// At the production budget:route ratio of exactly 1.0 — a smaller ratio fails to reproduce this failure mode.
describe("upload admission charge", () => {
  it("admits concurrent undeclared-length starts at the production budget:route ratio of 1.0", async () => {
    const cap = 256 * 1024;
    const config = testConfig({
      bodyLimitBytes: cap,
      inflightUploadBudgetBytes: cap,
      requestPhaseTimeoutMs: 5000,
    });
    const { server, baseUrl } = await launch(config);
    const executionIds = ["e-a", "e-b", "e-c"];
    const clients: { client: RawClient; rest: string }[] = [];
    for (const executionId of executionIds) {
      const client = await openRawClient(portOf(server));
      const body = JSON.stringify({ executionId, prompt: "p" });
      client.write(CHUNKED_START_HEAD + chunkOf(body.slice(0, 10)));
      clients.push({ client, rest: body.slice(10) });
    }
    // All three in flight, none finished: under a worst-case charge the first would hold the whole budget.
    await new Promise((resolve) => setTimeout(resolve, 100));
    for (const { client, rest } of clients) {
      client.write(`${chunkOf(rest)}0\r\n\r\n`);
    }
    const statuses = await Promise.all(clients.map(({ client }) => client.status()));
    expect(statuses).toEqual([202, 202, 202]);
    for (const { client } of clients) {
      client.destroy();
    }
    for (const executionId of executionIds) {
      expect((await fetch(`${baseUrl}/v1/runs/${executionId}`)).status).toBe(200);
    }
  });

  it("refuses an undeclared body mid-read only once the budget is genuinely exceeded, after the drain", async () => {
    const cap = 1024 * 1024;
    const config = testConfig({
      bodyLimitBytes: cap,
      inflightUploadBudgetBytes: cap,
      requestPhaseTimeoutMs: 5000,
    });
    const { server, baseUrl } = await launch(config);
    const holder = await openRawClient(portOf(server));
    holder.write(
      "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
        "content-length: 900000\r\n\r\nx",
    );
    await new Promise((resolve) => setTimeout(resolve, 50));
    const chunked = await openRawClient(portOf(server));
    chunked.write(CHUNKED_START_HEAD + chunkOf("y".repeat(1024)));
    await new Promise((resolve) => setTimeout(resolve, 100));
    // Admitted at the provisional charge: no refusal before the bytes arrive.
    expect(chunked.received()).toBe("");
    chunked.write(`${chunkOf("y".repeat(200_000))}0\r\n\r\n`);
    expect(await chunked.status()).toBe(503);
    expect(chunked.received()).toContain('"reason":"upload_budget"');
    chunked.destroy();
    holder.destroy();
    const admitted = await retryUntilAdmitted(
      baseUrl,
      JSON.stringify({ executionId: "e-after", prompt: "p" }),
    );
    expect(admitted.status).toBe(202);
  });

  it("answers a declaration past the route cap with 413 before reserving anything", async () => {
    const { events, logger } = eventsLogger();
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 64 }), undefined, { logger });
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e", prompt: "x".repeat(200) }));
    expect(response.status).toBe(413);
    expect(
      events.filter(
        (entry) => entry.event === "budget_reserved" && entry.fields["budget"] === "inflight_upload",
      ),
    ).toHaveLength(0);
  });

  it("answers an upload-budget refusal with its reason and a jittered, estimate-derived Retry-After", async () => {
    const config = testConfig({
      bodyLimitBytes: 4096,
      inflightUploadBudgetBytes: 4096,
      requestPhaseTimeoutMs: 5000,
    });
    const { server, baseUrl } = await launch(config, undefined, { random: () => 1 });
    const holder = await openRawClient(portOf(server));
    holder.write(
      "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
        "content-length: 4096\r\n\r\n{",
    );
    await new Promise((resolve) => setTimeout(resolve, 50));
    const refused = await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }));
    expect(refused.status).toBe(503);
    expect(await refused.json()).toEqual({ error: "capacity", reason: "upload_budget" });
    // Cold-start body-read estimate of 5 s, jittered upward by the full ratio.
    expect(refused.headers.get("retry-after")).toBe("7");
    holder.destroy();
  });

  it("never tags budget logs with an executionId the validator would refuse, nor from a non-start body", async () => {
    const { events, logger } = eventsLogger();
    const { baseUrl } = await launch(testConfig(), undefined, { logger });
    const hostile = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "not a legal id! ".repeat(4), prompt: "p" }),
    );
    expect(hostile.status).toBe(400);
    await waitFor(() =>
      events.some(
        (entry) => entry.event === "budget_released" && entry.fields["budget"] === "inflight_upload",
      ),
    );
    const released = events.filter(
      (entry) => entry.event === "budget_released" && entry.fields["budget"] === "inflight_upload",
    );
    expect(released[0]?.fields["executionId"]).toBeUndefined();
    const smuggled = await fetch(`${baseUrl}/v1/runs/e-url/input`, {
      method: "POST",
      headers: { "content-type": "application/json", ...NO_REUSE_HEADERS },
      body: JSON.stringify({ executionId: "e-smuggled", questionId: "q", answer: "a" }),
    });
    expect(smuggled.status).not.toBe(500);
    await waitFor(
      () =>
        events.filter(
          (entry) => entry.event === "budget_released" && entry.fields["budget"] === "inflight_upload",
        ).length >= 2,
    );
    const inputReleased = events.find(
      (entry) => entry.event === "budget_released" && entry.fields["path"] === "/v1/runs/e-url/input",
    );
    expect(inputReleased).toBeDefined();
    expect(inputReleased?.fields["executionId"]).toBeUndefined();
  });

  it("ties budget reservations to the run that took them on the logs", async () => {
    const { events, logger } = eventsLogger();
    const { baseUrl } = await launch(testConfig(), undefined, { logger });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-tie", prompt: "p", maxRetainedOutputBytes: 1024 }),
    );
    expect(response.status).toBe(202);
    await waitFor(() =>
      events.some(
        (entry) => entry.event === "budget_released" && entry.fields["budget"] === "inflight_upload",
      ),
    );
    const released = events.find(
      (entry) => entry.event === "budget_released" && entry.fields["budget"] === "inflight_upload",
    );
    expect(released?.fields["executionId"]).toBe("e-tie");
    const reserved = events.find(
      (entry) => entry.event === "budget_reserved" && entry.fields["budget"] === "inflight_upload",
    );
    expect(reserved?.fields["path"]).toBe("/v1/runs");
    const output = events.find(
      (entry) => entry.event === "budget_reserved" && entry.fields["budget"] === "retained_output",
    );
    expect(output?.fields["executionId"]).toBe("e-tie");
  });
});

describe("admission control", () => {
  it("refuses a start that would overrun the in-flight upload budget, and admits it once freed", async () => {
    const config = testConfig({
      bodyLimitBytes: 4096,
      inflightUploadBudgetBytes: 4096,
      requestPhaseTimeoutMs: 5000,
    });
    const { server, baseUrl } = await launch(config, () => Promise.resolve(stubResult));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const socket = connect(address.port, "127.0.0.1", () => {
      socket.write(
        "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
          "content-length: 4096\r\n\r\n{",
      );
    });
    socket.on("error", () => {});
    await new Promise((resolve) => setTimeout(resolve, 50));
    const refused = await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }));
    expect(refused.status).toBe(503);
    expect(retryAfterOf(refused)).toBeWithin(5, 8);
    expect(await refused.json()).toEqual({ error: "capacity", reason: "upload_budget" });
    socket.destroy();
    let admitted = refused;
    for (let attempt = 0; attempt < 100 && admitted.status !== 202; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 10));
      admitted = await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }));
    }
    expect(admitted.status).toBe(202);
  });

  it("refuses a start whose staged inputs would overrun the retained-input budget", async () => {
    const config = testConfig({ bodyLimitBytes: 65536, retainedInputBudgetBytes: 8 });
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), { documentStore });
    const startBody = (executionId: string): string =>
      JSON.stringify({
        executionId,
        prompt: "p",
        documents: [{ name: "a.txt", contentBase64: Buffer.from("hello").toString("base64") }],
      });
    expect((await postRuns(baseUrl, startBody("e-1"))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const refused = await postRuns(baseUrl, startBody("e-2"));
    expect(refused.status).toBe(503);
    expect(retryAfterOf(refused)).toBeWithin(5, 8);
    expect(await refused.json()).toEqual({ error: "capacity", reason: "retained_input_budget" });
    expect(documentStore.saved.has("e-2")).toBe(false);
    expect((await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" })).status).toBe(200);
    expect((await retryUntilAdmitted(baseUrl, startBody("e-2"))).status).toBe(202);
  });

  it("refuses a start while uncollected runs hold the retained-output budget, admitting after complete", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: RETAINED_OUTPUT_WORST_CASE_BYTES });
    const runFiles = entriesRunFilesView([
      { path: "out/report.pdf", sizeBytes: RETAINED_OUTPUT_WORST_CASE_BYTES },
    ]);
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), { runFiles, documentStore });
    const fullDeclaration = JSON.stringify({
      executionId: "e-1",
      prompt: "p",
      maxRetainedOutputBytes: RETAINED_OUTPUT_WORST_CASE_BYTES,
    });
    expect((await postRuns(baseUrl, fullDeclaration)).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const startBody = JSON.stringify({
      executionId: "e-2",
      prompt: "p",
      documents: [{ name: "a.txt", contentBase64: Buffer.from("hello").toString("base64") }],
    });
    const refused = await postRuns(baseUrl, startBody);
    expect(refused.status).toBe(503);
    expect(retryAfterOf(refused)).toBeWithin(5, 8);
    expect(await refused.json()).toEqual({ error: "capacity", reason: "retained_output_budget" });
    expect(documentStore.saved.has("e-2")).toBe(false);
    expect((await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" })).status).toBe(200);
    expect((await retryUntilAdmitted(baseUrl, startBody)).status).toBe(202);
  });

  it("accounts an admitted run at its declared cap until its retained bytes are measured at terminal", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: RETAINED_OUTPUT_WORST_CASE_BYTES + 1024 });
    const runFiles = entriesRunFilesView([
      { path: "out/report.md", sizeBytes: 8 },
      { path: "trace/trace.ndjson", sizeBytes: 20 },
    ]);
    const { baseUrl } = await launch(config, signalAwareHangingFake(), { runFiles });
    const fullDeclaration = JSON.stringify({
      executionId: "e-1",
      prompt: "p",
      maxRetainedOutputBytes: RETAINED_OUTPUT_WORST_CASE_BYTES,
    });
    expect((await postRuns(baseUrl, fullDeclaration)).status).toBe(202);
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }))).status).toBe(503);
    expect((await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" })).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "cancelled");
    const admitted = await retryUntilAdmitted(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }));
    expect(admitted.status).toBe(202);
  });

  it("ignores a stale settle from a replaced run, keeping the live run's declared charge", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: RETAINED_OUTPUT_WORST_CASE_BYTES });
    let releaseList = (): void => {};
    const listGate = new Promise<void>((resolve) => {
      releaseList = resolve;
    });
    const runFiles = stubRunFilesView({
      list: async () => {
        await listGate;
        return [];
      },
    });
    let failedOnce = false;
    const runReportFn: SpawnRunFn = (params, options) => {
      if (!failedOnce) {
        failedOnce = true;
        return Promise.reject(new Error("first attempt dies retryably"));
      }
      return signalAwareHangingFake()(params, options);
    };
    const { baseUrl } = await launch(config, runReportFn, { runFiles });
    const body = JSON.stringify({
      executionId: "e-1",
      prompt: "p",
      maxRetainedOutputBytes: RETAINED_OUTPUT_WORST_CASE_BYTES,
    });
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect((await retryUntilAdmitted(baseUrl, body)).status).toBe(202);
    releaseList();
    await new Promise((resolve) => setTimeout(resolve, 25));
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }))).status).toBe(503);
  });

  it("releases the declared output charge when the staged-input reservation refuses the start", async () => {
    const config = testConfig({
      bodyLimitBytes: 65536,
      retainedInputBudgetBytes: 4,
      retainedOutputBudgetBytes: RETAINED_OUTPUT_WORST_CASE_BYTES,
    });
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), { documentStore });
    const refused = await postRuns(
      baseUrl,
      JSON.stringify({
        executionId: "e-1",
        prompt: "p",
        documents: [{ name: "a.txt", contentBase64: Buffer.from("hello").toString("base64") }],
      }),
    );
    expect(refused.status).toBe(503);
    expect(documentStore.saved.has("e-1")).toBe(false);
    // Only admissible if e-1's output charge came back in full: e-2 asks for the whole budget.
    const admitted = await retryUntilAdmitted(
      baseUrl,
      JSON.stringify({
        executionId: "e-2",
        prompt: "p",
        maxRetainedOutputBytes: RETAINED_OUTPUT_WORST_CASE_BYTES,
      }),
    );
    expect(admitted.status).toBe(202);
  });

  it("releases a reaped run's retained-output charge with its files", async () => {
    const clock = { now: Date.now() };
    const config = testConfig({ retainedOutputBudgetBytes: RETAINED_OUTPUT_WORST_CASE_BYTES });
    const runFiles = entriesRunFilesView([
      { path: "out/report.pdf", sizeBytes: RETAINED_OUTPUT_WORST_CASE_BYTES },
    ]);
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), {
      runFiles,
      now: () => clock.now,
    });
    const bodyOf = (executionId: string): string =>
      JSON.stringify({
        executionId,
        prompt: "p",
        maxRetainedOutputBytes: RETAINED_OUTPUT_WORST_CASE_BYTES,
      });
    expect((await postRuns(baseUrl, bodyOf("e-1"))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect((await postRuns(baseUrl, bodyOf("e-2"))).status).toBe(503);
    clock.now += DAY_MS + 60_000;
    expect((await fetch(`${baseUrl}/v1/runs/e-1`)).status).toBe(404);
    const admitted = await retryUntilAdmitted(baseUrl, bodyOf("e-2"));
    expect(admitted.status).toBe(202);
  });

  it("sheds a saturated start immediately, consuming neither the executionId nor the bytes", async () => {
    const documentStore = memoryDocumentStore();
    const admission = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 0, random: () => 0 });
    for (let i = 0; i < 8; i += 1) {
      admission.recordRunDuration(LONG_RUN_MS);
    }
    const { baseUrl } = await launch(
      testConfig({ bodyLimitBytes: 65536 }),
      () => Promise.resolve(stubResult),
      { documentStore, admission },
    );
    const refused = await postRuns(
      baseUrl,
      JSON.stringify({
        executionId: "e-1",
        prompt: "p",
        documents: [{ name: "a.txt", contentBase64: Buffer.from("hello").toString("base64") }],
      }),
    );
    expect(refused.status).toBe(503);
    expect(refused.headers.get("retry-after")).toBe(String(Math.ceil(LONG_RUN_MS / 32 / 1000)));
    expect(await refused.json()).toEqual({ error: "capacity", reason: "admission_shed" });
    expect((await fetch(`${baseUrl}/v1/runs/e-1`)).status).toBe(404);
    expect(documentStore.saved.has("e-1")).toBe(false);
  });

  it("admits a start while a run slot is free, and after a bounded wait once one frees", async () => {
    const slots = { free: 1 };
    const admission = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => slots.free });
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);
    slots.free = 0;
    const waiting = postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }));
    await waitFor(() => admission.waitingCount() === 1);
    slots.free = 1;
    admission.notifySlotFreed();
    expect((await waiting).status).toBe(202);
  });

  it("answers an already-registered executionId from the registry even when saturated", async () => {
    const slots = { free: 1 };
    const admission = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => slots.free });
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const body = JSON.stringify({ executionId: "e-1", prompt: "p" });
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    slots.free = 0;
    for (let i = 0; i < 8; i += 1) {
      admission.recordRunDuration(LONG_RUN_MS);
    }
    const retried = await postRuns(baseUrl, body);
    expect(retried.status).toBe(200);
    expect((await monitorOf(retried))["status"]).toBe("running");
  });
});

describe("admission classes", () => {
  const RECOMMENDED_CHAT_RESERVE = 4;

  function classedAdmission(slots: { free: number }): AdmissionQueue {
    return createAdmissionQueue(
      {
        poolSize: 32,
        targetWaitMs: 60_000,
        chatReservedSlots: RECOMMENDED_CHAT_RESERVE,
        chatMaxSlots: DEFAULT_CHAT_MAX_SLOTS,
      },
      { freeSlots: () => slots.free },
    );
  }

  it("refuses an admission class it does not know before anything is admitted", async () => {
    const admission = classedAdmission({ free: 32 });
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", admissionClass: "urgent" }),
    );
    expect(response.status).toBe(400);
    expect(admission.waitingCount()).toBe(0);
    expect(admission.chatSlotsHeld()).toBe(0);
  });

  it("refuses chat as chat_disabled with no Retry-After when the chat cap is zero", async () => {
    const admission = createAdmissionQueue(
      { poolSize: 32, chatReservedSlots: 0, chatMaxSlots: 0 },
      { freeSlots: () => 32 },
    );
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const refused = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", admissionClass: "chat" }),
    );
    expect(refused.status).toBe(503);
    expect(refused.headers.get("retry-after")).toBeNull();
    expect(await refused.json()).toEqual({ error: "capacity", reason: "chat_disabled" });
    const workflow = await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }));
    expect(workflow.status).toBe(202);
  });

  it("holds a chat slot for a chat run's whole life and gives it back when the run settles", async () => {
    const admission = classedAdmission({ free: 32 });
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const started = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", admissionClass: "chat" }),
    );
    expect(started.status).toBe(202);
    expect(admission.chatSlotsHeld()).toBe(1);
    await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    await waitFor(() => admission.chatSlotsHeld() === 0);
    expect(admission.chatSlotsHeld()).toBe(0);
  });

  it("admits a chat turn into the reserve while a workflow start waits for a slot of its own", async () => {
    const slots = { free: RECOMMENDED_CHAT_RESERVE };
    const admission = classedAdmission(slots);
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const workflow = postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await waitFor(() => admission.waitingCount() === 1);
    const chat = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-2", prompt: "p", admissionClass: "chat" }),
    );
    expect(chat.status).toBe(202);
    expect(admission.waitingCount()).toBe(1);
    admission.drain();
    expect((await workflow).status).toBe(202);
  });
});

describe("bounded admission wait", () => {
  // Production constants on purpose: a small burst must queue under the real target and pool size.
  function waitingQueue(slots: { free: number }): AdmissionQueue {
    return createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => slots.free });
  }

  it("admits a burst larger than the free slots in FIFO order", async () => {
    const slots = { free: 0 };
    const admission = waitingQueue(slots);
    const started: string[] = [];
    const runReportFn: SpawnRunFn = (params, options) => {
      started.push(params.executionId);
      return signalAwareHangingFake()(params, options);
    };
    const { baseUrl } = await launch(testConfig(), runReportFn, { admission });
    const responses: Promise<Response>[] = [];
    for (const executionId of ["e-a", "e-b", "e-c"]) {
      responses.push(postRuns(baseUrl, JSON.stringify({ executionId, prompt: "p" })));
      await waitFor(() => admission.waitingCount() === responses.length);
    }
    slots.free = 1;
    for (let admitted = 0; admitted < responses.length; admitted += 1) {
      admission.notifySlotFreed();
      const response = responses[admitted];
      if (response === undefined) {
        throw new Error("missing response");
      }
      expect((await response).status).toBe(202);
      expect(started.length).toBe(admitted + 1);
    }
    expect(started).toEqual(["e-a", "e-b", "e-c"]);
  });

  // Exercised with a server-side destroy, which every runtime surfaces (measured in
  // connection-close.test.ts).
  it("evicts a waiter whose socket closes, freeing its queue slot and registering nothing", async () => {
    const slots = { free: 0 };
    const admission = waitingQueue(slots);
    const { server, baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const held: Socket[] = [];
    server.on("connection", (socket) => held.push(socket));
    const body = JSON.stringify({ executionId: "e-gone", prompt: "p" });
    const client = connect(address.port, "127.0.0.1", () => {
      client.write(
        "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
          `content-length: ${Buffer.byteLength(body)}\r\n\r\n${body}`,
      );
    });
    client.on("error", () => {});
    await waitFor(() => admission.waitingCount() === 1);
    for (const socket of held) {
      socket.destroy();
    }
    await waitFor(() => admission.waitingCount() === 0);
    expect((await fetch(`${baseUrl}/v1/runs/e-gone`)).status).toBe(404);
  });

  // Bun since 1.4.0 delivers the client's own close on a fully consumed request (the matrix in
  // connection-close.test.ts), so the same listener now evicts a waiter whose client hangs up;
  // red on 1.3.14 by construction, where only the server-side destroy above reaches it.
  it("evicts a waiter whose client hangs up, on a runtime that delivers the client's close", async () => {
    const slots = { free: 0 };
    const admission = waitingQueue(slots);
    const { server, baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const body = JSON.stringify({ executionId: "e-hung-up", prompt: "p" });
    const client = connect(address.port, "127.0.0.1", () => {
      client.write(
        "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
          `content-length: ${Buffer.byteLength(body)}\r\n\r\n${body}`,
      );
    });
    client.on("error", () => {});
    await waitFor(() => admission.waitingCount() === 1);
    client.end();
    await waitFor(() => admission.waitingCount() === 0);
    expect((await fetch(`${baseUrl}/v1/runs/e-hung-up`)).status).toBe(404);
  });

  it("holds the in-flight upload reservation for the whole wait", async () => {
    const slots = { free: 0 };
    const admission = waitingQueue(slots);
    const config = testConfig({ bodyLimitBytes: 2048, inflightUploadBudgetBytes: 3000 });
    const { baseUrl } = await launch(config, signalAwareHangingFake(), { admission });
    const bodyOf = (executionId: string): string => JSON.stringify({ executionId, prompt: "p".repeat(1500) });
    const waiting = postRuns(baseUrl, bodyOf("e-1"));
    await waitFor(() => admission.waitingCount() === 1);
    const refused = await postRuns(baseUrl, bodyOf("e-2"));
    expect(refused.status).toBe(503);
    expect(retryAfterOf(refused)).toBeWithin(1, 8);
    slots.free = 1;
    admission.notifySlotFreed();
    expect((await waiting).status).toBe(202);
  });

  it("does not count the wait against the slow-client phase timer", async () => {
    const slots = { free: 0 };
    const admission = waitingQueue(slots);
    const { baseUrl } = await launch(testConfig({ requestPhaseTimeoutMs: 100 }), signalAwareHangingFake(), {
      admission,
    });
    const waiting = postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await waitFor(() => admission.waitingCount() === 1);
    await new Promise((resolve) => setTimeout(resolve, 300));
    slots.free = 1;
    admission.notifySlotFreed();
    expect((await waiting).status).toBe(202);
  });

  it("answers a drained waiter with the registry's shutting-down refusal, not a capacity shed", async () => {
    const slots = { free: 0 };
    const admission = waitingQueue(slots);
    const { server, baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const waiting = postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await waitFor(() => admission.waitingCount() === 1);
    server.drainRuns();
    const drained = await waiting;
    expect(drained.status).toBe(503);
    expect(await drained.json()).toEqual({ error: "shutting down" });
    // The latched queue never parks a post-drain arrival either.
    const late = await postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p" }));
    expect(late.status).toBe(503);
    expect(await late.json()).toEqual({ error: "shutting down" });
  });

  it("puts an absorbed wake back when an admitted start turns out to be a duplicate", async () => {
    const slots = { free: 0 };
    const admission = waitingQueue(slots);
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const body = JSON.stringify({ executionId: "e-x", prompt: "p" });
    const first = postRuns(baseUrl, body);
    await waitFor(() => admission.waitingCount() === 1);
    const duplicate = postRuns(baseUrl, body);
    await waitFor(() => admission.waitingCount() === 2);
    const behind = postRuns(baseUrl, JSON.stringify({ executionId: "e-y", prompt: "p" }));
    await waitFor(() => admission.waitingCount() === 3);
    slots.free = 1;
    admission.notifySlotFreed();
    expect((await first).status).toBe(202);
    // The duplicate's admission consumed a wake but no slot; its finally hands the wake on to e-y.
    admission.notifySlotFreed();
    expect((await duplicate).status).toBe(200);
    expect((await behind).status).toBe(202);
  });

  it("settles an undeclared upload's reservation to its measured bytes for the wait", async () => {
    const slots = { free: 0 };
    const admission = waitingQueue(slots);
    const config = testConfig({ bodyLimitBytes: 2048, inflightUploadBudgetBytes: 3000 });
    const { server, baseUrl } = await launch(config, signalAwareHangingFake(), { admission });
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const chunked = JSON.stringify({ executionId: "e-1", prompt: "p" });
    const socket = connect(address.port, "127.0.0.1", () => {
      socket.write(
        "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
          "transfer-encoding: chunked\r\n\r\n" +
          `${Buffer.byteLength(chunked).toString(16)}\r\n${chunked}\r\n0\r\n\r\n`,
      );
    });
    socket.on("error", () => {});
    await waitFor(() => admission.waitingCount() === 1);
    // Undeclared, so admission charged the whole 2048-byte route cap; the post-read settle to its
    // measured ~50 bytes is what leaves room for this second declared body inside the 3000-byte budget.
    const declared = postRuns(baseUrl, JSON.stringify({ executionId: "e-2", prompt: "p".repeat(1500) }));
    await waitFor(() => admission.waitingCount() === 2);
    slots.free = 1;
    // Each wake admits one waiter only once the previous grant has settled, so keep poking until empty.
    await waitFor(() => {
      admission.notifySlotFreed();
      return admission.waitingCount() === 0;
    });
    expect((await declared).status).toBe(202);
    socket.destroy();
  });

  it("feeds each settled run's slot-holding duration back into the estimate", async () => {
    const clock = { now: Date.now() };
    const admission = createAdmissionQueue({ poolSize: 1 }, { freeSlots: () => 1 });
    const runReportFn: SpawnRunFn = () => {
      clock.now += 5000;
      return Promise.resolve(stubResult);
    };
    const { baseUrl } = await launch(testConfig(), runReportFn, {
      admission,
      now: () => clock.now,
    });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    await waitFor(() => admission.estimatedWaitMs(0) === 5000);
  });

  it("keeps a run that never leased a slot out of the estimate", async () => {
    const clock = { now: Date.now() };
    const admission = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 1 });
    const runReportFn: SpawnRunFn = () => {
      clock.now += 1500;
      return Promise.reject(
        new RunFailedError("all uids in use", {
          subtype: "uid_pool_exhausted",
          estimatedCostUsd: 0,
          sessionId: null,
        }),
      );
    };
    const { baseUrl } = await launch(testConfig(), runReportFn, { admission, now: () => clock.now });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "failed");
    // A 1.5ms-scale exhaustion failure held no slot; recording it would collapse p75 to the floor.
    expect(admission.estimatedWaitMs(0)).toBe(COLD_START_RUN_DURATION_MS / 32);
  });

  function countingQueue(): AdmissionQueue & {
    admits: number;
    wakes: number;
    settles: { becameRun: boolean; admissionClass?: AdmissionClass }[];
  } {
    const state = {
      admits: 0,
      wakes: 0,
      settles: [] as { becameRun: boolean; admissionClass?: AdmissionClass }[],
    };
    return {
      get admits() {
        return state.admits;
      },
      get wakes() {
        return state.wakes;
      },
      get settles() {
        return state.settles;
      },
      admit: () => {
        state.admits += 1;
        return Promise.resolve({ outcome: "admitted" });
      },
      recordRunDuration: () => {},
      notifySlotFreed: () => {
        state.wakes += 1;
      },
      settleAdmission: (input) => {
        state.settles.push(input);
      },
      waitingCount: () => 0,
      chatSlotsHeld: () => 0,
      workflowSlotsFree: () => 0,
      releaseChatSlot: () => {},
      drain: () => {},
      estimatedWaitMs: () => 0,
    };
  }

  it("settles a started run's grant exactly once, and mints no wake for it", async () => {
    const admission = countingQueue();
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);
    expect(admission.admits).toBe(1);
    expect(admission.settles).toEqual([{ becameRun: true, admissionClass: "workflow" }]);
    expect(admission.wakes).toBe(0);
  });

  it("neither consults admission nor mints a wake for a retried start of a live run", async () => {
    const admission = countingQueue();
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { admission });
    const body = JSON.stringify({ executionId: "e-1", prompt: "p" });
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    const retried = await postRuns(baseUrl, body);
    expect(retried.status).toBe(200);
    expect(admission.admits).toBe(1);
    expect(admission.settles).toEqual([{ becameRun: true, admissionClass: "workflow" }]);
    expect(admission.wakes).toBe(0);
  });

  it("puts a retryable-failed executionId back through admission instead of letting it barge", async () => {
    const slots = { free: 1 };
    const admission = createAdmissionQueue(
      { poolSize: 32 },
      { freeSlots: () => slots.free, random: () => 0 },
    );
    const outcomes = new Map<string, "fail" | "succeed">([
      ["e-retry", "fail"],
      ["e-done", "succeed"],
    ]);
    const runReportFn: SpawnRunFn = (params) =>
      outcomes.get(params.executionId) === "fail"
        ? Promise.reject(
            new RunFailedError("all uids in use", {
              subtype: "uid_pool_exhausted",
              estimatedCostUsd: 0,
              sessionId: null,
            }),
          )
        : Promise.resolve(stubResult);
    const { baseUrl } = await launch(testConfig(), runReportFn, { admission });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-retry", prompt: "p" }))).status).toBe(
      202,
    );
    await pollRunUntilStatus(baseUrl, "e-retry", "failed");
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-done", prompt: "p" }))).status).toBe(
      202,
    );
    await pollRunUntilStatus(baseUrl, "e-done", "succeeded");
    for (let i = 0; i < 8; i += 1) {
      admission.recordRunDuration(LONG_RUN_MS);
    }
    slots.free = 0;
    const barged = await postRuns(baseUrl, JSON.stringify({ executionId: "e-retry", prompt: "p" }));
    expect(barged.status).toBe(503);
    expect(await barged.json()).toEqual({ error: "capacity", reason: "admission_shed" });
    const duplicate = await postRuns(baseUrl, JSON.stringify({ executionId: "e-done", prompt: "p" }));
    expect(duplicate.status).toBe(200);
  });
});

describe("socket abandonment", () => {
  it("is pre-aborted for a missing or already-destroyed socket", () => {
    expect(socketAbandonment(null).signal.aborted).toBe(true);
    const dead = new Socket();
    dead.destroy();
    expect(socketAbandonment(dead).signal.aborted).toBe(true);
  });

  it("aborts when a live socket closes, and not after unsubscribe", async () => {
    const closing = new Socket();
    const watched = socketAbandonment(closing);
    expect(watched.signal.aborted).toBe(false);
    closing.destroy();
    await waitFor(() => watched.signal.aborted);
    const surviving = new Socket();
    const unsubscribed = socketAbandonment(surviving);
    unsubscribed.unsubscribe();
    surviving.destroy();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(unsubscribed.signal.aborted).toBe(false);
  });
});

describe("retained-output declarations", () => {
  function startBody(executionId: string, maxRetainedOutputBytes?: number): string {
    return JSON.stringify({
      executionId,
      prompt: "p",
      ...(maxRetainedOutputBytes === undefined ? {} : { maxRetainedOutputBytes }),
    });
  }

  // Stateful per executionId, like the real filesystem: a single shared list would let one run's purge
  // empty the measurement another run under test is still mid-teardown on, settling its charge to zero.
  function trackedRunFilesView(
    initial: { path: string; sizeBytes: number }[],
  ): RunFilesView & { purges: string[]; artifactPurges: string[] } {
    const published = new Map<string, RunFileEntry[]>();
    const purges: string[] = [];
    const artifactPurges: string[] = [];
    const filesOf = (executionId: string): RunFileEntry[] => {
      const existing = published.get(executionId);
      if (existing !== undefined) {
        return existing;
      }
      const entries = toRunFileEntries(initial);
      published.set(executionId, entries);
      return entries;
    };
    return {
      ...stubRunFilesView({ list: (executionId) => Promise.resolve(filesOf(executionId)) }),
      purges,
      artifactPurges,
      purge: (executionId) => {
        purges.push(executionId);
        published.set(executionId, []);
        return Promise.resolve();
      },
      purgeArtifacts: (executionId) => {
        artifactPurges.push(executionId);
        published.set(
          executionId,
          filesOf(executionId).filter((entry) => entry.kind === "trace"),
        );
        return Promise.resolve();
      },
    };
  }

  it("rejects malformed or out-of-bounds declarations before any registration", async () => {
    const { baseUrl } = await launch();
    const badValues: unknown[] = [0, -1, 1.5, "24", true, RETAINED_OUTPUT_WORST_CASE_BYTES + 1];
    for (const bad of badValues) {
      const response = await postRuns(
        baseUrl,
        JSON.stringify({ executionId: "e-bad", prompt: "p", maxRetainedOutputBytes: bad }),
      );
      expect(response.status).toBe(400);
    }
    expect((await fetch(`${baseUrl}/v1/runs/e-bad`)).status).toBe(404);
  });

  it("admits runs at their declared caps rather than the worst case", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 100 });
    const { baseUrl } = await launch(config, signalAwareHangingFake());
    expect((await postRuns(baseUrl, startBody("e-1", 50))).status).toBe(202);
    expect((await postRuns(baseUrl, startBody("e-2", 50))).status).toBe(202);
    const refused = await postRuns(baseUrl, startBody("e-3", 1));
    expect(refused.status).toBe(503);
    expect(retryAfterOf(refused)).toBeWithin(5, 8);
    expect(await refused.json()).toEqual({ error: "capacity", reason: "retained_output_budget" });
  });

  it("charges an undeclared run the default reservation, not the worst case", async () => {
    const config = testConfig({
      retainedOutputBudgetBytes: 2 * DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
    });
    const { baseUrl } = await launch(config, signalAwareHangingFake());
    expect((await postRuns(baseUrl, startBody("e-1"))).status).toBe(202);
    expect((await postRuns(baseUrl, startBody("e-2"))).status).toBe(202);
    expect((await postRuns(baseUrl, startBody("e-3"))).status).toBe(503);
  });

  it("admits a full uid pool of default-reservation runs against the production budget", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: MAX_RETAINED_OUTPUT_BYTES });
    const { baseUrl } = await launch(config, signalAwareHangingFake());
    for (let i = 0; i < UID_POOL_SIZE; i += 1) {
      expect((await postRuns(baseUrl, startBody(`e-${i}`))).status).toBe(202);
    }
  });

  it("extends the payload fingerprint over the declaration", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    expect((await postRuns(baseUrl, startBody("e-1", 1024))).status).toBe(202);
    const conflicting = await postRuns(baseUrl, startBody("e-1", 2048));
    expect(conflicting.status).toBe(409);
    expect(await conflicting.json()).toEqual({ code: "payload_conflict" });
    expect((await postRuns(baseUrl, startBody("e-1"))).status).toBe(409);
    const duplicate = await postRuns(baseUrl, startBody("e-1", 1024));
    expect(duplicate.status).toBe(200);
    expect((await monitorOf(duplicate))["status"]).toBe("running");
  });

  it("fails a run whose retained bytes exceed its declaration and tears the artifacts down", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = trackedRunFilesView([{ path: "out/report.pdf", sizeBytes: 800 }]);
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1", 100))).status).toBe(202);
    const failed = await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect(failed["error"]).toEqual({ subtype: "retained_budget_exceeded" });
    expect(failed["estimatedCostUsd"]).toBe(0.1);
    await waitFor(() => runFiles.artifactPurges.includes("e-1"));
    // With no trace to spare, the whole charge comes back.
    expect((await retryUntilAdmitted(baseUrl, startBody("e-2", 1000))).status).toBe(202);
  });

  it("settles a run under its declaration down to the measured bytes", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = trackedRunFilesView([{ path: "out/report.md", sizeBytes: 10 }]);
    const runReportFn: SpawnRunFn = (params, options) =>
      params.executionId === "e-1" ? Promise.resolve(stubResult) : signalAwareHangingFake()(params, options);
    const { baseUrl } = await launch(config, runReportFn, { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1", 1000))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    expect((await retryUntilAdmitted(baseUrl, startBody("e-2", 990))).status).toBe(202);
    expect((await postRuns(baseUrl, startBody("e-3", 1))).status).toBe(503);
  });

  it("parks a pause normally while retained bytes fit the declaration", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = trackedRunFilesView([{ path: "out/draft.md", sizeBytes: 10 }]);
    const runReportFn: SpawnRunFn = async (_params, options) => {
      await options?.pause?.(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(config, runReportFn, { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1", 1000))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "awaiting_input");
  });

  it("fails a pause whose published bytes exceed the declaration before it parks", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = trackedRunFilesView([{ path: "out/draft.md", sizeBytes: 800 }]);
    const store = memoryStore();
    const runReportFn: SpawnRunFn = async (_params, options) => {
      await options?.pause?.(pauseRequest());
      return stubResult;
    };
    const { baseUrl } = await launch(config, runReportFn, { runFiles, checkpointStore: store });
    expect((await postRuns(baseUrl, startBody("e-1", 100))).status).toBe(202);
    const failed = await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect(failed["error"]).toEqual({ subtype: "retained_budget_exceeded" });
    expect(failed["estimatedCostUsd"]).toBe(0.05);
    expect(store.saves).toEqual([]);
    await waitFor(() => runFiles.artifactPurges.includes("e-1"));
    expect((await retryUntilAdmitted(baseUrl, startBody("e-2", 1000))).status).toBe(202);
  });

  it("spares an over-declaration failure's trace while keeping its own subtype", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = trackedRunFilesView([{ path: "trace/trace.ndjson", sizeBytes: 800 }]);
    const runReportFn: SpawnRunFn = (params, options) =>
      params.executionId === "e-1"
        ? Promise.reject(
            new RunFailedError("deadline", {
              subtype: "deadline_exceeded",
              estimatedCostUsd: 0.2,
              sessionId: null,
            }),
          )
        : signalAwareHangingFake()(params, options);
    const { baseUrl } = await launch(config, runReportFn, { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1", 100))).status).toBe(202);
    const failed = await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect(failed["error"]).toEqual({ subtype: "deadline_exceeded" });
    await waitFor(() => runFiles.artifactPurges.includes("e-1"));
    // The overshoot was all trace, so all 800 bytes stay resident and charged.
    expect((await retryUntilAdmitted(baseUrl, startBody("e-2", 200))).status).toBe(202);
    expect((await postRuns(baseUrl, startBody("e-3", 1))).status).toBe(503);
  });

  it("never lets an over-budget teardown reach a retried successor's data", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    let releaseList = (): void => {};
    const listGate = new Promise<void>((resolve) => {
      releaseList = resolve;
    });
    let releasePurge = (): void => {};
    const purgeGate = new Promise<void>((resolve) => {
      releasePurge = resolve;
    });
    const purges: string[] = [];
    const runFiles = stubRunFilesView({
      list: async () => {
        await listGate;
        return [{ path: "out/big.bin", sizeBytes: 900, sha256: "0".repeat(64), kind: "artifact" }];
      },
      purge: async (executionId) => {
        purges.push(executionId);
        if (purges.length === 1) {
          await purgeGate;
        }
      },
    });
    let failedOnce = false;
    const runReportFn: SpawnRunFn = (params, options) => {
      if (!failedOnce) {
        failedOnce = true;
        return Promise.reject(new Error("first attempt dies retryably"));
      }
      return signalAwareHangingFake()(params, options);
    };
    const { baseUrl } = await launch(config, runReportFn, { runFiles });
    const body = startBody("e-1", 100);
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    // The retryable failure's release is held open inside purge; the retry blocks on that in-flight release.
    await waitFor(() => purges.length === 1);
    const retry = postRuns(baseUrl, body);
    await new Promise((resolve) => setTimeout(resolve, 25));
    // The measurement lands over budget while the predecessor's reservation is still live, scheduling a
    // second teardown — which must not touch the successor's files or reservation.
    releaseList();
    await new Promise((resolve) => setTimeout(resolve, 25));
    releasePurge();
    expect((await retry).status).toBe(202);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(purges).toEqual(["e-1"]);
    expect((await postRuns(baseUrl, startBody("e-2", 950))).status).toBe(503);
  });

  it("spares the trace pack when tearing down an over-declaration publish", async () => {
    const base = mkdtempSync(join(tmpdir(), "server-spared-trace-"));
    const tracePath = join(base, "trace.ndjson");
    writeFileSync(tracePath, '{"type":"envelope"}\n');
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = trackedRunFilesView([
      { path: "out/report.pdf", sizeBytes: 800 },
      { path: "trace/trace.ndjson", sizeBytes: 40 },
    ]);
    const listedPaths = async (): Promise<string[]> =>
      (await runFiles.list("e-1", 1)).map((file) => file.path);
    runFiles.resolve = async (_executionId, _attempt, path) =>
      path === "trace/trace.ndjson" && (await listedPaths()).includes(path)
        ? { file: openSizedFile(tracePath), contentType: "application/octet-stream" }
        : undefined;
    const onlyFirstRunFinishes: SpawnRunFn = (params, options) =>
      params.executionId === "e-1" ? Promise.resolve(stubResult) : signalAwareHangingFake()(params, options);
    const { baseUrl } = await launch(config, onlyFirstRunFinishes, { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1", 100))).status).toBe(202);
    const failed = await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect(failed["error"]).toEqual({ subtype: "retained_budget_exceeded" });
    await waitFor(() => runFiles.artifactPurges.includes("e-1"));
    // The deliverables are gone; the evidence is not.
    expect(runFiles.purges).toEqual([]);
    const manifest = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(manifest.status).toBe(200);
    const listed = asRecord(await manifest.json());
    expect(Array.isArray(listed["files"])).toBe(true);
    const paths = (listed["files"] as { path: string }[]).map((file) => file.path);
    expect(paths).toEqual(["trace/trace.ndjson"]);
    const traceFetch = await fetch(`${baseUrl}/v1/runs/e-1/files/trace/trace.ndjson`);
    expect(traceFetch.status).toBe(200);
    expect(await traceFetch.text()).toBe('{"type":"envelope"}\n');
    // The spared bytes stay charged: 1000 - 40 leaves room for 960, not 961.
    expect((await retryUntilAdmitted(baseUrl, startBody("e-2", 960))).status).toBe(202);
    expect((await postRuns(baseUrl, startBody("e-3", 1))).status).toBe(503);
  });

  it("keeps the full spared-trace charge when the trace alone exceeds the declaration", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = trackedRunFilesView([
      { path: "out/report.pdf", sizeBytes: 800 },
      { path: "trace/trace.ndjson", sizeBytes: 300 },
    ]);
    const onlyFirstRunFinishes: SpawnRunFn = (params, options) =>
      params.executionId === "e-1" ? Promise.resolve(stubResult) : signalAwareHangingFake()(params, options);
    const { baseUrl } = await launch(config, onlyFirstRunFinishes, { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1", 100))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "failed");
    await waitFor(() => runFiles.artifactPurges.includes("e-1"));
    // Evidence beats budget: the trace's full 300 bytes stay charged, even though only 100 was reserved.
    expect((await retryUntilAdmitted(baseUrl, startBody("e-2", 700))).status).toBe(202);
    expect((await postRuns(baseUrl, startBody("e-3", 1))).status).toBe(503);
  });

  it("removes the spared trace through the ordinary complete teardown", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = trackedRunFilesView([
      { path: "out/report.pdf", sizeBytes: 800 },
      { path: "trace/trace.ndjson", sizeBytes: 40 },
    ]);
    const onlyFirstRunFinishes: SpawnRunFn = (params, options) =>
      params.executionId === "e-1" ? Promise.resolve(stubResult) : signalAwareHangingFake()(params, options);
    const { baseUrl } = await launch(config, onlyFirstRunFinishes, { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1", 100))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "failed");
    await waitFor(() => runFiles.artifactPurges.includes("e-1"));
    expect((await fetch(`${baseUrl}/v1/runs/e-1/complete`, { method: "POST" })).status).toBe(200);
    await waitFor(() => runFiles.purges.length > 0);
    expect(runFiles.purges).toEqual(["e-1"]);
    // Sparing was punitive-path only: after complete, nothing is charged.
    expect((await retryUntilAdmitted(baseUrl, startBody("e-4", 1000))).status).toBe(202);
  });

  it("keeps the resident bytes charged when an over-budget teardown fails to remove them", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: 1000 });
    const runFiles = stubRunFilesView({
      list: () =>
        Promise.resolve([{ path: "out/big.bin", sizeBytes: 900, sha256: "0".repeat(64), kind: "artifact" }]),
      purgeArtifacts: () => Promise.reject(new Error("tmpfs refused the purge")),
    });
    const onlyFirstRunFinishes: SpawnRunFn = (params, options) =>
      params.executionId === "e-1" ? Promise.resolve(stubResult) : signalAwareHangingFake()(params, options);
    const { baseUrl } = await launch(config, onlyFirstRunFinishes, { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1", 100))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "failed");
    await new Promise((resolve) => setTimeout(resolve, 50));
    // The bytes are still there, so the budget describes all 900 of them, not the 100 declared.
    expect((await postRuns(baseUrl, startBody("e-2", 101))).status).toBe(503);
    expect((await retryUntilAdmitted(baseUrl, startBody("e-3", 100))).status).toBe(202);
  });

  it("ignores a stale over-budget measurement from a replaced run", async () => {
    const config = testConfig({ retainedOutputBudgetBytes: DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES });
    let releaseList = (): void => {};
    const listGate = new Promise<void>((resolve) => {
      releaseList = resolve;
    });
    const purges: string[] = [];
    const runFiles = stubRunFilesView({
      list: async () => {
        await listGate;
        return [
          {
            path: "out/huge.bin",
            sizeBytes: 2 * DEFAULT_RETAINED_OUTPUT_RESERVATION_BYTES,
            sha256: "0".repeat(64),
            kind: "artifact",
          },
        ];
      },
      purge: (executionId) => {
        purges.push(executionId);
        return Promise.resolve();
      },
    });
    let failedOnce = false;
    const runReportFn: SpawnRunFn = (params, options) => {
      if (!failedOnce) {
        failedOnce = true;
        return Promise.reject(new Error("first attempt dies retryably"));
      }
      return signalAwareHangingFake()(params, options);
    };
    const { baseUrl } = await launch(config, runReportFn, { runFiles });
    expect((await postRuns(baseUrl, startBody("e-1"))).status).toBe(202);
    await pollRunUntilStatus(baseUrl, "e-1", "failed");
    expect((await retryUntilAdmitted(baseUrl, startBody("e-1"))).status).toBe(202);
    expect(purges).toEqual(["e-1"]);
    releaseList();
    await new Promise((resolve) => setTimeout(resolve, 25));
    // The stale measurement of the dead attempt must not purge the live run's files or free its charge.
    expect(purges).toEqual(["e-1"]);
    expect((await postRuns(baseUrl, startBody("e-2", 1))).status).toBe(503);
  });
});

describe("connection hardening", () => {
  it("destroys connections that never finish sending a request", async () => {
    const { server, baseUrl } = await launch(testConfig({ requestPhaseTimeoutMs: 150 }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const startedAt = Date.now();
    const closedInMs = await new Promise<number>((resolve, reject) => {
      // Host and content-type headers, or the request is refused (400, 415) before the body phase this
      // test polices; and the client reads, or a runtime that FINs by itself never shows it the close.
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write(
          "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
            'content-length: 500\r\n\r\n{"partial',
        );
      });
      socket.resume();
      socket.on("close", () => resolve(Date.now() - startedAt));
      socket.on("error", () => resolve(Date.now() - startedAt));
      setTimeout(() => reject(new Error("socket was not torn down")), 3000);
    });
    expect(closedInMs).toBeLessThan(1500);
    void baseUrl;
  });

  // A client that ignores the server's FIN (allowHalfOpen) holds the server socket after the
  // response; the phase timer re-armed on `finish` is what tears it down, on every runtime — Bun
  // 1.4.0 and later FIN after a connection: close response itself, which the idle-socket tests above then
  // observe, so this is the one test that still proves the re-arm there.
  it("destroys the server socket held by a client that ignores the FIN after its response", async () => {
    const { server } = await launch(testConfig({ requestPhaseTimeoutMs: 200 }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const held = Promise.withResolvers<number>();
    const startedAt = Date.now();
    server.on("connection", (socket) => {
      socket.once("close", () => held.resolve(Date.now() - startedAt));
    });
    const client = connect({ port: address.port, host: "127.0.0.1", allowHalfOpen: true }, () => {
      client.write("GET /healthz HTTP/1.1\r\nhost: localhost\r\n\r\n");
    });
    client.resume();
    client.on("error", () => {});
    const timeout = setTimeout(() => held.reject(new Error("held server socket was never torn down")), 4000);
    try {
      expect(await held.promise).toBeLessThan(2000);
    } finally {
      clearTimeout(timeout);
      client.destroy();
    }
  });

  // Bun since 1.4.0 fires `connection` on the client's first bytes, before the head parses, so the phase
  // timer armed there polices a client that stalls inside its first request's headers. On 1.3.14
  // `connection` fired only once the head had parsed and only Bun's own ~12 s teardown reached such
  // a client — this pin is red there by construction.
  it("destroys a connection that stalls inside its first request's headers", async () => {
    const { server } = await launch(testConfig({ requestPhaseTimeoutMs: 150 }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const startedAt = Date.now();
    const closedInMs = await new Promise<number>((resolve, reject) => {
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write("POST /v1/runs HTTP/1.1\r\nhost: localhost\r\n");
      });
      socket.resume();
      socket.on("close", () => resolve(Date.now() - startedAt));
      socket.on("error", () => resolve(Date.now() - startedAt));
      setTimeout(() => reject(new Error("header-stalled socket was not torn down")), 3000);
    });
    expect(closedInMs).toBeLessThan(1500);
  });

  // Bun's HTTP listener accepts a client that sends nothing only about 1 s after the handshake, and
  // `connection` fires at that accept: hence the 3 s bound, against 1.5 s for a client that sends.
  it("destroys a connection that sends nothing", async () => {
    const { server } = await launch(testConfig({ requestPhaseTimeoutMs: 150 }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const startedAt = Date.now();
    const closedInMs = await new Promise<number>((resolve, reject) => {
      const socket = connect(address.port, "127.0.0.1");
      socket.resume();
      socket.on("close", () => resolve(Date.now() - startedAt));
      socket.on("error", () => resolve(Date.now() - startedAt));
      setTimeout(() => reject(new Error("silent socket was not torn down")), 4000);
    });
    expect(closedInMs).toBeLessThan(3000);
  });

  it("returns 404 for unknown routes", async () => {
    const { baseUrl } = await launch();
    for (const path of ["/nope", "/runs/e-1/unknown", "/runs/e-1/input/extra"]) {
      const response = await fetch(`${baseUrl}${path}`);
      expect(response.status).toBe(404);
    }
  });

  // Deliberately larger than the send window: a smaller response can't catch a truncating teardown,
  // since Bun reports finish/writableLength as flushed while the bytes are still queued.
  it("delivers a response larger than the socket send window whole", async () => {
    const bigResult: RunReportResult = { ...stubResult, output: "x".repeat(6 * 1024 * 1024) };
    const { server, baseUrl } = await launch(testConfig(), () => Promise.resolve(bigResult));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await pollRunUntilStatus(baseUrl, "e-1", "succeeded");
    const delivered = await new Promise<{ declared: number; received: number }>((resolve, reject) => {
      let raw = Buffer.alloc(0);
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write("GET /v1/runs/e-1 HTTP/1.1\r\nhost: localhost\r\n\r\n");
      });
      const measure = (): { declared: number; received: number } => {
        const split = raw.indexOf("\r\n\r\n");
        if (split < 0) {
          return { declared: 0, received: 0 };
        }
        return {
          declared: Number(/content-length: (\d+)/i.exec(raw.subarray(0, split).toString())?.[1] ?? 0),
          received: raw.length - split - 4,
        };
      };
      socket.on("data", (chunk: Buffer) => {
        raw = Buffer.concat([raw, chunk]);
        const seen = measure();
        if (seen.declared > 0 && seen.received >= seen.declared) {
          resolve(seen);
        }
      });
      socket.on("close", () => resolve(measure()));
      socket.on("error", reject);
      setTimeout(() => reject(new Error("large response never completed")), 15_000);
    });
    expect(delivered.received).toBe(delivered.declared);
    expect(delivered.declared).toBeGreaterThan(6 * 1024 * 1024);
  }, 30_000);

  it("destroys a socket left idle after its response instead of holding it forever", async () => {
    const { server } = await launch(testConfig({ requestPhaseTimeoutMs: 200 }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const closedInMs = await new Promise<number>((resolve, reject) => {
      const startedAt = Date.now();
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write("GET /healthz HTTP/1.1\r\nhost: localhost\r\n\r\n");
      });
      socket.resume();
      socket.on("close", () => resolve(Date.now() - startedAt));
      socket.on("error", () => resolve(Date.now() - startedAt));
      setTimeout(() => reject(new Error("idle socket was never torn down")), 4000);
    });
    expect(closedInMs).toBeLessThan(2000);
  });

  it("destroys an idle socket on a body-carrying route too, where the read's timer is cleared", async () => {
    const { server } = await launch(testConfig({ requestPhaseTimeoutMs: 200 }), signalAwareHangingFake());
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const body = JSON.stringify({ executionId: "e-1", prompt: "p" });
    const closedInMs = await new Promise<number>((resolve, reject) => {
      const startedAt = Date.now();
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write(
          "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
            `content-length: ${Buffer.byteLength(body)}\r\n\r\n${body}`,
        );
      });
      socket.resume();
      socket.on("close", () => resolve(Date.now() - startedAt));
      socket.on("error", () => resolve(Date.now() - startedAt));
      setTimeout(() => reject(new Error("idle socket on a body route was never torn down")), 4000);
    });
    expect(closedInMs).toBeLessThan(2000);
  });

  it("polices the header phase of a second request on a socket the client reuses", async () => {
    const { server } = await launch(testConfig({ requestPhaseTimeoutMs: 200 }));
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const transcript = await new Promise<string>((resolve, reject) => {
      let seen = "";
      let secondRequestSent = false;
      const socket = connect(address.port, "127.0.0.1", () => {
        socket.write("GET /healthz HTTP/1.1\r\nhost: localhost\r\n\r\n");
      });
      socket.on("data", (chunk: Buffer) => {
        seen += chunk.toString();
        if (!secondRequestSent && seen.includes("\r\n\r\n")) {
          secondRequestSent = true;
          // Deliberately unfinished header phase — the dribble the re-armed timer must police.
          socket.write("GET /healthz HTTP/1.1\r\nhost: localhost\r\n");
        }
      });
      socket.on("close", () => resolve(seen));
      socket.on("error", () => resolve(seen));
      setTimeout(() => reject(new Error("reused socket was never torn down")), 4000);
    });
    expect(transcript.match(/HTTP\/1\.1 /g)).toHaveLength(1);
  });
});

describe("structured events", () => {
  function recordingMetrics(): {
    counts: { name: string; dimensions: Record<string, string> }[];
    gauges: Map<string, number>;
    metrics: Metrics;
  } {
    const counts: { name: string; dimensions: Record<string, string> }[] = [];
    const gauges = new Map<string, number>();
    return {
      counts,
      gauges,
      metrics: {
        count: (name, options) => counts.push({ name, dimensions: options?.dimensions ?? {} }),
        gauge: (name, value) => gauges.set(name, value),
      },
    };
  }

  it("logs and counts an auth rejection", async () => {
    const { events, logger } = eventsLogger();
    const { counts, metrics } = recordingMetrics();
    const { baseUrl } = await launch(testConfig({ auth: { token: "secret" } }), undefined, {
      logger,
      metrics,
    });
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(response.status).toBe(401);
    expect(events.some((entry) => entry.event === "auth_rejected" && entry.level === "warn")).toBe(true);
    expect(counts.some((entry) => entry.name === "AuthRejected")).toBe(true);
  });

  it("logs a capacity refusal with its refusing dimension and counts it", async () => {
    const { events, logger } = eventsLogger();
    const { counts, metrics } = recordingMetrics();
    const { baseUrl } = await launch(testConfig({ retainedOutputBudgetBytes: 1 }), undefined, {
      logger,
      metrics,
    });
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(response.status).toBe(503);
    const refusal = events.find((entry) => entry.event === "capacity_refused");
    expect(refusal?.fields["reason"]).toBe("retained_output_budget");
    expect(
      counts.some(
        (entry) =>
          entry.name === "CapacityRefused" && entry.dimensions["reason"] === "retained_output_budget",
      ),
    ).toBe(true);
    expect(events.some((entry) => entry.event === "budget_refused")).toBe(true);
  });

  it("logs the lifecycle of a successful run and counts its settle", async () => {
    const { events, logger } = eventsLogger();
    const { counts, metrics } = recordingMetrics();
    const { server, baseUrl } = await launch(testConfig(), undefined, { logger, metrics });
    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(response.status).toBe(202);
    await waitFor(() => events.some((entry) => entry.event === "run_settled"));
    expect(events.some((entry) => entry.event === "run_started")).toBe(true);
    const finished = events.find((entry) => entry.event === "run_finished");
    expect(finished?.fields["executionId"]).toBe("e-1");
    expect(finished?.fields["estimatedCostUsd"]).toBe(0.1);
    await waitFor(() =>
      counts.some((entry) => entry.name === "RunsSettled" && entry.dimensions["status"] === "succeeded"),
    );
    server.close();
  });

  it("logs a failed run with its subtype and counts the settle under it", async () => {
    const { events, logger } = eventsLogger();
    const { counts, metrics } = recordingMetrics();
    const failure = new RunFailedError("blew the deadline", {
      subtype: "deadline_exceeded",
      estimatedCostUsd: 0.3,
      sessionId: null,
    });
    const { baseUrl } = await launch(testConfig(), () => Promise.reject(failure), { logger, metrics });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    await waitFor(() => events.some((entry) => entry.event === "run_failed"));
    const failed = events.find((entry) => entry.event === "run_failed");
    expect(failed?.fields["executionId"]).toBe("e-1");
    expect(failed?.fields["subtype"]).toBe("deadline_exceeded");
    await waitFor(() =>
      counts.some((entry) => entry.name === "RunsSettled" && entry.dimensions["status"] === "failed"),
    );
    const settled = events.find((entry) => entry.event === "run_settled");
    expect(settled?.fields["subtype"]).toBe("deadline_exceeded");
  });

  it("emits a run_polled debug line naming the run and its status on each monitor poll", async () => {
    const { events, logger } = eventsLogger();
    const { baseUrl } = await launch(testConfig(), undefined, { logger });
    await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    const response = await fetch(`${baseUrl}/v1/runs/e-1`);
    expect(response.status).toBe(200);
    const polled = events.find((entry) => entry.event === "run_polled");
    expect(polled?.level).toBe("debug");
    expect(polled?.fields["executionId"]).toBe("e-1");
    expect(typeof polled?.fields["status"]).toBe("string");
  });

  it("samples runtime gauges on the injected interval", async () => {
    const { gauges, metrics } = recordingMetrics();
    const uidPool = createUidPool({ baseUid: UID_POOL_BASE, size: 9 });
    uidPool.acquire();
    uidPool.retain(uidPool.acquire());
    await launch(testConfig(), undefined, {
      metrics,
      uidPool,
      gaugeIntervalMs: 10,
    });
    await waitFor(() => gauges.has("UidPoolFree"));
    expect(gauges.get("UidPoolFree")).toBe(7);
    expect(gauges.get("UidPoolSize")).toBe(9);
    expect(gauges.get("UidPoolRetained")).toBe(1);
    await waitFor(() => gauges.has("RunsActive"));
    expect(gauges.has("InflightUploadBytes")).toBe(true);
  });
});

describe("handler deadline cancels rather than abandons", () => {
  it("launches no run when the deadline expires mid-admission, and frees what it reserved", async () => {
    const { logger } = eventsLogger();
    const documentStore = memoryDocumentStore({ holdSaves: true });
    let launches = 0;
    const countingRun: SpawnRunFn = () => {
      launches += 1;
      return Promise.resolve(stubResult);
    };
    const body = JSON.stringify({
      executionId: "cancel-1",
      prompt: "p",
      documents: [docOf("doc bytes")],
    });
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 4096 }), countingRun, {
      documentStore,
      logger,
      handlerProcessingBudgetMs: 50,
    });

    const expired = await postRuns(baseUrl, body);
    expect(expired.status).toBe(500);

    documentStore.releaseSaves();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(launches).toBe(0);

    const monitor = await monitorOf(await fetch(`${baseUrl}/v1/runs/cancel-1`));
    expect(monitor["status"]).toBe("failed");
  });

  it("is reachable on a body-carrying route — the slow-client timer does not pre-empt it", async () => {
    const { events, logger } = eventsLogger();
    const documentStore = memoryDocumentStore({ holdSaves: true });
    const body = JSON.stringify({
      executionId: "reach-1",
      prompt: "p",
      documents: [docOf("doc bytes")],
    });
    const { baseUrl } = await launch(
      testConfig({ bodyLimitBytes: 4096, requestPhaseTimeoutMs: 100 }),
      undefined,
      { documentStore, logger, handlerProcessingBudgetMs: 400 },
    );
    const response = await postRuns(baseUrl, body);
    expect(response.status).toBe(500);
    const expiry = events.find((entry) => entry.event === "handler_deadline_exceeded");
    expect(expiry?.fields["executionId"]).toBe("reach-1");
    expect(expiry?.fields["path"]).toBe("/v1/runs");
  });
});

describe("terminal instrumentation", () => {
  function settleWatcher(): {
    settles: { name: string; dimensions: Record<string, string> }[];
    metrics: Metrics;
  } {
    const settles: { name: string; dimensions: Record<string, string> }[] = [];
    return {
      settles,
      metrics: {
        count: (name, options) => settles.push({ name, dimensions: options?.dimensions ?? {} }),
        gauge: () => {},
      },
    };
  }

  it("counts a settle under its terminal status, not the failure subtype", async () => {
    const { events, logger } = eventsLogger();
    const { settles, metrics } = settleWatcher();
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake(), { logger, metrics });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);
    await fetch(`${baseUrl}/v1/runs/e-1/cancel`, { method: "POST" });
    await pollRunUntilStatus(baseUrl, "e-1", "cancelled");
    await waitFor(() => settles.some((entry) => entry.name === "RunsSettled"));
    const settle = settles.find((entry) => entry.name === "RunsSettled");
    expect(settle?.dimensions["status"]).toBe("cancelled");
    const settled = events.find((entry) => entry.event === "run_settled");
    expect(settled?.fields["executionId"]).toBe("e-1");
    expect(settled?.fields["status"]).toBe("cancelled");
  });

  it("logs and counts a terminal the run path never sees, like a document-store failure", async () => {
    const { events, logger } = eventsLogger();
    const { settles, metrics } = settleWatcher();
    const documentStore = memoryDocumentStore({ failSave: true });
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 4096 }), undefined, {
      documentStore,
      logger,
      metrics,
    });
    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", documents: [docOf("bytes")] }),
    );
    expect(response.status).toBe(500);
    expect(
      settles.some((entry) => entry.name === "RunsSettled" && entry.dimensions["status"] === "failed"),
    ).toBe(true);
    const settled = events.find((entry) => entry.event === "run_settled");
    expect(settled?.fields["subtype"]).toBe("internal_error");
  });
});

describe("auth rejection volume", () => {
  it("throttles the log and metric under a flood rather than emitting one per attempt", async () => {
    const { events, logger } = eventsLogger();
    let counted = 0;
    const metrics: Metrics = {
      count: (name) => {
        if (name === "AuthRejected") {
          counted += 1;
        }
      },
      gauge: () => {},
    };
    const { baseUrl } = await launch(testConfig({ auth: { token: "secret" } }), undefined, {
      logger,
      metrics,
    });
    for (let attempt = 0; attempt < 20; attempt += 1) {
      const response = await fetch(`${baseUrl}/v1/runs/e-1`);
      expect(response.status).toBe(401);
    }
    const logged = events.filter((entry) => entry.event === "auth_rejected");
    expect(logged.length).toBeLessThan(20);
    expect(logged.length).toBeGreaterThan(0);
    expect(counted).toBe(logged.length);
    expect(logged[0]?.fields["suppressed"]).toBe(0);
  });
});

describe("handler deadline", () => {
  // The follower must be refused while the hanging start still holds its reservation, so the budget
  // must outlast the follower's round trip, which exceeds 50 ms on an emulated arm64 runner.
  const BUDGET_OUTLASTING_A_FOLLOWER_MS = 1_000;

  it("fails a start whose document persist hangs, frees its upload reservation, and logs the expiry", async () => {
    const hangingBody = JSON.stringify({
      executionId: "hang-1",
      prompt: "p",
      documents: [docOf("doc bytes")],
    });
    const followerBody = JSON.stringify({ executionId: "next-1", prompt: "p" });
    const { events, logger } = eventsLogger();
    const documentStore = memoryDocumentStore({ holdSaves: true });
    const config = testConfig({
      bodyLimitBytes: 4096,
      inflightUploadBudgetBytes: Buffer.byteLength(hangingBody),
    });
    const { baseUrl } = await launch(config, () => Promise.resolve(stubResult), {
      documentStore,
      logger,
      handlerProcessingBudgetMs: BUDGET_OUTLASTING_A_FOLLOWER_MS,
    });

    const hanging = postRuns(baseUrl, hangingBody);
    await waitFor(() => documentStore.heldSaves() > 0);
    const refused = await postRuns(baseUrl, followerBody);
    expect(refused.status).toBe(503);

    const expired = await hanging;
    expect(expired.status).toBe(500);
    expect(await expired.json()).toEqual({ error: "internal error" });
    expect(events.some((entry) => entry.event === "handler_deadline_exceeded")).toBe(true);

    const monitor = await monitorOf(await fetch(`${baseUrl}/v1/runs/hang-1`));
    expect(monitor["status"]).toBe("failed");
    expect(monitor["error"]).toEqual({ subtype: "internal_error" });

    const admitted = await postRuns(baseUrl, followerBody);
    expect(admitted.status).toBe(202);
  });

  it("fails a non-start route whose work hangs instead of holding the request open", async () => {
    const { logger } = eventsLogger();
    let hangListing = false;
    const runFiles = stubRunFilesView({
      list: () => (hangListing ? new Promise<RunFileEntry[]>(() => {}) : Promise.resolve([])),
    });
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), {
      runFiles,
      logger,
      handlerProcessingBudgetMs: 50,
    });
    const started = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(started.status).toBe(202);
    const untilTerminal = async (): Promise<void> => {
      for (let i = 0; i < 200; i += 1) {
        const monitor = await monitorOf(await fetch(`${baseUrl}/v1/runs/e-1`));
        if (monitor["status"] === "succeeded") {
          return;
        }
        await new Promise((resolve) => setTimeout(resolve, 5));
      }
      throw new Error("run never settled");
    };
    await untilTerminal();
    hangListing = true;
    const listed = await fetch(`${baseUrl}/v1/runs/e-1/files`);
    expect(listed.status).toBe(500);
  });
});

describe("starting continuations", () => {
  function sealedBundle(text = "bundle bytes"): { sha256: string; bytesBase64: string } {
    const bytes = Buffer.from(text);
    return { sha256: sha256Hex(bytes), bytesBase64: bytes.toString("base64") };
  }

  function outFileOf(text: string, path: string): Record<string, unknown> {
    const bytes = Buffer.from(text);
    return { path, contentBase64: bytes.toString("base64"), sha256: sha256Hex(bytes) };
  }

  function continuationBody(continuation: Record<string, unknown>, executionId = "e-c1"): string {
    return JSON.stringify({ executionId, prompt: "revise with these findings", continuation });
  }

  it("accepts a continuation start with 202 and hands the spawner the bundle and the revision prompt", async () => {
    const seen: { params?: RunReportParams; options?: SpawnRunOptions | undefined } = {};
    const { baseUrl } = await launch(testConfig(), (params, options) => {
      seen.params = params;
      seen.options = options;
      return Promise.resolve(stubResult);
    });
    const bundle = sealedBundle();
    const response = await postRuns(
      baseUrl,
      continuationBody({
        sessionBundle: bundle,
        outFiles: [outFileOf("print('check')", "checks/model.py")],
      }),
    );
    expect(response.status).toBe(202);
    await waitFor(() => seen.options !== undefined);
    expect(seen.params?.prompt).toBe("revise with these findings");
    const checkpoint = seen.options?.continuation?.takeCheckpoint();
    expect(checkpoint?.sha256).toBe(bundle.sha256);
    expect(Buffer.from(checkpoint?.bytes ?? []).toString("utf8")).toBe("bundle bytes");
  });

  it("persists the re-carried out/ files into the document store under their own area", async () => {
    const documentStore = memoryDocumentStore();
    const { baseUrl } = await launch(testConfig(), () => Promise.resolve(stubResult), { documentStore });
    const response = await postRuns(
      baseUrl,
      continuationBody({
        sessionBundle: sealedBundle(),
        outFiles: [outFileOf("# draft", "draft.md"), outFileOf("a,b\n", "tables/q1.csv")],
      }),
    );
    expect(response.status).toBe(202);
    const staged = documentStore.saved.get("e-c1") ?? [];
    expect(staged.map((input) => [input.area, input.name])).toEqual([
      ["recarried_out", "draft.md"],
      ["recarried_out", "tables/q1.csv"],
    ]);
  });

  it("refuses a malformed continuation with 400 before any run starts", async () => {
    let called = false;
    const { baseUrl } = await launch(testConfig(), () => {
      called = true;
      return Promise.resolve(stubResult);
    });
    const good = sealedBundle();
    for (const continuation of [
      {},
      { outFiles: [] },
      { sessionBundle: good },
      { sessionBundle: good, outFiles: [outFileOf("x", "../escape.py")] },
      { sessionBundle: good, outFiles: [outFileOf("x", "a/../b.py")] },
      {
        sessionBundle: good,
        outFiles: [{ path: "a.md", contentBase64: Buffer.from("x").toString("base64") }],
      },
      {
        sessionBundle: good,
        outFiles: [
          { path: "a.md", contentBase64: Buffer.from("x").toString("base64"), sha256: "0".repeat(64) },
        ],
      },
      { sessionBundle: { ...good, sha256: "0".repeat(64) }, outFiles: [] },
      { sessionBundle: { ...good, extra: true }, outFiles: [] },
      { sessionBundle: good, outFiles: [], extra: true },
    ]) {
      const response = await postRuns(baseUrl, continuationBody(continuation));
      expect(response.status).toBe(400);
    }
    expect(called).toBe(false);
  });

  it("charges the re-carried bytes to the shared decoded budget and answers 413 over it", async () => {
    const { baseUrl } = await launch(testConfig({ documentBudgetBytes: 8 }));
    const response = await postRuns(
      baseUrl,
      continuationBody({
        sessionBundle: sealedBundle(),
        outFiles: [outFileOf("x".repeat(32), "big.md")],
      }),
    );
    expect(response.status).toBe(413);
  });

  it("treats a repeat with a different bundle or re-carried set as a payload conflict", async () => {
    const { baseUrl } = await launch(testConfig(), signalAwareHangingFake());
    const body = continuationBody({ sessionBundle: sealedBundle("one"), outFiles: [] });
    expect((await postRuns(baseUrl, body)).status).toBe(202);
    expect((await postRuns(baseUrl, body)).status).toBe(200);
    const differentBundle = await postRuns(
      baseUrl,
      continuationBody({ sessionBundle: sealedBundle("two"), outFiles: [] }),
    );
    expect(differentBundle.status).toBe(409);
    const differentOut = await postRuns(
      baseUrl,
      continuationBody({ sessionBundle: sealedBundle("one"), outFiles: [outFileOf("x", "a.md")] }),
    );
    expect(differentOut.status).toBe(409);
  });
});

const UNREADABLE_FD = 100_000;

describe("unexpected 5xx instrumentation", () => {
  function failureWatcher(): {
    counts: { name: string; dimensions: Record<string, string>; aggregate: boolean }[];
    metrics: Metrics;
  } {
    const counts: { name: string; dimensions: Record<string, string>; aggregate: boolean }[] = [];
    return {
      counts,
      metrics: {
        count: (name, options) =>
          counts.push({
            name,
            dimensions: options?.dimensions ?? {},
            aggregate: options?.aggregate === true,
          }),
        gauge: () => {},
      },
    };
  }

  // A handler bug, forced through a real seam: the monitor route logs run_polled before answering.
  function loggerThrowingOn(targetEvent: LogEventName): {
    events: { level: string; event: string; fields: CapturedFields }[];
    logger: Logger;
  } {
    const { events, logger } = eventsLogger();
    const guarded = (inner: Logger): Logger => ({
      debug: (event, fields) => {
        inner.debug(event, fields);
        if (event === targetEvent) {
          throw new Error("handler bug");
        }
      },
      info: (event, fields) => inner.info(event, fields),
      warn: (event, fields) => inner.warn(event, fields),
      error: (event, fields) => inner.error(event, fields),
      with: (fields) => guarded(inner.with(fields)),
    });
    return { events, logger: guarded(logger) };
  }

  type FailureCount = { name: string; dimensions: Record<string, string>; aggregate: boolean };

  function failuresIn(counts: FailureCount[]): FailureCount[] {
    return counts.filter((entry) => entry.name === "RequestFailed");
  }

  it("counts an unhandled throw on the monitor route under its route and cause", async () => {
    const { events, logger } = loggerThrowingOn("run_polled");
    const { counts, metrics } = failureWatcher();
    const { baseUrl } = await launch(testConfig(), undefined, { logger, metrics });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);

    const response = await fetch(`${baseUrl}/v1/runs/e-1`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(500);

    const failures = failuresIn(counts);
    expect(failures).toHaveLength(1);
    expect(failures[0]?.dimensions).toEqual({ route: "run_monitor", cause: "unhandled" });
    expect(failures[0]?.aggregate).toBe(true);
    const logged = events.find((entry) => entry.event === "request_failed");
    expect(logged?.level).toBe("error");
    expect(logged?.fields["route"]).toBe("run_monitor");
  });

  it("counts an unhandled throw on the files route under that route", async () => {
    const { counts, metrics } = failureWatcher();
    const runFiles = stubRunFilesView({ list: () => Promise.reject(new Error("listing bug")) });
    const { baseUrl } = await launch(testConfig(), undefined, { metrics, runFiles });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);

    const response = await fetch(`${baseUrl}/v1/runs/e-1/files`, { headers: NO_REUSE_HEADERS });
    expect(response.status).toBe(500);

    const failures = failuresIn(counts);
    expect(failures).toHaveLength(1);
    expect(failures[0]?.dimensions).toEqual({ route: "run_files", cause: "unhandled" });
  });

  it("counts a handler-deadline 500 on the start route under the deadline cause", async () => {
    const { counts, metrics } = failureWatcher();
    const documentStore = memoryDocumentStore({ holdSaves: true });
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 4096 }), undefined, {
      documentStore,
      metrics,
      handlerProcessingBudgetMs: 50,
    });

    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "hang-1", prompt: "p", documents: [docOf("doc bytes")] }),
    );
    expect(response.status).toBe(500);

    const failures = failuresIn(counts);
    expect(failures).toHaveLength(1);
    expect(failures[0]?.dimensions).toEqual({ route: "runs_start", cause: "handler_deadline" });
    documentStore.releaseSaves();
  });

  it("leaves a deliberate capacity 503 out of the unexpected-failure counter", async () => {
    const { counts, metrics } = failureWatcher();
    const { baseUrl } = await launch(testConfig({ retainedOutputBudgetBytes: 1 }), undefined, { metrics });

    const response = await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }));
    expect(response.status).toBe(503);

    expect(counts.some((entry) => entry.name === "CapacityRefused")).toBe(true);
    expect(failuresIn(counts)).toHaveLength(0);
  });

  it("leaves answered requests out of the counter", async () => {
    const { counts, metrics } = failureWatcher();
    const { baseUrl } = await launch(testConfig(), undefined, { metrics });

    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);
    expect((await fetch(`${baseUrl}/v1/runs/e-1`, { headers: NO_REUSE_HEADERS })).status).toBe(200);
    expect((await fetch(`${baseUrl}/v1/runs/missing`, { headers: NO_REUSE_HEADERS })).status).toBe(404);
    expect((await fetch(`${baseUrl}/v1/nope`, { headers: NO_REUSE_HEADERS })).status).toBe(404);

    expect(failuresIn(counts)).toHaveLength(0);
  });

  it("counts the start route's persist failure, a 500 no throw ever reaches the funnel for", async () => {
    const { counts, metrics } = failureWatcher();
    const documentStore = memoryDocumentStore({ failSave: true });
    const { baseUrl } = await launch(testConfig({ bodyLimitBytes: 4096 }), undefined, {
      documentStore,
      metrics,
    });

    const response = await postRuns(
      baseUrl,
      JSON.stringify({ executionId: "e-1", prompt: "p", documents: [docOf("bytes")] }),
    );
    expect(response.status).toBe(500);

    const failures = failuresIn(counts);
    expect(failures).toHaveLength(1);
    expect(failures[0]?.dimensions).toEqual({ route: "runs_start", cause: "persist_failed" });
  });

  it("answers the client its 500 and lets no telemetry failure escape the funnel", async () => {
    const metrics: Metrics = {
      count: (name) => {
        if (name === "RequestFailed") {
          throw new Error("metrics sink down");
        }
      },
      gauge: () => {},
    };
    const { logger } = loggerThrowingOn("run_polled");
    const escaped: unknown[] = [];
    const onRejection = (reason: unknown): void => {
      escaped.push(reason);
    };
    process.on("unhandledRejection", onRejection);
    try {
      const { baseUrl } = await launch(testConfig(), undefined, { logger, metrics });
      expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);

      const response = await fetch(`${baseUrl}/v1/runs/e-1`, { headers: NO_REUSE_HEADERS });
      expect(response.status).toBe(500);

      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(escaped).toEqual([]);
    } finally {
      process.off("unhandledRejection", onRejection);
    }
  });

  // A client on a bad link is not a runtime fault; paging on one would spend the alarm's credibility.
  it("leaves a client that aborts mid-upload out of the counter", async () => {
    const { counts, metrics } = failureWatcher();
    const { server } = await launch(
      testConfig({ bodyLimitBytes: 4096, requestPhaseTimeoutMs: 5000 }),
      undefined,
      { metrics },
    );
    const address = server.address();
    if (address === null || typeof address === "string") {
      throw new Error("no port");
    }
    const socket = connect(address.port, "127.0.0.1", () => {
      socket.write(
        "POST /v1/runs HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
          'content-length: 4096\r\n\r\n{"executionId":"e-1"',
      );
    });
    socket.on("error", () => {});
    await new Promise((resolve) => setTimeout(resolve, 50));
    socket.resetAndDestroy();
    await new Promise((resolve) => setTimeout(resolve, 150));

    expect(failuresIn(counts)).toHaveLength(0);
  });

  it("counts a served file whose stream dies after the response already committed", async () => {
    const { counts, metrics } = failureWatcher();
    const runFiles = stubRunFilesView({
      list: () => Promise.resolve(toRunFileEntries([{ path: "out/a.md", sizeBytes: 4 }])),
      resolve: () =>
        Promise.resolve({ file: { fd: UNREADABLE_FD, sizeBytes: 4 }, contentType: "text/markdown" }),
    });
    const { baseUrl } = await launch(testConfig(), undefined, { metrics, runFiles });
    expect((await postRuns(baseUrl, JSON.stringify({ executionId: "e-1", prompt: "p" }))).status).toBe(202);

    await fetch(`${baseUrl}/v1/runs/e-1/files/out/a.md`, { headers: NO_REUSE_HEADERS }).catch(
      () => undefined,
    );

    await waitFor(() => failuresIn(counts).length > 0);
    expect(failuresIn(counts)[0]?.dimensions).toEqual({ route: "run_files", cause: "stream_failed" });
  });
});
