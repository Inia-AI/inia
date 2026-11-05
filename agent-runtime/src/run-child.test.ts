import { afterAll, describe, expect, it } from "bun:test";
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { createAsyncQueue } from "./async-queue.js";
import { CHECKPOINT_BUNDLE_FORMAT, decodeCheckpointManifest } from "./checkpoint-bundle.js";
import { sealCheckpoint } from "./checkpoint-store.js";
import type { CargoByteReader } from "./document-channel.js";
import { composeUserTurn } from "./document-statement.js";
import { readPersistedFinalResponse } from "./final-response-file.js";
import { sha256Hex } from "./hash.js";
import { createLogger } from "./logger.js";
import { EXECUTION_ID_HEADER } from "./model-provider.js";
import { buildRunEnv, buildTraceEnvelope, deliverFinalFrame, runChild } from "./run-child.js";
import { ARTIFACTS_MANIFEST_NAME, artifactAttemptDirOf, publishRunArtifacts } from "./run-files.js";
import {
  RunFailedError,
  type RunReportFn,
  type RunReportOptions,
  type RunReportParams,
  type RunReportResult,
  sandboxConfigId,
} from "./run-report.js";
import {
  fromWireCheckpoint,
  RESULT_UNDELIVERED_EXIT_CODE,
  toWireCheckpoint,
  toWireCheckpointDescriptor,
  type WireChildFrame,
  type WireResponse,
} from "./run-wire.js";
import type { SandboxProbeFn } from "./sandbox-probe.js";
import {
  type CreateRunTraceInput,
  ENVELOPE_ABSENT,
  type RunTrace,
  type TraceEnvelopeInput,
} from "./trace.js";
import { RESERVED_WORKSPACE_DIRS } from "./workspace-layout.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "run-child-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

let caseId = 0;

function newWorkspaceRoot(): string {
  caseId += 1;
  return mkdtempSync(join(TEST_ROOT, `case-${caseId}-`));
}

const ZERO_USAGE = {
  inputTokens: 0,
  outputTokens: 0,
  cacheCreationInputTokens: 0,
  cacheReadInputTokens: 0,
};

const RESULT: RunReportResult = {
  executionId: "e-1",
  output: "the report",
  estimatedCostUsd: 0.4,
  sessionId: "s-1",
  usage: ZERO_USAGE,
};

const QUESTION = {
  id: "q-1",
  toolName: "mcp__platform__request_analyst_input",
  prompt: "Which fund?",
  input: { question: "Which fund?" },
};
const ANSWER = "the second fund";

type SeenCall = { params: RunReportParams; options: RunReportOptions };

const PROBE_OK: SandboxProbeFn = () => Promise.resolve({ ok: true });

function emptyCargo(): CargoByteReader {
  return { read: () => Promise.resolve(0), close: () => {} };
}

function cargoOver(bodies: Buffer[]): CargoByteReader {
  const wire = Buffer.concat(
    bodies.flatMap((body) => {
      const prefix = Buffer.alloc(8);
      prefix.writeBigUInt64BE(BigInt(body.byteLength));
      return [prefix, body];
    }),
  );
  let offset = 0;
  return {
    read: (into) => {
      const take = Math.min(into.byteLength, wire.byteLength - offset);
      wire.copy(into, 0, offset, offset + take);
      offset += take;
      return Promise.resolve(take);
    },
    close: () => {},
  };
}

function checkpointCargo(checkpointBytes: Uint8Array, rest: Buffer[] = []): CargoByteReader {
  return cargoOver([Buffer.from(checkpointBytes), ...rest]);
}

function trackingCargo(bodies: Buffer[]): { cargo: CargoByteReader; closes: () => number } {
  const inner = cargoOver(bodies);
  let closes = 0;
  return {
    cargo: {
      read: (into) => inner.read(into),
      close: () => {
        closes += 1;
      },
    },
    closes: () => closes,
  };
}

const PROBE_FAILURE_DETAIL = "bwrap: loopback: Failed RTM_NEWADDR: Operation not permitted";

function recordingReportFn(
  calls: SeenCall[],
  behave: (params: RunReportParams, options: RunReportOptions) => Promise<RunReportResult> = () =>
    Promise.resolve(RESULT),
): RunReportFn {
  return (params, options = {}) => {
    calls.push({ params, options });
    return behave(params, options);
  };
}

function startRequestLine(extra: Record<string, unknown> = {}): string {
  return JSON.stringify({
    kind: "start",
    executionId: "e-1",
    attempt: 1,
    prompt: "p",
    maxBudgetUsd: 5,
    maxTurns: 42,
    deadlineMs: 900,
    documentBudgetBytes: 200 * 1024 * 1024,
    maxRetainedOutputBytes: 200 * 1024 * 1024,
    ...extra,
  });
}

function manifestFor(
  workspacePath: string,
  stamp?: Record<string, unknown>,
): {
  bytes: Uint8Array;
  manifest: { sessionId: string; workspacePath: string; files: Record<string, string> };
} {
  const manifest = {
    sessionId: "s-9",
    workspacePath,
    files: {
      "config/projects/session.jsonl": Buffer.from('{"type":"user"}\n').toString("base64"),
      "workspace/draft.md": Buffer.from("# partial report").toString("base64"),
    },
    ...(stamp === undefined ? {} : { version: stamp }),
  };
  return { bytes: new TextEncoder().encode(JSON.stringify(manifest)), manifest };
}

// Frames the descriptor for the sealed bytes; the bytes themselves must ride the cargo
// channel (checkpointCargo below), leading every other entry. `extra` spreads last, so a
// test can add fields and override the fixed ones through the same parameter.
function resumeRequestLine(
  checkpointBytes: Uint8Array,
  allowFurtherPause = true,
  extra: Record<string, unknown> = {},
): string {
  return JSON.stringify({
    kind: "resume",
    executionId: "e-1",
    attempt: 1,
    maxBudgetUsd: 5,
    maxTurns: 42,
    deadlineMs: 900,
    documentBudgetBytes: 200 * 1024 * 1024,
    maxRetainedOutputBytes: 200 * 1024 * 1024,
    priorCostUsd: 0.3,
    checkpoint: toWireCheckpointDescriptor(sealCheckpoint(checkpointBytes)),
    answer: ANSWER,
    allowFurtherPause,
    ...extra,
  });
}

