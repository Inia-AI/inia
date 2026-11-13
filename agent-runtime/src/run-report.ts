import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { join } from "node:path";
import { type EffortLevel, type Options, query } from "@anthropic-ai/claude-agent-sdk";
import { AGENT_TOOL_MATCHERS, createAgentGateHook } from "./agent-gate.js";
import {
  type AssistantErrorObservation,
  type AssistantErrorTap,
  createAssistantErrorTap,
} from "./assistant-error-tap.js";
import { STATIC_CREDENTIAL_VARS } from "./aws-profile.js";
import { serializeSessionCheckpoint } from "./checkpoint-bundle.js";
import { type CheckpointBundle, sealCheckpoint } from "./checkpoint-store.js";
import { EXPECTED_CLAUDE_CODE_VERSION } from "./claude-code-pin.js";
import {
  DEADLINE_ITERATOR_CLOSE_TIMEOUT_MS,
  DEFAULT_MAX_BUDGET_USD,
  DEFAULT_MAX_TURNS,
  DEFAULT_RUN_DEADLINE_MS,
  MAX_OUTPUT_FORMAT_CHARS,
  MAX_PROMPT_CHARS,
  MAX_RUN_TOKEN_CHARS,
  MAX_SYSTEM_PROMPT_CHARS,
  SANDBOX_DENIED_READ_ROOTS,
  WORKSPACE_ROOT,
} from "./config.js";
import { composeSystemPrompt } from "./environment-section.js";
import { messageOf } from "./errors.js";
import { sha256HexOfJson } from "./hash.js";
import { MODEL_PROVIDERS, type ModelProvider } from "./model-provider.js";
import { createNarrower, type Narrower } from "./narrow.js";
import { createReadGateHook, READ_TOOL_NAME, type ReadMirror } from "./read-gate.js";
import type { ResolvedRunAgent } from "./run-agents.js";
import { BASH_TOOL_NAME } from "./run-tool-names.js";
import { EXECUTION_ID_PATTERN } from "./safe-path.js";
import {
  createRestoredSessionCostReader,
  createRestoredSessionModelUsageReader,
  type RestoredSessionCostReader,
  type RestoredSessionModelUsageReader,
} from "./session-cost-state.js";
import { SKILL_ASSETS_ENV_VAR, SKILL_TOOL_NAME, type SkillPluginBinding } from "./skill-plugin.js";
import { createStructuredOutputTap, type StructuredOutputTap } from "./structured-output-salvage.js";
import { createToolErrorWatch, type ToolErrorObserver } from "./tool-error-watch.js";
import type { RunTrace, TraceReasoningConfig } from "./trace.js";
import {
  type CostMeter,
  createCostMeter,
  INIT_MESSAGE_SUBTYPE,
  INIT_MESSAGE_TYPE,
  isStreamEventMessage,
  type ModelUsageTotals,
  NO_MODEL_USAGE,
  type SubagentApiMessagesReader,
  type TokenUsage,
} from "./usage-cost.js";
import { RESERVED_WORKSPACE_DIRS } from "./workspace-layout.js";

export class ValidationError extends Error {}

export const RUNTIME_FAILURE_SUBTYPES = [
  "aborted",
  "budget_exhausted",
  "capacity_rejected",
  "checkpoint_failed",
  "checkpoint_missing",
  "checkpoint_restore_failed",
  "checkpoint_version_mismatch",
  "cli_version_drift",
  "deadline_exceeded",
  "errored_result",
  "internal_error",
  "malformed_pause",
  "malformed_result",
  "mcp_connect_failed",
  "missing_result",
  "pause_rejected",
  "pause_unsupported",
  "retained_budget_exceeded",
  "run_child_error",
  "runtime_shutdown",
  "sandbox_unavailable",
  "session_id_mismatch",
  "skill_register_failed",
  "stream_error",
  "uid_pool_exhausted",
  "upstream_error",
  "zero_turns",
] as const;

// The union stays open — SDK subtypes pass through verbatim; named members are autocomplete only.
export type RunFailureSubtype = (typeof RUNTIME_FAILURE_SUBTYPES)[number] | (string & Record<never, never>);

export type RunFailure = {
  message: string;
  subtype: RunFailureSubtype;
  estimatedCostUsd: number;
  sessionId: string | null;
  usage?: TokenUsage;
};

export class RunFailedError extends Error {
  readonly subtype: RunFailureSubtype;
  readonly estimatedCostUsd: number;
  readonly sessionId: string | null;
  readonly usage: TokenUsage | undefined;

  constructor(message: string, details: Omit<RunFailure, "message">) {
    super(message);
    this.subtype = details.subtype;
    this.estimatedCostUsd = details.estimatedCostUsd;
    this.sessionId = details.sessionId;
    this.usage = details.usage;
  }

  get failure(): RunFailure {
    return {
      message: this.message,
      subtype: this.subtype,
      estimatedCostUsd: this.estimatedCostUsd,
      sessionId: this.sessionId,
      ...(this.usage === undefined ? {} : { usage: this.usage }),
    };
  }

  static from(failure: RunFailure): RunFailedError {
    return new RunFailedError(failure.message, failure);
  }
}

export type RunReportParams = {
  executionId: string;
  prompt: string;
};

export type RunOutputFormat = {
  type: "json_schema";
  schema: Record<string, unknown>;
};

const OUTPUT_FORMAT_TYPE = "json_schema";
const OUTPUT_FORMAT_FIELDS: readonly string[] = ["type", "schema"];

export function parseRunOutputFormat(value: unknown, parse: Narrower, label: string): RunOutputFormat {
  const record = parse.asRecord(value, label);
  parse.rejectUnknownFields(record, OUTPUT_FORMAT_FIELDS, label);
  if (record["type"] !== OUTPUT_FORMAT_TYPE) {
    parse.fail(`${label}.type must be "${OUTPUT_FORMAT_TYPE}"`);
  }
  const schema = parse.asRecord(record["schema"], `${label}.schema`);
  if (JSON.stringify(schema).length > MAX_OUTPUT_FORMAT_CHARS) {
    parse.fail(`${label}.schema must serialize to at most ${MAX_OUTPUT_FORMAT_CHARS} characters`);
  }
  return { type: OUTPUT_FORMAT_TYPE, schema };
}

// The array variant marks the SDK's cache boundary: static prefix vs. dynamic tail.
export type RunSystemPrompt = string | string[];

const SYSTEM_PROMPT_SHAPE = "a non-empty string, or a non-empty array of non-empty string blocks";

function parseSystemPromptBlock(value: unknown, parse: Narrower, path: string): string {
  if (typeof value !== "string" || value === "") {
    parse.fail(`${path} must be a non-empty string`);
  }
  return value;
}

