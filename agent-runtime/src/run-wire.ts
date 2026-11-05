import type { CheckpointBundle } from "./checkpoint-store.js";
import { MAX_CHECKPOINT_BUNDLE_BYTES, MAX_WIRE_FRAME_LENGTH } from "./config.js";
import { sha256Hex } from "./hash.js";
import { createNarrower, type Narrower } from "./narrow.js";
import { parseResolvedRunAgents, type ResolvedRunAgent } from "./run-agents.js";
import {
  parseDocumentMetas,
  parseRecarriedOutMetas,
  parseRequiredSha256,
  parseSkillFileMetas,
  type StagedFileMeta,
} from "./run-documents.js";
import {
  type PauseQuestion,
  type PauseResolution,
  parseMcpServerUrl,
  parseRunEffort,
  parseRunOutputFormat,
  parseRunReportResult,
  parseRunSystemPrompt,
  parseRunToken,
  parseTokenUsage,
  type RunEffort,
  type RunFailure,
  type RunOutputFormat,
  type RunReportResult,
  type RunSystemPrompt,
} from "./run-report.js";
import { EXECUTION_ID_PATTERN } from "./safe-path.js";
import type { TokenUsage } from "./usage-cost.js";

export class WireError extends Error {}

// A child that could not write its final frame to stdout exits with this code, so a clean
// exit never masquerades as a delivered result; the supervisor recovers the response from
// disk regardless of the code.
export const RESULT_UNDELIVERED_EXIT_CODE = 3;

export type WireCheckpoint = {
  sha256: string;
  bytesBase64: string;
};

// The supervisor→child form: the frame declares the bundle, the bytes arrive as the first
// entry on the fd-3 cargo channel — before the documents, because the child restores the
// session before it stages anything. Child→supervisor bundles (pause and terminal
// checkpoints) keep the base64 WireCheckpoint form: frames on stdout are the only channel
// that direction has.
export type WireCheckpointDescriptor = {
  sha256: string;
  sizeBytes: number;
};

export type WireStartRequest = {
  kind: "start";
  executionId: string;
  attempt: number;
  prompt: string;
  maxBudgetUsd: number;
  maxTurns: number;
  effort?: RunEffort;
  deadlineMs: number;
  documentBudgetBytes: number;
  maxRetainedOutputBytes: number;
  documents?: StagedFileMeta[];
  skillFiles?: StagedFileMeta[];
  agents?: ResolvedRunAgent[];
  model?: string;
  outputFormat?: RunOutputFormat;
  systemPrompt?: RunSystemPrompt;
  mcpServerUrl?: string;
  runToken?: string;
};

// `continuation` marks a platform-carried restore: the checkpoint must carry a matching
// version stamp, restagePublishedOut is skipped, and out/ is staged from exactly the listed
// files. A continuation leg's own cold resume after a pause omits it: by then the leg has
// published its own mirror, which is fresher than the platform's set.
export type WireResumeRequest = {
  kind: "resume";
  executionId: string;
  attempt: number;
  maxBudgetUsd: number;
  maxTurns: number;
  effort?: RunEffort;
  deadlineMs: number;
  documentBudgetBytes: number;
  maxRetainedOutputBytes: number;
  priorCostUsd: number;
  priorUsage?: TokenUsage;
  checkpoint: WireCheckpointDescriptor;
  continuation?: { outFiles: StagedFileMeta[] };
  documents?: StagedFileMeta[];
  skillFiles?: StagedFileMeta[];
  agents?: ResolvedRunAgent[];
  model?: string;
  outputFormat?: RunOutputFormat;
  systemPrompt?: RunSystemPrompt;
  mcpServerUrl?: string;
  runToken?: string;
} & PauseResolution;

export type WireRequest = WireStartRequest | WireResumeRequest;

export type WireAnswerFrame = {
  kind: "answer";
} & PauseResolution;

export type WirePausedFrame = {
  kind: "paused";
  question: PauseQuestion;
  sessionId: string;
  estimatedCostUsd: number;
  checkpoint: WireCheckpoint;
};

export type WireFinalFrame = {
  kind: "final";
  response: WireResponse;
};

export type WireTerminalCheckpointFrame = {
  kind: "terminal_checkpoint";
  sessionId: string;
  checkpoint: WireCheckpoint;
};

export type WireCostFrame = {
  kind: "cost";
  estimatedCostUsd: number;
  usage: TokenUsage;
};

export type WireChildFrame = WirePausedFrame | WireCostFrame | WireTerminalCheckpointFrame | WireFinalFrame;

export type WireResponse = { ok: true; result: RunReportResult } | { ok: false; error: RunFailure };

const parse: Narrower = createNarrower((message) => new WireError(message));

