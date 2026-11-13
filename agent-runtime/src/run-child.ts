import { mkdtempSync, rmSync } from "node:fs";
import { join } from "node:path";
import {
  applyAssumeRoleProfile,
  applyCredentialsFileProfile,
  withoutStaticCredentials,
} from "./aws-profile.js";
import {
  CHECKPOINT_BUNDLE_FORMAT,
  type CheckpointManifest,
  type CheckpointVersionStamp,
  composeCheckpointVersionStamp,
  decodeCheckpointManifest,
  restoreSessionCheckpoint,
  serializeSessionCheckpoint,
} from "./checkpoint-bundle.js";
import { sealCheckpoint } from "./checkpoint-store.js";
import { WORKSPACE_ROOT } from "./config.js";
import { type CargoByteReader, type CargoReceiver, createCargoReceiver } from "./document-channel.js";
import { composeUserTurn } from "./document-statement.js";
import { messageOf, toError } from "./errors.js";
import { persistFinalResponse } from "./final-response-file.js";
import { sha256Hex, sha256HexOfJson } from "./hash.js";
import { createLogger, type Logger, stderrOnlySink } from "./logger.js";
import {
  buildProviderSubprocessEnv,
  EXECUTION_ID_HEADER,
  MODEL_PROVIDER_ENV_VAR,
  parseModelProvider,
} from "./model-provider.js";
import { createNarrower, type Narrower } from "./narrow.js";
import { claimReadMirror, type StagedDocumentDigest } from "./read-gate.js";
import type { ResolvedRunAgent } from "./run-agents.js";
import {
  createDocumentBudget,
  DOCUMENTS_AREA,
  receiveCheckpointBytes,
  type StagedInputSummary,
  stageStreamedInputs,
  summarizeStagedFileMetas,
} from "./run-documents.js";
import {
  discardPublishedArtifacts,
  publishRunArtifacts,
  RetainedCapExceededError,
  restagePublishedOut,
} from "./run-files.js";
import {
  type McpServerBinding,
  type PauseFn,
  RUN_AGENT_TOOL_NAME,
  RUN_TOOLS,
  type RunEffort,
  type RunEffortChoice,
  RunFailedError,
  type RunOutputFormat,
  type RunReportFn,
  type RunReportResult,
  type RunSystemPrompt,
  resolveReasoningConfig,
  runEffortChoice,
  runReport,
  sandboxConfigId,
} from "./run-report.js";
import {
  parseWireAnswerFrame,
  parseWireRequest,
  RESULT_UNDELIVERED_EXIT_CODE,
  toWireCheckpoint,
  type WireChildFrame,
  type WireCostFrame,
  WireError,
  type WireRequest,
  type WireResponse,
} from "./run-wire.js";
import { describeSandboxFailure, type SandboxProbeFn } from "./sandbox-probe.js";
import { SKILL_TOOL_NAME, type SkillPluginBinding, stageSkillPlugin } from "./skill-plugin.js";
import { createSubagentTranscriptReader } from "./subagent-usage.js";
import {
  type CreateRunTraceInput,
  createRunTrace,
  ENVELOPE_ABSENT,
  type RunTrace,
  readInstalledSdkVersion,
  type TraceEnvelopeInput,
} from "./trace.js";
import type { TokenUsage } from "./usage-cost.js";
import { ARTIFACTS_DIR_NAME, RESERVED_WORKSPACE_DIRS, TRACES_DIR_NAME } from "./workspace-layout.js";

const childLog = createLogger({ sink: stderrOnlySink });

export type CreateTraceFn = (input: CreateRunTraceInput) => RunTrace;

export type RunChildDeps = {
  lines: { next(): Promise<string> };
  writeFrame: (frame: WireChildFrame) => void;
  env: Record<string, string | undefined>;
  // Required, never defaulted: the real probe execs bwrap, which refuses any capable-but-not-setuid
  // process, so only the caller knows whether this process may run it (the run child can, the
  // supervisor and the test runner cannot).
  probeSandboxFn: SandboxProbeFn;
  // Required like the probe: the fd-3 side-channel document bytes arrive on. Closing it after
  // staging is a run invariant — the SDK's CLI subprocess would inherit the open pipe otherwise —
  // so a caller must always wire one, documents or not.
  cargo: CargoByteReader;
  runReportFn?: RunReportFn;
  createTraceFn?: CreateTraceFn;
  publishArtifactsFn?: typeof publishRunArtifacts;
  onTerminationRequested?: (cleanup: () => void) => void;
  logger?: Logger;
};

