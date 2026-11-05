import { createHash, timingSafeEqual } from "node:crypto";
import type { IncomingMessage, ServerResponse } from "node:http";
import type { Socket } from "node:net";
import { yieldToEventLoop } from "./concurrency.js";
import { MS_PER_SECOND, UNDECLARED_BODY_PROVISIONAL_BYTES } from "./config.js";
import { hasErrorCode, toError } from "./errors.js";
import { renderHtmlInertJson } from "./html-inert-json.js";
import { isRecord } from "./narrow.js";
import { EXECUTION_ID_PATTERN } from "./safe-path.js";

export class BodyLimitError extends Error {}
export class UnsupportedMediaTypeError extends Error {}
// Closed, and typed onto every refusal site: a new saturation signal cannot reach the wire
// without joining this list, which is what the published wire contract enumerates.
export const CAPACITY_REASONS = [
  "admission_shed",
  "chat_disabled",
  "files_listing",
  "files_listing_bytes",
  "retained_input_budget",
  "retained_output_budget",
  "upload_budget",
] as const;

export type CapacityReason = (typeof CAPACITY_REASONS)[number];

export class CapacityError extends Error {
  readonly reason: CapacityReason;
  constructor(reason: CapacityReason) {
    super(`no capacity: ${reason}`);
    this.reason = reason;
  }
}

export const ADMISSION_SHED_REASON = "admission_shed";
export const CHAT_DISABLED_REASON = "chat_disabled";
export const UPLOAD_BUDGET_REASON = "upload_budget";
export const FILES_LISTING_REASON = "files_listing";
export const FILES_LISTING_BYTES_REASON = "files_listing_bytes";
export const INFLIGHT_BUDGET_REASONS: ReadonlySet<CapacityReason> = new Set([
  UPLOAD_BUDGET_REASON,
  FILES_LISTING_REASON,
  FILES_LISTING_BYTES_REASON,
]);
export type HandlerDeadlineContext = { executionId?: string; path?: string };

export class HandlerDeadlineError extends Error {
  readonly fields: HandlerDeadlineContext = {};
}

export const HANDLER_PROCESSING_BUDGET_MS = 60_000;

export function withHandlerDeadline<T>(
  run: (signal: AbortSignal) => Promise<T>,
  deadlineMs: number,
): Promise<T> {
  const cancellation = new AbortController();
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      cancellation.abort();
      reject(new HandlerDeadlineError(`handler exceeded its ${deadlineMs}ms processing deadline`));
    }, deadlineMs);
    run(cancellation.signal).then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      (error: unknown) => {
        clearTimeout(timer);
        reject(toError(error));
      },
    );
  });
}

// Logging every rejection would let an unauthenticated flood set the log volume; this window caps it.
export const AUTH_REJECTION_LOG_WINDOW_MS = 10_000;

export function createWindowedCounter(
  windowMs: number,
  now: () => number,
): { take(): { suppressed: number } | undefined } {
  let windowStartedAt = Number.NEGATIVE_INFINITY;
  let suppressed = 0;
  return {
    take(): { suppressed: number } | undefined {
      const at = now();
      if (at - windowStartedAt >= windowMs) {
        windowStartedAt = at;
        const emitted = { suppressed };
        suppressed = 0;
        return emitted;
      }
      suppressed += 1;
      return undefined;
    },
  };
}

// Must be spread last in openResponse's writeHead call so no caller-supplied header can override it.
const SNIFFING_DEFENCE_HEADERS: Record<string, string> = { "x-content-type-options": "nosniff" };

// RFC 9110: a 401 MUST carry a WWW-Authenticate challenge naming the scheme.
export const BEARER_CHALLENGE_HEADERS: Record<string, string> = { "www-authenticate": "Bearer" };

export const NOT_FOUND_BODY = { error: "not found" };
export const INTERNAL_ERROR_BODY = { error: "internal error" };

const WRITE_HEAD_VALIDATION_ERROR_CODES = [
  "ERR_HTTP_INVALID_HEADER_VALUE",
  "ERR_INVALID_CHAR",
  "ERR_INVALID_HTTP_TOKEN",
  "ERR_HTTP_INVALID_STATUS_CODE",
] as const;