function parseSystemPromptVariant(value: unknown, parse: Narrower, label: string): RunSystemPrompt {
  if (typeof value === "string") {
    return parseSystemPromptBlock(value, parse, label);
  }
  if (!Array.isArray(value)) {
    parse.fail(`${label} must be ${SYSTEM_PROMPT_SHAPE} — the SDK's preset form is not accepted`);
  }
  if (value.length === 0) {
    parse.fail(`${label} must be ${SYSTEM_PROMPT_SHAPE}`);
  }
  return value.map((block, index) => parseSystemPromptBlock(block, parse, `${label}[${index}]`));
}

export function parseRunSystemPrompt(value: unknown, parse: Narrower, label: string): RunSystemPrompt {
  const systemPrompt = parseSystemPromptVariant(value, parse, label);
  if (JSON.stringify(systemPrompt).length > MAX_SYSTEM_PROMPT_CHARS) {
    parse.fail(`${label} must serialize to at most ${MAX_SYSTEM_PROMPT_CHARS} characters`);
  }
  return systemPrompt;
}

export const RUN_EFFORT_LEVELS = [
  "low",
  "medium",
  "high",
  "xhigh",
  "max",
] as const satisfies readonly EffortLevel[];

export type RunEffort = (typeof RUN_EFFORT_LEVELS)[number];

export function parseRunEffort(value: unknown, parse: Narrower, label: string): RunEffort {
  const effort = RUN_EFFORT_LEVELS.find((level) => level === value);
  if (effort === undefined) {
    parse.fail(`${label} must be one of ${RUN_EFFORT_LEVELS.join(", ")}`);
  }
  return effort;
}

export type McpServerBinding = {
  url: string;
  runToken?: string;
};

function urlProtocolOf(value: string): string | undefined {
  try {
    return new URL(value).protocol;
  } catch {
    return undefined;
  }
}

const MCP_URL_PROTOCOLS: readonly string[] = ["http:", "https:"];

const RUN_TOKEN_PATTERN = /^[\x21-\x7E]+$/;

export function parseRunToken(value: unknown, parse: Narrower, label: string): string {
  if (typeof value !== "string" || value === "") {
    parse.fail(`${label} must be a non-empty string when present`);
  }
  if (value.length > MAX_RUN_TOKEN_CHARS || !RUN_TOKEN_PATTERN.test(value)) {
    parse.fail(
      `${label} must be at most ${MAX_RUN_TOKEN_CHARS} printable ASCII characters ` +
        "with no spaces or control characters",
    );
  }
  return value;
}

export function parseMcpServerUrl(value: unknown, parse: Narrower, label: string): string {
  if (typeof value !== "string" || value === "") {
    parse.fail(`${label} must be a non-empty string`);
  }
  const protocol = urlProtocolOf(value);
  if (protocol === undefined || !MCP_URL_PROTOCOLS.includes(protocol)) {
    parse.fail(`${label} must be an absolute http or https URL`);
  }
  return value;
}

export type RunReportResult = {
  executionId: string;
  output: string;
  estimatedCostUsd: number;
  sessionId: string;
  usage: TokenUsage;
  structuredOutput?: unknown;
  tracePath?: string;
  budgetExhausted?: true;
};

export function parseTokenUsage(value: unknown, parse: Narrower, label: string): TokenUsage {
  const record = parse.asRecord(value, label);
  return {
    inputTokens: parse.requireNonNegativeNumber(record, "inputTokens", label),
    outputTokens: parse.requireNonNegativeNumber(record, "outputTokens", label),
    cacheCreationInputTokens: parse.requireNonNegativeNumber(record, "cacheCreationInputTokens", label),
    cacheReadInputTokens: parse.requireNonNegativeNumber(record, "cacheReadInputTokens", label),
  };
}

export function parseRunReportResult(value: unknown, parse: Narrower, label: string): RunReportResult {
  const record = parse.asRecord(value, label);
  const tracePath = parse.optionalString(record, "tracePath", label);
  const budgetExhausted = parse.optionalBoolean(record, "budgetExhausted", label);
  return {
    executionId: parse.requireString(record, "executionId", label),
    output: parse.requirePossiblyEmptyString(record, "output", label),
    estimatedCostUsd: parse.requireNumber(record, "estimatedCostUsd", label),
    sessionId: parse.requirePossiblyEmptyString(record, "sessionId", label),
    usage: parseTokenUsage(record["usage"], parse, `${label}.usage`),
    // Opaque passthrough: already validated against the caller's schema by the SDK, never re-narrowed here.
    ...("structuredOutput" in record ? { structuredOutput: record["structuredOutput"] } : {}),
    ...(tracePath === undefined ? {} : { tracePath }),
    ...(budgetExhausted === true ? { budgetExhausted: true as const } : {}),
  };
}

export type RunStreamMessage = {
  type: string;
  subtype?: string;
  event?: unknown;
  message?: unknown;
  parent_tool_use_id?: string | null;
  tool_use_result?: unknown;
  is_error?: boolean;
  error?: unknown;
  api_error_status?: unknown;
  claude_code_version?: string;
  num_turns?: number;
  result?: string;
  structured_output?: unknown;
  mcp_servers?: { name: string; status: string }[];
  skills?: string[];
  total_cost_usd?: number;
  modelUsage?: unknown;
  session_id?: string;
  errors?: string[];
  terminal_reason?: string;
  deferred_tool_use?: {
    id: string;
    name: string;
    input: Record<string, unknown>;
  };
};

export type QueryInput = {
  prompt: string;
  workspacePath: string;
  maxBudgetUsd: number;
  maxTurns: number;
  effort?: RunEffort;
  resumeSessionId?: string;
};

export type QueryFn = (input: QueryInput) => AsyncIterable<RunStreamMessage>;

export type PauseQuestion = {
  id: string;
  toolName: string;
  prompt: string;
  input: Record<string, unknown>;
};

export type PauseResolution = {
  answer: string;
  allowFurtherPause: boolean;
};

export type PauseRequest = {
  question: PauseQuestion;
  sessionId: string;
  estimatedCostUsd: number;
  checkpoint: CheckpointBundle;
};

export type PauseFn = (input: PauseRequest) => Promise<PauseResolution>;

export type Checkpointer = (input: { sessionId: string; workspacePath: string }) => Promise<Uint8Array>;

export type RunReportOptions = {
  queryFn?: QueryFn;
  deadlineMs?: number;
  maxBudgetUsd?: number;
  maxTurns?: number;
  effort?: RunEffort;
  subprocessEnv?: Record<string, string>;
  workspaceRoot?: string;
  workspacePath?: string;
  readMirror?: ReadMirror;
  skillPlugin?: SkillPluginBinding;
  agents?: ResolvedRunAgent[];
  signal?: AbortSignal;
  pause?: PauseFn;
  checkpointer?: Checkpointer;
  resumeFrom?: { sessionId: string };
  priorCostUsd?: number;
  priorUsage?: TokenUsage;
  allowFurtherPause?: boolean;
  outputFormat?: RunOutputFormat;
  systemPrompt?: RunSystemPrompt;
  mcpServer?: McpServerBinding;
  trace?: RunTrace;
  onCostUpdate?: (estimatedCostUsd: number, usage: TokenUsage) => void;
  onToolError?: ToolErrorObserver;
  subagentApiMessages?: SubagentApiMessagesReader;
  restoredSessionCostUsd?: RestoredSessionCostReader;
  restoredSessionModelUsage?: RestoredSessionModelUsageReader;
};

