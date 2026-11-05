import { describe, expect, it } from "bun:test";
import {
  type BOUND_LOG_KEYS,
  bestEffortSink,
  createLogger,
  type ExactBoundFields,
  type ExactEventFields,
  type LOG_EVENT_KEYS,
  type LogEventFields,
  type LogEventName,
  type LogLevel,
  type LogSink,
  renderLogRecord,
} from "./logger.js";
import { RunFailedError } from "./run-report.js";

type CapturedLine = { line: string; level: LogLevel };

function captureLogger(
  now: () => number = () => 1_700_000_000_000,
  level?: LogLevel,
): {
  lines: CapturedLine[];
  logger: ReturnType<typeof createLogger>;
} {
  const lines: CapturedLine[] = [];
  const logger = createLogger({
    sink: (line, lineLevel) => lines.push({ line, level: lineLevel }),
    now,
    ...(level === undefined ? {} : { level }),
  });
  return { lines, logger };
}

function parsedRecord(captured: CapturedLine): Record<string, unknown> {
  const parsed: unknown = JSON.parse(captured.line);
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    throw new Error("log line is not a JSON object");
  }
  return parsed as Record<string, unknown>;
}

// the gate here is tsc refusing to compile, not the expect() below
type EmitAccepts<E extends LogEventName, T> = [T] extends [ExactEventFields<E, T>] ? true : false;
type BoundAccepts<T> = [T] extends [ExactBoundFields<T>] ? true : false;

const catalogueAcceptsDeclaredFields: EmitAccepts<"run_started", { executionId: string; model: string }> =
  true;
const boundAcceptsRunContext: BoundAccepts<{ executionId: string; uid: number }> = true;
const runTokenCannotRideAnEvent: EmitAccepts<"run_started", { executionId: string; runToken: string }> =
  false;
const authTokenCannotRideAnEvent: EmitAccepts<
  "listening",
  { port: number; authMode: string; version: string; revision: string; AUTH_TOKEN: string }
> = false;
const documentBytesCannotRideAnUndeclaredField: EmitAccepts<
  "run_finished",
  {
    executionId: string;
    estimatedCostUsd: number;
    sessionId: string | null;
    durationMs: number;
    documentBytes: Uint8Array;
  }
> = false;
const bytesCannotImpersonateADeclaredStringField: EmitAccepts<"run_started", { executionId: Uint8Array }> =
  false;
const promptTextCannotRideAnEvent: EmitAccepts<"run_started", { executionId: string; prompt: string }> =
  false;
const uncataloguedEventsDoNotExist: "made_up_event" extends LogEventName ? true : false = false;
const boundBagRefusesARunToken: BoundAccepts<{ executionId: string; runToken: string }> = false;
const boundBagRefusesDocumentBytes: BoundAccepts<{ executionId: string; documentBytes: Uint8Array }> = false;

// the direction `satisfies` can't check: a declared key the projection list forgot
type CatalogueKeysMissingFromProjection = {
  [E in LogEventName]: Exclude<Extract<keyof LogEventFields[E], string>, (typeof LOG_EVENT_KEYS)[E][number]>;
}[LogEventName];
const projectionCoversEveryCatalogueKey: [CatalogueKeysMissingFromProjection] extends [never] ? true : false =
  true;
const boundProjectionCoversEveryBoundKey: [
  Exclude<"executionId" | "uid", (typeof BOUND_LOG_KEYS)[number]>,
] extends [never]
  ? true
  : false = true;

describe("log catalogue (compile-time)", () => {
  it("closes both the emit fields and the with() bound bag against undeclared keys", () => {
    expect(catalogueAcceptsDeclaredFields).toBe(true);
    expect(boundAcceptsRunContext).toBe(true);
    expect(runTokenCannotRideAnEvent).toBe(false);
    expect(authTokenCannotRideAnEvent).toBe(false);
    expect(documentBytesCannotRideAnUndeclaredField).toBe(false);
    expect(bytesCannotImpersonateADeclaredStringField).toBe(false);
    expect(promptTextCannotRideAnEvent).toBe(false);
    expect(uncataloguedEventsDoNotExist).toBe(false);
    expect(boundBagRefusesARunToken).toBe(false);
    expect(boundBagRefusesDocumentBytes).toBe(false);
  });

  it("keeps the projection key lists and the type catalogue pinned to each other", () => {
    expect(projectionCoversEveryCatalogueKey).toBe(true);
    expect(boundProjectionCoversEveryBoundKey).toBe(true);
  });
});