describe("runChild", () => {
  it("runs a start request in a fresh workspace with a private home, then reports success", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls).toHaveLength(1);
    expect(calls[0]?.params).toEqual({ executionId: "e-1", prompt: "p" });
    const options = calls[0]?.options;
    expect(options?.deadlineMs).toBe(900);
    expect(options?.maxBudgetUsd).toBe(5);
    expect(options?.maxTurns).toBe(42);
    expect(options?.effort).toBeUndefined();
    expect(options?.priorCostUsd).toBe(0);
    expect(options?.workspaceRoot).toBe(workspaceRoot);
    expect(options?.workspacePath?.startsWith(join(workspaceRoot, "run-"))).toBe(true);
    expect(options?.resumeFrom).toBeUndefined();
    const home = options?.subprocessEnv?.["HOME"];
    expect(home?.startsWith(join(workspaceRoot, "home-"))).toBe(true);
    expect(options?.subprocessEnv?.["CLAUDE_CONFIG_DIR"]).toBe(join(home ?? "", ".claude"));
  });

  it("fails sandbox_unavailable before any model work when the pre-run sandbox probe fails", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
      probeSandboxFn: () => Promise.resolve({ ok: false, detail: PROBE_FAILURE_DETAIL }),
      cargo: emptyCargo(),
    });
    expect(response.ok).toBe(false);
    if (!response.ok) {
      expect(response.error.subtype).toBe("sandbox_unavailable");
      expect(response.error.estimatedCostUsd).toBe(0);
      expect(response.error.sessionId).toBeNull();
      expect(response.error.message).toContain(PROBE_FAILURE_DETAIL);
    }
    expect(calls).toHaveLength(0);
    expect(readdirSync(workspaceRoot).some((name) => name.startsWith("run-"))).toBe(false);
  });

  it("logs an errored tool call as a warn event through the injected logger", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const logLines: { line: string; level: string }[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options = {}) => {
        options.onToolError?.({ toolName: "Bash", erroredCalls: 3 });
        return Promise.resolve(RESULT);
      },
      logger: createLogger({ sink: (line, level) => logLines.push({ line, level }) }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const warns = logLines.filter((entry) => entry.level === "warn");
    expect(warns).toHaveLength(1);
    const parsed: unknown = JSON.parse(warns[0]?.line ?? "{}");
    expect(parsed).toMatchObject({ event: "tool_call_errored", toolName: "Bash", erroredCalls: 3 });
  });

  it("streams each cost update over the wire as a cost frame while the run progresses", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const frames: WireChildFrame[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => frames.push(frame),
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options = {}) => {
        options.onCostUpdate?.(0.1, ZERO_USAGE);
        options.onCostUpdate?.(0.25, ZERO_USAGE);
        return Promise.resolve(RESULT);
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(frames).toEqual([
      { kind: "cost", estimatedCostUsd: 0.1, usage: ZERO_USAGE },
      { kind: "cost", estimatedCostUsd: 0.25, usage: ZERO_USAGE },
    ]);
  });

  it("drops a cost update whose status-pipe write fails, and the run still reaches its terminal", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const logLines: { line: string; level: string }[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => {
        if (frame.kind === "cost") {
          throw new Error("write EPIPE");
        }
      },
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options = {}) => {
        options.onToolError?.({ toolName: "Read", erroredCalls: 1 });
        options.onCostUpdate?.(0.1, ZERO_USAGE);
        return Promise.resolve(RESULT);
      },
      logger: createLogger({ sink: (line, level) => logLines.push({ line, level }) }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const dropped = logLines.filter((entry) => {
      const parsed: unknown = JSON.parse(entry.line);
      return (parsed as { event?: string }).event === "cost_update_dropped";
    });
    expect(dropped).toHaveLength(1);
  });

  it("fails the run loudly when the paused-frame write throws, so only the cost frame is guarded", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const checkpoint = sealCheckpoint(new TextEncoder().encode("session state"));
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => {
        if (frame.kind === "paused") {
          throw new Error("write EPIPE");
        }
      },
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: async (_params, options) => {
        await options?.pause?.({
          question: QUESTION,
          sessionId: "s-1",
          estimatedCostUsd: 0.2,
          checkpoint,
        });
        return RESULT;
      },
    });
    expect(response.ok).toBe(false);
    if (response.ok) {
      throw new Error("unreachable");
    }
    expect(response.error.message).toMatch(/write EPIPE/);
  });

  it("keeps the run's spend on a failure to publish a successful run's deliverables", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
      publishArtifactsFn: () => {
        throw new Error("published bytes exceed the retained-output cap");
      },
    });
    expect(response.ok).toBe(false);
    if (response.ok) {
      throw new Error("unreachable");
    }
    expect(response.error.subtype).toBe("run_child_error");
    expect(response.error.estimatedCostUsd).toBe(RESULT.estimatedCostUsd);
    expect(response.error.sessionId).toBe(RESULT.sessionId);
    expect(response.error.message).toMatch(/retained-output cap/);
  });

  it("removes its private home and workspace once the run settles", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        expect(existsSync(options.workspacePath ?? "")).toBe(true);
        return Promise.resolve(RESULT);
      }),
    });
    const options = calls[0]?.options;
    expect(existsSync(options?.workspacePath ?? "missing")).toBe(false);
    expect(existsSync(options?.subprocessEnv?.["HOME"] ?? "missing")).toBe(false);
  });

  it("restores a resume checkpoint at its identical paths and resumes the named session", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes));
    const calls: SeenCall[] = [];
    const filesSeenAtRunTime: boolean[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        filesSeenAtRunTime.push(
          existsSync(join(options.workspacePath ?? "", "draft.md")),
          existsSync(join(options.subprocessEnv?.["CLAUDE_CONFIG_DIR"] ?? "", "projects", "session.jsonl")),
        );
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const options = calls[0]?.options;
    expect(options?.workspacePath).toBe(workspacePath);
    expect(options?.resumeFrom).toEqual({ sessionId: "s-9" });
    expect(options?.priorCostUsd).toBe(0.3);
    expect(options?.maxTurns).toBe(42);
    expect(options?.allowFurtherPause).toBe(true);
    expect(calls[0]?.params.prompt).toBe(ANSWER);
    expect(filesSeenAtRunTime).toEqual([true, true]);
  });

  it("hands a wire-carried effort level through to the run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ effort: "medium" }));
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls[0]?.options.effort).toBe("medium");
  });

  it.each([
    ["bedrock", "us.anthropic.claude-sonnet-5-5", "high"],
    ["foundry", "claude-sonnet-5-5", "high"],
    ["foundry", "claude-haiku-5-5", "high"],
    ["bedrock", "us.anthropic.claude-haiku-5-5", undefined],
    ["bedrock", "us.anthropic.claude-sonnet-5", undefined],
  ] as const)("in %s mode, a %s run that sets no effort is handed %s", async (provider, model, effort) => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ model }));
    const calls: SeenCall[] = [];
    const traceInputs: CreateRunTraceInput[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot, AGENT_RUNTIME_MODEL_PROVIDER: provider },
      runReportFn: recordingReportFn(calls),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return stubTrace();
      },
    });
    expect(calls[0]?.options.effort).toBe(effort);
    if (effort !== undefined) {
      expect(traceInputs[0]?.envelope.reasoning).toMatchObject({ effort, effortSource: "runtime_default" });
    }
  });

  it("hands a declared effort over the runtime's own default for the model", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ model: "claude-sonnet-5-5", effort: "low" }));
    const calls: SeenCall[] = [];
    const traceInputs: CreateRunTraceInput[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot, AGENT_RUNTIME_MODEL_PROVIDER: "foundry" },
      runReportFn: recordingReportFn(calls),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return stubTrace();
      },
    });
    expect(calls[0]?.options.effort).toBe("low");
    expect(traceInputs[0]?.envelope.reasoning).toMatchObject({ effort: "low", effortSource: "request" });
  });

  it("hands allowFurtherPause false through to the resumed run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes, false));
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls[0]?.options.allowFurtherPause).toBe(false);
  });

  it("rejects a resume checkpoint whose cargo bytes fail their sha256 without running anything", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { bytes } = manifestFor(join(workspaceRoot, "run-restored"));
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes));
    const calls: SeenCall[] = [];
    // The frame's descriptor promises the sealed bytes; the channel delivers zeros of the
    // declared length instead.
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([Buffer.alloc(bytes.byteLength)]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls).toHaveLength(0);
    if (response.ok) {
      throw new Error("a tampered checkpoint must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("run_child_error");
    expect(response.error.message).toContain("sha256");
  });

  it("writes a paused frame carrying the checkpoint and feeds the answer frame back to the run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    lines.push(JSON.stringify({ kind: "answer", answer: ANSWER, allowFurtherPause: false }));
    const frames: WireChildFrame[] = [];
    const checkpoint = sealCheckpoint(new TextEncoder().encode("session state"));
    const resolutionsSeenByRun: unknown[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => frames.push(frame),
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: async (_params, options) => {
        const resolution = await options?.pause?.({
          question: QUESTION,
          sessionId: "s-1",
          estimatedCostUsd: 0.2,
          checkpoint,
        });
        resolutionsSeenByRun.push(resolution);
        return RESULT;
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(resolutionsSeenByRun).toEqual([{ answer: ANSWER, allowFurtherPause: false }]);
    expect(frames).toHaveLength(1);
    const paused = frames[0];
    if (paused?.kind !== "paused") {
      throw new Error("expected a paused frame");
    }
    expect(paused.question).toEqual(QUESTION);
    expect(paused.sessionId).toBe("s-1");
    expect(paused.estimatedCostUsd).toBe(0.2);
    expect(paused.checkpoint).toEqual(toWireCheckpoint(checkpoint));
  });

  it("fails the run when the answer line is not an answer frame", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    lines.push(JSON.stringify({ kind: "paused", answer: ANSWER }));
    const checkpoint = sealCheckpoint(new TextEncoder().encode("session state"));
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) =>
        options
          ?.pause?.({ question: QUESTION, sessionId: "s-1", estimatedCostUsd: 0.2, checkpoint })
          .then(() => RESULT) ?? Promise.reject(new Error("pause seam missing")),
    });
    if (response.ok) {
      throw new Error("a malformed answer frame must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("run_child_error");
    expect(response.error.message).toContain("answer");
  });

  it("registers a termination cleanup that removes its uid-owned dirs mid-run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    let terminationCleanup: (() => void) | undefined;
    let releaseRun: () => void = () => {};
    const calls: SeenCall[] = [];
    const childDone = runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      onTerminationRequested: (cleanup) => {
        terminationCleanup = cleanup;
      },
      runReportFn: recordingReportFn(
        calls,
        () =>
          new Promise((resolve) => {
            releaseRun = () => resolve(RESULT);
          }),
      ),
    });
    await new Promise((resolve) => setTimeout(resolve, 10));
    const options = calls[0]?.options;
    const workspacePath = options?.workspacePath ?? "missing";
    const home = options?.subprocessEnv?.["HOME"] ?? "missing";
    expect(existsSync(workspacePath)).toBe(true);
    expect(existsSync(home)).toBe(true);
    if (terminationCleanup === undefined) {
      throw new Error("the child never registered its termination cleanup");
    }
    terminationCleanup();
    expect(existsSync(workspacePath)).toBe(false);
    expect(existsSync(home)).toBe(false);
    releaseRun();
    await childDone;
  });
});

function stubTrace(): RunTrace {
  return {
    packDir: "/traces/e-1/attempt-1",
    appendMessage: () => {},
    emitStage: () => {},
    finalize: () => {},
  };
}

describe("runChild documents", () => {
  const DOC_BYTES = Buffer.from("annual report body");

  function wireDocumentOf(
    bytes: Buffer,
    name = "report.pdf",
    extra: Record<string, unknown> = {},
  ): Record<string, unknown> {
    return { name, sha256: sha256Hex(bytes), sizeBytes: bytes.byteLength, ...extra };
  }

  it("stages documents and their manifest in the workspace before the run starts", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        documents: [
          wireDocumentOf(DOC_BYTES, "report.pdf", {
            mediaType: "application/pdf",
            documentClass: "AnnualReport",
          }),
          wireDocumentOf(Buffer.from("notes"), "notes.txt"),
        ],
      }),
    );
    const seenAtRunTime: { files?: string[]; manifest?: unknown; bytes?: Buffer } = {};
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([DOC_BYTES, Buffer.from("notes")]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        const docsDir = join(options?.workspacePath ?? "", "docs");
        seenAtRunTime.files = readdirSync(docsDir).sort();
        seenAtRunTime.bytes = readFileSync(join(docsDir, "report.pdf"));
        seenAtRunTime.manifest = JSON.parse(readFileSync(join(docsDir, "manifest.json"), "utf8"));
        return Promise.resolve(RESULT);
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(seenAtRunTime.files).toEqual(["manifest.json", "notes.txt", "report.pdf"]);
    expect(seenAtRunTime.bytes?.equals(DOC_BYTES)).toBe(true);
    expect(seenAtRunTime.manifest).toEqual([
      {
        name: "report.pdf",
        mediaType: "application/pdf",
        documentClass: "AnnualReport",
        sizeBytes: DOC_BYTES.length,
        sha256: sha256Hex(DOC_BYTES),
      },
      { name: "notes.txt", sizeBytes: 5, sha256: sha256Hex(Buffer.from("notes")) },
    ]);
  });

  it("opens the session with the staged-document statement ahead of the caller's prompt, verbatim below", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        documents: [
          wireDocumentOf(DOC_BYTES, "report.pdf", { documentClass: "AnnualReport" }),
          wireDocumentOf(Buffer.from("notes"), "notes.txt"),
        ],
      }),
    );
    const calls: SeenCall[] = [];
    const statedPathsExistedAtRunTime: boolean[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([DOC_BYTES, Buffer.from("notes")]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        const docsDir = join(options.workspacePath ?? "", "docs");
        statedPathsExistedAtRunTime.push(
          existsSync(join(docsDir, "report.pdf")),
          existsSync(join(docsDir, "notes.txt")),
        );
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(statedPathsExistedAtRunTime).toEqual([true, true]);
    const workspacePath = calls[0]?.options.workspacePath ?? "";
    expect(calls[0]?.params.prompt).toBe(
      composeUserTurn("p", workspacePath, [
        {
          area: "documents",
          name: "notes.txt",
          sha256: sha256Hex(Buffer.from("notes")),
          sizeBytes: 5,
        },
        {
          area: "documents",
          name: "report.pdf",
          sha256: sha256Hex(DOC_BYTES),
          sizeBytes: DOC_BYTES.length,
          documentClass: "AnnualReport",
        },
      ]),
    );
    expect(calls[0]?.params.prompt.endsWith("\n\np")).toBe(true);
    expect(calls[0]?.params.prompt).toContain(join(workspacePath, "docs", "report.pdf"));
  });

  it("sends the caller's prompt byte for byte when only skill files are staged", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const skillBytes = Buffer.from("grading rules");
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        skillFiles: [{ name: "grading.md", sha256: sha256Hex(skillBytes), sizeBytes: skillBytes.byteLength }],
      }),
    );
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([skillBytes]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.params.prompt).toBe("p");
  });

  it("sends a cold resume's answer verbatim, without restating the document statement", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { bytes } = manifestFor(join(workspaceRoot, "run-restored"));
    const lines = createAsyncQueue<string>();
    lines.push(
      resumeRequestLine(bytes, true, {
        documents: [wireDocumentOf(DOC_BYTES, "report.pdf", { documentClass: "AnnualReport" })],
      }),
    );
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes, [DOC_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.params.prompt).toBe(ANSWER);
  });

  it("keeps promptSha256 hashing the caller's prompt, never the composed first turn", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ documents: [wireDocumentOf(DOC_BYTES, "report.pdf")] }));
    const traceInputs: CreateRunTraceInput[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([DOC_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return stubTrace();
      },
    });
    expect(traceInputs[0]?.envelope.promptSha256).toBe(sha256Hex(new TextEncoder().encode("p")));
  });

  it("hands the run a read mirror under the private home covering the documents and only them", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const skillBytes = Buffer.from("grading rules");
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        documents: [wireDocumentOf(DOC_BYTES, "report.pdf")],
        skillFiles: [{ name: "grading.md", sha256: sha256Hex(skillBytes), sizeBytes: skillBytes.byteLength }],
      }),
    );
    const calls: SeenCall[] = [];
    const seen: { mirrorDirExisted?: boolean } = {};
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([DOC_BYTES, skillBytes]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        seen.mirrorDirExisted = existsSync(options.readMirror?.dir ?? "missing");
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const options = calls[0]?.options;
    const home = options?.subprocessEnv?.["HOME"] ?? "missing";
    expect(options?.readMirror?.dir).toBe(join(home, "doc-mirror"));
    expect(options?.readMirror?.documents).toEqual([
      { name: "report.pdf", sha256: sha256Hex(DOC_BYTES), sizeBytes: DOC_BYTES.length },
    ]);
    expect(seen.mirrorDirExisted).toBe(true);
    // The mirror lives inside the private home, so termination cleanup removes it with everything else.
    expect(existsSync(home)).toBe(false);
  });

  it("re-stages documents beside the restored checkpoint files on a cold resume", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes, true, { documents: [wireDocumentOf(DOC_BYTES)] }));
    const seenAtRunTime: { doc?: Buffer; draft?: boolean } = {};
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes, [DOC_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        seenAtRunTime.doc = readFileSync(join(options?.workspacePath ?? "", "docs", "report.pdf"));
        seenAtRunTime.draft = existsSync(join(options?.workspacePath ?? "", "draft.md"));
        return Promise.resolve(RESULT);
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(seenAtRunTime.doc?.equals(DOC_BYTES)).toBe(true);
    expect(seenAtRunTime.draft).toBe(true);
  });

  it("enforces the wire-carried document budget before pulling streamed bytes", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ documentBudgetBytes: 8, documents: [wireDocumentOf(DOC_BYTES)] }));
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([DOC_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls).toHaveLength(0);
    if (response.ok) {
      throw new Error("an over-budget frame must not produce a success envelope");
    }
    expect(response.error.message).toContain("budget");
  });

  it("publishes and traces under the attempt the wire request names", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ attempt: 2 }));
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        const outDir = join(options?.workspacePath ?? "", "out");
        mkdirSync(outDir, { recursive: true });
        writeFileSync(join(outDir, "report.md"), "# attempt two");
        return Promise.resolve(RESULT);
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const published = join(workspaceRoot, "artifacts", "e-1", "attempt-2", "out", "report.md");
    expect(readFileSync(published, "utf8")).toBe("# attempt two");
  });

  it("re-stages out/ from the published artifacts mirror on a cold resume", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const publishedOut = join(workspaceRoot, "artifacts", "e-1", "attempt-1", "out");
    mkdirSync(join(publishedOut, "tables"), { recursive: true });
    writeFileSync(join(publishedOut, "draft.md"), "# draft so far");
    writeFileSync(join(publishedOut, "tables", "q1.csv"), "a,b\n");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes));
    const seenAtRunTime: { draft?: string; nested?: string } = {};
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        const outDir = join(options?.workspacePath ?? "", "out");
        seenAtRunTime.draft = readFileSync(join(outDir, "draft.md"), "utf8");
        seenAtRunTime.nested = readFileSync(join(outDir, "tables", "q1.csv"), "utf8");
        return Promise.resolve(RESULT);
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(seenAtRunTime.draft).toBe("# draft so far");
    expect(seenAtRunTime.nested).toBe("a,b\n");
  });

  it("fails before running anything when a wire document does not match its sha256", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        documents: [{ name: "report.pdf", sha256: "0".repeat(64), sizeBytes: DOC_BYTES.byteLength }],
      }),
    );
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([DOC_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls).toHaveLength(0);
    if (response.ok) {
      throw new Error("a tampered document must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("run_child_error");
    expect(response.error.message).toContain("sha256");
  });
});