export type RunReportFn = (params: RunReportParams, options?: RunReportOptions) => Promise<RunReportResult>;

const PARAM_FIELDS: readonly string[] = ["executionId", "prompt"];

const parse: Narrower = createNarrower((message) => new ValidationError(message));

export function parseRunReportParams(body: unknown): RunReportParams {
  const record = parse.asRecord(body, "request body");
  parse.rejectUnknownFields(record, PARAM_FIELDS);
  const executionId = parse.requireString(record, "executionId");
  if (!EXECUTION_ID_PATTERN.test(executionId)) {
    parse.fail(`executionId must match ${EXECUTION_ID_PATTERN.source}`);
  }
  const prompt = parse.requireString(record, "prompt");
  if (prompt.length > MAX_PROMPT_CHARS) {
    parse.fail(`prompt must not exceed ${MAX_PROMPT_CHARS} characters`);
  }
  return { executionId, prompt };
}

function isResultMessage(message: RunStreamMessage): message is RunStreamMessage & { subtype: string } {
  return message.type === "result" && typeof message.subtype === "string";
}

export const UPSTREAM_ERROR_CODES: readonly string[] = ["server_error", "overloaded", "rate_limit"];

export type ResultStreamContext = {
  assistantError?: AssistantErrorObservation;
};

const NO_ERROR_TEXT_REPORTED = "the SDK reported no error text";

function erroredResultFailure(
  message: RunStreamMessage,
  context: ResultStreamContext,
  details: { estimatedCostUsd: number; sessionId: string | null },
): RunFailedError {
  const assistantError = context.assistantError;
  const reported =
    typeof message.result === "string" && message.result !== "" ? message.result : assistantError?.text;
  const cause = reported === undefined || reported === "" ? NO_ERROR_TEXT_REPORTED : reported;
  const classification = [
    ...(assistantError === undefined ? [] : [`sdk error: ${assistantError.code}`]),
    ...(typeof message.api_error_status === "number"
      ? [`api status: ${String(message.api_error_status)}`]
      : []),
  ];
  const suffix = classification.length === 0 ? "" : ` [${classification.join(", ")}]`;
  if (assistantError !== undefined && UPSTREAM_ERROR_CODES.includes(assistantError.code)) {
    return new RunFailedError(`agent run failed (upstream_error): ${cause}${suffix}`, {
      subtype: "upstream_error",
      ...details,
    });
  }
  return new RunFailedError(`agent run failed (errored_result): ${cause}${suffix}`, {
    subtype: "errored_result",
    ...details,
  });
}

export function toRunResult(
  message: RunStreamMessage & { subtype: string },
  executionId: string,
  context: ResultStreamContext = {},
): Omit<RunReportResult, "usage"> {
  const estimatedCostUsd = message.total_cost_usd ?? 0;
  const sessionId = message.session_id ?? null;
  if (message.subtype === "success") {
    // The SDK reports in-band failures as subtype "success" with is_error set — delivered
    // verbatim it would be persisted as the agent's answer.
    if (message.is_error === true) {
      throw erroredResultFailure(message, context, { estimatedCostUsd, sessionId });
    }
    if (typeof message.result !== "string") {
      throw new RunFailedError("success result carried no output text", {
        subtype: "malformed_result",
        estimatedCostUsd,
        sessionId,
      });
    }
    if (message.num_turns === 0) {
      throw new RunFailedError(`run terminated with zero turns — nothing executed: ${message.result}`, {
        subtype: "zero_turns",
        estimatedCostUsd,
        sessionId,
      });
    }
    return {
      executionId,
      output: message.result,
      estimatedCostUsd,
      sessionId: sessionId ?? "",
      ...(message.structured_output === undefined ? {} : { structuredOutput: message.structured_output }),
    };
  }
  const diagnostics =
    message.errors !== undefined && message.errors.length > 0 ? `: ${message.errors.join("; ")}` : "";
  throw new RunFailedError(`agent run failed (${message.subtype})${diagnostics}`, {
    subtype: message.subtype,
    estimatedCostUsd,
    sessionId,
  });
}

export const BUDGET_KILL_RESULT_SUBTYPE = "error_max_budget_usd";

// The budget fuse can only fire after a turn completes, so a kill can land the instant after the
// model delivered its complete answer. The SDK's error result drops that answer (only the success
// shape carries structured_output), so the stream tap holds the last unrejected StructuredOutput
// input and the run settles on it instead of binning paid-for work.
function salvagedBudgetKillResult(
  message: RunStreamMessage & { subtype: string },
  executionId: string,
  structuredOutputs: StructuredOutputTap,
): Omit<RunReportResult, "usage"> | undefined {
  if (message.subtype !== BUDGET_KILL_RESULT_SUBTYPE) {
    return undefined;
  }
  const held = structuredOutputs.held();
  if (held === undefined) {
    return undefined;
  }
  return {
    executionId,
    // The platform stores the text result as the block answer, so this must match a normal
    // outputFormat success, where the CLI serializes structured_output into `result`
    // (measured on CLI 2.1.235).
    output: JSON.stringify(held.value),
    estimatedCostUsd: message.total_cost_usd ?? 0,
    sessionId: message.session_id ?? "",
    structuredOutput: held.value,
    budgetExhausted: true,
  };
}

export const TOOL_DEFERRED_TERMINAL_REASON = "tool_deferred";

type PauseSignal = {
  question: PauseQuestion;
  sessionId: string;
  estimatedCostUsd: number;
};

function isPauseMessage(message: RunStreamMessage): boolean {
  return message.type === "result" && message.terminal_reason === TOOL_DEFERRED_TERMINAL_REASON;
}

type RunInitMessage = RunStreamMessage & {
  type: typeof INIT_MESSAGE_TYPE;
  subtype: typeof INIT_MESSAGE_SUBTYPE;
};

function isInitMessage(message: RunStreamMessage): message is RunInitMessage {
  return message.type === INIT_MESSAGE_TYPE && message.subtype === INIT_MESSAGE_SUBTYPE;
}