function parseCheckpoint(value: unknown, label: string): WireCheckpoint {
  const record = parse.asRecord(value, label);
  return {
    sha256: parse.requireString(record, "sha256", label),
    bytesBase64: parse.requireString(record, "bytesBase64", label),
  };
}

// The size bound stands in for the frame cap the base64 form paid implicitly: the child
// reads exactly this many cargo bytes before restore, so an unbounded declaration would
// let a frame command an unbounded buffer.
function parseCheckpointDescriptor(value: unknown, label: string): WireCheckpointDescriptor {
  const record = parse.asRecord(value, label);
  const sizeBytes = parse.requirePositiveInteger(record, "sizeBytes", label);
  if (sizeBytes > MAX_CHECKPOINT_BUNDLE_BYTES) {
    parse.fail(`${label}.sizeBytes must be at most ${MAX_CHECKPOINT_BUNDLE_BYTES}`);
  }
  return {
    sha256: parseRequiredSha256(record, parse, label),
    sizeBytes,
  };
}

function parseResolution(record: Record<string, unknown>, label: string): PauseResolution {
  return {
    answer: parse.requirePossiblyEmptyString(record, "answer", label),
    allowFurtherPause: parse.requireBoolean(record, "allowFurtherPause", label),
  };
}

function parseQuestion(value: unknown, label: string): PauseQuestion {
  const record = parse.asRecord(value, label);
  return {
    id: parse.requireString(record, "id", label),
    toolName: parse.requireString(record, "toolName", label),
    prompt: parse.requireString(record, "prompt", label),
    input: parse.asRecord(record["input"], `${label}.input`),
  };
}

export function parseWireRequest(body: unknown): WireRequest {
  const record = parse.asRecord(body, "run request");
  const kind = record["kind"];
  if (kind !== "start" && kind !== "resume") {
    parse.fail('run request.kind must be "start" or "resume"');
  }
  const executionId = parse.requireString(record, "executionId", "run request");
  if (!EXECUTION_ID_PATTERN.test(executionId)) {
    parse.fail(`run request.executionId must match ${EXECUTION_ID_PATTERN.source}`);
  }
  const documents =
    record["documents"] === undefined ? undefined : parseDocumentMetas(record["documents"], parse);
  const skillFiles =
    record["skillFiles"] === undefined ? undefined : parseSkillFileMetas(record["skillFiles"], parse);
  const agents =
    record["agents"] === undefined
      ? undefined
      : parseResolvedRunAgents(record["agents"], parse, "run request.agents");
  const model = parse.optionalString(record, "model", "run request");
  const outputFormat = parse.optionalWith(record, "outputFormat", parseRunOutputFormat, "run request");
  const systemPrompt = parse.optionalWith(record, "systemPrompt", parseRunSystemPrompt, "run request");
  const mcpServerUrl = parse.optionalWith(record, "mcpServerUrl", parseMcpServerUrl, "run request");
  const runToken = parse.optionalWith(record, "runToken", parseRunToken, "run request");
  const effort = parse.optionalWith(record, "effort", parseRunEffort, "run request");
  const common = {
    executionId,
    attempt: parse.requirePositiveNumber(record, "attempt", "run request"),
    maxBudgetUsd: parse.requirePositiveNumber(record, "maxBudgetUsd", "run request"),
    maxTurns: parse.requirePositiveInteger(record, "maxTurns", "run request"),
    ...(effort === undefined ? {} : { effort }),
    deadlineMs: parse.requirePositiveNumber(record, "deadlineMs", "run request"),
    documentBudgetBytes: parse.requirePositiveNumber(record, "documentBudgetBytes", "run request"),
    maxRetainedOutputBytes: parse.requirePositiveNumber(record, "maxRetainedOutputBytes", "run request"),
    ...(documents === undefined ? {} : { documents }),
    ...(skillFiles === undefined ? {} : { skillFiles }),
    ...(agents === undefined ? {} : { agents }),
    ...(model === undefined ? {} : { model }),
    ...(outputFormat === undefined ? {} : { outputFormat }),
    ...(systemPrompt === undefined ? {} : { systemPrompt }),
    ...(mcpServerUrl === undefined ? {} : { mcpServerUrl }),
    ...(runToken === undefined ? {} : { runToken }),
  };
  if (kind === "start") {
    return { kind, ...common, prompt: parse.requireString(record, "prompt", "run request") };
  }
  const priorCostUsd = parse.requireNonNegativeNumber(record, "priorCostUsd", "run request");
  const priorUsage = parse.optionalWith(record, "priorUsage", parseTokenUsage, "run request");
  const continuation = parseContinuation(record["continuation"]);
  return {
    kind,
    ...common,
    priorCostUsd,
    ...(priorUsage === undefined ? {} : { priorUsage }),
    checkpoint: parseCheckpointDescriptor(record["checkpoint"], "run request.checkpoint"),
    ...(continuation === undefined ? {} : { continuation }),
    ...parseResolution(record, "run request"),
  };
}