describe("runChild skill files", () => {
  const SKILL_BYTES = Buffer.from("# how to grade");
  const DOC_BYTES = Buffer.from("annual report body");

  function wireFileOf(bytes: Buffer, name: string): Record<string, unknown> {
    return { name, sha256: sha256Hex(bytes), sizeBytes: bytes.byteLength };
  }

  it("stages skill files as a plugin outside the workspace and hands the binding to the run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        documents: [wireFileOf(DOC_BYTES, "report.pdf")],
        skillFiles: [{ ...wireFileOf(SKILL_BYTES, "grading.md"), description: "How to grade" }],
      }),
    );
    const seenAtRunTime: { skillMd?: string; workspaceDirs?: string[]; docs?: string[] } = {};
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([DOC_BYTES, SKILL_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        const pluginPath = options.skillPlugin?.path ?? "";
        seenAtRunTime.skillMd = readFileSync(join(pluginPath, "skills", "grading", "SKILL.md"), "utf8");
        seenAtRunTime.workspaceDirs = readdirSync(options.workspacePath ?? "").sort();
        seenAtRunTime.docs = readdirSync(join(options.workspacePath ?? "", "docs")).sort();
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const binding = calls[0]?.options.skillPlugin;
    expect(binding?.skillNames).toEqual(["skills:grading"]);
    expect(binding?.path.startsWith(calls[0]?.options.workspacePath ?? "!")).toBe(false);
    expect(seenAtRunTime.skillMd).toBe(
      `---\nname: "grading"\ndescription: "How to grade"\n---\n\n${SKILL_BYTES.toString("utf8")}`,
    );
    expect(seenAtRunTime.workspaceDirs).toEqual(["docs"]);
    expect(seenAtRunTime.docs).toEqual(["manifest.json", "report.pdf"]);
  });

  it("hands no skill plugin to a run without skill files", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.skillPlugin).toBeUndefined();
  });

  it("charges documents and skill files against the one wire-carried budget", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        documentBudgetBytes: DOC_BYTES.length + 4,
        documents: [wireFileOf(DOC_BYTES, "report.pdf")],
        skillFiles: [wireFileOf(SKILL_BYTES, "grading.md")],
      }),
    );
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([DOC_BYTES, SKILL_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls).toHaveLength(0);
    if (response.ok) {
      throw new Error("an over-budget frame must not produce a success envelope");
    }
    expect(response.error.message).toContain("budget");
  });

  it("re-stages the skill plugin beside the restored checkpoint on a cold resume", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes, true, { skillFiles: [wireFileOf(SKILL_BYTES, "grading.md")] }));
    const seenAtRunTime: { skillMd?: string } = {};
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes, [SKILL_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        const pluginPath = options.skillPlugin?.path ?? "";
        seenAtRunTime.skillMd = readFileSync(join(pluginPath, "skills", "grading", "SKILL.md"), "utf8");
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.skillPlugin?.skillNames).toEqual(["skills:grading"]);
    expect(seenAtRunTime.skillMd).toBe(`---\nname: "grading"\n---\n\n${SKILL_BYTES.toString("utf8")}`);
  });

  it("stages skill assets in their own dir outside workspace and home, and hands the path on the binding", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const assetBytes = Buffer.from("print('check')\n");
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        skillFiles: [
          {
            ...wireFileOf(SKILL_BYTES, "grading.md"),
            description: "How to grade",
            assets: [wireFileOf(assetBytes, "verify.py")],
          },
        ],
      }),
    );
    const seenAtRunTime: { assetText?: string } = {};
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([SKILL_BYTES, assetBytes]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        const assetsPath = options.skillPlugin?.assetsPath ?? "";
        seenAtRunTime.assetText = readFileSync(join(assetsPath, "grading", "verify.py"), "utf8");
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const binding = calls[0]?.options.skillPlugin;
    const assetsPath = binding?.assetsPath ?? "!";
    expect(assetsPath.startsWith(`${workspaceRoot}/`)).toBe(true);
    expect(assetsPath.startsWith(`${calls[0]?.options.workspacePath ?? "!"}/`)).toBe(false);
    const privateHome = dirname(binding?.path ?? "!");
    expect(assetsPath.startsWith(`${privateHome}/`)).toBe(false);
    expect(seenAtRunTime.assetText).toBe("print('check')\n");
    expect(existsSync(assetsPath)).toBe(false);
  });

  it("hands no assets path to a run whose skills ship no assets", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ skillFiles: [wireFileOf(SKILL_BYTES, "grading.md")] }));
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([SKILL_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls[0]?.options.skillPlugin?.assetsPath).toBeUndefined();
  });

  it("re-stages skill assets from the wire on a cold resume — no checkpoint carries them", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const assetBytes = Buffer.from("print('check')\n");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(
      resumeRequestLine(bytes, true, {
        skillFiles: [
          { ...wireFileOf(SKILL_BYTES, "grading.md"), assets: [wireFileOf(assetBytes, "verify.py")] },
        ],
      }),
    );
    const seenAtRunTime: { assetText?: string } = {};
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes, [SKILL_BYTES, assetBytes]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        const assetsPath = options.skillPlugin?.assetsPath ?? "";
        seenAtRunTime.assetText = readFileSync(join(assetsPath, "grading", "verify.py"), "utf8");
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(seenAtRunTime.assetText).toBe("print('check')\n");
  });
});

describe("runChild agents", () => {
  const AGENT_SKILL_BYTES = Buffer.from("# how to grade");
  const RESOLVED_AGENTS = [
    { name: "verifier", description: "checks figures", prompt: "verify", skills: ["skills:grading"] },
  ];

  it("verifies declared agents against the staged skills and hands them to the run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        skillFiles: [
          {
            name: "grading.md",
            sha256: sha256Hex(AGENT_SKILL_BYTES),
            sizeBytes: AGENT_SKILL_BYTES.byteLength,
          },
        ],
        agents: RESOLVED_AGENTS,
      }),
    );
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([AGENT_SKILL_BYTES]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.agents).toEqual(RESOLVED_AGENTS);
  });

  it("fails the run before it starts when an agent references a skill the run does not stage", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        agents: [{ name: "verifier", description: "d", prompt: "p", skills: ["skills:missing"] }],
      }),
    );
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response.ok).toBe(false);
    expect(calls).toHaveLength(0);
  });
});

describe("runChild model", () => {
  it("overrides the child run env's model pin with the wire-carried model", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ model: "us.anthropic.claude-sonnet-4-6" }));
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot, ANTHROPIC_MODEL: "default-pin" },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.subprocessEnv?.["ANTHROPIC_MODEL"]).toBe("us.anthropic.claude-sonnet-4-6");
  });

  it("keeps the env default when the wire request names no model", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot, ANTHROPIC_MODEL: "default-pin" },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls[0]?.options.subprocessEnv?.["ANTHROPIC_MODEL"]).toBe("default-pin");
  });

  it("records the effective per-run model in the trace envelope, not the env default", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ model: "us.anthropic.claude-sonnet-4-6" }));
    const traceInputs: CreateRunTraceInput[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot, ANTHROPIC_MODEL: "default-pin" },
      runReportFn: recordingReportFn([]),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return stubTrace();
      },
    });
    expect(traceInputs[0]?.envelope.modelPin).toBe("us.anthropic.claude-sonnet-4-6");
  });

  it("records the effective reasoning config in the trace envelope, defaults labeled as the SDK's", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const traceInputs: CreateRunTraceInput[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return stubTrace();
      },
    });
    expect(traceInputs[0]?.envelope.reasoning).toEqual({
      effort: "high",
      effortSource: "sdk_default",
      thinking: { type: "adaptive" },
      thinkingSource: "sdk_default_assumed",
    });
  });

  it("records a wire-carried effort in the trace envelope as the request's own choice", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ effort: "medium" }));
    const traceInputs: CreateRunTraceInput[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return stubTrace();
      },
    });
    expect(traceInputs[0]?.envelope.reasoning).toEqual({
      effort: "medium",
      effortSource: "request",
      thinking: { type: "adaptive" },
      thinkingSource: "sdk_default_assumed",
    });
  });

  it("carries the model on a cold resume frame so the resumed run continues on it", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes, true, { model: "us.anthropic.claude-sonnet-4-6" }));
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot, ANTHROPIC_MODEL: "default-pin" },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls[0]?.options.subprocessEnv?.["ANTHROPIC_MODEL"]).toBe("us.anthropic.claude-sonnet-4-6");
  });
});

