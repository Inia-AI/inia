// The anthropic mode's key holder (ADR 0151): a loopback process that holds the plain API key and
// forwards the run child's two model routes to the Anthropic API with it. The child holds only a
// placeholder, so a sandbox or read-gate slip in a run finds no key to take.
import {
  type ClientRequest,
  request as httpRequest,
  type IncomingHttpHeaders,
  type IncomingMessage,
  type OutgoingHttpHeaders,
  type ServerResponse,
} from "node:http";
import { request as httpsRequest } from "node:https";
import { messageOf } from "./errors.js";
import { declaredContentLength } from "./http-transport.js";
import { createLogger, type Logger } from "./logger.js";
import {
  COUNT_TOKENS_PATH,
  endIfAlive,
  HEALTH_PATH,
  HTTP_BAD_GATEWAY,
  HTTP_NOT_FOUND,
  HTTP_PAYLOAD_TOO_LARGE,
  type LoopbackServer,
  listenLoopbackServer,
  MESSAGES_API_MAX_REQUEST_BYTES,
  MESSAGES_PATH,
  onClientDeparted,
  REQUEST_ID_HEADER,
  requestLogOf,
  sendHealthy,
  sendJsonError,
  writeIfAlive,
} from "./loopback-http.js";
import { EXECUTION_ID_HEADER } from "./model-provider.js";

const FORWARDED_PATHS: readonly string[] = [MESSAGES_PATH, COUNT_TOKENS_PATH];
const API_KEY_HEADER = "x-api-key";

// Hop-by-hop headers belong to one connection, never the next. Beyond them the proxy drops every
// credential the child could send (the placeholder, a bearer token), the run's execution id (it
// binds the proxy's log lines, and Anthropic has no use for it), and the length and encoding it
// sets itself: identity, so the bytes it forwards are the bytes upstream sent.
const DROPPED_REQUEST_HEADERS: readonly string[] = [
  "connection",
  "keep-alive",
  "proxy-authorization",
  "proxy-connection",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
  "host",
  "content-length",
  "accept-encoding",
  API_KEY_HEADER,
  "authorization",
  EXECUTION_ID_HEADER,
];

const DROPPED_RESPONSE_HEADERS: readonly string[] = [
  "connection",
  "keep-alive",
  "proxy-authenticate",
  "trailer",
  "transfer-encoding",
  "upgrade",
];

export type AnthropicProxyDeps = {
  apiKey: string;
  upstreamUrl: string;
  host: string;
  port: number;
  logger?: Logger;
  now?: () => number;
  maxRequestBytes?: number;
};

function withoutHeaders(headers: IncomingHttpHeaders, dropped: readonly string[]): OutgoingHttpHeaders {
  const kept: OutgoingHttpHeaders = {};
  for (const [name, value] of Object.entries(headers)) {
    if (value !== undefined && !dropped.includes(name)) {
      kept[name] = value;
    }
  }
  return kept;
}