describe("runtime key projection (structural aliasing)", () => {
  const SECRET = "SECRET-TOKEN-VALUE";

  it("drops undeclared keys riding a structurally narrowed alias on emit", () => {
    const { lines, logger } = captureLogger();
    const wide = { executionId: "run-1", runToken: SECRET };
    const narrow: { executionId: string } = wide;
    logger.info("run_started", narrow);
    const first = lines[0];
    if (first === undefined) {
      throw new Error("no line captured");
    }
    expect(first.line).not.toContain(SECRET);
    const record = parsedRecord(first);
    expect(record["executionId"]).toBe("run-1");
    expect(record["runToken"]).toBeUndefined();
    expect(record["droppedLogKeys"]).toBe(1);
  });

  it("drops undeclared keys riding a structurally narrowed alias into with(), on every later line", () => {
    const { lines, logger } = captureLogger();
    const wide = { executionId: "run-1", runToken: SECRET };
    const narrowBound: { executionId: string } = wide;
    const bound = logger.with(narrowBound);
    bound.info("run_completed", { executionId: "run-1" });
    bound.info("run_resumed", { executionId: "run-1" });
    expect(lines).toHaveLength(2);
    for (const captured of lines) {
      expect(captured.line).not.toContain(SECRET);
      expect(parsedRecord(captured)["runToken"]).toBeUndefined();
      expect(parsedRecord(captured)["droppedLogKeys"]).toBe(1);
    }
  });

  it("drops undeclared keys admitted by a union annotation (keyof a union is the key intersection)", () => {
    const { lines, logger } = captureLogger();
    const unionFields = (): { executionId: string } | { executionId: string; runToken: string } => ({
      executionId: "run-1",
      runToken: SECRET,
    });
    logger.info("run_started", unionFields());
    const first = lines[0];
    if (first === undefined) {
      throw new Error("no line captured");
    }
    expect(first.line).not.toContain(SECRET);
    expect(parsedRecord(first)["runToken"]).toBeUndefined();
  });

  it("drops undeclared keys admitted by explicitly pinned generic type arguments", () => {
    const { lines, logger } = captureLogger();
    logger.info<"run_started", { executionId: string } | { executionId: string; runToken: string }>(
      "run_started",
      { executionId: "run-1", runToken: SECRET },
    );
    const first = lines[0];
    if (first === undefined) {
      throw new Error("no line captured");
    }
    expect(first.line).not.toContain(SECRET);
    expect(parsedRecord(first)["runToken"]).toBeUndefined();
  });

  it("emits no droppedLogKeys field when every key is declared", () => {
    const { lines, logger } = captureLogger();
    logger.info("run_started", { executionId: "run-1", model: "m" });
    const first = lines[0];
    if (first === undefined) {
      throw new Error("no line captured");
    }
    expect(parsedRecord(first)["droppedLogKeys"]).toBeUndefined();
  });
});