describe("runChild outputFormat", () => {
  it("hands the wire-carried output format to the run, absent when unset", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ outputFormat: { type: "json_schema", schema: { type: "object" } } }));
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.outputFormat).toEqual({ type: "json_schema", schema: { type: "object" } });
    const bare = createAsyncQueue<string>();
    bare.push(startRequestLine());
    const bareCalls: SeenCall[] = [];
    await runChild({
      lines: bare,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(bareCalls),
    });
    expect("outputFormat" in (bareCalls[0]?.options ?? {})).toBe(false);
  });

  it("carries the output format on a cold resume frame so the resumed segment still enforces it", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(
      resumeRequestLine(bytes, true, { outputFormat: { type: "json_schema", schema: { type: "object" } } }),
    );
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls[0]?.options.outputFormat).toEqual({ type: "json_schema", schema: { type: "object" } });
  });
});

describe("runChild systemPrompt", () => {
  const SYSTEM_PROMPT = ["static role and rules", "per-pass context"];

  it("hands the wire-carried system prompt to the run, absent when unset", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ systemPrompt: SYSTEM_PROMPT }));
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.systemPrompt).toEqual(SYSTEM_PROMPT);
    const bare = createAsyncQueue<string>();
    bare.push(startRequestLine());
    const bareCalls: SeenCall[] = [];
    await runChild({
      lines: bare,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(bareCalls),
    });
    expect("systemPrompt" in (bareCalls[0]?.options ?? {})).toBe(false);
  });

  it("carries the system prompt on a cold resume frame so the resumed segment keeps its rules", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes, true, { systemPrompt: SYSTEM_PROMPT }));
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls[0]?.options.systemPrompt).toEqual(SYSTEM_PROMPT);
  });
});

describe("runChild MCP server", () => {
  it("hands the MCP URL and run token to the run in memory, never the subprocess env", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ mcpServerUrl: "https://platform.internal/mcp", runToken: "tok-1" }));
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.mcpServer).toEqual({
      url: "https://platform.internal/mcp",
      runToken: "tok-1",
    });
    expect(JSON.stringify(calls[0]?.options.subprocessEnv)).not.toContain("tok-1");
    const traceDir = join(workspaceRoot, "traces");
    const traceFiles = readdirSync(traceDir, { recursive: true, encoding: "utf8" });
    expect(traceFiles.length).toBeGreaterThan(0);
    for (const file of traceFiles) {
      const absolute = join(traceDir, file);
      if (!statSync(absolute).isFile()) {
        continue;
      }
      expect(readFileSync(absolute, "utf8")).not.toContain("tok-1");
    }
  });

  it("passes no MCP seam at all when the wire request names no server", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect("mcpServer" in (calls[0]?.options ?? {})).toBe(false);
  });

  it("carries the MCP seam through a cold resume frame", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const { bytes } = manifestFor(workspacePath);
    const lines = createAsyncQueue<string>();
    lines.push(
      resumeRequestLine(bytes, true, { mcpServerUrl: "https://platform.internal/mcp", runToken: "tok-1" }),
    );
    const calls: SeenCall[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls[0]?.options.mcpServer).toEqual({
      url: "https://platform.internal/mcp",
      runToken: "tok-1",
    });
  });
});

describe("runChild deliverables", () => {
  function writeOutFile(options: RunReportOptions | undefined, name: string, content: string): void {
    const outDir = join(options?.workspacePath ?? "missing", "out");
    mkdirSync(outDir, { recursive: true });
    writeFileSync(join(outDir, name), content);
  }

  it("publishes out/ to the retained artifacts area at terminal, outliving workspace cleanup", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        writeOutFile(options, "report.md", "# final");
        return Promise.resolve(RESULT);
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const published = join(workspaceRoot, "artifacts", "e-1", "attempt-1", "out", "report.md");
    expect(readFileSync(published, "utf8")).toBe("# final");
  });

  it("publishes the current out/ draft before the paused frame is written", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const checkpoint = sealCheckpoint(new TextEncoder().encode("session state"));
    const draftSeenAtPauseFrame: string[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => {
        if (frame.kind === "paused") {
          const published = join(workspaceRoot, "artifacts", "e-1", "attempt-1", "out", "draft.md");
          draftSeenAtPauseFrame.push(existsSync(published) ? readFileSync(published, "utf8") : "missing");
          lines.push(JSON.stringify({ kind: "answer", answer: ANSWER, allowFurtherPause: true }));
        }
      },
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: async (_params, options) => {
        writeOutFile(options, "draft.md", "# draft");
        await options?.pause?.({ question: QUESTION, sessionId: "s-1", estimatedCostUsd: 0.2, checkpoint });
        return RESULT;
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(draftSeenAtPauseFrame).toEqual(["# draft"]);
  });

  it("still publishes out/ when the run fails, so a failed run's partial work stays fetchable", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        writeOutFile(options, "partial.md", "# partial");
        return Promise.reject(
          new RunFailedError("boom", { subtype: "error_max_turns", estimatedCostUsd: 0.1, sessionId: "s-1" }),
        );
      },
    });
    expect(response.ok).toBe(false);
    const published = join(workspaceRoot, "artifacts", "e-1", "attempt-1", "out", "partial.md");
    expect(readFileSync(published, "utf8")).toBe("# partial");
  });

  it("turns a successful run into a failure when its deliverables cannot be published", async () => {
    const workspaceRoot = newWorkspaceRoot();
    writeFileSync(join(workspaceRoot, "artifacts"), "blocks the artifacts root");
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        writeOutFile(options, "report.md", "# final");
        return Promise.resolve(RESULT);
      },
    });
    if (response.ok) {
      throw new Error("an unpublishable success must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("run_child_error");
  });

  it("publishes exactly once on the success path — a failed terminal publish is never retried", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    let publishAttempts = 0;
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      publishArtifactsFn: () => {
        publishAttempts += 1;
        throw new Error("artifacts area unavailable");
      },
      runReportFn: () => Promise.resolve(RESULT),
    });
    expect(response.ok).toBe(false);
    expect(publishAttempts).toBe(1);
  });

  it("publishes exactly once on the failure path, keeping the run's own failure", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    let publishAttempts = 0;
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      publishArtifactsFn: () => {
        publishAttempts += 1;
        throw new Error("artifacts area unavailable");
      },
      runReportFn: () =>
        Promise.reject(
          new RunFailedError("boom", { subtype: "error_max_turns", estimatedCostUsd: 0.1, sessionId: "s-1" }),
        ),
    });
    if (response.ok) {
      throw new Error("a failed run must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("error_max_turns");
    expect(publishAttempts).toBe(1);
  });

  it("fails the pause cleanly when the publish-at-pause throws, like a failed checkpoint save", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const checkpoint = sealCheckpoint(new TextEncoder().encode("session state"));
    const frames: WireChildFrame[] = [];
    let pauseRejected = false;
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => frames.push(frame),
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      publishArtifactsFn: () => {
        throw new Error("artifacts area unavailable");
      },
      runReportFn: async (_params, options) => {
        try {
          await options?.pause?.({ question: QUESTION, sessionId: "s-1", estimatedCostUsd: 0.2, checkpoint });
        } catch {
          pauseRejected = true;
          throw new RunFailedError("pause could not publish", {
            subtype: "pause_rejected",
            estimatedCostUsd: 0.2,
            sessionId: "s-1",
          });
        }
        return RESULT;
      },
    });
    expect(pauseRejected).toBe(true);
    expect(frames).toEqual([]);
    if (response.ok) {
      throw new Error("a pause that cannot publish must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("pause_rejected");
  });

  it("refuses to publish an out/ tree over the published-size cap, failing the run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      publishArtifactsFn: (input) => publishRunArtifacts({ ...input, maxTotalBytes: 16 }),
      runReportFn: (_params, options) => {
        writeOutFile(options, "report.md", "x".repeat(64));
        return Promise.resolve(RESULT);
      },
    });
    if (response.ok) {
      throw new Error("an over-cap publish must not produce a success envelope");
    }
    expect(response.error.message).toContain("cap");
  });

  it("fails a successful run retained_budget_exceeded when the publish refuses the declared cap, forfeiting the prior mirror", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const artifactsRoot = join(workspaceRoot, "artifacts");
    const priorWorkspace = join(workspaceRoot, "prior-segment");
    mkdirSync(join(priorWorkspace, "out"), { recursive: true });
    writeFileSync(join(priorWorkspace, "out", "draft.md"), "early draft");
    publishRunArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1, workspacePath: priorWorkspace });
    const attemptDir = artifactAttemptDirOf(artifactsRoot, "e-1", 1);
    expect(existsSync(join(attemptDir, "out", "draft.md"))).toBe(true);

    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ maxRetainedOutputBytes: 16 }));
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        writeOutFile(options, "report.md", "x".repeat(64));
        return Promise.resolve(RESULT);
      },
    });
    if (response.ok) {
      throw new Error("a publish over the declared cap must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("retained_budget_exceeded");
    expect(response.error.estimatedCostUsd).toBe(RESULT.estimatedCostUsd);
    expect(response.error.sessionId).toBe(RESULT.sessionId);
    expect(existsSync(join(attemptDir, "out"))).toBe(false);
    expect(existsSync(join(attemptDir, ARTIFACTS_MANIFEST_NAME))).toBe(false);
  });

  it("fails the pause retained_budget_exceeded when the pause publish refuses the declared cap", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ maxRetainedOutputBytes: 16 }));
    const checkpoint = sealCheckpoint(new TextEncoder().encode("session state"));
    const frames: WireChildFrame[] = [];
    let pauseError: unknown;
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => frames.push(frame),
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: async (_params, options) => {
        writeOutFile(options, "report.md", "x".repeat(64));
        try {
          await options?.pause?.({ question: QUESTION, sessionId: "s-1", estimatedCostUsd: 0.2, checkpoint });
        } catch (error) {
          pauseError = error;
          throw error;
        }
        return RESULT;
      },
    });
    expect(pauseError).toBeInstanceOf(RunFailedError);
    expect(frames).toEqual([]);
    if (response.ok) {
      throw new Error("a pause over the declared cap must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("retained_budget_exceeded");
    expect(response.error.estimatedCostUsd).toBe(0.2);
    expect(response.error.sessionId).toBe("s-1");
  });

  it("keeps a failed run's own subtype while forfeiting the prior mirror when the settle publish refuses the declared cap", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const artifactsRoot = join(workspaceRoot, "artifacts");
    const priorWorkspace = join(workspaceRoot, "prior-segment");
    mkdirSync(join(priorWorkspace, "out"), { recursive: true });
    writeFileSync(join(priorWorkspace, "out", "draft.md"), "early draft");
    publishRunArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1, workspacePath: priorWorkspace });
    const attemptDir = artifactAttemptDirOf(artifactsRoot, "e-1", 1);

    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ maxRetainedOutputBytes: 16 }));
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options) => {
        writeOutFile(options, "report.md", "x".repeat(64));
        return Promise.reject(
          new RunFailedError("boom", { subtype: "error_max_turns", estimatedCostUsd: 0.1, sessionId: "s-1" }),
        );
      },
    });
    if (response.ok) {
      throw new Error("a failed run must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("error_max_turns");
    expect(existsSync(join(attemptDir, "out"))).toBe(false);
    expect(existsSync(join(attemptDir, ARTIFACTS_MANIFEST_NAME))).toBe(false);
  });
});

