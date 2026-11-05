import { join } from "node:path";
import { closeSizedFile, openSizedFileOrUndefined, readSizedRange } from "./fs-read.js";
import { isRecord } from "./narrow.js";
import { isSafePathSegment } from "./safe-path.js";
import { PROJECTS_DIR_NAME } from "./subagent-usage.js";
import { type ModelUsageTotals, modelUsageTotalsOf, NO_MODEL_USAGE } from "./usage-cost.js";

// The CLI (since 2.1.281) appends a `cost-state` line to the session transcript and, on resume,
// restores the last one for the session into its cost ledger, so a resumed query's
// total_cost_usd also carries every earlier segment's spend (measured). Its budget fuse already
// subtracts the restored figure; its reported cost does not, so the runtime does.
export const COST_STATE_LINE_TYPE = "cost-state";
const TRANSCRIPT_EXTENSION = ".jsonl";

// The record is appended at the end of each segment, so the tail holds the last one; a record
// further back than this reads as zero, which overcounts rather than undercounts.
export const TRANSCRIPT_TAIL_BYTES = 1024 * 1024;

// The CLI names a session's project dir after its cwd with every non-alphanumeric character
// replaced, and past this length truncates the name and appends a hash of its own (measured in
// the 2.1.281 binary). Run cwds are short workspace paths; a longer one is not reimplemented.
const CLI_PROJECT_DIR_NAME_MAX_CHARS = 200;
const NON_ALPHANUMERIC = /[^a-zA-Z0-9]/g;

export type RestoredSessionCostReader = (sessionId: string) => number;

export type RestoredSessionCostReaderInput = {
  configDir: string;
  cwd: string;
  tailBytes?: number;
};

function costStateOfLine(line: string, sessionId: string): Record<string, unknown> | undefined {
  let parsed: unknown;
  try {
    parsed = JSON.parse(line);
  } catch {
    return undefined;
  }
  if (!isRecord(parsed) || parsed["type"] !== COST_STATE_LINE_TYPE || parsed["sessionId"] !== sessionId) {
    return undefined;
  }
  return parsed;
}

function costOfLine(line: string, sessionId: string): number | undefined {
  const parsed = costStateOfLine(line, sessionId);
  if (parsed === undefined) {
    return undefined;
  }
  const totalCostUsd = parsed["totalCostUSD"];
  if (typeof totalCostUsd !== "number" || !Number.isFinite(totalCostUsd) || totalCostUsd < 0) {
    return undefined;
  }
  return totalCostUsd;
}

export function restoredSessionCostUsdOf(transcriptText: string, sessionId: string): number {
  let restored = 0;
  for (const line of transcriptText.split("\n")) {
    if (line === "") {
      continue;
    }
    restored = costOfLine(line, sessionId) ?? restored;
  }
  return restored;
}

// The same record carries the session's per-model token totals, which a resumed segment's result
// also restores into its own; reading none overcounts the resumed segment rather than undercounts.
export function restoredSessionModelUsageOf(transcriptText: string, sessionId: string): ModelUsageTotals {
  let restored: ModelUsageTotals = NO_MODEL_USAGE;
  for (const line of transcriptText.split("\n")) {
    if (line === "") {
      continue;
    }
    const parsed = costStateOfLine(line, sessionId);
    restored = (parsed === undefined ? undefined : modelUsageTotalsOf(parsed["modelUsage"])) ?? restored;
  }
  return restored;
}

export type RestoredSessionModelUsageReader = (sessionId: string) => ModelUsageTotals;

function cliProjectDirNameOf(cwd: string): string | undefined {
  const name = cwd.replace(NON_ALPHANUMERIC, "-");
  return name.length <= CLI_PROJECT_DIR_NAME_MAX_CHARS ? name : undefined;
}

function transcriptTailOf(path: string, tailBytes: number): string | undefined {
  const opened = openSizedFileOrUndefined(path);
  if (opened === undefined) {
    return undefined;
  }
  try {
    const offset = Math.max(0, opened.sizeBytes - tailBytes);
    const text = readSizedRange(opened.fd, offset, opened.sizeBytes - offset).toString("utf8");
    if (offset === 0) {
      return text;
    }
    const firstLineEnd = text.indexOf("\n");
    return firstLineEnd === -1 ? "" : text.slice(firstLineEnd + 1);
  } finally {
    closeSizedFile(opened);
  }
}

function createTranscriptTailReader<T>(
  input: RestoredSessionCostReaderInput,
  read: (tail: string, sessionId: string) => T,
  unreadable: T,
): (sessionId: string) => T {
  const tailBytes = input.tailBytes ?? TRANSCRIPT_TAIL_BYTES;
  return (sessionId) => {
    const projectDirName = cliProjectDirNameOf(input.cwd);
    if (!isSafePathSegment(sessionId) || projectDirName === undefined) {
      return unreadable;
    }
    const transcriptPath = join(
      input.configDir,
      PROJECTS_DIR_NAME,
      projectDirName,
      `${sessionId}${TRANSCRIPT_EXTENSION}`,
    );
    try {
      const tail = transcriptTailOf(transcriptPath, tailBytes);
      return tail === undefined ? unreadable : read(tail, sessionId);
    } catch {
      return unreadable;
    }
  };
}

// Fail-soft as a whole: a restored figure that cannot be read is zero, so the segment reports
// the SDK's figure unadjusted instead of failing the run before its query().
export function createRestoredSessionCostReader(
  input: RestoredSessionCostReaderInput,
): RestoredSessionCostReader {
  return createTranscriptTailReader(input, restoredSessionCostUsdOf, 0);
}

export function createRestoredSessionModelUsageReader(
  input: RestoredSessionCostReaderInput,
): RestoredSessionModelUsageReader {
  return createTranscriptTailReader(input, restoredSessionModelUsageOf, NO_MODEL_USAGE);
}