// Built with fromEntries, not per-key assignment, so a "__proto__" header name lands as an own
// property instead of silently rewriting the object's prototype.
function stringValuedHeaders(headers: Record<string, string | undefined>): Record<string, string> {
  return Object.fromEntries(
    Object.entries(headers).filter((entry): entry is [string, string] => typeof entry[1] === "string"),
  );
}

export function openResponse(
  res: ServerResponse,
  status: number,
  headers: Record<string, string | undefined>,
): boolean {
  if (res.writableEnded) {
    return false;
  }
  try {
    res.writeHead(status, {
      connection: "close",
      ...stringValuedHeaders(headers),
      ...SNIFFING_DEFENCE_HEADERS,
    });
    return true;
  } catch (error) {
    // A malformed header or status is a code bug the request funnel must log and answer, rethrown
    // before the head is sent so the caller can still respond. Stream-state failures stay
    // swallowed: that client is simply gone, and destroying its socket is the correct quiet teardown.
    if (hasErrorCode(error, ...WRITE_HEAD_VALIDATION_ERROR_CODES)) {
      throw error;
    }
    res.destroy();
    return false;
  }
}

export function sendJson(
  res: ServerResponse,
  status: number,
  body: unknown,
  extraHeaders: Record<string, string> = {},
): void {
  sendSerializedJson(res, status, JSON.stringify(body), extraHeaders);
}

// Takes JSON serialized but not yet escaped, so a caller can measure the response size without a
// second serialization; escaping happens only here, the sole path to the socket.
export function sendSerializedJson(
  res: ServerResponse,
  status: number,
  json: string,
  extraHeaders: Record<string, string> = {},
): void {
  const body = renderHtmlInertJson(json);
  const headers = {
    "content-type": "application/json",
    ...extraHeaders,
    // Declared here, in bytes: Bun sends a res.end(body) body chunked, and the wire contract
    // records a content-length on every JSON response.
    "content-length": String(Buffer.byteLength(body)),
  };
  if (!openResponse(res, status, headers)) {
    return;
  }
  try {
    res.end(body);
  } catch {
    res.destroy();
  }
}

const MIN_UPLOAD_BYTES_PER_SECOND = 1024 * 1024;

export function bodyReadBudgetMs(maxBodyBytes: number, baseMs: number): number {
  return baseMs + Math.ceil((maxBodyBytes / MIN_UPLOAD_BYTES_PER_SECOND) * MS_PER_SECOND);
}

export function decodePathSegments(segments: string[]): string | undefined {
  try {
    return segments.map((segment) => decodeURIComponent(segment)).join("/");
  } catch {
    return undefined;
  }
}

export function bearerTokenMatches(header: string | undefined, expected: string): boolean {
  if (header === undefined || !header.startsWith("Bearer ")) {
    return false;
  }
  const presented = header.slice("Bearer ".length);
  const presentedDigest = createHash("sha256").update(presented).digest();
  const expectedDigest = createHash("sha256").update(expected).digest();
  return timingSafeEqual(presentedDigest, expectedDigest);
}

export function socketAbandonment(socket: Socket | null): {
  signal: AbortSignal;
  unsubscribe: () => void;
} {
  const abandonment = new AbortController();
  if (socket === null || socket.destroyed) {
    abandonment.abort();
    return { signal: abandonment.signal, unsubscribe: () => {} };
  }
  const onClose = (): void => abandonment.abort();
  socket.once("close", onClose);
  return { signal: abandonment.signal, unsubscribe: () => socket.off("close", onClose) };
}

export function declaredContentLength(req: IncomingMessage): number | undefined {
  const declaredBytes = Number(req.headers["content-length"]);
  if (!Number.isInteger(declaredBytes) || declaredBytes < 0) {
    return undefined;
  }
  return declaredBytes;
}

export function uploadAdmissionChargeOf(declaredBytes: number | undefined, maxBodyBytes: number): number {
  return declaredBytes ?? Math.min(UNDECLARED_BODY_PROVISIONAL_BYTES, maxBodyBytes);
}

// Log-context only: the pattern check keeps an unbounded caller-chosen string out of a log line.
export function executionIdOf(body: unknown): string | undefined {
  if (!isRecord(body)) {
    return undefined;
  }
  const value = body["executionId"];
  return typeof value === "string" && EXECUTION_ID_PATTERN.test(value) ? value : undefined;
}

