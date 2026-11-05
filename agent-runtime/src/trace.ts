import { randomUUID } from "node:crypto";
import { appendFileSync, chmodSync, existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { hasErrorCode, toError } from "./errors.js";
import { sha256Hex } from "./hash.js";
import { createLogger, stderrOnlySink } from "./logger.js";
import type { ModelProvider } from "./model-provider.js";
import { isRecord } from "./narrow.js";
import { claimCreatedDirs } from "./private-fs.js";
import type { RunEffort } from "./run-report.js";
import { createSubagentTranscriptReader, SUBAGENT_FILE_PATTERN, subagentsDirsOf } from "./subagent-usage.js";
import {
  type ApiMessageUsage,
  createUsageTracker,
  INIT_MESSAGE_SUBTYPE,
  INIT_MESSAGE_TYPE,
  isStreamEventMessage,
  type ModelUsageTotals,
  subagentUsageOf,
} from "./usage-cost.js";

const traceLog = createLogger({ sink: stderrOnlySink });

export const TRACE_FORMAT_VERSION = 11;
// A digest of an empty value would collide "no system prompt" with "an empty system prompt".
export const ENVELOPE_ABSENT = "absent";
export const TRACE_FILE_NAME = "trace.ndjson";
export const TRACE_SEQ_FILE_NAME = "trace.seq";
const FLUSH_PENDING_RECORD_LIMIT = 16;
const SUB_DIR_NAME = "sub";
const TOOL_INPUT_TRUNCATE_CHARS = 500;
// Which carrier a usage record's numbers came from (format v8): the main loop's stream
// deltas, a completed sub-agent's transcript (one record per API call it made), or, when that
// transcript could not be read, the Agent tool_result's own usage, which is the sub-agent's
// last API call only and under-counts a multi-call sub-agent.
export const USAGE_SOURCE_STREAM = "stream";
export const USAGE_SOURCE_SUBAGENT_TRANSCRIPT = "subagent_transcript";
export const USAGE_SOURCE_SUBAGENT_RESULT = "subagent_result";
const NO_AGENT = null;
const NO_MESSAGE_ID = null;
// Set explicitly: the pack is written by a run child under a pool uid and read back by
// the supervisor, and mkdir/open modes are masked by an ambient umask nothing here guarantees.
const TRACE_DIR_MODE = 0o755;
const TRACE_FILE_MODE = 0o644;
const TOOL_ERROR_CLASS = "tool_error";
const UNRESOLVED_ERROR_CLASS = "unresolved";

export type TraceReasoningConfig = {
  effort: RunEffort | null;
  effortSource: "request" | "runtime_default" | "sdk_default" | "model_takes_none";
  thinking: { type: string };
  thinkingSource: "request" | "sdk_default_assumed";
};

// A digest or identifier only, never the payload: the envelope is a durable artifact no
// secret or megabyte-scale input may land in.
export type TraceEnvelopeInput = {
  modelProvider: ModelProvider;
  modelPin: string;
  promptSha256: string;
  systemPromptSha256: string;
  outputFormatSha256: string;
  stagedInputsSha256: string;
  mcpServerSha256: string;
  tools: string[];
  skills: string[];
  agents: string[];
  sandboxConfigId: string;
  sdkVersion: string;
  reasoning: TraceReasoningConfig;
};

export type StageEventName = "run.started" | "run.completed" | "run.failed" | "gate.decision";

export type TraceLabelRecord = {
  type: "label";
  uuid: string;
  at: string;
  executionId: string;
  attempt: number;
  entryUuid: string;
  label: string;
  value: unknown;
  source: string;
};

export type RunTrace = {
  packDir: string;
  appendMessage(message: unknown): void;
  appendModelUsage?(totals: ModelUsageTotals): void;
  emitStage(event: StageEventName, detail?: Record<string, unknown>): void;
  finalize(input: { sessionId: string | null }): void;
};

export type CreateRunTraceInput = {
  traceRoot: string;
  executionId: string;
  attempt: number;
  configDir: string;
  envelope: TraceEnvelopeInput;
  now?: () => number;
};

export function tracePackDirOf(traceRoot: string, executionId: string, attempt: number): string {
  return join(traceRoot, executionId, `attempt-${attempt}`);
}

const SDK_PACKAGE_NAME = "@anthropic-ai/claude-agent-sdk";
const UNKNOWN_SDK_VERSION = "unknown";

// The SDK's exports map exposes no "./package.json" subpath, so read the version from
// the file beside the resolved entry module instead.
export function readInstalledSdkVersion(): string {
  try {
    const requireFromHere = createRequire(import.meta.url);
    const packageDir = dirname(requireFromHere.resolve(SDK_PACKAGE_NAME));
    const parsed: unknown = JSON.parse(readFileSync(join(packageDir, "package.json"), "utf8"));
    const version = asRecord(parsed)?.["version"];
    return typeof version === "string" && version !== "" ? version : UNKNOWN_SDK_VERSION;
  } catch {
    return UNKNOWN_SDK_VERSION;
  }
}

export function buildLabelRecord(input: {
  executionId: string;
  attempt: number;
  entryUuid: string;
  label: string;
  value: unknown;
  source: string;
  now?: () => number;
}): TraceLabelRecord {
  return {
    type: "label",
    uuid: randomUUID(),
    at: new Date((input.now ?? Date.now)()).toISOString(),
    executionId: input.executionId,
    attempt: input.attempt,
    entryUuid: input.entryUuid,
    label: input.label,
    value: input.value,
    source: input.source,
  };
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return isRecord(value) ? value : undefined;
}

export function contentBlocksOf(message: unknown): Record<string, unknown>[] {
  const inner = asRecord(asRecord(message)?.["message"]);
  const content = inner?.["content"];
  if (!Array.isArray(content)) {
    return [];
  }
  return content.map(asRecord).filter((block): block is Record<string, unknown> => block !== undefined);
}

const THINKING_TOKENS_SUBTYPE = "thinking_tokens";
const IMAGE_BLOCK_TYPE = "image";
const BASE64_SOURCE_TYPE = "base64";
const TOOL_RESULT_BLOCK_TYPE = "tool_result";
const IMAGE_STUB_BLOCK_TYPE = "image_stub";

function isThinkingTokensHeartbeat(message: unknown): boolean {
  const record = asRecord(message);
  return record?.["type"] === INIT_MESSAGE_TYPE && record["subtype"] === THINKING_TOKENS_SUBTYPE;
}

function imageStubOf(
  block: Record<string, unknown>,
  toolUseId: string | null,
): Record<string, unknown> | undefined {
  if (block["type"] !== IMAGE_BLOCK_TYPE) {
    return undefined;
  }
  const source = asRecord(block["source"]);
  const data = source?.["data"];
  if (source?.["type"] !== BASE64_SOURCE_TYPE || typeof data !== "string") {
    return undefined;
  }
  const decoded = Buffer.from(data, "base64");
  const mediaType = source["media_type"];
  return {
    type: IMAGE_STUB_BLOCK_TYPE,
    media_type: typeof mediaType === "string" ? mediaType : null,
    decoded_bytes: decoded.byteLength,
    sha256: sha256Hex(decoded),
    tool_use_id: toolUseId,
  };
}

function withStubbedContentBlock(block: unknown): unknown {
  const record = asRecord(block);
  if (record === undefined) {
    return block;
  }
  const directStub = imageStubOf(record, null);
  if (directStub !== undefined) {
    return directStub;
  }
  const toolResultContent = record["content"];
  if (record["type"] !== TOOL_RESULT_BLOCK_TYPE || !Array.isArray(toolResultContent)) {
    return block;
  }
  const rawToolUseId = record["tool_use_id"];
  const toolUseId = typeof rawToolUseId === "string" ? rawToolUseId : null;
  let stubbedAny = false;
  const content = toolResultContent.map((inner) => {
    const innerRecord = asRecord(inner);
    const stub = innerRecord === undefined ? undefined : imageStubOf(innerRecord, toolUseId);
    if (stub === undefined) {
      return inner;
    }
    stubbedAny = true;
    return stub;
  });
  return stubbedAny ? { ...record, content } : block;
}

function withStubbedImages(message: unknown): unknown {
  const record = asRecord(message);
  const inner = asRecord(record?.["message"]);
  const content = inner?.["content"];
  if (record === undefined || inner === undefined || !Array.isArray(content)) {
    return message;
  }
  let changed = false;
  const stubbed = content.map((block) => {
    const replaced = withStubbedContentBlock(block);
    if (replaced !== block) {
      changed = true;
    }
    return replaced;
  });
  return changed ? { ...record, message: { ...inner, content: stubbed } } : message;
}

function truncatedJson(value: unknown, limit: number): string {
  return JSON.stringify(value)?.slice(0, limit) ?? "";
}

type PendingToolUse = {
  toolName: string;
  inputTruncated: string;
  startedAtMs: number;
};

function messageSeqOf(line: string): number {
  try {
    const record = asRecord(JSON.parse(line));
    const seq = record?.["seq"];
    return record?.["type"] === "message" && typeof seq === "number" ? seq : 0;
  } catch {
    return 0;
  }
}

// Fallback for when the seq sidecar's write was lost: reconstruct seq by scanning the
// pack itself rather than assuming the sidecar is authoritative.
function lastMessageSeqOf(traceFile: string): number {
  try {
    return readFileSync(traceFile, "utf8")
      .split("\n")
      .reduce((max, line) => Math.max(max, messageSeqOf(line)), 0);
  } catch {
    return 0;
  }
}

function seededSeqOf(seqFile: string, traceFile: string): number {
  try {
    const stored = Number.parseInt(readFileSync(seqFile, "utf8").trim(), 10);
    if (Number.isInteger(stored) && stored >= 0) {
      return stored;
    }
  } catch {}
  return existsSync(traceFile) ? lastMessageSeqOf(traceFile) : 0;
}

function isSegmentBoundary(message: unknown): boolean {
  return asRecord(message)?.["type"] === "result";
}

function initMessageOf(message: unknown): Record<string, unknown> | undefined {
  const record = asRecord(message);
  return record?.["type"] === INIT_MESSAGE_TYPE && record["subtype"] === INIT_MESSAGE_SUBTYPE
    ? record
    : undefined;
}

// "wx" fails EEXIST rather than truncating a pack a previous segment already opened,
// with no check-then-write window; the result also gates whether the envelope is written.
function createSupervisorReadableFile(path: string): boolean {
  try {
    writeFileSync(path, "", { flag: "wx", mode: TRACE_FILE_MODE });
  } catch (error) {
    if (hasErrorCode(error, "EEXIST")) {
      return false;
    }
    throw error;
  }
  chmodSync(path, TRACE_FILE_MODE);
  return true;
}

// Kept verbatim rather than dropped: an unparseable line cannot carry a base64 image
// block (those are JSON), so it is safe to leave alone.
function sweptTranscriptLineOf(line: string): string | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(line);
  } catch {
    return line;
  }
  if (isThinkingTokensHeartbeat(parsed)) {
    return null;
  }
  const stubbed = withStubbedImages(parsed);
  return stubbed === parsed ? line : JSON.stringify(stubbed);
}