function toPauseSignal(message: RunStreamMessage): PauseSignal {
  const estimatedCostUsd = message.total_cost_usd ?? 0;
  const sessionId = message.session_id;
  const deferred = message.deferred_tool_use;
  const question = deferred?.input["question"];
  if (
    sessionId === undefined ||
    sessionId === "" ||
    deferred === undefined ||
    typeof question !== "string" ||
    question === ""
  ) {
    throw new RunFailedError("deferred pause carried no session or question to resume from", {
      subtype: "malformed_pause",
      estimatedCostUsd,
      sessionId: sessionId ?? null,
    });
  }
  return {
    question: { id: deferred.id, toolName: deferred.name, prompt: question, input: { ...deferred.input } },
    sessionId,
    estimatedCostUsd,
  };
}

export const RUN_TOOLS: readonly string[] = [BASH_TOOL_NAME, READ_TOOL_NAME];

// The sub-agent tool registers under this name in `tools` and the init report; the
// model's tool_use blocks call it "Agent" (both measured).
export const RUN_AGENT_TOOL_NAME = "Task";

export const PLATFORM_MCP_SERVER_NAME = "platform";
const PLATFORM_MCP_TOOL_PATTERN = `mcp__${PLATFORM_MCP_SERVER_NAME}__*`;

// A superset of STATIC_CREDENTIAL_VARS, not an alias: the metadata endpoint is denied to the
// sandbox but must stay in the run child's own env.
const SANDBOX_DENIED_ENV_VARS: readonly string[] = [
  ...STATIC_CREDENTIAL_VARS,
  "AWS_EC2_METADATA_SERVICE_ENDPOINT",
];

// A leased run uid has no passwd entry and HOME is denied, so git cannot derive an identity itself.
export const RUN_GIT_IDENTITY_ENV = {
  GIT_AUTHOR_NAME: "Agent runtime",
  GIT_AUTHOR_EMAIL: "agent-runtime@localhost",
  GIT_COMMITTER_NAME: "Agent runtime",
  GIT_COMMITTER_EMAIL: "agent-runtime@localhost",
} as const;

export function buildQueryOptions(input: {
  maxBudgetUsd: number;
  maxTurns: number;
  effort?: RunEffort;
  abortController: AbortController;
  env: Record<string, string>;
  workspacePath: string;
  readMirror?: ReadMirror;
  skillPlugin?: SkillPluginBinding;
  agents?: ResolvedRunAgent[];
  resumeSessionId?: string;
  outputFormat?: RunOutputFormat;
  systemPrompt?: RunSystemPrompt;
  mcpServer?: McpServerBinding;
}): Options {
  const declaredAgents = input.agents ?? [];
  const skillAssetsPath = input.skillPlugin?.assetsPath;
  const runTools = [
    ...(input.skillPlugin === undefined ? RUN_TOOLS : [...RUN_TOOLS, SKILL_TOOL_NAME]),
    ...(declaredAgents.length === 0 ? [] : [RUN_AGENT_TOOL_NAME]),
  ];
  return {
    tools: runTools,
    // allowedTools auto-approves, it does not restrict — only `tools` decides what exists.
    allowedTools: input.mcpServer === undefined ? runTools : [...runTools, PLATFORM_MCP_TOOL_PATTERN],
    ...(declaredAgents.length === 0
      ? {}
      : {
          agents: Object.fromEntries(
            declaredAgents.map((agent) => [
              agent.name,
              {
                description: agent.description,
                prompt: composeSystemPrompt(agent.prompt),
                tools: [...RUN_TOOLS],
                maxTurns: input.maxTurns,
                ...(agent.skills.length === 0 ? {} : { skills: [...agent.skills] }),
              },
            ]),
          ),
        }),
    skills: input.skillPlugin === undefined ? [] : [...input.skillPlugin.skillNames],
    ...(input.skillPlugin === undefined
      ? {}
      : { plugins: [{ type: "local" as const, path: input.skillPlugin.path, skipMcpDiscovery: true }] }),
    strictMcpConfig: true,
    ...(input.mcpServer === undefined
      ? {}
      : {
          mcpServers: {
            [PLATFORM_MCP_SERVER_NAME]: {
              type: "http" as const,
              url: input.mcpServer.url,
              ...(input.mcpServer.runToken === undefined
                ? {}
                : { headers: { Authorization: `Bearer ${input.mcpServer.runToken}` } }),
              // Blocks the first turn until connected, so the init status check below is meaningful.
              alwaysLoad: true,
            },
          },
        }),
    permissionMode: "dontAsk",
    // Without this, assembled messages carry only the message_start output_tokens placeholder
    // (~1 token), not the real count — the raw stream's final message_delta has that.
    includePartialMessages: true,
    // Under "dontAsk", allowedTools auto-approves before canUseTool runs, so containment must
    // be this PreToolUse hook, which runs first.
    hooks: {
      PreToolUse: [
        // No matcher: Options.toolAliases could rename Read, and a renamed Read must still meet the gate.
        { hooks: [createReadGateHook(input.workspacePath, input.readMirror)] },
        ...(declaredAgents.length === 0
          ? []
          : AGENT_TOOL_MATCHERS.map((matcher) => ({
              matcher,
              hooks: [createAgentGateHook(declaredAgents.map((agent) => agent.name))],
            }))),
      ],
    },
    cwd: input.workspacePath,
    sandbox: {
      enabled: true,
      failIfUnavailable: true,
      allowUnsandboxedCommands: false,
      network: { allowedDomains: [], strictAllowlist: true },
      filesystem: {
        denyRead: [...SANDBOX_DENIED_READ_ROOTS],
        allowRead: [input.workspacePath, ...(skillAssetsPath === undefined ? [] : [skillAssetsPath])],
        allowWrite: [input.workspacePath],
      },
      credentials: {
        envVars: SANDBOX_DENIED_ENV_VARS.map((name) => ({ name, mode: "deny" as const })),
      },
    },
    maxTurns: input.maxTurns,
    settingSources: [],
    maxBudgetUsd: input.maxBudgetUsd,
    abortController: input.abortController,
    env: {
      ...input.env,
      ...RUN_GIT_IDENTITY_ENV,
      ...(skillAssetsPath === undefined ? {} : { [SKILL_ASSETS_ENV_VAR]: skillAssetsPath }),
    },
    ...(input.effort === undefined ? {} : { effort: input.effort }),
    ...(input.resumeSessionId === undefined ? {} : { resume: input.resumeSessionId }),
    ...(input.outputFormat === undefined ? {} : { outputFormat: input.outputFormat }),
    systemPrompt: composeSystemPrompt(input.systemPrompt),
  };
}

// The SDK's undeclared defaults (sdk.d.ts `Options.effort`/`Options.thinking`) when neither knob is
// sent — "assumed" on thinking since it's model-dependent and unverifiable here. The effort default
// is per model: measured on CLI 2.1.308 against a capturing stub, it sends medium to the 5.5 models,
// and no effort at all to Haiku 4.5, even when a run declares one.
const SDK_DEFAULT_EFFORT: RunEffort = "high";
const SDK_MODEL_DEFAULT_EFFORTS: readonly { pattern: RegExp; effort: RunEffort }[] = [
  { pattern: /(opus|sonnet|haiku)-5-5/i, effort: "medium" },
];
const MODELS_WITHOUT_EFFORT: readonly RegExp[] = [/haiku-4-5/i];
const SDK_ASSUMED_DEFAULT_THINKING = { type: "adaptive" } as const;