const UNPINNED_MODEL = "unpinned";

function modelPinOf(model: string | undefined, env: Record<string, string | undefined>): string {
  return model ?? env["ANTHROPIC_MODEL"] ?? UNPINNED_MODEL;
}

function runEffortOf(run: PreparedRun, env: Record<string, string | undefined>): RunEffortChoice | undefined {
  return runEffortChoice(
    run.effort,
    modelPinOf(run.model, env),
    parseModelProvider(env[MODEL_PROVIDER_ENV_VAR]),
  );
}

const SKILL_ASSETS_DIR_PREFIX = "skill-assets-";

const wireParse: Narrower = createNarrower((message) => new WireError(message));

// Serialized before hashing so shape is part of identity — a prompt as one string vs. the same
// text as blocks must not share a digest.
function digestOrAbsent(value: unknown): string {
  if (value === undefined) {
    return ENVELOPE_ABSENT;
  }
  return sha256HexOfJson(value);
}

export function buildTraceEnvelope(input: {
  prompt: string;
  env: Record<string, string | undefined>;
  model?: string | undefined;
  effort?: RunEffortChoice | undefined;
  systemPrompt?: RunSystemPrompt | undefined;
  outputFormat?: RunOutputFormat | undefined;
  stagedInputs?: StagedInputSummary[] | undefined;
  skills?: readonly string[] | undefined;
  agents?: readonly string[] | undefined;
  mcpServerUrl?: string | undefined;
}): TraceEnvelopeInput {
  const stagedInputs = input.stagedInputs ?? [];
  const skills = input.skills ?? [];
  const agents = input.agents ?? [];
  const modelPin = modelPinOf(input.model, input.env);
  return {
    modelProvider: parseModelProvider(input.env[MODEL_PROVIDER_ENV_VAR]),
    modelPin,
    promptSha256: sha256Hex(new TextEncoder().encode(input.prompt)),
    systemPromptSha256: digestOrAbsent(input.systemPrompt),
    outputFormatSha256: digestOrAbsent(input.outputFormat),
    stagedInputsSha256: digestOrAbsent(stagedInputs.length === 0 ? undefined : stagedInputs),
    // URL alone — the runToken is a credential, and rotating it changes what a run may do, not what it was asked to do.
    mcpServerSha256: digestOrAbsent(input.mcpServerUrl),
    tools: [
      ...(skills.length === 0 ? RUN_TOOLS : [...RUN_TOOLS, SKILL_TOOL_NAME]),
      ...(agents.length === 0 ? [] : [RUN_AGENT_TOOL_NAME]),
    ],
    skills: [...skills],
    agents: [...agents],
    sandboxConfigId: sandboxConfigId(),
    sdkVersion: readInstalledSdkVersion(),
    reasoning: resolveReasoningConfig(input.effort, modelPin),
  };
}

function openTrace(input: {
  createTraceFn: CreateTraceFn;
  workspaceRoot: string;
  configDir: string;
  env: Record<string, string | undefined>;
  run: PreparedRun;
  effort: RunEffortChoice | undefined;
}): RunTrace | undefined {
  try {
    return input.createTraceFn({
      traceRoot: join(input.workspaceRoot, TRACES_DIR_NAME),
      executionId: input.run.executionId,
      attempt: input.run.attempt,
      configDir: input.configDir,
      envelope: buildTraceEnvelope({
        prompt: input.run.prompt,
        env: input.env,
        model: input.run.model,
        effort: input.effort,
        systemPrompt: input.run.systemPrompt,
        outputFormat: input.run.outputFormat,
        stagedInputs: input.run.stagedInputs,
        skills: input.run.skillPlugin?.skillNames,
        agents: input.run.agents?.map((agent) => agent.name),
        mcpServerUrl: input.run.mcpServer?.url,
      }),
    });
  } catch (error) {
    childLog.error("trace_creation_failed", { error: toError(error) });
    return undefined;
  }
}

function stagedDocumentDigests(stagedInputs: StagedInputSummary[]): StagedDocumentDigest[] {
  return stagedInputs
    .filter((input) => input.area === DOCUMENTS_AREA)
    .map((input) => ({ name: input.name, sha256: input.sha256, sizeBytes: input.sizeBytes }));
}

