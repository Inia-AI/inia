// The HTTP half both loopback model processes share — the gateway shim (ADR 0137) and the Anthropic
// key holder (ADR 0151): the routes the run child's CLI calls, the Bun-hardened write guards, and
// one server bootstrap.
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { messageOf, toError } from "./errors.js";
import type { Logger } from "./logger.js";
import { EXECUTION_ID_HEADER } from "./model-provider.js";
import { EXECUTION_ID_PATTERN } from "./safe-path.js";

export const MESSAGES_PATH = "/v1/messages";
export const COUNT_TOKENS_PATH = "/v1/messages/count_tokens";
export const HEALTH_PATH = "/healthz";
// The Messages API's own request ceiling (32 MB); neither loopback process is in the memory
// pairing (ADR 0137).
export const MESSAGES_API_MAX_REQUEST_BYTES = 32 * 1024 * 1024;
export const REQUEST_ID_HEADER = "request-id";
export const JSON_CONTENT_TYPE = "application/json";
export const HTTP_OK = 200;
export const HTTP_NOT_FOUND = 404;
export const HTTP_PAYLOAD_TOO_LARGE = 413;
export const HTTP_BAD_GATEWAY = 502;

export function headerMapOf(message: IncomingMessage): Record<string, string> {
  const headers: Record<string, string> = {};
  for (const [name, value] of Object.entries(message.headers)) {
    if (typeof value === "string") {
      headers[name] = value;
    } else if (Array.isArray(value)) {
      headers[name] = value.join(", ");
    }
  }
  return headers;
}

export function collectBounded(message: IncomingMessage, maxBytes: number): Promise<Buffer | undefined> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    let total = 0;
    let over = false;
    message.on("data", (chunk: Buffer) => {
      if (over) {
        return;
      }
      total += chunk.length;
      if (total > maxBytes) {
        over = true;
        chunks.length = 0;
        return;
      }
      chunks.push(chunk);
    });
    message.on("end", () => resolve(over ? undefined : Buffer.concat(chunks)));
    message.on("error", reject);
  });
}

export function sseEvent(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

export function anthropicErrorBody(type: string, message: string): string {
  return JSON.stringify({ type: "error", error: { type, message } });
}

export function executionIdHeaderOf(req: IncomingMessage): string | undefined {
  const raw = req.headers[EXECUTION_ID_HEADER];
  const value = Array.isArray(raw) ? raw[0] : raw;
  return value !== undefined && EXECUTION_ID_PATTERN.test(value) ? value : undefined;
}

export function requestLogOf(log: Logger, req: IncomingMessage): Logger {
  const executionId = executionIdHeaderOf(req);
  return executionId === undefined ? log : log.with({ executionId });
}

// The one door every byte to the client goes through. Measured under Bun (ADR 0137): a client that
// left emits no `error` anywhere — the write after the departure flips `destroyed`, emits `close`,
// and hands later writes' failures to their callbacks — so the guard keys on `destroyed`, and the
// try/catch is for the Bun that throws instead. Nothing here may reach the process handlers: a
// loopback process's crash exit drains every run in the container.
export function writeIfAlive(res: ServerResponse, write: () => void): boolean {
  if (res.destroyed) {
    return false;
  }
  try {
    write();
    return true;
  } catch {
    res.destroy();
    return false;
  }
}

export function endIfAlive(res: ServerResponse): void {
  writeIfAlive(res, () => res.end());
}

export function sendJsonError(res: ServerResponse, status: number, type: string, message: string): void {
  writeIfAlive(res, () => {
    res.writeHead(status, { "content-type": JSON_CONTENT_TYPE });
    res.end(anthropicErrorBody(type, message));
  });
}

export function sendHealthy(res: ServerResponse): void {
  writeIfAlive(res, () => {
    res.writeHead(HTTP_OK, { "content-type": JSON_CONTENT_TYPE });
    res.end(JSON.stringify({ ok: true }));
  });
}

// `close` before the response ended is the client leaving; after `end` it is the normal teardown.
export function onClientDeparted(res: ServerResponse, departed: () => void): void {
  res.on("close", () => {
    if (!res.writableEnded) {
      departed();
    }
  });
}

function swallowStreamErrors(res: ServerResponse): void {
  res.on("error", () => undefined);
  res.socket?.on("error", () => undefined);
}

export type LoopbackServer = { url: string; port: number; close(): Promise<void> };

type Route = (req: IncomingMessage, res: ServerResponse) => Promise<void>;

// A route that throws before it returns its promise — `new URL` on a malformed target, a header
// value the client cannot send — must answer like one that rejects: a throw that escapes the request
// handler reaches the process handlers, and a loopback process's crash exit drains every run.
function invokeRoute(route: Route, req: IncomingMessage, res: ServerResponse): Promise<void> {
  try {
    return route(req, res);
  } catch (error) {
    return Promise.reject(error);
  }
}

export type LoopbackServerEvents = {
  listening: "anthropic_proxy_listening";
  failed: "anthropic_proxy_upstream_failed";
};

export function listenLoopbackServer(input: {
  host: string;
  port: number;
  log: Logger;
  label: string;
  events: LoopbackServerEvents;
  route: Route;
}): Promise<LoopbackServer> {
  const { log, events } = input;
  const server: Server = createServer((req, res) => {
    swallowStreamErrors(res);
    invokeRoute(input.route, req, res).catch((error: unknown) => {
      log.error(events.failed, { error: toError(error) });
      if (!res.headersSent) {
        sendJsonError(res, HTTP_BAD_GATEWAY, "api_error", `${input.label} failure: ${messageOf(error)}`);
      } else {
        endIfAlive(res);
      }
    });
  });
  // A malformed request line or a parse error on a socket: the stranger's socket goes, the
  // process stays. Node's default would write a 400 first; nothing the run child sends is malformed.
  server.on("clientError", (_error, socket) => {
    socket.destroy();
  });
  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(input.port, input.host, () => {
      const address = server.address();
      if (address === null || typeof address === "string") {
        reject(new Error(`${input.label} bound no TCP port`));
        return;
      }
      log.info(events.listening, { port: address.port });
      resolve({
        url: `http://${input.host}:${address.port}`,
        port: address.port,
        close: () =>
          new Promise<void>((resolveClose, rejectClose) => {
            server.close((error) => (error === undefined ? resolveClose() : rejectClose(error)));
            server.closeAllConnections();
          }),
      });
    });
  });
}