function parseContinuation(value: unknown): { outFiles: StagedFileMeta[] } | undefined {
  if (value === undefined) {
    return undefined;
  }
  const record = parse.asRecord(value, "run request.continuation");
  return { outFiles: parseRecarriedOutMetas(record["outFiles"], parse) };
}

export function parseWireAnswerFrame(body: unknown): WireAnswerFrame {
  const record = parse.asRecord(body, "answer frame");
  if (record["kind"] !== "answer") {
    parse.fail('answer frame.kind must be "answer"');
  }
  return { kind: "answer", ...parseResolution(record, "answer frame") };
}

export function parseWireChildFrame(body: unknown): WireChildFrame {
  const record = parse.asRecord(body, "child frame");
  const kind = record["kind"];
  if (kind === "paused") {
    return {
      kind,
      question: parseQuestion(record["question"], "child frame.question"),
      sessionId: parse.requireString(record, "sessionId", "child frame"),
      estimatedCostUsd: parse.requireNonNegativeNumber(record, "estimatedCostUsd", "child frame"),
      checkpoint: parseCheckpoint(record["checkpoint"], "child frame.checkpoint"),
    };
  }
  if (kind === "cost") {
    return {
      kind,
      estimatedCostUsd: parse.requireNonNegativeNumber(record, "estimatedCostUsd", "child frame"),
      usage: parseTokenUsage(record["usage"], parse, "child frame.usage"),
    };
  }
  if (kind === "terminal_checkpoint") {
    return {
      kind,
      sessionId: parse.requireString(record, "sessionId", "child frame"),
      checkpoint: parseCheckpoint(record["checkpoint"], "child frame.checkpoint"),
    };
  }
  if (kind === "final") {
    return { kind, response: parseWireResponse(record["response"]) };
  }
  parse.fail('child frame.kind must be "paused", "cost", "terminal_checkpoint", or "final"');
}

export function parseWireResponse(body: unknown): WireResponse {
  const record = parse.asRecord(body, "run response");
  const ok = parse.requireBoolean(record, "ok", "run response");
  if (ok) {
    return { ok: true, result: parseRunReportResult(record["result"], parse, "run response.result") };
  }
  const error = parse.asRecord(record["error"], "run response.error");
  const sessionId = error["sessionId"];
  if (sessionId !== null && typeof sessionId !== "string") {
    parse.fail("run response.error.sessionId must be a string or null");
  }
  const usage = parse.optionalWith(error, "usage", parseTokenUsage, "run response.error");
  return {
    ok: false,
    error: {
      message: parse.requireString(error, "message", "run response.error"),
      subtype: parse.requireString(error, "subtype", "run response.error"),
      estimatedCostUsd: parse.requireNumber(error, "estimatedCostUsd", "run response.error"),
      sessionId,
      ...(usage === undefined ? {} : { usage }),
    },
  };
}

export function toWireCheckpoint(bundle: CheckpointBundle): WireCheckpoint {
  return { sha256: bundle.sha256, bytesBase64: Buffer.from(bundle.bytes).toString("base64") };
}

export function toWireCheckpointDescriptor(bundle: CheckpointBundle): WireCheckpointDescriptor {
  return { sha256: bundle.sha256, sizeBytes: bundle.bytes.byteLength };
}

export function fromWireCheckpoint(checkpoint: WireCheckpoint): CheckpointBundle {
  const bytes = Buffer.from(checkpoint.bytesBase64, "base64");
  if (sha256Hex(bytes) !== checkpoint.sha256) {
    throw new WireError("wire checkpoint failed its sha256 integrity check");
  }
  return { bytes, sha256: checkpoint.sha256 };
}

export function createLineSplitter(
  onLine: (line: string) => void,
  maxLineLength: number = MAX_WIRE_FRAME_LENGTH,
): (chunk: string) => void {
  let pending = "";
  // Everything before this offset is known newline-free, so a frame arriving in many chunks is scanned once, not once per chunk.
  let searchedUpTo = 0;
  const overflow = (): never => {
    pending = "";
    searchedUpTo = 0;
    throw new WireError(`wire frame exceeds the ${maxLineLength}-character cap`);
  };
  return (chunk) => {
    pending += chunk;
    while (true) {
      const newlineAt = pending.indexOf("\n", searchedUpTo);
      if (newlineAt === -1) {
        searchedUpTo = pending.length;
        if (pending.length > maxLineLength) {
          overflow();
        }
        return;
      }
      const line = pending.slice(0, newlineAt);
      pending = pending.slice(newlineAt + 1);
      searchedUpTo = 0;
      if (line.length > maxLineLength) {
        overflow();
      }
      if (line !== "") {
        onLine(line);
      }
    }
  };
}