// Telemetry: a pipe that dies under it degrades the accounting, never the run, and is logged once.
function createCostChannel(
  writeFrame: (frame: WireChildFrame) => void,
  log: Logger,
): (frame: WireCostFrame) => void {
  let dead = false;
  return (frame) => {
    if (dead) {
      return;
    }
    try {
      writeFrame(frame);
    } catch (error) {
      dead = true;
      log.warn("cost_update_dropped", { error: toError(error) });
    }
  };
}

export type FinalFrameWriter = (line: string, callback: (error?: Error | null) => void) => unknown;

// The final frame's write is the one a dead stdout pipe loses, and a callback error is
// as real as a throw: either way the child exits non-zero so a clean exit never reads as
// a delivered result, and the supervisor turns to the persisted response instead.
export function deliverFinalFrame(input: {
  response: WireResponse;
  write: FinalFrameWriter;
  exit: (code: number) => void;
  logger?: Logger;
}): void {
  const log = input.logger ?? childLog;
  const line = `${JSON.stringify({ kind: "final", response: input.response } satisfies WireChildFrame)}\n`;
  const undelivered = (error: unknown): void => {
    log.error("final_frame_write_failed", { error: toError(error) });
    input.exit(RESULT_UNDELIVERED_EXIT_CODE);
  };
  try {
    input.write(line, (error) => {
      if (error === undefined || error === null) {
        input.exit(0);
      } else {
        undelivered(error);
      }
    });
  } catch (error) {
    undelivered(error);
  }
}

function toFailure(error: unknown, lastKnownUsage: TokenUsage | undefined): WireResponse {
  if (error instanceof RunFailedError) {
    const failure = error.failure;
    if (failure.usage === undefined && lastKnownUsage !== undefined) {
      return { ok: false, error: { ...failure, usage: lastKnownUsage } };
    }
    return { ok: false, error: failure };
  }
  return {
    ok: false,
    error: {
      message: messageOf(error),
      subtype: "run_child_error",
      estimatedCostUsd: 0,
      sessionId: null,
      ...(lastKnownUsage === undefined ? {} : { usage: lastKnownUsage }),
    },
  };
}

export function buildRunEnv(input: {
  env: Record<string, string | undefined>;
  privateHome: string;
  executionId: string;
  model?: string | undefined;
}): Record<string, string> {
  const provider = parseModelProvider(input.env[MODEL_PROVIDER_ENV_VAR]);
  const subprocessEnv = buildProviderSubprocessEnv(provider, {
    ...input.env,
    ...(input.model === undefined ? {} : { ANTHROPIC_MODEL: input.model }),
    HOME: input.privateHome,
    CLAUDE_CONFIG_DIR: join(input.privateHome, ".claude"),
  });
  switch (provider) {
    case "foundry":
      return subprocessEnv;
    case "anthropic":
      // The shim and the key holder bind their log lines to the run through this header; the CLI
      // attaches ANTHROPIC_CUSTOM_HEADERS to every Messages request.
      return { ...subprocessEnv, ANTHROPIC_CUSTOM_HEADERS: `${EXECUTION_ID_HEADER}: ${input.executionId}` };
    case "bedrock":
      return bedrockRunEnv(input, subprocessEnv);
  }
}

function bedrockRunEnv(
  input: { env: Record<string, string | undefined>; privateHome: string; executionId: string },
  subprocessEnv: Record<string, string>,
): Record<string, string> {
  const roleArn = input.env["AGENT_RUNTIME_ROLE_ARN"];
  if (roleArn !== undefined && roleArn !== "") {
    return applyAssumeRoleProfile({
      env: subprocessEnv,
      homeDir: input.privateHome,
      roleArn,
      executionId: input.executionId,
    });
  }
  const credentialsFile = input.env["AGENT_RUNTIME_CREDENTIALS_FILE"];
  if (credentialsFile !== undefined && credentialsFile !== "") {
    return applyCredentialsFileProfile({ env: subprocessEnv, homeDir: input.privateHome, credentialsFile });
  }
  if (input.env["AGENT_RUNTIME_METADATA_CREDENTIALS"] === "true") {
    return withoutStaticCredentials(subprocessEnv);
  }
  return subprocessEnv;
}