describe("runChild trace wiring", () => {
  it("creates a trace keyed by executionId and attempt 1 and hands it to the run", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const calls: SeenCall[] = [];
    const traceInputs: CreateRunTraceInput[] = [];
    const trace = stubTrace();
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot, ANTHROPIC_MODEL: "test-model-pin" },
      runReportFn: recordingReportFn(calls),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return trace;
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.trace).toBe(trace);
    const input = traceInputs[0];
    expect(input?.traceRoot).toBe(join(workspaceRoot, "traces"));
    expect(input?.executionId).toBe("e-1");
    expect(input?.attempt).toBe(1);
    expect(input?.configDir.startsWith(join(workspaceRoot, "home-"))).toBe(true);
    expect(input?.configDir.endsWith(".claude")).toBe(true);
    expect(input?.envelope).toEqual({
      modelProvider: "bedrock",
      modelPin: "test-model-pin",
      promptSha256: sha256Hex(new TextEncoder().encode("p")),
      systemPromptSha256: ENVELOPE_ABSENT,
      outputFormatSha256: ENVELOPE_ABSENT,
      stagedInputsSha256: ENVELOPE_ABSENT,
      mcpServerSha256: ENVELOPE_ABSENT,
      tools: ["Bash", "Read"],
      skills: [],
      agents: [],
      sandboxConfigId: sandboxConfigId(),
      sdkVersion: input?.envelope.sdkVersion ?? "",
      reasoning: {
        effort: "high",
        effortSource: "sdk_default",
        thinking: { type: "adaptive" },
        thinkingSource: "sdk_default_assumed",
      },
    });
    expect(input?.envelope.sdkVersion).not.toBe("");
  });

  it("fingerprints the resume prompt when resuming from a checkpoint", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { bytes } = manifestFor(join(workspaceRoot, "run-restored"));
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes));
    const traceInputs: CreateRunTraceInput[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return stubTrace();
      },
    });
    expect(traceInputs[0]?.envelope.promptSha256).toBe(sha256Hex(new TextEncoder().encode(ANSWER)));
  });

  it("still runs to success when trace creation throws", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
      createTraceFn: () => {
        throw new Error("trace root is not writable");
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.trace).toBeUndefined();
  });

  it("writes a real envelope-headed trace pack that outlives the child cleanup by default", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot, ANTHROPIC_MODEL: "test-model-pin" },
      runReportFn: recordingReportFn([]),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    const traceFile = join(workspaceRoot, "traces", "e-1", "attempt-1", "trace.ndjson");
    expect(existsSync(traceFile)).toBe(true);
    const firstLine = readFileSync(traceFile, "utf8").split("\n")[0] ?? "";
    const envelope = JSON.parse(firstLine) as Record<string, unknown>;
    expect(envelope).toMatchObject({ type: "envelope", executionId: "e-1", modelPin: "test-model-pin" });
  });
});

describe("runChild trace envelope fingerprints", () => {
  const MCP_URL = "https://platform.internal/mcp";

  function serializedSha256(value: unknown): string {
    return sha256Hex(new TextEncoder().encode(JSON.stringify(value)));
  }

  function wireFileOf(bytes: Buffer, name: string): Record<string, unknown> {
    return { name, contentBase64: bytes.toString("base64"), sha256: sha256Hex(bytes) };
  }

  function splitBodies(value: unknown): { metas: Record<string, unknown>[]; bodies: Buffer[] } {
    const bodies: Buffer[] = [];
    const metas = (Array.isArray(value) ? value : []).map((item) => {
      const { contentBase64, ...rest } = item as Record<string, unknown>;
      const body = Buffer.from(String(contentBase64 ?? ""), "base64");
      bodies.push(body);
      return { ...rest, sizeBytes: body.byteLength };
    });
    return { metas, bodies };
  }

  async function envelopeOf(extra: Record<string, unknown>): Promise<TraceEnvelopeInput> {
    const docs = splitBodies(extra["documents"]);
    const skills = splitBodies(extra["skillFiles"]);
    const frameExtra = {
      ...extra,
      ...(extra["documents"] === undefined ? {} : { documents: docs.metas }),
      ...(extra["skillFiles"] === undefined ? {} : { skillFiles: skills.metas }),
    };
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine(frameExtra));
    const traceInputs: CreateRunTraceInput[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([...docs.bodies, ...skills.bodies]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
      createTraceFn: (input) => {
        traceInputs.push(input);
        return stubTrace();
      },
    });
    if (!response.ok) {
      throw new Error(`the run failed: ${response.error.message}`);
    }
    const envelope = traceInputs[0]?.envelope;
    if (envelope === undefined) {
      throw new Error("the run opened no trace");
    }
    return envelope;
  }

  it("fingerprints the system prompt, telling one string apart from the same text in blocks", async () => {
    const single = await envelopeOf({ systemPrompt: "role and rulesper-pass context" });
    const blocks = await envelopeOf({ systemPrompt: ["role and rules", "per-pass context"] });
    expect(single.systemPromptSha256).toBe(serializedSha256("role and rulesper-pass context"));
    expect(blocks.systemPromptSha256).toBe(serializedSha256(["role and rules", "per-pass context"]));
    expect(blocks.systemPromptSha256).not.toBe(single.systemPromptSha256);
  });

  it("marks an absent system prompt absent instead of hashing an empty value", async () => {
    const envelope = await envelopeOf({});
    expect(envelope.systemPromptSha256).toBe(ENVELOPE_ABSENT);
    expect(envelope.systemPromptSha256).not.toBe(serializedSha256(""));
    expect(envelope.systemPromptSha256).not.toBe(sha256Hex(new TextEncoder().encode("")));
    expect(ENVELOPE_ABSENT).not.toMatch(/^[0-9a-f]{64}$/);
  });

  it("fingerprints the requested output format, absent when the run asks for none", async () => {
    const outputFormat = { type: "json_schema", schema: { type: "object" } };
    const structured = await envelopeOf({ outputFormat });
    expect(structured.outputFormatSha256).toBe(serializedSha256(outputFormat));
    expect((await envelopeOf({})).outputFormatSha256).toBe(ENVELOPE_ABSENT);
  });

  it("digests the staged inputs by area, name and content, whatever order they arrive in", async () => {
    const report = wireFileOf(Buffer.from("annual report body"), "report.pdf");
    const notes = wireFileOf(Buffer.from("notes"), "notes.txt");
    const skill = wireFileOf(Buffer.from("# how to grade"), "grading.md");
    const forward = await envelopeOf({ documents: [report, notes], skillFiles: [skill] });
    const reversed = await envelopeOf({ documents: [notes, report], skillFiles: [skill] });
    expect(forward.stagedInputsSha256).toBe(reversed.stagedInputsSha256);

    const edited = await envelopeOf({
      documents: [report, wireFileOf(Buffer.from("other notes"), "notes.txt")],
      skillFiles: [skill],
    });
    expect(edited.stagedInputsSha256).not.toBe(forward.stagedInputsSha256);

    const asDocument = await envelopeOf({ documents: [skill] });
    const asSkillFile = await envelopeOf({ skillFiles: [skill] });
    expect(asDocument.stagedInputsSha256).not.toBe(asSkillFile.stagedInputsSha256);
  });

  it("covers the document metadata the agent reads off the staged manifest", async () => {
    const bytes = Buffer.from("annual report body");
    const plain = await envelopeOf({ documents: [wireFileOf(bytes, "report.pdf")] });
    const classified = await envelopeOf({
      documents: [{ ...wireFileOf(bytes, "report.pdf"), documentClass: "AnnualReport" }],
    });
    expect(classified.stagedInputsSha256).not.toBe(plain.stagedInputsSha256);
  });

  it("covers the skill description, which shapes when the model invokes the skill", async () => {
    const bytes = Buffer.from("# how to grade");
    const plain = await envelopeOf({ skillFiles: [wireFileOf(bytes, "grading.md")] });
    const described = await envelopeOf({
      skillFiles: [{ ...wireFileOf(bytes, "grading.md"), description: "How to grade" }],
    });
    expect(described.stagedInputsSha256).not.toBe(plain.stagedInputsSha256);
  });

  it("states the run's actual tool surface and registered skills, empty without any", async () => {
    const withSkills = await envelopeOf({
      skillFiles: [wireFileOf(Buffer.from("# how to grade"), "grading.md")],
    });
    expect(withSkills.tools).toEqual(["Bash", "Read", "Skill"]);
    expect(withSkills.skills).toEqual(["skills:grading"]);
    const without = await envelopeOf({});
    expect(without.tools).toEqual(["Bash", "Read"]);
    expect(without.skills).toEqual([]);
  });

  it("marks a run that stages no inputs absent", async () => {
    expect((await envelopeOf({})).stagedInputsSha256).toBe(ENVELOPE_ABSENT);
  });

  it("fingerprints the bound MCP server, absent when the run gets no platform tools", async () => {
    const bound = await envelopeOf({ mcpServerUrl: MCP_URL, runToken: "tok-1" });
    expect(bound.mcpServerSha256).toBe(serializedSha256(MCP_URL));
    expect((await envelopeOf({})).mcpServerSha256).toBe(ENVELOPE_ABSENT);
  });

  it("keeps the run token out of the envelope entirely", async () => {
    const first = await envelopeOf({ mcpServerUrl: MCP_URL, runToken: "tok-1" });
    const second = await envelopeOf({ mcpServerUrl: MCP_URL, runToken: "tok-2" });
    expect(second).toEqual(first);
    expect(JSON.stringify(first)).not.toContain("tok-1");
  });
});

describe("buildRunEnv", () => {
  const HOST_ENV = {
    PATH: "/usr/bin",
    AWS_REGION: "us-west-2",
    AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338",
    AWS_ACCESS_KEY_ID: "AKIA",
    AWS_SECRET_ACCESS_KEY: "sk",
    AWS_SESSION_TOKEN: "st",
  };

  it("strips static credentials and keeps the metadata endpoint in metadata mode", () => {
    const env = buildRunEnv({
      env: { ...HOST_ENV, AGENT_RUNTIME_METADATA_CREDENTIALS: "true" },
      privateHome: newWorkspaceRoot(),
      executionId: "e-1",
    });
    expect(env["AWS_ACCESS_KEY_ID"]).toBeUndefined();
    expect(env["AWS_SECRET_ACCESS_KEY"]).toBeUndefined();
    expect(env["AWS_SESSION_TOKEN"]).toBeUndefined();
    expect(env["AWS_EC2_METADATA_SERVICE_ENDPOINT"]).toBe("http://imds-mock:1338");
    expect(env["AWS_CONFIG_FILE"]).toBeUndefined();
    expect(env["AWS_PROFILE"]).toBeUndefined();
  });

  it("writes the assume-role config, strips static credentials, and keeps the endpoint in role mode", () => {
    const privateHome = newWorkspaceRoot();
    const env = buildRunEnv({
      env: { ...HOST_ENV, AGENT_RUNTIME_ROLE_ARN: "arn:aws:iam::1:role/agent" },
      privateHome,
      executionId: "e-1",
    });
    expect(env["AWS_CONFIG_FILE"]).toBe(join(privateHome, ".aws", "config"));
    expect(env["AWS_PROFILE"]).toBeDefined();
    expect(env["AWS_ACCESS_KEY_ID"]).toBeUndefined();
    expect(env["AWS_EC2_METADATA_SERVICE_ENDPOINT"]).toBe("http://imds-mock:1338");
  });

  it("leaves static credentials in place only in the ambient break-glass mode", () => {
    const env = buildRunEnv({ env: HOST_ENV, privateHome: newWorkspaceRoot(), executionId: "e-1" });
    expect(env["AWS_ACCESS_KEY_ID"]).toBe("AKIA");
    expect(env["AWS_CONFIG_FILE"]).toBeUndefined();
  });
});