describe("createLogger", () => {
  it("emits one JSON object per event with ts, level, event, and fields", () => {
    const { lines, logger } = captureLogger();
    logger.info("run_started", { executionId: "run-1", model: "m" });
    expect(lines).toHaveLength(1);
    const first = lines[0];
    if (first === undefined) {
      throw new Error("no line captured");
    }
    expect(first.level).toBe("info");
    const record = parsedRecord(first);
    expect(record["ts"]).toBe(new Date(1_700_000_000_000).toISOString());
    expect(record["level"]).toBe("info");
    expect(record["event"]).toBe("run_started");
    expect(record["executionId"]).toBe("run-1");
    expect(record["model"]).toBe("m");
  });

  it("routes each level to the sink with its level tag", () => {
    const { lines, logger } = captureLogger(undefined, "debug");
    logger.debug("run_polled", { executionId: "run-1", status: "running" });
    logger.info("run_started", { executionId: "run-1" });
    logger.warn("drain", { failedRuns: 0 });
    logger.error("request_failed", { route: "run_monitor", error: new Error("boom") });
    expect(lines.map((entry) => entry.level)).toEqual(["debug", "info", "warn", "error"]);
  });

  it("suppresses debug lines at the default info level", () => {
    const { lines, logger } = captureLogger();
    logger.debug("run_polled", { executionId: "run-1", status: "running" });
    logger.info("run_started", { executionId: "run-1" });
    expect(lines).toHaveLength(1);
    const first = lines[0];
    if (first === undefined) {
      throw new Error("no line captured");
    }
    expect(parsedRecord(first)["event"]).toBe("run_started");
  });

  it("suppresses info below a warn threshold but keeps warn and error", () => {
    const { lines, logger } = captureLogger(undefined, "warn");
    logger.info("run_started", { executionId: "run-1" });
    logger.warn("drain", { failedRuns: 2 });
    logger.error("request_failed", { route: "run_monitor", error: new Error("boom") });
    expect(lines.map((entry) => entry.level)).toEqual(["warn", "error"]);
  });

  it("serializes Error field values as message and stack on one line", () => {
    const { lines, logger } = captureLogger();
    logger.error("run_failed", {
      executionId: "run-2",
      subtype: "internal_error",
      estimatedCostUsd: 0,
      error: new Error("boom"),
    });
    const first = lines[0];
    if (first === undefined) {
      throw new Error("no line captured");
    }
    expect(first.line).not.toContain("\n");
    const error = parsedRecord(first)["error"];
    expect(error).toMatchObject({ message: "boom" });
    if (typeof error !== "object" || error === null) {
      throw new Error("error field is not an object");
    }
    expect(typeof (error as Record<string, unknown>)["stack"]).toBe("string");
  });

  it("carries a failure subtype on serialized errors that have one", () => {
    const { lines, logger } = captureLogger();
    const failure = new RunFailedError("over budget", {
      subtype: "deadline_exceeded",
      estimatedCostUsd: 1.2,
      sessionId: null,
    });
    logger.error("run_failed", {
      executionId: "run-2",
      subtype: "deadline_exceeded",
      estimatedCostUsd: 1.2,
      error: failure,
    });
    const first = lines[0];
    if (first === undefined) {
      throw new Error("no line captured");
    }
    expect(parsedRecord(first)["error"]).toMatchObject({
      message: "over budget",
      subtype: "deadline_exceeded",
    });
  });

  it("binds fields with `with` and lets per-event fields override them", () => {
    const { lines, logger } = captureLogger();
    const bound = logger.with({ executionId: "run-3", uid: 20001 });
    bound.info("run_cancel_requested", { executionId: "run-3" });
    bound.error("run_signal_failed", { uid: 20002, signal: "TERM" });
    const [first, second] = lines;
    if (first === undefined || second === undefined) {
      throw new Error("expected two lines");
    }
    expect(parsedRecord(first)).toMatchObject({ executionId: "run-3", uid: 20001 });
    expect(parsedRecord(second)).toMatchObject({ executionId: "run-3", uid: 20002 });
  });
});

describe("renderLogRecord", () => {
  it("still renders a parseable line when fields cannot be serialized", () => {
    const circular: Record<string, unknown> = {};
    circular["self"] = circular;
    const line = renderLogRecord({
      ts: "2023-11-14T22:13:20.000Z",
      level: "info",
      event: "run_started",
      circular,
    });
    const parsed: unknown = JSON.parse(line);
    expect(parsed).toMatchObject({
      ts: "2023-11-14T22:13:20.000Z",
      level: "info",
      event: "run_started",
      logError: "fields were not serializable and were dropped",
    });
  });

  it("serializes bigint values instead of throwing", () => {
    const line = renderLogRecord({
      ts: "2023-11-14T22:13:20.000Z",
      level: "info",
      event: "run_started",
      bytes: 42n,
    });
    const parsed: unknown = JSON.parse(line);
    expect(parsed).toMatchObject({ bytes: "42" });
  });
});

describe("bestEffortSink", () => {
  // A child's stdout pipe can die under a living parent (ADR 0129); a process whose log line
  // cannot be written must lose the line, never itself.
  it("swallows a sink that throws and keeps delivering to one that works", () => {
    let attempts = 0;
    const flaky: LogSink = () => {
      attempts += 1;
      throw Object.assign(new Error("EPIPE: broken pipe, write"), { code: "EPIPE" });
    };
    const log = createLogger({ sink: bestEffortSink(flaky) });
    expect(() =>
      log.info("listening", { port: 1, authMode: "token", provider: "gateway", version: "v", revision: "r" }),
    ).not.toThrow();
    expect(attempts).toBe(1);
    const lines: string[] = [];
    const working = createLogger({ sink: bestEffortSink((line) => lines.push(line)) });
    working.info("drain", { failedRuns: 0 });
    expect(lines).toHaveLength(1);
  });
});