// CLI 2.1.308 runs Sonnet 5.5 and Haiku 5.5 at medium when a run declares no effort. CLI 2.1.281
// ran them at high, except Haiku 5.5 in Bedrock mode, which it sent no effort and the API ran at its
// own medium; the runtime declares what 2.1.281 ran, so the SDK bump moves no run's effort (ADR 0154).
const RUNTIME_DEFAULT_EFFORTS: readonly {
  pattern: RegExp;
  effort: RunEffort;
  providers: readonly ModelProvider[];
}[] = [
  { pattern: /sonnet-5-5/i, effort: "high", providers: MODEL_PROVIDERS },
  {
    pattern: /haiku-5-5/i,
    effort: "high",
    providers: MODEL_PROVIDERS.filter((provider) => provider !== "bedrock"),
  },
];

export function runtimeDefaultEffort(model: string, provider: ModelProvider): RunEffort | undefined {
  return RUNTIME_DEFAULT_EFFORTS.find(
    (entry) => entry.pattern.test(model) && entry.providers.includes(provider),
  )?.effort;
}

// The effort a run hands the SDK, and whose choice it was: one value for the SDK option and the
// trace envelope alike, so the envelope can never claim an effort the SDK was not handed.
export type RunEffortChoice = { effort: RunEffort; source: "request" | "runtime_default" };

export function runEffortChoice(
  declared: RunEffort | undefined,
  model: string,
  provider: ModelProvider,
): RunEffortChoice | undefined {
  if (declared !== undefined) {
    return { effort: declared, source: "request" };
  }
  const runtimeEffort = runtimeDefaultEffort(model, provider);
  return runtimeEffort === undefined ? undefined : { effort: runtimeEffort, source: "runtime_default" };
}

export function resolveReasoningConfig(
  runEffort: RunEffortChoice | undefined,
  model: string,
): TraceReasoningConfig {
  const thinking = { thinking: SDK_ASSUMED_DEFAULT_THINKING, thinkingSource: "sdk_default_assumed" } as const;
  if (MODELS_WITHOUT_EFFORT.some((pattern) => pattern.test(model))) {
    return { effort: null, effortSource: "model_takes_none", ...thinking };
  }
  if (runEffort !== undefined) {
    return { effort: runEffort.effort, effortSource: runEffort.source, ...thinking };
  }
  const sdkEffort =
    SDK_MODEL_DEFAULT_EFFORTS.find(({ pattern }) => pattern.test(model))?.effort ?? SDK_DEFAULT_EFFORT;
  return { effort: sdkEffort, effortSource: "sdk_default", ...thinking };
}

const SANDBOX_FINGERPRINT_WORKSPACE = "/workspace";

// Fuses are excluded from the fingerprint so same-build traces agree regardless of per-run values.
export function sandboxConfigId(): string {
  const options = buildQueryOptions({
    maxBudgetUsd: DEFAULT_MAX_BUDGET_USD,
    maxTurns: DEFAULT_MAX_TURNS,
    abortController: new AbortController(),
    env: {},
    workspacePath: SANDBOX_FINGERPRINT_WORKSPACE,
  });
  const lockdownShape = {
    tools: options.tools,
    allowedTools: options.allowedTools,
    permissionMode: options.permissionMode,
    sandbox: options.sandbox,
    settingSources: options.settingSources,
  };
  return sha256HexOfJson(lockdownShape);
}

async function* tapMessages(
  stream: AsyncIterable<RunStreamMessage>,
  meter: CostMeter,
  trace: RunTrace | undefined,
  observeToolErrors: ((message: unknown) => void) | undefined,
  structuredOutputs: StructuredOutputTap,
  assistantErrors: AssistantErrorTap,
): AsyncGenerator<RunStreamMessage> {
  for await (const message of stream) {
    meter.recordMessage(message);
    trace?.appendMessage(message);
    if (isStreamEventMessage(message)) {
      continue;
    }
    observeToolErrors?.(message);
    structuredOutputs.recordMessage(message);
    assistantErrors.recordMessage(message);
    yield message;
  }
}

function defaultCheckpointer(subprocessEnv: Record<string, string>): Checkpointer {
  return (input) => {
    const configDir = subprocessEnv["CLAUDE_CONFIG_DIR"];
    if (configDir === undefined || configDir === "") {
      return Promise.reject(new Error("CLAUDE_CONFIG_DIR is not set, so there is no session dir to save"));
    }
    return Promise.resolve(
      // Deliberately unstamped: a pause bundle never leaves this container — it is restored by
      // the same build that wrote it, minutes later — so it has no reader the stamp could serve.
      // The terminal bundle, which does leave, carries one.
      serializeSessionCheckpoint({
        sessionId: input.sessionId,
        workspacePath: input.workspacePath,
        configDir,
        reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      }),
    );
  };
}

function defaultRestoredSessionCostReader(
  subprocessEnv: Record<string, string>,
  workspacePath: string,
): RestoredSessionCostReader {
  const configDir = subprocessEnv["CLAUDE_CONFIG_DIR"];
  if (configDir === undefined || configDir === "") {
    return () => 0;
  }
  return createRestoredSessionCostReader({ configDir, cwd: workspacePath });
}

// A resumed session always restores some totals; reading none means they could not be read.
function knownOrNull(restored: ModelUsageTotals): ModelUsageTotals | null {
  return restored.size === 0 ? null : restored;
}

function defaultRestoredSessionModelUsageReader(
  subprocessEnv: Record<string, string>,
  workspacePath: string,
): RestoredSessionModelUsageReader {
  const configDir = subprocessEnv["CLAUDE_CONFIG_DIR"];
  if (configDir === undefined || configDir === "") {
    return () => NO_MODEL_USAGE;
  }
  return createRestoredSessionModelUsageReader({ configDir, cwd: workspacePath });
}

type SegmentOutcome =
  | { kind: "result"; message: RunStreamMessage & { subtype: string } }
  | { kind: "pause"; signal: PauseSignal };

const MCP_CONNECTED_STATUS = "connected";

// A missing tool surface must fail the run, not degrade into the model silently improvising.
function mcpConnectFailure(message: RunInitMessage, serverName: string): RunFailedError | undefined {
  const server = message.mcp_servers?.find((candidate) => candidate.name === serverName);
  if (server?.status === MCP_CONNECTED_STATUS) {
    return undefined;
  }
  const observed = server === undefined ? "absent from the init report" : `status "${server.status}"`;
  return new RunFailedError(`MCP server "${serverName}" is not connected (${observed})`, {
    subtype: "mcp_connect_failed",
    estimatedCostUsd: 0,
    sessionId: null,
  });
}