describe("buildRunEnv per provider", () => {
  const FOUNDRY_CHILD_ENV = {
    PATH: "/opt/venv/bin:/usr/bin",
    HOME: "/home/bun",
    NODE_PATH: "/opt/node/node_modules",
    CLAUDE_CONFIG_DIR: "/home/bun/.claude",
    CLAUDE_CODE_USE_FOUNDRY: "1",
    CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
    ANTHROPIC_MODEL: "claude-sonnet-5",
    ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
    ANTHROPIC_FOUNDRY_RESOURCE: "inia-foundry",
    WORKSPACE_ROOT: "/workspaces",
    AGENT_RUNTIME_VERSION: "0.3.1",
    AGENT_RUNTIME_REVISION: "abc",
    AGENT_RUNTIME_MODEL_PROVIDER: "foundry",
  };

  it("foundry: the Foundry allowlist, no AWS profile written, no AWS variable, no identity step", () => {
    const home = newWorkspaceRoot();
    expect(
      buildRunEnv({
        env: {
          ...FOUNDRY_CHILD_ENV,
          AGENT_RUNTIME_ROLE_ARN: "arn:aws:iam::1:role/agent",
          AWS_REGION: "us-west-2",
        },
        privateHome: home,
        executionId: "e-1",
        model: "claude-opus-4-8",
      }),
    ).toEqual({
      PATH: "/opt/venv/bin:/usr/bin",
      HOME: home,
      NODE_PATH: "/opt/node/node_modules",
      CLAUDE_CONFIG_DIR: join(home, ".claude"),
      CLAUDE_CODE_USE_FOUNDRY: "1",
      CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
      ANTHROPIC_MODEL: "claude-opus-4-8",
      ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
      ANTHROPIC_FOUNDRY_RESOURCE: "inia-foundry",
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
      CLAUDE_CODE_MAX_OUTPUT_TOKENS: "64000",
      ENABLE_TOOL_SEARCH: "false",
    });
    expect(() => readFileSync(join(home, ".aws", "config"))).toThrow();
  });

  it("anthropic: the key holder's address, the placeholder key, the traffic switch, and this run's execution-id header", () => {
    const home = newWorkspaceRoot();
    expect(
      buildRunEnv({
        env: {
          PATH: "/opt/venv/bin:/usr/bin",
          HOME: "/home/bun",
          NODE_PATH: "/opt/node/node_modules",
          CLAUDE_CONFIG_DIR: "/home/bun/.claude",
          CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
          ANTHROPIC_MODEL: "claude-sonnet-5-5",
          ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
          ANTHROPIC_BASE_URL: "http://127.0.0.1:8131",
          ANTHROPIC_API_KEY: "agent-runtime-anthropic-proxy",
          CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
          WORKSPACE_ROOT: "/workspaces",
          AGENT_RUNTIME_MODEL_PROVIDER: "anthropic",
          AWS_ACCESS_KEY_ID: "AKIA",
        },
        privateHome: home,
        executionId: "run-8",
      }),
    ).toEqual({
      PATH: "/opt/venv/bin:/usr/bin",
      HOME: home,
      NODE_PATH: "/opt/node/node_modules",
      CLAUDE_CONFIG_DIR: join(home, ".claude"),
      CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
      ANTHROPIC_MODEL: "claude-sonnet-5-5",
      ANTHROPIC_DEFAULT_HAIKU_MODEL: "claude-haiku-4-5",
      ANTHROPIC_BASE_URL: "http://127.0.0.1:8131",
      ANTHROPIC_API_KEY: "agent-runtime-anthropic-proxy",
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
      CLAUDE_CODE_MAX_OUTPUT_TOKENS: "64000",
      ENABLE_TOOL_SEARCH: "false",
      ANTHROPIC_CUSTOM_HEADERS: `${EXECUTION_ID_HEADER}: run-8`,
    });
  });

  it("refuses an unknown provider value rather than falling back to Bedrock", () => {
    expect(() =>
      buildRunEnv({
        env: { ...FOUNDRY_CHILD_ENV, AGENT_RUNTIME_MODEL_PROVIDER: "azure" },
        privateHome: newWorkspaceRoot(),
        executionId: "e-1",
      }),
    ).toThrow(/AGENT_RUNTIME_MODEL_PROVIDER/);
  });
});

describe("the trace envelope names the provider that served the run", () => {
  it("defaults to bedrock and follows the child's provider setting otherwise", () => {
    expect(buildTraceEnvelope({ prompt: "p", env: {} }).modelProvider).toBe("bedrock");
    expect(
      buildTraceEnvelope({ prompt: "p", env: { AGENT_RUNTIME_MODEL_PROVIDER: "foundry" } }).modelProvider,
    ).toBe("foundry");
    expect(
      buildTraceEnvelope({ prompt: "p", env: { AGENT_RUNTIME_MODEL_PROVIDER: "anthropic" } }).modelProvider,
    ).toBe("anthropic");
  });
});

describe("the trace envelope records the effort the caller hands the SDK", () => {
  const SONNET_5_5_ENV = { AGENT_RUNTIME_MODEL_PROVIDER: "anthropic", ANTHROPIC_MODEL: "claude-sonnet-5-5" };

  it("records the CLI's own default for a caller that hands the SDK no effort", () => {
    expect(buildTraceEnvelope({ prompt: "p", env: SONNET_5_5_ENV }).reasoning).toMatchObject({
      effort: "medium",
      effortSource: "sdk_default",
    });
  });

  it("records the runtime's default as the runtime's when the caller hands it on", () => {
    expect(
      buildTraceEnvelope({
        prompt: "p",
        env: SONNET_5_5_ENV,
        effort: { effort: "high", source: "runtime_default" },
      }).reasoning,
    ).toMatchObject({ effort: "high", effortSource: "runtime_default" });
  });
});

describe("the Bedrock run env, pinned byte for byte per identity mode", () => {
  // A literal object per mode, not assertions on a few keys: the provider seam wraps this path
  // and must leave every byte of it alone.
  const CHILD_ENV = {
    PATH: "/opt/venv/bin:/usr/bin",
    HOME: "/home/bun",
    NODE_PATH: "/opt/node/node_modules",
    CLAUDE_CONFIG_DIR: "/home/bun/.claude",
    CLAUDE_CODE_USE_BEDROCK: "1",
    CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
    ANTHROPIC_MODEL: "us.anthropic.claude-sonnet-4-6",
    ANTHROPIC_DEFAULT_HAIKU_MODEL: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
    ENABLE_PROMPT_CACHING_1H: "1",
    AWS_REGION: "us-west-2",
    AWS_DEFAULT_REGION: "us-west-2",
    AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338",
    AWS_ACCESS_KEY_ID: "AKIA",
    AWS_SECRET_ACCESS_KEY: "sk",
    AWS_SESSION_TOKEN: "st",
    AWS_CONTAINER_CREDENTIALS_RELATIVE_URI: "/creds",
    AWS_CONTAINER_CREDENTIALS_FULL_URI: "http://169.254.170.2/creds",
    WORKSPACE_ROOT: "/workspaces",
    AGENT_RUNTIME_VERSION: "0.3.1",
    AGENT_RUNTIME_REVISION: "abc",
  };
  const privateHome = "/workspaces/run-home";
  const common = {
    PATH: "/opt/venv/bin:/usr/bin",
    HOME: privateHome,
    NODE_PATH: "/opt/node/node_modules",
    CLAUDE_CONFIG_DIR: `${privateHome}/.claude`,
    CLAUDE_CODE_USE_BEDROCK: "1",
    CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
    ANTHROPIC_MODEL: "us.anthropic.claude-opus-4-8",
    ANTHROPIC_DEFAULT_HAIKU_MODEL: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
    ENABLE_PROMPT_CACHING_1H: "1",
    AWS_REGION: "us-west-2",
    AWS_DEFAULT_REGION: "us-west-2",
    AWS_EC2_METADATA_SERVICE_ENDPOINT: "http://imds-mock:1338",
    CLAUDE_CODE_MAX_OUTPUT_TOKENS: "64000",
    ENABLE_TOOL_SEARCH: "false",
  };

  it("ambient mode: the allowlist verbatim, static credentials included", () => {
    expect(
      buildRunEnv({ env: CHILD_ENV, privateHome, executionId: "e-1", model: "us.anthropic.claude-opus-4-8" }),
    ).toEqual({
      ...common,
      AWS_ACCESS_KEY_ID: "AKIA",
      AWS_SECRET_ACCESS_KEY: "sk",
      AWS_SESSION_TOKEN: "st",
      AWS_CONTAINER_CREDENTIALS_RELATIVE_URI: "/creds",
      AWS_CONTAINER_CREDENTIALS_FULL_URI: "http://169.254.170.2/creds",
    });
  });

  it("metadata mode: the allowlist minus every static credential, endpoint kept", () => {
    expect(
      buildRunEnv({
        env: { ...CHILD_ENV, AGENT_RUNTIME_METADATA_CREDENTIALS: "true" },
        privateHome,
        executionId: "e-1",
        model: "us.anthropic.claude-opus-4-8",
      }),
    ).toEqual(common);
  });

  it("role mode: the metadata shape plus the per-run profile pointer", () => {
    const home = newWorkspaceRoot();
    expect(
      buildRunEnv({
        env: { ...CHILD_ENV, AGENT_RUNTIME_ROLE_ARN: "arn:aws:iam::1:role/agent" },
        privateHome: home,
        executionId: "e-1",
        model: "us.anthropic.claude-opus-4-8",
      }),
    ).toEqual({
      ...common,
      HOME: home,
      CLAUDE_CONFIG_DIR: join(home, ".claude"),
      AWS_CONFIG_FILE: join(home, ".aws", "config"),
      AWS_PROFILE: "agent-runtime",
    });
  });
});

describe("token usage over the child wire", () => {
  const USAGE = {
    inputTokens: 5_000,
    outputTokens: 800,
    cacheCreationInputTokens: 100,
    cacheReadInputTokens: 9_000,
  };

  it("carries the error's own usage on the failure envelope when the run report knew it", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: () =>
        Promise.reject(
          new RunFailedError("budget gone", {
            subtype: "budget_exhausted",
            estimatedCostUsd: 2.5,
            sessionId: "s-1",
            usage: USAGE,
          }),
        ),
    });
    if (response.ok) {
      throw new Error("expected a failure envelope");
    }
    expect(response.error.usage).toEqual(USAGE);
  });

  it("falls back to the last streamed usage when the failure lost its own", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options = {}) => {
        options.onCostUpdate?.(0.4, USAGE);
        return Promise.reject(new Error("stream died"));
      },
    });
    if (response.ok) {
      throw new Error("expected a failure envelope");
    }
    expect(response.error.usage).toEqual(USAGE);
  });
});