type PreparedRun = {
  executionId: string;
  attempt: number;
  workspacePath: string;
  prompt: string;
  deadlineMs: number;
  maxBudgetUsd: number;
  maxTurns: number;
  maxRetainedOutputBytes: number;
  effort: RunEffort | undefined;
  priorCostUsd: number;
  priorUsage: TokenUsage | undefined;
  allowFurtherPause: boolean;
  stagedInputs: StagedInputSummary[];
  skillPlugin?: SkillPluginBinding;
  agents?: ResolvedRunAgent[];
  resumeFrom?: { sessionId: string };
  model?: string;
  outputFormat?: RunOutputFormat;
  systemPrompt?: RunSystemPrompt;
  mcpServer?: McpServerBinding;
};

// Only the bundle format is checked here: whether the stamped sidecar, SDK or model is
// acceptable is the platform's call before dispatch.
function continuationStampFailure(manifest: CheckpointManifest): RunFailedError | undefined {
  const version = manifest.version;
  if (version === undefined) {
    return new RunFailedError("continuation bundle carries no version stamp", {
      subtype: "checkpoint_version_mismatch",
      estimatedCostUsd: 0,
      sessionId: null,
    });
  }
  if (version.bundleFormat !== CHECKPOINT_BUNDLE_FORMAT) {
    return new RunFailedError(
      `continuation bundle format "${version.bundleFormat}" is not this sidecar's format ` +
        `"${CHECKPOINT_BUNDLE_FORMAT}"`,
      {
        subtype: "checkpoint_version_mismatch",
        estimatedCostUsd: 0,
        sessionId: null,
      },
    );
  }
  return undefined;
}

// A pause bundle is this sidecar's own recent write: a failure here is the child's raw
// error, not a wire-distinct subtype.
function restorePauseManifest(
  checkpointBytes: Uint8Array,
  input: { workspaceRoot: string; configDir: string },
): CheckpointManifest {
  const manifest = decodeCheckpointManifest(checkpointBytes, {
    workspaceRoot: input.workspaceRoot,
    reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
  });
  restoreSessionCheckpoint(manifest, {
    configDir: input.configDir,
    reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
  });
  return manifest;
}

// Restore runs inside prepare, before the SDK call, so these failures cost nothing, which is
// what lets the platform degrade them to a scratch leg in the same round.
function continuationRestoreFailure(action: string, error: unknown): RunFailedError {
  return new RunFailedError(`continuation bundle could not be ${action}: ${messageOf(error)}`, {
    subtype: "checkpoint_restore_failed",
    estimatedCostUsd: 0,
    sessionId: null,
  });
}

