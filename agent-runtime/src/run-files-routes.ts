import { createReadStream } from "node:fs";
import type { ServerResponse } from "node:http";
import type { InstrumentedByteBudget } from "./byte-budget.js";
import { type CheckpointStore, terminalCheckpointKeyOf } from "./checkpoint-store.js";
import { FILES_LISTING_RESIDENT_BYTES, MAX_FILES_LISTING_BYTES } from "./config.js";
import { toError } from "./errors.js";
import { closeSizedFile } from "./fs-read.js";
import { measureHtmlInertJson } from "./html-inert-json.js";
import {
  bodyReadBudgetMs,
  CapacityError,
  createStallGuard,
  FILES_LISTING_BYTES_REASON,
  FILES_LISTING_REASON,
  NOT_FOUND_BODY,
  openResponse,
  type PhaseTimers,
  sendJson,
  sendSerializedJson,
} from "./http-transport.js";
import type { Logger } from "./logger.js";
import type { Metrics } from "./metrics.js";
import { DEFAULT_CONTENT_TYPE, type ResolvedRunFile, type RunFilesView } from "./run-files.js";
import type { RunMonitorView, RunRegistry } from "./run-registry.js";

const COMPLETED_GONE_BODY = { code: "completed" };

// The digest the store verified when it wrote the bundle, declared to the fetching client so it
// can hash what it received. The bytes stream from disk and are never hashed again here: this
// process would be hashing its own buffer, while the client's hash is the one that answers
// whether the transfer arrived intact.
export const CHECKPOINT_SHA256_HEADER = "x-checkpoint-sha256";
// Opaque bytes, not a declared encoding. The store holds whatever the run's own build wrote,
// and a bundle is self-describing — the reader sniffs the gzip magic, exactly as this repo's own
// decoder does — so a content type that promises compression would be a claim this route cannot
// keep under every checkpoint-root configuration.
const CHECKPOINT_CONTENT_TYPE = DEFAULT_CONTENT_TYPE;

export type RunFilesRoutesDeps = {
  registry: Pick<RunRegistry, "get">;
  runFiles: RunFilesView;
  checkpointStore: Pick<CheckpointStore, "open">;
  inflightBytes: InstrumentedByteBudget;
  timers: PhaseTimers;
  requestPhaseTimeoutMs: number;
  log: Logger;
  metrics: Metrics;
};

export type RunFilesRoutes = {
  list(res: ServerResponse, executionId: string): Promise<void>;
  get(res: ServerResponse, executionId: string, path: string): Promise<void>;
  checkpoint(res: ServerResponse, executionId: string): Promise<void>;
};

export function createRunFilesRoutes(deps: RunFilesRoutesDeps): RunFilesRoutes {
  const { registry, runFiles, checkpointStore, inflightBytes, timers, requestPhaseTimeoutMs, log, metrics } =
    deps;

  function servableRun(res: ServerResponse, executionId: string): RunMonitorView | undefined {
    const view = registry.get(executionId);
    if (view === undefined) {
      sendJson(res, 404, NOT_FOUND_BODY);
      return undefined;
    }
    if (view.completed) {
      sendJson(res, 410, COMPLETED_GONE_BODY);
      return undefined;
    }
    return view;
  }

  function sendFileStream(
    res: ServerResponse,
    path: string,
    resolved: ResolvedRunFile,
    extraHeaders: Record<string, string> = {},
  ): void {
    const opened = resolved.file;
    const armStallTimer = createStallGuard({
      timers,
      socket: res.socket,
      totalBudgetMs: bodyReadBudgetMs(opened.sizeBytes, requestPhaseTimeoutMs),
      phaseTimeoutMs: requestPhaseTimeoutMs,
    });
    let headWritten: boolean;
    try {
      headWritten = openResponse(res, 200, {
        "content-type": resolved.contentType,
        "content-length": String(opened.sizeBytes),
        "content-disposition": "attachment",
        ...extraHeaders,
      });
    } catch (error) {
      closeSizedFile(opened);
      throw error;
    }
    if (!headWritten || opened.sizeBytes === 0) {
      closeSizedFile(opened);
      if (headWritten) {
        res.end();
      }
      return;
    }
    // Bounded to the reported size so a trace still being appended to can't exceed the promised content-length.
    const stream = createReadStream("", {
      fd: opened.fd,
      start: 0,
      end: opened.sizeBytes - 1,
      autoClose: true,
    });
    // The response head is already out, so no 5xx can be sent — but the request still failed, and
    // an artifacts root that fails every fetch must not be invisible to the error-rate alarm.
    stream.on("error", (error) => {
      log.error("run_file_stream_failed", { path, error: toError(error) });
      metrics.count("RequestFailed", {
        dimensions: { route: "run_files", cause: "stream_failed" },
        aggregate: true,
      });
      stream.destroy();
      res.destroy();
    });
    armStallTimer();
    stream.on("data", armStallTimer);
    stream.pipe(res);
    res.once("close", () => {
      stream.destroy();
    });
  }

  return {
    async list(res, executionId) {
      const view = servableRun(res, executionId);
      if (view === undefined) {
        return;
      }
      const reservation = inflightBytes.tryReserve(FILES_LISTING_RESIDENT_BYTES, { executionId });
      if (reservation === undefined) {
        throw new CapacityError(FILES_LISTING_REASON);
      }
      const release = (): void => {
        reservation.release();
      };
      try {
        const files = await runFiles.list(executionId, view.attempt);
        const { json, renderedBytes } = measureHtmlInertJson({ files });
        if (renderedBytes > MAX_FILES_LISTING_BYTES) {
          throw new CapacityError(FILES_LISTING_BYTES_REASON);
        }
        res.once("close", release);
        sendSerializedJson(res, 200, json);
        if (res.destroyed) {
          release();
        }
      } catch (error) {
        release();
        throw error;
      }
    },
    async get(res, executionId, path) {
      const view = servableRun(res, executionId);
      if (view === undefined) {
        return;
      }
      const resolved = await runFiles.resolve(executionId, view.attempt, path);
      if (resolved === undefined) {
        sendJson(res, 404, NOT_FOUND_BODY);
        return;
      }
      sendFileStream(res, path, resolved);
    },
    // Served on its own route, never folded into the files listing: the listing is a manifest
    // agreement over the run uid's own artifact tree, and the bundle has no path in it, no
    // manifest entry, a different root and a different owner.
    async checkpoint(res, executionId) {
      const view = servableRun(res, executionId);
      if (view === undefined) {
        return;
      }
      const opened = await checkpointStore.open(terminalCheckpointKeyOf(executionId, view.attempt));
      if (opened === undefined) {
        sendJson(res, 404, NOT_FOUND_BODY);
        return;
      }
      sendFileStream(
        res,
        "checkpoint",
        { file: opened.file, contentType: CHECKPOINT_CONTENT_TYPE },
        { [CHECKPOINT_SHA256_HEADER]: opened.sha256 },
      );
    },
  };
}