export type ReadJsonBodyOptions = {
  limitBytes: number;
  declaredBytes?: number;
  onProgress?: () => void;
  tryChargeTo?: (totalBytes: number) => boolean;
  onSized?: (totalBytes: number) => void;
};

export async function readJsonBody(req: IncomingMessage, options: ReadJsonBodyOptions): Promise<unknown> {
  const { limitBytes, declaredBytes, onProgress, tryChargeTo, onSized } = options;
  const contentType = req.headers["content-type"];
  if (contentType === undefined || !contentType.includes("application/json")) {
    throw new UnsupportedMediaTypeError("content-type must be application/json");
  }
  if (declaredBytes !== undefined && declaredBytes > limitBytes) {
    throw new BodyLimitError(`request body exceeds ${limitBytes} bytes`);
  }
  const chunks: Buffer[] = [];
  let totalBytes = 0;
  let overLimit = false;
  let overBudget = false;
  for await (const chunk of req) {
    onProgress?.();
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    totalBytes += buffer.length;
    if (overLimit || overBudget) {
      continue;
    }
    if (totalBytes > limitBytes) {
      overLimit = true;
      chunks.length = 0;
      continue;
    }
    if (tryChargeTo !== undefined && !tryChargeTo(totalBytes)) {
      overBudget = true;
      chunks.length = 0;
      continue;
    }
    chunks.push(buffer);
  }
  if (overLimit) {
    throw new BodyLimitError(`request body exceeds ${limitBytes} bytes`);
  }
  if (overBudget) {
    throw new CapacityError(UPLOAD_BUDGET_REASON);
  }
  onSized?.(totalBytes);
  return decodeJsonBody(chunks);
}

export const BODY_UTF8_DECODE_CHUNK_BYTES = 2 * 1024 * 1024;

// ignoreBOM: TextDecoder strips a BOM by default, which Buffer#toString does not — without it a
// BOM-prefixed body would decode differently here than the JSON.parse it must still fail against.
export async function decodeJsonBody(chunks: Buffer[]): Promise<unknown> {
  const decoder = new TextDecoder("utf-8", { ignoreBOM: true });
  chunks.reverse();
  let text = "";
  let bytesSinceYield = 0;
  for (let chunk = chunks.pop(); chunk !== undefined; chunk = chunks.pop()) {
    text += decoder.decode(chunk, { stream: true });
    bytesSinceYield += chunk.length;
    if (bytesSinceYield >= BODY_UTF8_DECODE_CHUNK_BYTES) {
      bytesSinceYield = 0;
      await yieldToEventLoop();
    }
  }
  text += decoder.decode();
  resolveAccumulatedString(text);
  await yieldToEventLoop();
  return JSON.parse(text);
}

// Forces JSC to flatten the accumulated rope here rather than inside JSON.parse's synchronous
// block; the impossible-branch throw is what keeps this read from being optimized away.
function resolveAccumulatedString(value: string): void {
  if (value.charCodeAt(value.length - 1) < 0) {
    throw new Error("unreachable: charCodeAt is never negative");
  }
}

export type PhaseTimers = {
  arm(socket: Socket, timeoutMs: number): void;
  clear(socket: Socket): void;
};

export function createPhaseTimers(): PhaseTimers {
  const timers = new WeakMap<Socket, ReturnType<typeof setTimeout>>();
  function clear(socket: Socket): void {
    const timer = timers.get(socket);
    if (timer !== undefined) {
      clearTimeout(timer);
      timers.delete(socket);
    }
  }
  function arm(socket: Socket, timeoutMs: number): void {
    clear(socket);
    const timer = setTimeout(() => {
      socket.destroy();
    }, timeoutMs);
    timers.set(socket, timer);
  }
  return { arm, clear };
}

export function createStallGuard(input: {
  timers: PhaseTimers;
  socket: Socket | null;
  totalBudgetMs: number;
  phaseTimeoutMs: number;
}): () => void {
  const { timers, socket, phaseTimeoutMs } = input;
  const deadlineAt = Date.now() + input.totalBudgetMs;
  return () => {
    if (socket === null) {
      return;
    }
    const remainingMs = Math.min(phaseTimeoutMs, deadlineAt - Date.now());
    if (remainingMs <= 0) {
      socket.destroy();
      return;
    }
    timers.arm(socket, remainingMs);
  };
}