describe("runChild terminal session bundle", () => {
  function sessionWritingReportFn(calls: SeenCall[]): RunReportFn {
    return recordingReportFn(calls, (_params, options) => {
      const configDir = options.subprocessEnv?.["CLAUDE_CONFIG_DIR"] ?? "";
      mkdirSync(join(configDir, "projects"), { recursive: true });
      writeFileSync(join(configDir, "projects", "session.jsonl"), '{"type":"user"}\n');
      const workspacePath = options.workspacePath ?? "";
      writeFileSync(join(workspacePath, "notes.md"), "# working notes");
      mkdirSync(join(workspacePath, "out"), { recursive: true });
      writeFileSync(join(workspacePath, "out", "report.md"), "# final");
      mkdirSync(join(workspacePath, "docs"), { recursive: true });
      writeFileSync(join(workspacePath, "docs", "input.txt"), "immutable input");
      return Promise.resolve(RESULT);
    });
  }

  async function runChildWith(input: {
    workspaceRoot: string;
    runReportFn: RunReportFn;
    requestLine?: string;
    env?: Record<string, string | undefined>;
  }): Promise<{ response: WireResponse; frames: WireChildFrame[] }> {
    const lines = createAsyncQueue<string>();
    lines.push(input.requestLine ?? startRequestLine());
    const frames: WireChildFrame[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => frames.push(frame),
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: input.workspaceRoot, ...input.env },
      runReportFn: input.runReportFn,
    });
    return { response, frames };
  }

  function terminalFrameOf(
    frames: WireChildFrame[],
  ): Extract<WireChildFrame, { kind: "terminal_checkpoint" }> {
    const terminal = frames.find((frame) => frame.kind === "terminal_checkpoint");
    if (terminal === undefined || terminal.kind !== "terminal_checkpoint") {
      throw new Error("no terminal_checkpoint frame was emitted");
    }
    return terminal;
  }

  function manifestOfFrame(
    terminal: Extract<WireChildFrame, { kind: "terminal_checkpoint" }>,
    workspaceRoot: string,
  ): ReturnType<typeof decodeCheckpointManifest> {
    return decodeCheckpointManifest(fromWireCheckpoint(terminal.checkpoint).bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
  }

  async function finishedRun(workspaceRoot: string): Promise<{
    terminal: Extract<WireChildFrame, { kind: "terminal_checkpoint" }>;
    workspacePath: string;
  }> {
    const calls: SeenCall[] = [];
    const { response, frames } = await runChildWith({
      workspaceRoot,
      runReportFn: sessionWritingReportFn(calls),
    });
    expect(response.ok).toBe(true);
    return { terminal: terminalFrameOf(frames), workspacePath: calls[0]?.options.workspacePath ?? "" };
  }

  it("emits a terminal_checkpoint frame for a finished run, excluding docs/ and out/", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { terminal, workspacePath } = await finishedRun(workspaceRoot);
    expect(terminal.sessionId).toBe(RESULT.sessionId);
    const manifest = manifestOfFrame(terminal, workspaceRoot);
    expect(manifest.sessionId).toBe(RESULT.sessionId);
    expect(manifest.workspacePath).toBe(workspacePath);
    const paths = Object.keys(manifest.files);
    expect(paths).toContain("workspace/notes.md");
    expect(paths).toContain("config/projects/session.jsonl");
    expect(paths.some((path) => path.startsWith("workspace/docs/"))).toBe(false);
    expect(paths.some((path) => path.startsWith("workspace/out/"))).toBe(false);
  });

  const STAMP_ENV = {
    AGENT_RUNTIME_VERSION: "9.9.9",
    AGENT_RUNTIME_REVISION: "deadbee",
    ANTHROPIC_MODEL: "the-pinned-model",
  };

  it("stamps the bundle with the sidecar build on its env and the model it declared", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { frames } = await runChildWith({
      workspaceRoot,
      env: STAMP_ENV,
      requestLine: startRequestLine({ model: "a-declared-model" }),
      runReportFn: sessionWritingReportFn([]),
    });
    const manifest = manifestOfFrame(terminalFrameOf(frames), workspaceRoot);
    expect(manifest.version?.sidecar).toEqual({ version: "9.9.9", revision: "deadbee" });
    expect(manifest.version?.model).toBe("a-declared-model");
  });

  it("stamps the pinned model when the run declared none, which is the model that governed it", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { frames } = await runChildWith({
      workspaceRoot,
      env: STAMP_ENV,
      runReportFn: sessionWritingReportFn([]),
    });
    expect(manifestOfFrame(terminalFrameOf(frames), workspaceRoot).version?.model).toBe("the-pinned-model");
  });

  it("leaves the bundle unstamped when its env names no sidecar build, rather than inventing one", async () => {
    const workspaceRoot = newWorkspaceRoot();
    // The model is known and only the build is missing, so a stamp here could only be a
    // half-stamp — which is the thing that must not happen.
    const { frames } = await runChildWith({
      workspaceRoot,
      env: { ANTHROPIC_MODEL: "the-pinned-model" },
      runReportFn: sessionWritingReportFn([]),
    });
    const manifest = manifestOfFrame(terminalFrameOf(frames), workspaceRoot);
    expect(manifest.version).toBeUndefined();
  });

  it("resumes from a finished run's bundle at the identical session and workspace paths", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { terminal, workspacePath } = await finishedRun(workspaceRoot);
    expect(existsSync(workspacePath)).toBe(false);
    const bundleBytes = fromWireCheckpoint(terminal.checkpoint).bytes;
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bundleBytes));
    const calls: SeenCall[] = [];
    const resumed = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bundleBytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        expect(readFileSync(join(options.workspacePath ?? "", "notes.md"), "utf8")).toBe("# working notes");
        const configDir = options.subprocessEnv?.["CLAUDE_CONFIG_DIR"] ?? "";
        expect(readFileSync(join(configDir, "projects", "session.jsonl"), "utf8")).toBe('{"type":"user"}\n');
        return Promise.resolve(RESULT);
      }),
    });
    expect(resumed.ok).toBe(true);
    expect(calls[0]?.options.workspacePath).toBe(workspacePath);
    expect(calls[0]?.options.resumeFrom).toEqual({ sessionId: RESULT.sessionId });
  });

  it("still succeeds, without a bundle, when the session dir cannot be serialized", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const frames: WireChildFrame[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => frames.push(frame),
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(frames.some((frame) => frame.kind === "terminal_checkpoint")).toBe(false);
  });

  function failingAfterSessionReportFn(failure: RunFailedError): RunReportFn {
    return recordingReportFn([], (_params, options) => {
      const configDir = options.subprocessEnv?.["CLAUDE_CONFIG_DIR"] ?? "";
      mkdirSync(join(configDir, "projects"), { recursive: true });
      writeFileSync(join(configDir, "projects", "session.jsonl"), '{"type":"user"}\n');
      writeFileSync(join(options.workspacePath ?? "", "notes.md"), "# working notes");
      return Promise.reject(failure);
    });
  }

  it("publishes the terminal bundle of a run that died at structured output", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { response, frames } = await runChildWith({
      workspaceRoot,
      runReportFn: failingAfterSessionReportFn(
        new RunFailedError("success result carried no structured output despite outputFormat", {
          subtype: "malformed_result",
          estimatedCostUsd: 0.2,
          sessionId: "s-1",
        }),
      ),
    });
    expect(response.ok).toBe(false);
    const terminal = terminalFrameOf(frames);
    expect(terminal.sessionId).toBe("s-1");
    const manifest = manifestOfFrame(terminal, workspaceRoot);
    expect(manifest.sessionId).toBe("s-1");
    expect(Object.keys(manifest.files)).toContain("config/projects/session.jsonl");
  });

  it("publishes the terminal bundle of a run killed on its deadline", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { response, frames } = await runChildWith({
      workspaceRoot,
      runReportFn: failingAfterSessionReportFn(
        new RunFailedError("run deadline exceeded after 900ms", {
          subtype: "deadline_exceeded",
          estimatedCostUsd: 0.4,
          sessionId: "s-1",
        }),
      ),
    });
    expect(response.ok).toBe(false);
    if (!response.ok) {
      expect(response.error.subtype).toBe("deadline_exceeded");
      expect(response.error.sessionId).toBe("s-1");
    }
    const terminal = terminalFrameOf(frames);
    expect(terminal.sessionId).toBe("s-1");
    const manifest = manifestOfFrame(terminal, workspaceRoot);
    expect(manifest.sessionId).toBe("s-1");
    expect(Object.keys(manifest.files)).toContain("config/projects/session.jsonl");
  });

  it("publishes no terminal bundle for a failure that never reached the model", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { response, frames } = await runChildWith({
      workspaceRoot,
      runReportFn: failingAfterSessionReportFn(
        new RunFailedError("the CLI drifted from its pin", {
          subtype: "cli_version_drift",
          estimatedCostUsd: 0,
          sessionId: null,
        }),
      ),
    });
    expect(response.ok).toBe(false);
    expect(frames.some((frame) => frame.kind === "terminal_checkpoint")).toBe(false);
  });

  it("keeps a failed run's own outcome when the bundle cannot be serialized", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const failure = new RunFailedError("model refused", {
      subtype: "run_failed",
      estimatedCostUsd: 0.2,
      sessionId: "s-1",
    });
    // The workspace is gone by the time the bundle is built, so serialization throws.
    const { response, frames } = await runChildWith({
      workspaceRoot,
      runReportFn: recordingReportFn([], (_params, options) => {
        rmSync(options.workspacePath ?? "", { recursive: true, force: true });
        return Promise.reject(failure);
      }),
    });
    if (response.ok) {
      throw new Error("expected a failure envelope");
    }
    expect(response.error.subtype).toBe("run_failed");
    expect(frames.some((frame) => frame.kind === "terminal_checkpoint")).toBe(false);
  });

  it("keeps a successful run successful when the bundle cannot be serialized", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { response, frames } = await runChildWith({
      workspaceRoot,
      runReportFn: recordingReportFn([], (_params, options) => {
        rmSync(join(options.subprocessEnv?.["CLAUDE_CONFIG_DIR"] ?? ""), {
          recursive: true,
          force: true,
        });
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(frames.some((frame) => frame.kind === "terminal_checkpoint")).toBe(false);
  });

  it("emits no terminal bundle when publishing the deliverables fails", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const frames: WireChildFrame[] = [];
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => frames.push(frame),
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: sessionWritingReportFn(calls),
      publishArtifactsFn: () => {
        throw new Error("disk full");
      },
    });
    expect(response.ok).toBe(false);
    expect(frames.some((frame) => frame.kind === "terminal_checkpoint")).toBe(false);
  });
});

describe("runChild cargo channel lifecycle", () => {
  const DOC_BYTES = Buffer.from("annual report body");

  function docMeta(): Record<string, unknown> {
    return { name: "report.pdf", sha256: sha256Hex(DOC_BYTES), sizeBytes: DOC_BYTES.byteLength };
  }

  it("closes the cargo fd after staging, before the run report starts", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ documents: [docMeta()] }));
    const { cargo, closes } = trackingCargo([DOC_BYTES]);
    const closesSeenAtRunTime: number[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo,
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, _options) => {
        closesSeenAtRunTime.push(closes());
        return Promise.resolve(RESULT);
      },
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(closesSeenAtRunTime).toEqual([1]);
  });

  it("closes the cargo fd before the run starts even when the run carries no inputs", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const { cargo, closes } = trackingCargo([]);
    const closesSeenAtRunTime: number[] = [];
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo,
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: () => {
        closesSeenAtRunTime.push(closes());
        return Promise.resolve(RESULT);
      },
    });
    expect(closesSeenAtRunTime).toEqual([1]);
  });

  it("closes the cargo fd when staging itself fails", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(
      startRequestLine({
        documents: [{ name: "report.pdf", sha256: "0".repeat(64), sizeBytes: DOC_BYTES.byteLength }],
      }),
    );
    const { cargo, closes } = trackingCargo([DOC_BYTES]);
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo,
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
    });
    expect(response.ok).toBe(false);
    expect(closes()).toBe(1);
  });
});