// The CLI writes session transcripts mode 600; rewritten here at the pack's mode so the
// supervisor (a different uid) can read them.
function sweepTranscriptFile(sourcePath: string, targetPath: string): void {
  const sweptLines = readFileSync(sourcePath, "utf8")
    .split("\n")
    .filter((line) => line !== "")
    .map(sweptTranscriptLineOf)
    .filter((line): line is string => line !== null);
  writeFileSync(targetPath, sweptLines.length === 0 ? "" : `${sweptLines.join("\n")}\n`, {
    mode: TRACE_FILE_MODE,
  });
  chmodSync(targetPath, TRACE_FILE_MODE);
}

export function createRunTrace(input: CreateRunTraceInput): RunTrace {
  const now = input.now ?? Date.now;
  const packDir = tracePackDirOf(input.traceRoot, input.executionId, input.attempt);
  const traceFile = join(packDir, TRACE_FILE_NAME);
  const seqFile = join(packDir, TRACE_SEQ_FILE_NAME);
  const pendingToolUses = new Map<string, PendingToolUse>();
  const usageTracker = createUsageTracker();
  let seq = seededSeqOf(seqFile, traceFile);
  let finalized = false;
  let pendingLines: string[] = [];

  function isoNow(): string {
    return new Date(now()).toISOString();
  }

  function writeRecord(record: Record<string, unknown>): void {
    pendingLines.push(JSON.stringify({ uuid: randomUUID(), at: isoNow(), ...record }));
  }

  function flush(): void {
    if (pendingLines.length === 0) {
      return;
    }
    const lines = pendingLines;
    pendingLines = [];
    appendFileSync(traceFile, `${lines.join("\n")}\n`);
    writeFileSync(seqFile, `${seq}\n`);
  }

  function bestEffort(action: () => void): void {
    try {
      action();
    } catch (error) {
      traceLog.error("trace_write_failed", { packDir, error: toError(error) });
    }
  }

  claimCreatedDirs(packDir, TRACE_DIR_MODE);
  const openedFreshPack = createSupervisorReadableFile(traceFile);
  createSupervisorReadableFile(seqFile);
  if (openedFreshPack) {
    writeRecord({
      type: "envelope",
      traceVersion: TRACE_FORMAT_VERSION,
      executionId: input.executionId,
      attempt: input.attempt,
      modelPin: input.envelope.modelPin,
      promptSha256: input.envelope.promptSha256,
      systemPromptSha256: input.envelope.systemPromptSha256,
      outputFormatSha256: input.envelope.outputFormatSha256,
      stagedInputsSha256: input.envelope.stagedInputsSha256,
      mcpServerSha256: input.envelope.mcpServerSha256,
      tools: input.envelope.tools,
      skills: input.envelope.skills,
      agents: input.envelope.agents,
      sandboxConfigId: input.envelope.sandboxConfigId,
      sdkVersion: input.envelope.sdkVersion,
      reasoning: input.envelope.reasoning,
      // Appended, not inserted: every key a v8 reader knew keeps its position.
      modelProvider: input.envelope.modelProvider,
    });
    flush();
  }

  function writeUsageRecord(
    messageUsage: ApiMessageUsage,
    provenance: { agentId: string | null; source: string } = {
      agentId: NO_AGENT,
      source: USAGE_SOURCE_STREAM,
    },
  ): void {
    writeRecord({
      type: "usage",
      messageId: messageUsage.messageId,
      model: messageUsage.model,
      usage: { output_tokens_details: null, ...messageUsage.rawUsage },
      agentId: provenance.agentId,
      source: provenance.source,
    });
  }

  const readSubagentTranscript = createSubagentTranscriptReader(input.configDir);
  const distilledAgentIds = new Set<string>();

  function writeSubagentUsageRecords(message: unknown): boolean {
    const payload = subagentUsageOf(message);
    if (payload === undefined || distilledAgentIds.has(payload.agentId)) {
      return false;
    }
    distilledAgentIds.add(payload.agentId);
    const fallbackModel = payload.resolvedModel ?? "";
    const transcript =
      payload.sessionId === undefined
        ? undefined
        : readSubagentTranscript(payload.sessionId, payload.agentId);
    if (transcript === undefined || transcript.length === 0) {
      writeRecord({
        type: "usage",
        messageId: NO_MESSAGE_ID,
        model: fallbackModel,
        usage: { output_tokens_details: null, ...payload.rawUsage },
        agentId: payload.agentId,
        source: USAGE_SOURCE_SUBAGENT_RESULT,
      });
      return true;
    }
    for (const apiMessage of transcript) {
      writeUsageRecord(
        { ...apiMessage, model: apiMessage.model === "" ? fallbackModel : apiMessage.model },
        { agentId: payload.agentId, source: USAGE_SOURCE_SUBAGENT_TRANSCRIPT },
      );
    }
    return true;
  }

  function writeModelInfoRecord(message: Record<string, unknown>): void {
    writeRecord({
      type: "model_info",
      model: message["model"] ?? null,
      fastModeState: message["fast_mode_state"] ?? null,
      fastModeDisabledReason: message["fast_mode_disabled_reason"] ?? null,
      supportedEffortLevels: message["supportedEffortLevels"] ?? null,
      supportsAdaptiveThinking: message["supportsAdaptiveThinking"] ?? null,
    });
  }

  function recordToolUses(message: unknown): void {
    for (const block of contentBlocksOf(message)) {
      if (block["type"] !== "tool_use") {
        continue;
      }
      const id = block["id"];
      const name = block["name"];
      if (typeof id !== "string" || typeof name !== "string") {
        continue;
      }
      pendingToolUses.set(id, {
        toolName: name,
        inputTruncated: truncatedJson(block["input"], TOOL_INPUT_TRUNCATE_CHARS),
        startedAtMs: now(),
      });
    }
  }

  function writeToolCall(
    toolUseId: string,
    pending: PendingToolUse,
    result: { outputBytes: number | null; durationMs: number | null; errorClass: string | null },
  ): void {
    writeRecord({
      type: "tool_call",
      toolUseId,
      toolName: pending.toolName,
      inputTruncated: pending.inputTruncated,
      outputBytes: result.outputBytes,
      durationMs: result.durationMs,
      errorClass: result.errorClass,
    });
  }

  function resolveToolResults(message: unknown): boolean {
    let resolvedAny = false;
    for (const block of contentBlocksOf(message)) {
      if (block["type"] !== "tool_result") {
        continue;
      }
      const toolUseId = block["tool_use_id"];
      if (typeof toolUseId !== "string") {
        continue;
      }
      const pending = pendingToolUses.get(toolUseId);
      if (pending === undefined) {
        continue;
      }
      pendingToolUses.delete(toolUseId);
      writeToolCall(toolUseId, pending, {
        outputBytes: Buffer.byteLength(JSON.stringify(block["content"]) ?? ""),
        durationMs: now() - pending.startedAtMs,
        errorClass: block["is_error"] === true ? TOOL_ERROR_CLASS : null,
      });
      resolvedAny = true;
    }
    return resolvedAny;
  }

  function sweepSubagentTranscripts(sessionId: string): void {
    const agents: string[] = [];
    for (const subagentsDir of subagentsDirsOf(input.configDir, sessionId)) {
      for (const entry of readdirSync(subagentsDir, { withFileTypes: true })) {
        const match = entry.name.match(SUBAGENT_FILE_PATTERN);
        if (!entry.isFile() || match?.[1] === undefined) {
          continue;
        }
        claimCreatedDirs(join(packDir, SUB_DIR_NAME), TRACE_DIR_MODE);
        sweepTranscriptFile(join(subagentsDir, entry.name), join(packDir, SUB_DIR_NAME, entry.name));
        agents.push(match[1]);
      }
    }
    writeRecord({ type: "subagents", sessionId, agents: agents.sort() });
  }

  return {
    packDir,

    appendMessage(message) {
      bestEffort(() => {
        if (isStreamEventMessage(message)) {
          let wroteUsage = false;
          for (const update of usageTracker.record(message)) {
            if (update.settled) {
              writeUsageRecord(update);
              wroteUsage = true;
            }
          }
          if (wroteUsage) {
            flush();
          }
          return;
        }
        if (isThinkingTokensHeartbeat(message)) {
          return;
        }
        seq += 1;
        writeRecord({ type: "message", seq, message: withStubbedImages(message) });
        recordToolUses(message);
        const initMessage = initMessageOf(message);
        if (initMessage !== undefined) {
          writeModelInfoRecord(initMessage);
        }
        const resolvedToolCall = resolveToolResults(message);
        const wroteSubagentUsage = writeSubagentUsageRecords(message);
        if (
          initMessage !== undefined ||
          resolvedToolCall ||
          wroteSubagentUsage ||
          isSegmentBoundary(message) ||
          pendingLines.length >= FLUSH_PENDING_RECORD_LIMIT
        ) {
          flush();
        }
      });
    },

    appendModelUsage(totals) {
      bestEffort(() => {
        writeRecord({ type: "model_usage", modelUsage: Object.fromEntries(totals) });
        flush();
      });
    },

    emitStage(event, detail = {}) {
      bestEffort(() => {
        writeRecord({
          type: "stage",
          event,
          executionId: input.executionId,
          attempt: input.attempt,
          detail,
        });
        flush();
      });
    },

    finalize({ sessionId }) {
      if (finalized) {
        return;
      }
      finalized = true;
      bestEffort(() => {
        const inFlightUsage = usageTracker.settleInFlight();
        if (inFlightUsage !== undefined) {
          writeUsageRecord(inFlightUsage);
        }
        for (const [toolUseId, pending] of pendingToolUses) {
          writeToolCall(toolUseId, pending, {
            outputBytes: null,
            durationMs: null,
            errorClass: UNRESOLVED_ERROR_CLASS,
          });
        }
        pendingToolUses.clear();
        if (sessionId !== null) {
          sweepSubagentTranscripts(sessionId);
        } else {
          writeRecord({ type: "subagents", sessionId: null, agents: [] });
        }
        flush();
      });
    },
  };
}