function skillRegisterFailure(
  message: RunInitMessage,
  expectedSkills: readonly string[],
): RunFailedError | undefined {
  const registered = message.skills ?? [];
  const missing = expectedSkills.filter((skillName) => !registered.includes(skillName));
  if (missing.length === 0) {
    return undefined;
  }
  return new RunFailedError(`requested skills did not register: ${missing.join(", ")}`, {
    subtype: "skill_register_failed",
    estimatedCostUsd: 0,
    sessionId: null,
  });
}

// An init missing claude_code_version passes deliberately — the SDK always sends it in practice.
function cliVersionDriftFailure(message: RunInitMessage): RunFailedError | undefined {
  const reported = message.claude_code_version;
  if (reported === undefined || reported === EXPECTED_CLAUDE_CODE_VERSION) {
    return undefined;
  }
  return new RunFailedError(
    `CLI version drift: init reports claude_code_version "${reported}" but this build pins "${EXPECTED_CLAUDE_CODE_VERSION}"`,
    {
      subtype: "cli_version_drift",
      estimatedCostUsd: 0,
      sessionId: null,
    },
  );
}

function reportedSessionIdOf(message: RunStreamMessage): string | undefined {
  const reported = message.session_id;
  return reported === undefined || reported === "" ? undefined : reported;
}

// The SDK signals nothing on a failed resume, so the runtime compares the session it asked to
// resume against the one the stream reports, on init, pause and result. Init may omit
// session_id, so pause and result are the backstop.
// sessionId is null on purpose: a non-null id would publish a terminal bundle inviting the
// platform to resume the wrong conversation.
const SESSION_ID_MISMATCH_SUBTYPE = "session_id_mismatch";

function sessionIdMismatchFailure(expected: string, message: RunStreamMessage): RunFailedError | undefined {
  const reported = reportedSessionIdOf(message);
  if (reported === undefined || reported === expected) {
    return undefined;
  }
  return new RunFailedError(
    `resumed session "${expected}" but the stream reports session "${reported}" — ` +
      "the SDK started a different conversation instead of resuming",
    {
      subtype: "session_id_mismatch",
      estimatedCostUsd: message.total_cost_usd ?? 0,
      sessionId: null,
    },
  );
}

async function runSegment(input: {
  stream: AsyncIterable<RunStreamMessage>;
  deadlineMs: number;
  abortController: AbortController;
  externalAbort: Promise<never> | undefined;
  expectMcpServer?: string;
  expectSkills?: readonly string[];
  expectSessionId?: string;
}): Promise<SegmentOutcome> {
  const iterator = input.stream[Symbol.asyncIterator]();
  const sessionMismatchOf = (message: RunStreamMessage): RunFailedError | undefined =>
    input.expectSessionId === undefined
      ? undefined
      : sessionIdMismatchFailure(input.expectSessionId, message);
  let settled = false;
  // Tracked across the stream because a deadline kill abandons it before any segment settles,
  // and the deadline failure needs the id the init already named to publish a terminal bundle.
  // On a resume leg only the expected id is tracked: a mismatched stream is not this run's.
  let observedSessionId: string | null = null;
  let deadlineFired = false;
  let deadlineTimer: ReturnType<typeof setTimeout> | undefined;
  const deadline = new Promise<never>((_, reject) => {
    deadlineTimer = setTimeout(() => {
      deadlineFired = true;
      input.abortController.abort();
      reject(
        new RunFailedError(`run deadline exceeded after ${input.deadlineMs}ms`, {
          subtype: "deadline_exceeded",
          estimatedCostUsd: 0,
          sessionId: observedSessionId,
        }),
      );
    }, input.deadlineMs);
  });
  try {
    while (true) {
      const racers =
        input.externalAbort === undefined
          ? [iterator.next(), deadline]
          : [iterator.next(), deadline, input.externalAbort];
      const step = await Promise.race(racers);
      if (step.done) {
        throw new RunFailedError("stream ended with no result message", {
          subtype: "missing_result",
          estimatedCostUsd: 0,
          sessionId: null,
        });
      }
      const reportedSessionId = reportedSessionIdOf(step.value);
      if (
        reportedSessionId !== undefined &&
        (input.expectSessionId === undefined || reportedSessionId === input.expectSessionId)
      ) {
        observedSessionId = reportedSessionId;
      }
      if (isInitMessage(step.value)) {
        const failure =
          cliVersionDriftFailure(step.value) ??
          (input.expectMcpServer === undefined
            ? undefined
            : mcpConnectFailure(step.value, input.expectMcpServer)) ??
          sessionMismatchOf(step.value) ??
          (input.expectSkills === undefined
            ? undefined
            : skillRegisterFailure(step.value, input.expectSkills));
        if (failure !== undefined) {
          input.abortController.abort();
          throw failure;
        }
      }
      if (isPauseMessage(step.value) || isResultMessage(step.value)) {
        const mismatch = sessionMismatchOf(step.value);
        if (mismatch !== undefined) {
          settled = true;
          input.abortController.abort();
          throw mismatch;
        }
      }
      if (isPauseMessage(step.value)) {
        settled = true;
        return { kind: "pause", signal: toPauseSignal(step.value) };
      }
      if (isResultMessage(step.value)) {
        settled = true;
        return { kind: "result", message: step.value };
      }
    }
  } finally {
    // Disarmed before the close is awaited, not after: the deadline's job is bounding model
    // work, and the abortController it fires is shared by every segment, so a deadline that
    // landed during a settled segment's teardown would poison the next segment's query().
    clearTimeout(deadlineTimer);
    const closeBoundMs = settled
      ? undefined
      : deadlineFired
        ? DEADLINE_ITERATOR_CLOSE_TIMEOUT_MS
        : ABANDONED_ITERATOR_CLOSE_TIMEOUT_MS;
    await closeIterator(iterator, closeBoundMs);
  }
}

// Awaited before every exit: the bundle is captured from the config dir after this returns, and
// the SDK subprocess may still be writing its transcript. Bounded because the teardown is the
// SDK's code. Generous on a settled segment, short on an abort, and in between on a deadline,
// which publishes a terminal bundle afterwards.
const ITERATOR_CLOSE_TIMEOUT_MS = 10_000;
const ABANDONED_ITERATOR_CLOSE_TIMEOUT_MS = 500;

function closeIterator(
  iterator: { return?: (value?: undefined) => unknown },
  boundMs: number = ITERATOR_CLOSE_TIMEOUT_MS,
): Promise<void> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const bound = new Promise<void>((resolve) => {
    timer = setTimeout(resolve, boundMs).unref();
  });

  // Invoked inside the chain: `return()` is the SDK's code, and a synchronous throw would escape
  // the `finally` this is called from and replace the segment's real outcome.
  return Promise.race([Promise.resolve().then(() => iterator.return?.(undefined)), bound])
    .then(
      () => {},
      () => {},
    )
    .finally(() => clearTimeout(timer));
}