describe("runChild continuation", () => {
  const GOOD_STAMP = {
    bundleFormat: CHECKPOINT_BUNDLE_FORMAT,
    sidecar: { version: "0.3.900", revision: "abcdef1" },
    sdk: "2.1.235",
    model: "us.anthropic.claude-sonnet-4-6",
  };

  const OUT_DRAFT = Buffer.from("# draft so far");
  const OUT_MODEL = Buffer.from("print('check')");

  function continuationLine(
    checkpointBytes: Uint8Array,
    outFiles: { name: string; sha256: string; sizeBytes: number }[],
  ): string {
    return resumeRequestLine(checkpointBytes, true, { priorCostUsd: 0, continuation: { outFiles } });
  }

  function outMetaOf(bytes: Buffer, name: string): { name: string; sha256: string; sizeBytes: number } {
    return { name, sha256: sha256Hex(bytes), sizeBytes: bytes.byteLength };
  }

  // Unlike the pause arm, whose raw error stays run_child_error, a corrupt delivery of a
  // platform-held bundle must stay wire-distinct so the platform can degrade the leg pre-spend.
  it("maps a corrupt cargo delivery of a platform-held bundle to checkpoint_restore_failed at zero cost", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { bytes } = manifestFor(join(workspaceRoot, "run-restored"));
    const lines = createAsyncQueue<string>();
    lines.push(continuationLine(bytes, []));
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: cargoOver([Buffer.alloc(bytes.byteLength)]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(calls).toHaveLength(0);
    if (response.ok) {
      throw new Error("a tampered continuation bundle must not produce a success envelope");
    }
    expect(response.error.subtype).toBe("checkpoint_restore_failed");
    expect(response.error.estimatedCostUsd).toBe(0);
    expect(response.error.message).toContain("sha256");
  });

  it("restores the stamped bundle, stages the carried out/ set nested and manifest-free, and skips the mirror restage", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    // A published mirror for this execution exists and carries a poison file: a restage
    // would surface it, so its absence proves the platform's carried set is authoritative.
    const publishedOut = join(workspaceRoot, "artifacts", "e-1", "attempt-1", "out");
    mkdirSync(publishedOut, { recursive: true });
    writeFileSync(join(publishedOut, "stale-from-mirror.md"), "must not appear");
    const bundleBytes = manifestFor(workspacePath, GOOD_STAMP).bytes;
    const lines = createAsyncQueue<string>();
    lines.push(
      continuationLine(bundleBytes, [
        outMetaOf(OUT_DRAFT, "draft.md"),
        outMetaOf(OUT_MODEL, "checks/model.py"),
      ]),
    );
    const seen: { out?: string[]; draft?: string; nested?: string; restored?: string } = {};
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bundleBytes, [OUT_DRAFT, OUT_MODEL]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls, (_params, options) => {
        const outDir = join(options.workspacePath ?? "", "out");
        seen.out = readdirSync(outDir).sort();
        seen.draft = readFileSync(join(outDir, "draft.md"), "utf8");
        seen.nested = readFileSync(join(outDir, "checks", "model.py"), "utf8");
        seen.restored = readFileSync(join(options.workspacePath ?? "", "draft.md"), "utf8");
        return Promise.resolve(RESULT);
      }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(calls[0]?.options.resumeFrom).toEqual({ sessionId: "s-9" });
    expect(calls[0]?.params.prompt).toBe(ANSWER);
    expect(calls[0]?.options.priorCostUsd).toBe(0);
    expect(seen.out).toEqual(["checks", "draft.md"]);
    expect(seen.draft).toBe("# draft so far");
    expect(seen.nested).toBe("print('check')");
    expect(seen.restored).toBe("# partial report");
  });

  it("refuses an unstamped bundle as checkpoint_version_mismatch at $0, before any model work", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const bundleBytes = manifestFor(join(workspaceRoot, "run-x")).bytes;
    const lines = createAsyncQueue<string>();
    lines.push(continuationLine(bundleBytes, []));
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bundleBytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response.ok).toBe(false);
    if (!response.ok) {
      expect(response.error.subtype).toBe("checkpoint_version_mismatch");
      expect(response.error.estimatedCostUsd).toBe(0);
      expect(response.error.sessionId).toBeNull();
    }
    expect(calls).toHaveLength(0);
  });

  it("refuses a bundle whose format this sidecar does not read as checkpoint_version_mismatch", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const bundleBytes = manifestFor(join(workspaceRoot, "run-x"), { ...GOOD_STAMP, bundleFormat: "0" }).bytes;
    const lines = createAsyncQueue<string>();
    lines.push(continuationLine(bundleBytes, []));
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bundleBytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
    });
    expect(response.ok).toBe(false);
    if (!response.ok) {
      expect(response.error.subtype).toBe("checkpoint_version_mismatch");
      expect(response.error.message).toContain('"0"');
    }
  });

  it("reports an undecodable continuation bundle as checkpoint_restore_failed at $0", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const bundleBytes = new TextEncoder().encode("not json");
    const lines = createAsyncQueue<string>();
    lines.push(continuationLine(bundleBytes, []));
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bundleBytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response.ok).toBe(false);
    if (!response.ok) {
      expect(response.error.subtype).toBe("checkpoint_restore_failed");
      expect(response.error.estimatedCostUsd).toBe(0);
      expect(response.error.sessionId).toBeNull();
    }
    expect(calls).toHaveLength(0);
  });

  it("keeps the pause-flow cold resume free of the stamp requirement", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const { bytes } = manifestFor(join(workspaceRoot, "run-restored"));
    const lines = createAsyncQueue<string>();
    lines.push(resumeRequestLine(bytes));
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bytes),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
  });

  it("fails a carried out/ file whose streamed bytes do not match the digest, before any model work", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const workspacePath = join(workspaceRoot, "run-restored");
    const bundleBytes = manifestFor(workspacePath, GOOD_STAMP).bytes;
    const lines = createAsyncQueue<string>();
    lines.push(
      continuationLine(bundleBytes, [
        { name: "draft.md", sha256: "0".repeat(64), sizeBytes: OUT_DRAFT.byteLength },
      ]),
    );
    const calls: SeenCall[] = [];
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: checkpointCargo(bundleBytes, [OUT_DRAFT]),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn(calls),
    });
    expect(response.ok).toBe(false);
    if (!response.ok) {
      expect(response.error.message).toContain("sha256");
    }
    expect(calls).toHaveLength(0);
  });
});

describe("runChild result delivery", () => {
  function epipe(): Error {
    const error: NodeJS.ErrnoException = new Error("EPIPE: broken pipe, write");
    error.code = "EPIPE";
    return error;
  }

  it("survives a dead cost channel: the run completes, the channel retires, the failure logs once", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const logLines: { line: string; level: string }[] = [];
    let costWrites = 0;
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: (frame) => {
        if (frame.kind === "cost") {
          costWrites += 1;
          throw epipe();
        }
      },
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options = {}) => {
        options.onCostUpdate?.(0.1, ZERO_USAGE);
        options.onCostUpdate?.(0.2, ZERO_USAGE);
        options.onCostUpdate?.(0.3, ZERO_USAGE);
        return Promise.resolve(RESULT);
      },
      logger: createLogger({ sink: (line, level) => logLines.push({ line, level }) }),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(costWrites).toBe(1);
    const dropped = logLines.filter((entry) => {
      const parsed: unknown = JSON.parse(entry.line);
      return (parsed as { event?: string }).event === "cost_update_dropped";
    });
    expect(dropped).toHaveLength(1);
    expect(dropped[0]?.level).toBe("warn");
  });

  it("persists the final response under the artifacts attempt dir before returning it", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
    });
    expect(response).toEqual({ ok: true, result: RESULT });
    expect(
      readPersistedFinalResponse({
        artifactsRoot: join(workspaceRoot, "artifacts"),
        executionId: "e-1",
        attempt: 1,
      }),
    ).toEqual(response);
  });

  it("persists a failure response too, so a lost final frame still names the failure and its spend", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: () =>
        Promise.reject(
          new RunFailedError("agent run failed (errored_result): API Error: Server error mid-response", {
            subtype: "errored_result",
            estimatedCostUsd: 0.44,
            sessionId: "s-2",
          }),
        ),
    });
    expect(response.ok).toBe(false);
    expect(
      readPersistedFinalResponse({
        artifactsRoot: join(workspaceRoot, "artifacts"),
        executionId: "e-1",
        attempt: 1,
      }),
    ).toEqual(response);
  });

  it("persists the response under the attempt the wire request names", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine({ attempt: 2 }));
    const response = await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: recordingReportFn([]),
    });
    const artifactsRoot = join(workspaceRoot, "artifacts");
    expect(readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 2 })).toEqual(response);
    expect(readPersistedFinalResponse({ artifactsRoot, executionId: "e-1", attempt: 1 })).toBeUndefined();
  });
});

describe("deliverFinalFrame", () => {
  const response: WireResponse = { ok: true, result: RESULT };

  function capturing(): { lines: string[]; exits: number[]; logLines: { line: string; level: string }[] } {
    return { lines: [], exits: [], logLines: [] };
  }

  it("writes the final frame line and exits 0 once the write has completed", () => {
    const seen = capturing();
    deliverFinalFrame({
      response,
      write: (line, callback) => {
        seen.lines.push(line);
        callback();
      },
      exit: (code) => seen.exits.push(code),
      logger: createLogger({ sink: (line, level) => seen.logLines.push({ line, level }) }),
    });
    expect(seen.lines).toHaveLength(1);
    expect(JSON.parse(seen.lines[0] ?? "")).toEqual({ kind: "final", response });
    expect(seen.lines[0]?.endsWith("\n")).toBe(true);
    expect(seen.exits).toEqual([0]);
    expect(seen.logLines).toEqual([]);
  });

  it("exits with the undelivered code and logs when the write throws", () => {
    const seen = capturing();
    deliverFinalFrame({
      response,
      write: () => {
        throw new Error("EPIPE: broken pipe, write");
      },
      exit: (code) => seen.exits.push(code),
      logger: createLogger({ sink: (line, level) => seen.logLines.push({ line, level }) }),
    });
    expect(seen.exits).toEqual([RESULT_UNDELIVERED_EXIT_CODE]);
    expect(RESULT_UNDELIVERED_EXIT_CODE).not.toBe(0);
    expect(seen.logLines).toHaveLength(1);
    expect(JSON.parse(seen.logLines[0]?.line ?? "{}")).toMatchObject({ event: "final_frame_write_failed" });
  });

  it("exits with the undelivered code and logs when the write completes with an error", () => {
    const seen = capturing();
    deliverFinalFrame({
      response,
      write: (_line, callback) => callback(new Error("EPIPE: broken pipe, write")),
      exit: (code) => seen.exits.push(code),
      logger: createLogger({ sink: (line, level) => seen.logLines.push({ line, level }) }),
    });
    expect(seen.exits).toEqual([RESULT_UNDELIVERED_EXIT_CODE]);
    expect(seen.logLines).toHaveLength(1);
    expect(JSON.parse(seen.logLines[0]?.line ?? "{}")).toMatchObject({ event: "final_frame_write_failed" });
  });
});

describe("sub-agent transcript reader wiring", () => {
  it("hands runReport a reader over the run's private config dir, where the CLI writes sub-agent transcripts", async () => {
    const workspaceRoot = newWorkspaceRoot();
    const lines = createAsyncQueue<string>();
    lines.push(startRequestLine());
    let seenMessageIds: string[] | undefined;
    await runChild({
      lines,
      probeSandboxFn: PROBE_OK,
      cargo: emptyCargo(),
      writeFrame: () => {},
      env: { PATH: "/usr/bin", WORKSPACE_ROOT: workspaceRoot },
      runReportFn: (_params, options = {}) => {
        const configDir = options.subprocessEnv?.["CLAUDE_CONFIG_DIR"] ?? "missing";
        const subagentsDir = join(configDir, "projects", "-workspaces-run-w", "session-w", "subagents");
        mkdirSync(subagentsDir, { recursive: true });
        writeFileSync(
          join(subagentsDir, "agent-w1.jsonl"),
          `${JSON.stringify({ type: "assistant", message: { id: "msg-w", model: "m", usage: { output_tokens: 2 } } })}\n`,
        );
        seenMessageIds = options
          .subagentApiMessages?.("session-w", "w1")
          ?.map((message) => message.messageId);
        return Promise.resolve(RESULT);
      },
    });
    expect(seenMessageIds).toEqual(["msg-w"]);
  });
});