function restoreContinuationManifest(
  checkpointBytes: Uint8Array,
  input: { workspaceRoot: string; configDir: string },
): CheckpointManifest {
  let manifest: CheckpointManifest;
  try {
    manifest = decodeCheckpointManifest(checkpointBytes, {
      workspaceRoot: input.workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
  } catch (error) {
    throw continuationRestoreFailure("decoded", error);
  }
  const stampFailure = continuationStampFailure(manifest);
  if (stampFailure !== undefined) {
    throw stampFailure;
  }
  try {
    restoreSessionCheckpoint(manifest, {
      configDir: input.configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
  } catch (error) {
    throw continuationRestoreFailure("restored", error);
  }
  return manifest;
}

async function prepareWorkspace(
  request: WireRequest,
  input: {
    workspaceRoot: string;
    privateHome: string;
    configDir: string;
    cleanupPaths: string[];
    receiver: CargoReceiver;
  },
): Promise<PreparedRun> {
  const budget = createDocumentBudget(request.documentBudgetBytes);
  const documents = request.documents ?? [];
  const skillFiles = request.skillFiles ?? [];
  const recarriedOut = (request.kind === "resume" ? request.continuation?.outFiles : undefined) ?? [];
  let workspacePath: string;
  let prompt: string;
  let resumeFrom: { sessionId: string } | undefined;
  let priorCostUsd = 0;
  let priorUsage: TokenUsage | undefined;
  let allowFurtherPause = true;
  if (request.kind === "start") {
    workspacePath = mkdtempSync(join(input.workspaceRoot, "run-"));
    prompt = request.prompt;
    resumeFrom = undefined;
  } else {
    // The bundle leads the cargo order: the frame carries its descriptor and the bytes are
    // the first entry on the channel, because restore must run before anything stages —
    // its claim on the workspace dir is EEXIST-fatal if docs/skills/out are staged already.
    let checkpointBytes: Buffer;
    try {
      checkpointBytes = await receiveCheckpointBytes(input.receiver, request.checkpoint, wireParse);
    } catch (error) {
      if (request.continuation === undefined) {
        throw error;
      }
      // A short or corrupt delivery of a platform-held bundle must stay wire-distinct so
      // the platform can degrade the leg to scratch pre-spend, same as a decode failure.
      throw continuationRestoreFailure("delivered", error);
    }
    const manifest =
      request.continuation === undefined
        ? restorePauseManifest(checkpointBytes, input)
        : restoreContinuationManifest(checkpointBytes, input);
    workspacePath = manifest.workspacePath;
    prompt = request.answer;
    resumeFrom = { sessionId: manifest.sessionId };
    priorCostUsd = request.priorCostUsd;
    priorUsage = request.priorUsage;
    allowFurtherPause = request.allowFurtherPause;
  }
  input.cleanupPaths.push(workspacePath);
  const stagedSkillFiles = await stageStreamedInputs({
    documents,
    skillFiles,
    recarriedOut,
    receiver: input.receiver,
    parse: wireParse,
    budget,
    workspacePath,
  });
  const skillAssetsRoot = stagedSkillFiles.some((skillFile) => (skillFile.assets?.length ?? 0) > 0)
    ? mkdtempSync(join(input.workspaceRoot, SKILL_ASSETS_DIR_PREFIX))
    : undefined;
  if (skillAssetsRoot !== undefined) {
    input.cleanupPaths.push(skillAssetsRoot);
  }
  const skillPlugin = stageSkillPlugin(input.privateHome, stagedSkillFiles, skillAssetsRoot);
  const stagedSkillNames = skillPlugin?.skillNames ?? [];
  const agents = request.agents?.map((agent) => {
    for (const skill of agent.skills) {
      if (!stagedSkillNames.includes(skill)) {
        throw new WireError(
          `agent "${agent.name}" references skill "${skill}", which this run did not stage`,
        );
      }
    }
    return agent;
  });
  // The platform's re-carried set is authoritative on a continuation: restage is skipped,
  // so nothing appears in out/ that the platform did not send.
  if (request.kind === "resume" && request.continuation === undefined) {
    restagePublishedOut({
      artifactsRoot: join(input.workspaceRoot, ARTIFACTS_DIR_NAME),
      executionId: request.executionId,
      attempt: request.attempt,
      workspacePath,
    });
  }
  return {
    executionId: request.executionId,
    attempt: request.attempt,
    workspacePath,
    prompt,
    stagedInputs: summarizeStagedFileMetas(documents, skillFiles, recarriedOut),
    deadlineMs: request.deadlineMs,
    maxBudgetUsd: request.maxBudgetUsd,
    maxTurns: request.maxTurns,
    maxRetainedOutputBytes: request.maxRetainedOutputBytes,
    effort: request.effort,
    priorCostUsd,
    priorUsage,
    allowFurtherPause,
    ...(skillPlugin === undefined ? {} : { skillPlugin }),
    ...(agents === undefined ? {} : { agents }),
    ...(resumeFrom === undefined ? {} : { resumeFrom }),
    ...(request.model === undefined ? {} : { model: request.model }),
    ...(request.outputFormat === undefined ? {} : { outputFormat: request.outputFormat }),
    ...(request.systemPrompt === undefined ? {} : { systemPrompt: request.systemPrompt }),
    ...(request.mcpServerUrl === undefined
      ? {}
      : {
          mcpServer: {
            url: request.mcpServerUrl,
            ...(request.runToken === undefined ? {} : { runToken: request.runToken }),
          },
        }),
  };
}

// Best-effort by design: the deliverables are the run's value and a resume from the finished
// state is an optional enabler, so a bundle that cannot be produced is logged, never a failure.
function emitTerminalCheckpoint(input: {
  writeFrame: (frame: WireChildFrame) => void;
  sessionId: string;
  workspacePath: string;
  configDir: string;
  version: CheckpointVersionStamp | undefined;
}): void {
  try {
    if (input.sessionId === "") {
      throw new Error("run finished without a session id");
    }
    const bytes = serializeSessionCheckpoint({
      sessionId: input.sessionId,
      workspacePath: input.workspacePath,
      configDir: input.configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      ...(input.version === undefined ? {} : { version: input.version }),
    });
    input.writeFrame({
      kind: "terminal_checkpoint",
      sessionId: input.sessionId,
      checkpoint: toWireCheckpoint(sealCheckpoint(bytes)),
    });
  } catch (error) {
    childLog.error("terminal_checkpoint_failed", { error: toError(error) });
  }
}

// Absent either the sidecar build or the governing model, the bundle goes out unstamped rather
// than half-stamped.
function bundleVersionFor(
  model: string | undefined,
  env: Record<string, string | undefined>,
): CheckpointVersionStamp | undefined {
  const effectiveModel = model ?? env["ANTHROPIC_MODEL"];
  const version = env["AGENT_RUNTIME_VERSION"];
  const revision = env["AGENT_RUNTIME_REVISION"];
  if (effectiveModel === undefined || version === undefined || revision === undefined) {
    return undefined;
  }
  return composeCheckpointVersionStamp({ sidecar: { version, revision }, model: effectiveModel });
}

export async function runChild(deps: RunChildDeps): Promise<WireResponse> {
  const runReportFn = deps.runReportFn ?? runReport;
  const publishArtifactsFn = deps.publishArtifactsFn ?? publishRunArtifacts;
  const log = deps.logger ?? childLog;
  let request: WireRequest | undefined = parseWireRequest(JSON.parse(await deps.lines.next()) as unknown);
  const runIdentity = { executionId: request.executionId, attempt: request.attempt };
  let lastKnownUsage: TokenUsage | undefined = request.kind === "resume" ? request.priorUsage : undefined;
  const workspaceRoot = deps.env["WORKSPACE_ROOT"] ?? WORKSPACE_ROOT;
  const costChannel = createCostChannel(deps.writeFrame, log);
  const privateHome = mkdtempSync(join(workspaceRoot, "home-"));
  const cleanupPaths = [privateHome];
  const cleanup = (): void => {
    for (const path of cleanupPaths) {
      rmSync(path, { recursive: true, force: true });
    }
  };
  // A SIGTERM here means the checkpoint is already saved, so cleanup only needs to clear uid-owned leftovers.
  deps.onTerminationRequested?.(cleanup);

  const configDir = join(privateHome, ".claude");
  let response: WireResponse;
  try {
    const probe = await deps.probeSandboxFn();
    if (!probe.ok) {
      throw new RunFailedError(
        `sandbox unavailable — the pre-run bwrap probe failed: ${describeSandboxFailure(probe.detail)}`,
        { subtype: "sandbox_unavailable", estimatedCostUsd: 0, sessionId: null },
      );
    }
    let prepared: PreparedRun;
    try {
      prepared = await prepareWorkspace(request, {
        workspaceRoot,
        privateHome,
        configDir,
        cleanupPaths,
        receiver: createCargoReceiver(deps.cargo),
      });
    } finally {
      deps.cargo.close();
    }
    request = undefined;
    const { executionId, attempt, workspacePath } = prepared;
    const publishTerminalBundle = (sessionId: string): void =>
      emitTerminalCheckpoint({
        writeFrame: deps.writeFrame,
        sessionId,
        workspacePath,
        configDir,
        version: bundleVersionFor(prepared.model, deps.env),
      });
    const readMirror = claimReadMirror(privateHome, stagedDocumentDigests(prepared.stagedInputs));
    const publishDeliverables = (): void => {
      try {
        publishArtifactsFn({
          artifactsRoot: join(workspaceRoot, ARTIFACTS_DIR_NAME),
          executionId,
          attempt,
          workspacePath,
          declaredCapBytes: prepared.maxRetainedOutputBytes,
        });
      } catch (error) {
        if (error instanceof RetainedCapExceededError) {
          try {
            discardPublishedArtifacts({
              artifactsRoot: join(workspaceRoot, ARTIFACTS_DIR_NAME),
              executionId,
              attempt,
            });
          } catch (forfeitError) {
            childLog.error("publish_forfeit_failed", { error: toError(forfeitError) });
          }
        }
        throw error;
      }
    };

    const runEffort = runEffortOf(prepared, deps.env);
    const trace = openTrace({
      createTraceFn: deps.createTraceFn ?? createRunTrace,
      workspaceRoot,
      configDir,
      env: deps.env,
      run: prepared,
      effort: runEffort,
    });

    const pause: PauseFn = async (input) => {
      // A throw here fails the pause, fail-closed like a checkpoint save.
      try {
        publishDeliverables();
      } catch (publishError) {
        if (publishError instanceof RetainedCapExceededError) {
          throw new RunFailedError(messageOf(publishError), {
            subtype: "retained_budget_exceeded",
            estimatedCostUsd: input.estimatedCostUsd,
            sessionId: input.sessionId,
          });
        }
        throw publishError;
      }
      deps.writeFrame({
        kind: "paused",
        question: input.question,
        sessionId: input.sessionId,
        estimatedCostUsd: input.estimatedCostUsd,
        checkpoint: toWireCheckpoint(input.checkpoint),
      });
      const frame = parseWireAnswerFrame(JSON.parse(await deps.lines.next()) as unknown);
      return { answer: frame.answer, allowFurtherPause: frame.allowFurtherPause };
    };

    const sessionPrompt =
      prepared.resumeFrom === undefined
        ? composeUserTurn(prepared.prompt, workspacePath, prepared.stagedInputs)
        : prepared.prompt;
    let result: RunReportResult;
    try {
      result = await runReportFn(
        { executionId, prompt: sessionPrompt },
        {
          deadlineMs: prepared.deadlineMs,
          maxBudgetUsd: prepared.maxBudgetUsd,
          maxTurns: prepared.maxTurns,
          ...(runEffort === undefined ? {} : { effort: runEffort.effort }),
          subprocessEnv: buildRunEnv({ env: deps.env, privateHome, executionId, model: prepared.model }),
          workspaceRoot,
          workspacePath,
          readMirror,
          ...(prepared.skillPlugin === undefined ? {} : { skillPlugin: prepared.skillPlugin }),
          ...(prepared.agents === undefined ? {} : { agents: prepared.agents }),
          pause,
          onCostUpdate: (estimatedCostUsd, usage) => {
            lastKnownUsage = usage;
            costChannel({ kind: "cost", estimatedCostUsd, usage });
          },
          onToolError: (info) => log.warn("tool_call_errored", info),
          subagentApiMessages: createSubagentTranscriptReader(configDir),
          priorCostUsd: prepared.priorCostUsd,
          ...(prepared.priorUsage === undefined ? {} : { priorUsage: prepared.priorUsage }),
          allowFurtherPause: prepared.allowFurtherPause,
          ...(prepared.resumeFrom === undefined ? {} : { resumeFrom: prepared.resumeFrom }),
          ...(prepared.outputFormat === undefined ? {} : { outputFormat: prepared.outputFormat }),
          ...(prepared.systemPrompt === undefined ? {} : { systemPrompt: prepared.systemPrompt }),
          ...(prepared.mcpServer === undefined ? {} : { mcpServer: prepared.mcpServer }),
          ...(trace === undefined ? {} : { trace }),
        },
      );
    } catch (runError) {
      // A publish failure here must not mask the run's own failure below.
      try {
        publishDeliverables();
      } catch (publishError) {
        childLog.error("failed_run_publish_failed", { error: toError(publishError) });
      }
      // A dead run leaves a bundle when it has a session to continue, whatever killed it. A caller
      // abort reports no session: the supervisor SIGKILLs the cancelled child at once, so no one
      // is left to publish.
      if (runError instanceof RunFailedError && runError.sessionId !== null) {
        publishTerminalBundle(runError.sessionId);
      }
      throw runError;
    }
    try {
      publishDeliverables();
    } catch (publishError) {
      if (publishError instanceof RetainedCapExceededError) {
        throw new RunFailedError(messageOf(publishError), {
          subtype: "retained_budget_exceeded",
          estimatedCostUsd: result.estimatedCostUsd,
          sessionId: result.sessionId,
          usage: result.usage,
        });
      }
      throw new RunFailedError(
        `failed to publish a successful run's deliverables: ${messageOf(publishError)}`,
        {
          subtype: "run_child_error",
          estimatedCostUsd: result.estimatedCostUsd,
          sessionId: result.sessionId,
          usage: result.usage,
        },
      );
    }
    publishTerminalBundle(result.sessionId);
    response = { ok: true, result };
  } catch (error) {
    response = toFailure(error, lastKnownUsage);
  } finally {
    cleanup();
  }
  // Persisted before the final frame is attempted, never after: the frame is the write a
  // dead pipe loses, and the supervisor reads this file only when it never arrived.
  try {
    persistFinalResponse({
      artifactsRoot: join(workspaceRoot, ARTIFACTS_DIR_NAME),
      ...runIdentity,
      response,
    });
  } catch (error) {
    log.error("final_response_persist_failed", { error: toError(error) });
  }
  return response;
}