type ExternalAbortWiring = {
  promise: Promise<never> | undefined;
  cleanup: () => void;
};

function wireExternalAbort(
  signal: AbortSignal | undefined,
  abortController: AbortController,
): ExternalAbortWiring {
  if (signal === undefined) {
    return { promise: undefined, cleanup: () => {} };
  }
  let onAbort: (() => void) | undefined;
  const promise = new Promise<never>((_, reject) => {
    const trigger = (): void => {
      abortController.abort();
      reject(
        new RunFailedError("run aborted by caller", {
          subtype: "aborted",
          estimatedCostUsd: 0,
          sessionId: null,
        }),
      );
    };
    if (signal.aborted) {
      trigger();
      return;
    }
    signal.addEventListener("abort", trigger, { once: true });
    onAbort = trigger;
  });
  // Must still be handled even when it loses the race, or a late abort after settle crashes the process.
  void promise.catch(() => {});
  return {
    promise,
    cleanup: () => {
      if (onAbort !== undefined) {
        signal.removeEventListener("abort", onAbort);
      }
    },
  };
}

type SegmentLoopInput = {
  executionId: string;
  initialPrompt: string;
  queryFn: QueryFn;
  workspacePath: string;
  deadlineMs: number;
  maxBudgetUsd: number;
  maxTurns: number;
  effort: RunEffort | undefined;
  abortController: AbortController;
  externalAbort: Promise<never> | undefined;
  checkpointer: Checkpointer;
  pause: PauseFn | undefined;
  outputFormat: RunOutputFormat | undefined;
  expectMcpServer: string | undefined;
  expectSkills: readonly string[] | undefined;
  resumeSessionId: string | undefined;
  meter: CostMeter;
  structuredOutputs: StructuredOutputTap;
  assistantErrors: AssistantErrorTap;
  allowFurtherPause: boolean;
  restoredSessionCostUsd: RestoredSessionCostReader;
  restoredSessionModelUsage: RestoredSessionModelUsageReader;
};

// Accumulated spend is added exactly once per exit — at the catch boundary or the returned result —
// never at both, since a RunFailedError raised inside the loop carries only segment-local cost.
async function runSegmentLoop(input: SegmentLoopInput): Promise<RunReportResult> {
  const meter = input.meter;
  let prompt = input.initialPrompt;
  let resumeSessionId = input.resumeSessionId;
  let pauseAllowed = input.allowFurtherPause;
  // A resumed segment's SDK cost carries the spend its session restored; only the rest is its own.
  let restoredUsd = 0;
  let restoredModelUsage: ModelUsageTotals | null = NO_MODEL_USAGE;
  const ownSegmentCostUsd = (sdkCostUsd: number): number => Math.max(0, sdkCostUsd - restoredUsd);
  try {
    while (true) {
      // Per run, not per segment — a resumed query() starts its own spend at zero.
      const remainingBudgetUsd = input.maxBudgetUsd - meter.spentUsd();
      if (remainingBudgetUsd <= 0) {
        throw new RunFailedError(`run budget of $${input.maxBudgetUsd} exhausted across segments`, {
          subtype: "budget_exhausted",
          estimatedCostUsd: 0,
          sessionId: resumeSessionId ?? null,
        });
      }
      restoredUsd = resumeSessionId === undefined ? 0 : input.restoredSessionCostUsd(resumeSessionId);
      restoredModelUsage =
        resumeSessionId === undefined
          ? NO_MODEL_USAGE
          : knownOrNull(input.restoredSessionModelUsage(resumeSessionId));
      const stream = input.queryFn({
        prompt,
        workspacePath: input.workspacePath,
        maxBudgetUsd: remainingBudgetUsd,
        maxTurns: input.maxTurns,
        ...(input.effort === undefined ? {} : { effort: input.effort }),
        ...(resumeSessionId === undefined ? {} : { resumeSessionId }),
      });
      const segment = await runSegment({
        stream,
        deadlineMs: input.deadlineMs,
        abortController: input.abortController,
        externalAbort: input.externalAbort,
        ...(input.expectMcpServer === undefined ? {} : { expectMcpServer: input.expectMcpServer }),
        ...(input.expectSkills === undefined ? {} : { expectSkills: input.expectSkills }),
        ...(resumeSessionId === undefined ? {} : { expectSessionId: resumeSessionId }),
      });
      if (segment.kind === "result") {
        const assistantError = input.assistantErrors.last();
        const result =
          salvagedBudgetKillResult(segment.message, input.executionId, input.structuredOutputs) ??
          toRunResult(
            segment.message,
            input.executionId,
            assistantError === undefined ? {} : { assistantError },
          );
        // A success under outputFormat always carries structured_output (SDK-enforced); one without it
        // is malformed, not an absent-output success.
        if (input.outputFormat !== undefined && result.structuredOutput === undefined) {
          throw new RunFailedError("success result carried no structured output despite outputFormat", {
            subtype: "malformed_result",
            estimatedCostUsd: result.estimatedCostUsd,
            sessionId: result.sessionId,
          });
        }
        const settledCostUsd = meter.noteSegmentSettled(
          ownSegmentCostUsd(result.estimatedCostUsd),
          restoredModelUsage,
        );
        return { ...result, estimatedCostUsd: settledCostUsd, usage: meter.bestKnownUsage() };
      }
      const { signal } = segment;
      meter.noteSegmentSettled(ownSegmentCostUsd(signal.estimatedCostUsd), restoredModelUsage);
      if (!pauseAllowed) {
        throw new RunFailedError("run attempted to pause after the caller disallowed further pauses", {
          subtype: "pause_rejected",
          estimatedCostUsd: 0,
          sessionId: signal.sessionId,
        });
      }
      const pauseFn = input.pause;
      if (pauseFn === undefined) {
        throw new RunFailedError("run paused but no pause handler is wired", {
          subtype: "pause_unsupported",
          estimatedCostUsd: 0,
          sessionId: signal.sessionId,
        });
      }
      let checkpointBytes: Uint8Array;
      try {
        checkpointBytes = await input.checkpointer({
          sessionId: signal.sessionId,
          workspacePath: input.workspacePath,
        });
      } catch (error) {
        throw new RunFailedError(`failed to checkpoint the paused session: ${messageOf(error)}`, {
          subtype: "checkpoint_failed",
          estimatedCostUsd: 0,
          sessionId: signal.sessionId,
        });
      }
      const pausePromise = pauseFn({
        question: signal.question,
        sessionId: signal.sessionId,
        estimatedCostUsd: meter.spentUsd(),
        checkpoint: sealCheckpoint(checkpointBytes),
      });
      let resolution: PauseResolution;
      try {
        const racers =
          input.externalAbort === undefined ? [pausePromise] : [pausePromise, input.externalAbort];
        resolution = await Promise.race(racers);
      } catch (error) {
        if (error instanceof RunFailedError) {
          throw error;
        }
        throw new RunFailedError(`pause rejected before an answer arrived: ${messageOf(error)}`, {
          subtype: "pause_rejected",
          estimatedCostUsd: 0,
          sessionId: signal.sessionId,
        });
      }
      prompt = resolution.answer;
      pauseAllowed = resolution.allowFurtherPause;
      resumeSessionId = signal.sessionId;
    }
  } catch (error) {
    if (error instanceof RunFailedError) {
      // A mismatched stream is a fresh conversation the CLI restored nothing into.
      const mismatched = error.subtype === SESSION_ID_MISMATCH_SUBTYPE;
      const segmentCostUsd = mismatched ? error.estimatedCostUsd : ownSegmentCostUsd(error.estimatedCostUsd);
      const totalUsd = meter.noteSegmentSettled(
        segmentCostUsd,
        mismatched ? NO_MODEL_USAGE : restoredModelUsage,
      );
      throw RunFailedError.from({
        ...error.failure,
        estimatedCostUsd: totalUsd,
        usage: meter.bestKnownUsage(),
      });
    }
    if (meter.bestKnownUsd() > 0) {
      throw new RunFailedError(messageOf(error), {
        subtype: "stream_error",
        estimatedCostUsd: meter.bestKnownUsd(),
        sessionId: resumeSessionId ?? null,
        usage: meter.bestKnownUsage(),
      });
    }
    throw error;
  }
}