export function startAnthropicProxyServer(deps: AnthropicProxyDeps): Promise<LoopbackServer> {
  const log = deps.logger ?? createLogger();
  const now = deps.now ?? Date.now;
  const maxRequestBytes = deps.maxRequestBytes ?? MESSAGES_API_MAX_REQUEST_BYTES;
  const upstreamBase = new URL(deps.upstreamUrl);
  const send = upstreamBase.protocol === "https:" ? httpsRequest : httpRequest;

  function refuseTooLarge(res: ServerResponse): void {
    sendJsonError(
      res,
      HTTP_PAYLOAD_TOO_LARGE,
      "request_too_large",
      `request exceeds ${maxRequestBytes} bytes`,
    );
  }

  // The body streams through: the key holder never parses it, so it never holds a whole one. A
  // declared length over the ceiling is refused before a byte is read; an undeclared body that runs
  // over it is cut off upstream, drained to its end, and then refused.
  function forward(req: IncomingMessage, res: ServerResponse, target: URL): Promise<void> {
    const startedAt = now();
    const requestLog = requestLogOf(log, req);
    const declaredBytes = declaredContentLength(req);
    if (declaredBytes !== undefined && declaredBytes > maxRequestBytes) {
      refuseTooLarge(res);
      return Promise.resolve();
    }
    const headers: OutgoingHttpHeaders = {
      ...withoutHeaders(req.headers, DROPPED_REQUEST_HEADERS),
      [API_KEY_HEADER]: deps.apiKey,
      "accept-encoding": "identity",
      ...(declaredBytes === undefined ? {} : { "content-length": String(declaredBytes) }),
    };
    const upstreamUrl = new URL(`${target.pathname}${target.search}`, upstreamBase);
    let overrun = false;
    let departed = false;
    let failed = false;
    // One line per failed call, and none for a call the client left or the ceiling cut: the request
    // and its response can both report the same failure, and destroying the request on purpose
    // reports one too.
    const failUpstream = (error: Error): void => {
      if (overrun || departed || failed) {
        return;
      }
      failed = true;
      requestLog.error("anthropic_proxy_upstream_failed", { path: target.pathname, error });
      if (!res.headersSent) {
        sendJsonError(
          res,
          HTTP_BAD_GATEWAY,
          "api_error",
          `Anthropic API request failed: ${messageOf(error)}`,
        );
      } else if (!res.writableEnded) {
        res.destroy();
      }
    };
    const upstreamRequest: ClientRequest = send(upstreamUrl, { method: "POST", headers }, (upstream) => {
      const status = upstream.statusCode ?? HTTP_BAD_GATEWAY;
      const requestId = upstream.headers[REQUEST_ID_HEADER];
      const head = writeIfAlive(res, () =>
        res.writeHead(status, withoutHeaders(upstream.headers, DROPPED_RESPONSE_HEADERS)),
      );
      if (!head) {
        upstream.destroy();
        return;
      }
      upstream.on("data", (chunk: Buffer) => {
        let flushed = true;
        const alive = writeIfAlive(res, () => {
          flushed = res.write(chunk);
        });
        if (!alive) {
          upstream.destroy();
          return;
        }
        if (!flushed) {
          upstream.pause();
          res.once("drain", () => upstream.resume());
        }
      });
      upstream.on("end", () => {
        endIfAlive(res);
        requestLog.info("anthropic_proxy_request", {
          path: target.pathname,
          status,
          ...(typeof requestId === "string" ? { requestId } : {}),
          durationMs: now() - startedAt,
        });
      });
      upstream.on("error", failUpstream);
    });
    onClientDeparted(res, () => {
      departed = true;
      requestLog.info("anthropic_proxy_client_departed", {
        path: target.pathname,
        durationMs: now() - startedAt,
      });
      upstreamRequest.destroy();
    });
    // `on`, not `once`: a second error with no listener would throw, and the key holder's crash exit
    // drains every run in the container.
    upstreamRequest.on("error", failUpstream);
    let receivedBytes = 0;
    req.on("data", (chunk: Buffer) => {
      receivedBytes += chunk.length;
      if (overrun || upstreamRequest.destroyed) {
        return;
      }
      if (receivedBytes > maxRequestBytes) {
        overrun = true;
        upstreamRequest.destroy();
        return;
      }
      if (!upstreamRequest.write(chunk)) {
        req.pause();
        upstreamRequest.once("drain", () => req.resume());
      }
    });
    req.on("end", () => {
      if (overrun) {
        refuseTooLarge(res);
        return;
      }
      upstreamRequest.end();
    });
    req.on("error", () => upstreamRequest.destroy());
    return Promise.resolve();
  }

  function route(req: IncomingMessage, res: ServerResponse): Promise<void> {
    const target = new URL(req.url ?? "/", "http://proxy");
    if (req.method === "GET" && target.pathname === HEALTH_PATH) {
      sendHealthy(res);
      return Promise.resolve();
    }
    if (req.method === "POST" && FORWARDED_PATHS.includes(target.pathname)) {
      return forward(req, res, target);
    }
    sendJsonError(
      res,
      HTTP_NOT_FOUND,
      "not_found_error",
      `no route for ${req.method ?? ""} ${target.pathname}`,
    );
    return Promise.resolve();
  }

  return listenLoopbackServer({
    host: deps.host,
    port: deps.port,
    log,
    label: "proxy",
    events: { listening: "anthropic_proxy_listening", failed: "anthropic_proxy_upstream_failed" },
    route,
  });
}