export async function runReport(
  params: RunReportParams,
  options: RunReportOptions = {},
): Promise<RunReportResult> {
  const deadlineMs = options.deadlineMs ?? DEFAULT_RUN_DEADLINE_MS;
  const maxBudgetUsd = options.maxBudgetUsd ?? DEFAULT_MAX_BUDGET_USD;
  const maxTurns = options.maxTurns ?? DEFAULT_MAX_TURNS;
  const effort = options.effort;
  const subprocessEnv = options.subprocessEnv ?? {};
  const workspaceRoot = options.workspaceRoot ?? WORKSPACE_ROOT;
  const abortController = new AbortController();
  const checkpointer = options.checkpointer ?? defaultCheckpointer(subprocessEnv);
  const trace = options.trace;
  const baseQueryFn: QueryFn =
    options.queryFn ??
    ((input) =>
      query({
        prompt: input.prompt,
        options: buildQueryOptions({
          maxBudgetUsd: input.maxBudgetUsd,
          maxTurns: input.maxTurns,
          ...(input.effort === undefined ? {} : { effort: input.effort }),
          abortController,
          env: subprocessEnv,
          workspacePath: input.workspacePath,
          ...(options.readMirror === undefined ? {} : { readMirror: options.readMirror }),
          ...(options.skillPlugin === undefined ? {} : { skillPlugin: options.skillPlugin }),
          ...(options.agents === undefined ? {} : { agents: options.agents }),
          ...(input.resumeSessionId === undefined ? {} : { resumeSessionId: input.resumeSessionId }),
          ...(options.outputFormat === undefined ? {} : { outputFormat: options.outputFormat }),
          ...(options.systemPrompt === undefined ? {} : { systemPrompt: options.systemPrompt }),
          ...(options.mcpServer === undefined ? {} : { mcpServer: options.mcpServer }),
        }),
      }));
  const meter = createCostMeter({
    priorCostUsd: options.priorCostUsd ?? 0,
    ...(options.priorUsage === undefined ? {} : { priorUsage: options.priorUsage }),
    ...(options.onCostUpdate === undefined ? {} : { onCostUpdate: options.onCostUpdate }),
    ...(options.subagentApiMessages === undefined
      ? {}
      : { subagentApiMessages: options.subagentApiMessages }),
    ...(trace?.appendModelUsage === undefined
      ? {}
      : { onSegmentModelUsage: (totals) => trace.appendModelUsage?.(totals) }),
  });
  const observeToolErrors =
    options.onToolError === undefined ? undefined : createToolErrorWatch(options.onToolError);
  const structuredOutputs = createStructuredOutputTap();
  const assistantErrors = createAssistantErrorTap();
  const queryFn: QueryFn = (input) =>
    tapMessages(baseQueryFn(input), meter, trace, observeToolErrors, structuredOutputs, assistantErrors);

  const ownsWorkspace = options.workspacePath === undefined;
  let workspacePath: string;
  if (options.workspacePath === undefined) {
    mkdirSync(workspaceRoot, { recursive: true });
    workspacePath = mkdtempSync(join(workspaceRoot, "run-"));
  } else {
    workspacePath = options.workspacePath;
  }

  const abortWiring = wireExternalAbort(options.signal, abortController);

  trace?.emitStage("run.started", { resumed: options.resumeFrom !== undefined });
  try {
    const result = await runSegmentLoop({
      executionId: params.executionId,
      initialPrompt: params.prompt,
      queryFn,
      workspacePath,
      deadlineMs,
      maxBudgetUsd,
      maxTurns,
      effort,
      abortController,
      externalAbort: abortWiring.promise,
      checkpointer,
      pause: options.pause,
      outputFormat: options.outputFormat,
      expectMcpServer: options.mcpServer === undefined ? undefined : PLATFORM_MCP_SERVER_NAME,
      expectSkills: options.skillPlugin?.skillNames,
      resumeSessionId: options.resumeFrom?.sessionId,
      meter,
      structuredOutputs,
      assistantErrors,
      allowFurtherPause: options.allowFurtherPause ?? true,
      restoredSessionCostUsd:
        options.restoredSessionCostUsd ?? defaultRestoredSessionCostReader(subprocessEnv, workspacePath),
      restoredSessionModelUsage:
        options.restoredSessionModelUsage ??
        defaultRestoredSessionModelUsageReader(subprocessEnv, workspacePath),
    });
    if (trace === undefined) {
      return result;
    }
    trace.emitStage("run.completed", {
      estimatedCostUsd: result.estimatedCostUsd,
      sessionId: result.sessionId,
      ...(result.budgetExhausted === true ? { budgetExhausted: true } : {}),
    });
    trace.finalize({ sessionId: result.sessionId });
    return { ...result, tracePath: trace.packDir };
  } catch (error) {
    if (trace !== undefined) {
      const failure = error instanceof RunFailedError ? error : undefined;
      trace.emitStage("run.failed", {
        subtype: failure?.subtype ?? "internal_error",
        estimatedCostUsd: failure?.estimatedCostUsd ?? 0,
      });
      trace.finalize({ sessionId: failure?.sessionId ?? null });
    }
    throw error;
  } finally {
    abortWiring.cleanup();
    if (ownsWorkspace) {
      rmSync(workspacePath, { recursive: true, force: true });
    }
  }
}
