import { describe, expect, it } from "bun:test";
import { createServer, type IncomingMessage, request, type Server, type ServerResponse } from "node:http";
import { connect } from "node:net";
import Anthropic from "@anthropic-ai/sdk";
import { startAnthropicProxyServer } from "./anthropic-proxy.js";
import { createLogger } from "./logger.js";
import { sseEvent } from "./loopback-http.js";
import { jsonLineSink } from "./loopback-test-helpers.js";
import { ANTHROPIC_PROXY_PLACEHOLDER_API_KEY, EXECUTION_ID_HEADER } from "./model-provider.js";
import { isRecord } from "./narrow.js";

const API_KEY = "sk-ant-api03-the-real-key";
const MODEL = "claude-sonnet-5-5";
const STREAM_PAUSE_MS = 50;
const SMALL_CEILING_BYTES = 1024;
const RAW_EXCHANGE_TIMEOUT_MS = 2_000;
const LARGE_BODY_BYTES = 8 * 1024 * 1024;
const SLOW_READER_PAUSE_MS = 1;

type SeenRequest = {
  method: string;
  url: string;
  headers: Record<string, string | string[] | undefined>;
  body: string;
};

type Upstream = {
  url: string;
  requests: SeenRequest[];
  closedEarly: () => boolean;
  close(): Promise<void>;
};

const STREAM_HEAD =
  sseEvent("message_start", {
    type: "message_start",
    message: {
      id: "msg_proxy_1",
      type: "message",
      role: "assistant",
      model: MODEL,
      content: [],
      stop_reason: null,
      stop_sequence: null,
      usage: { input_tokens: 11, output_tokens: 1 },
    },
  }) +
  sseEvent("content_block_start", {
    type: "content_block_start",
    index: 0,
    content_block: { type: "text", text: "" },
  }) +
  sseEvent("content_block_delta", {
    type: "content_block_delta",
    index: 0,
    delta: { type: "text_delta", text: "streamed " },
  });

const STREAM_TAIL =
  sseEvent("content_block_delta", {
    type: "content_block_delta",
    index: 0,
    delta: { type: "text_delta", text: "through" },
  }) +
  sseEvent("content_block_stop", { type: "content_block_stop", index: 0 }) +
  sseEvent("message_delta", {
    type: "message_delta",
    delta: { stop_reason: "end_turn", stop_sequence: null },
    usage: { output_tokens: 3 },
  }) +
  sseEvent("message_stop", { type: "message_stop" });

function startUpstream(answer: (req: SeenRequest, res: ServerResponse) => void): Promise<Upstream> {
  const requests: SeenRequest[] = [];
  let closedEarly = false;
  const server: Server = createServer((req: IncomingMessage, res: ServerResponse) => {
    const chunks: Buffer[] = [];
    req.on("data", (chunk: Buffer) => chunks.push(chunk));
    req.on("end", () => {
      const seen = {
        method: req.method ?? "",
        url: req.url ?? "",
        headers: { ...req.headers },
        body: Buffer.concat(chunks).toString("utf8"),
      };
      requests.push(seen);
      res.on("close", () => {
        if (!res.writableEnded) {
          closedEarly = true;
        }
      });
      answer(seen, res);
    });
  });
  return new Promise((resolve) => {
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      const port = address !== null && typeof address === "object" ? address.port : 0;
      resolve({
        url: `http://127.0.0.1:${port}`,
        requests,
        closedEarly: () => closedEarly,
        close: () =>
          new Promise<void>((resolveClose) => {
            server.closeAllConnections();
            server.close(() => resolveClose());
          }),
      });
    });
  });
}

function streamingAnswer(_req: SeenRequest, res: ServerResponse): void {
  res.writeHead(200, { "content-type": "text/event-stream", "request-id": "req_upstream_1" });
  res.write(STREAM_HEAD);
  setTimeout(() => res.end(STREAM_TAIL), STREAM_PAUSE_MS);
}

async function withProxy(
  answer: (req: SeenRequest, res: ServerResponse) => void,
  run: (proxyUrl: string, upstream: Upstream, logLines: Record<string, unknown>[]) => Promise<void>,
  options: { upstreamUrl?: string; maxRequestBytes?: number; apiKey?: string } = {},
): Promise<void> {
  const upstream = await startUpstream(answer);
  const logLines: Record<string, unknown>[] = [];
  const proxy = await startAnthropicProxyServer({
    apiKey: options.apiKey ?? API_KEY,
    upstreamUrl: options.upstreamUrl ?? upstream.url,
    host: "127.0.0.1",
    port: 0,
    logger: createLogger({ level: "debug", sink: jsonLineSink(logLines) }),
    ...(options.maxRequestBytes === undefined ? {} : { maxRequestBytes: options.maxRequestBytes }),
  });
  try {
    await run(proxy.url, upstream, logLines);
  } finally {
    await proxy.close();
    await upstream.close();
  }
}

type RawReply = { status: number; headers: Record<string, string | string[] | undefined>; body: string };

function rawRequest(
  url: string,
  init: { method: string; path: string; headers?: Record<string, string>; body?: string; chunks?: string[] },
): Promise<RawReply> {
  return new Promise((resolve, reject) => {
    const req = request(
      new URL(init.path, url),
      { method: init.method, headers: { connection: "close", ...(init.headers ?? {}) } },
      (res) => {
        const chunks: Buffer[] = [];
        res.on("data", (chunk: Buffer) => chunks.push(chunk));
        res.on("end", () =>
          resolve({
            status: res.statusCode ?? 0,
            headers: res.headers,
            body: Buffer.concat(chunks).toString("utf8"),
          }),
        );
        res.on("error", reject);
      },
    );
    req.on("error", reject);
    for (const chunk of init.chunks ?? []) {
      req.write(chunk);
    }
    req.end(init.body);
  });
}

const CLI_HEADERS = {
  "content-type": "application/json",
  "x-api-key": ANTHROPIC_PROXY_PLACEHOLDER_API_KEY,
  authorization: "Bearer stray-token",
  "anthropic-version": "2023-06-01",
  "anthropic-beta": "interleaved-thinking-2025-05-14",
  [EXECUTION_ID_HEADER]: "run-42",
};

describe("the Anthropic key holder forwards the CLI's model calls with the real key", () => {
  it("streams a Messages answer through to the real SDK client, the key swapped in at the proxy", async () => {
    await withProxy(streamingAnswer, async (proxyUrl, upstream) => {
      const client = new Anthropic({
        baseURL: proxyUrl,
        apiKey: ANTHROPIC_PROXY_PLACEHOLDER_API_KEY,
        maxRetries: 0,
        defaultHeaders: { [EXECUTION_ID_HEADER]: "run-42" },
      });
      const message = await client.messages
        .stream({ model: MODEL, max_tokens: 64, messages: [{ role: "user", content: "hi" }] })
        .finalMessage();
      expect(message.content.map((block) => (block.type === "text" ? block.text : block.type))).toEqual([
        "streamed through",
      ]);
      expect(message.usage.output_tokens).toBe(3);
      expect(upstream.requests).toHaveLength(1);
      const seen = upstream.requests[0];
      expect(seen?.url).toBe("/v1/messages");
      expect(seen?.headers["x-api-key"]).toBe(API_KEY);
      expect(seen?.headers["anthropic-version"]).toBe("2023-06-01");
      const body: unknown = JSON.parse(seen?.body ?? "{}");
      expect(isRecord(body) && body["stream"]).toBe(true);
    });
  });

  it("drops the placeholder, any bearer token and the execution-id header, keeps the query and the betas", async () => {
    await withProxy(streamingAnswer, async (proxyUrl, upstream) => {
      const reply = await rawRequest(proxyUrl, {
        method: "POST",
        path: "/v1/messages?beta=true",
        headers: CLI_HEADERS,
        body: JSON.stringify({ model: MODEL, max_tokens: 8, stream: true, messages: [] }),
      });
      expect(reply.status).toBe(200);
      expect(reply.headers["content-type"]).toBe("text/event-stream");
      expect(reply.headers["request-id"]).toBe("req_upstream_1");
      expect(reply.body).toBe(STREAM_HEAD + STREAM_TAIL);
      const seen = upstream.requests[0];
      expect(seen?.url).toBe("/v1/messages?beta=true");
      expect(seen?.headers["x-api-key"]).toBe(API_KEY);
      expect(seen?.headers["authorization"]).toBeUndefined();
      expect(seen?.headers[EXECUTION_ID_HEADER]).toBeUndefined();
      expect(seen?.headers["anthropic-beta"]).toBe("interleaved-thinking-2025-05-14");
      expect(seen?.headers["accept-encoding"]).toBe("identity");
    });
  });

  it("forwards count_tokens the same way", async () => {
    const answer = (_req: SeenRequest, res: ServerResponse): void => {
      res.writeHead(200, { "content-type": "application/json" });
      res.end(JSON.stringify({ input_tokens: 17 }));
    };
    await withProxy(answer, async (proxyUrl, upstream) => {
      const reply = await rawRequest(proxyUrl, {
        method: "POST",
        path: "/v1/messages/count_tokens?beta=true",
        headers: CLI_HEADERS,
        body: JSON.stringify({ model: MODEL, messages: [] }),
      });
      expect(reply.status).toBe(200);
      expect(JSON.parse(reply.body)).toEqual({ input_tokens: 17 });
      expect(upstream.requests[0]?.url).toBe("/v1/messages/count_tokens?beta=true");
      expect(upstream.requests[0]?.headers["x-api-key"]).toBe(API_KEY);
    });
  });

  it("passes an upstream refusal through with its status, body and the headers the CLI retries on", async () => {
    const refusalBody = JSON.stringify({
      type: "error",
      error: { type: "rate_limit_error", message: "slow down" },
    });
    const answer = (_req: SeenRequest, res: ServerResponse): void => {
      res.writeHead(429, {
        "content-type": "application/json",
        "retry-after": "7",
        "x-should-retry": "true",
        "request-id": "req_limited",
        "anthropic-ratelimit-requests-remaining": "0",
      });
      res.end(refusalBody);
    };
    await withProxy(answer, async (proxyUrl) => {
      const reply = await rawRequest(proxyUrl, {
        method: "POST",
        path: "/v1/messages",
        headers: CLI_HEADERS,
        body: "{}",
      });
      expect(reply.status).toBe(429);
      expect(reply.body).toBe(refusalBody);
      expect(reply.headers["retry-after"]).toBe("7");
      expect(reply.headers["x-should-retry"]).toBe("true");
      expect(reply.headers["request-id"]).toBe("req_limited");
      expect(reply.headers["anthropic-ratelimit-requests-remaining"]).toBe("0");
    });
  });
});

// A raw socket, because an HTTP client refuses to send what these tests need the server to see.
// It resumes at once: a Bun socket nobody reads never sees the server's FIN (docs/operations/bun-vs-node.md).
function rawExchange(url: string, raw: string): Promise<string> {
  const { hostname, port } = new URL(url);
  return new Promise((resolve) => {
    let received = "";
    const socket = connect({ host: hostname, port: Number(port) }, () => socket.write(raw));
    socket.setEncoding("utf8");
    socket.on("data", (chunk: string) => {
      received += chunk;
    });
    const settle = (): void => {
      clearTimeout(timer);
      socket.destroy();
      resolve(received.split("\r\n")[0] ?? "");
    };
    const timer = setTimeout(settle, RAW_EXCHANGE_TIMEOUT_MS);
    socket.on("close", settle);
    socket.on("error", settle);
    socket.resume();
  });
}

describe("a request the key holder cannot forward costs one answer, never the key holder", () => {
  it("keeps a malformed request from upstream and stays up", async () => {
    await withProxy(streamingAnswer, async (proxyUrl, upstream) => {
      for (const request of [
        "POST //[ HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n",
        "POST //x:99999/ HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n",
        "POST /v1/messages HTTP/1.1\r\nHost: x\r\nx-odd: a\x7fb\r\ncontent-length: 2\r\nConnection: close\r\n\r\n{}",
      ]) {
        await rawExchange(proxyUrl, request);
      }
      expect((await rawRequest(proxyUrl, { method: "GET", path: "/healthz" })).status).toBe(200);
      expect(upstream.requests).toHaveLength(0);
    });
  });

  it("answers a key it cannot send with a 502 and stays up, never echoing the key", async () => {
    await withProxy(
      streamingAnswer,
      async (proxyUrl, _upstream, logLines) => {
        const reply = await rawRequest(proxyUrl, {
          method: "POST",
          path: "/v1/messages",
          headers: CLI_HEADERS,
          body: "{}",
        });
        expect(reply.status).toBe(502);
        expect(reply.body).not.toContain(API_KEY);
        expect(JSON.stringify(logLines)).not.toContain(API_KEY);
        expect((await rawRequest(proxyUrl, { method: "GET", path: "/healthz" })).status).toBe(200);
      },
      { apiKey: `${API_KEY}\n` },
    );
  });
});

describe("the Anthropic key holder streams both ways without holding a body", () => {
  it("delivers a large upload to upstream byte for byte", async () => {
    const upload = JSON.stringify({
      model: MODEL,
      max_tokens: 8,
      messages: [{ role: "user", content: "x".repeat(LARGE_BODY_BYTES) }],
    });
    await withProxy(streamingAnswer, async (proxyUrl, upstream) => {
      const reply = await rawRequest(proxyUrl, {
        method: "POST",
        path: "/v1/messages",
        headers: CLI_HEADERS,
        body: upload,
      });
      expect(reply.status).toBe(200);
      expect(upstream.requests[0]?.body).toBe(upload);
    });
  });

  it("delivers a large answer in full to a client that reads slowly", async () => {
    const answerBody = "y".repeat(LARGE_BODY_BYTES);
    const answer = (_req: SeenRequest, res: ServerResponse): void => {
      res.writeHead(200, { "content-type": "application/json" });
      res.end(answerBody);
    };
    await withProxy(answer, async (proxyUrl) => {
      const received = await new Promise<number>((resolve, reject) => {
        const req = request(
          new URL("/v1/messages", proxyUrl),
          { method: "POST", headers: CLI_HEADERS },
          (res) => {
            let bytes = 0;
            res.on("data", (chunk: Buffer) => {
              bytes += chunk.length;
              res.pause();
              setTimeout(() => res.resume(), SLOW_READER_PAUSE_MS);
            });
            res.on("end", () => resolve(bytes));
            res.on("error", reject);
          },
        );
        req.on("error", reject);
        req.end("{}");
      });
      expect(received).toBe(answerBody.length);
    });
  });

  it("aborts the client's answer when upstream dies mid-stream, and stays up", async () => {
    const dieMidStream = (_req: SeenRequest, res: ServerResponse): void => {
      res.writeHead(200, { "content-type": "text/event-stream" });
      res.write(STREAM_HEAD);
      setTimeout(() => res.socket?.destroy(), STREAM_PAUSE_MS);
    };
    await withProxy(dieMidStream, async (proxyUrl) => {
      const outcome = await new Promise<string>((resolve) => {
        const req = request(
          new URL("/v1/messages", proxyUrl),
          { method: "POST", headers: CLI_HEADERS },
          (res) => {
            res.on("data", () => undefined);
            res.on("end", () => resolve("ended"));
            res.on("error", () => resolve("aborted"));
            res.on("close", () => resolve(res.complete ? "ended" : "aborted"));
          },
        );
        req.on("error", () => resolve("aborted"));
        req.end("{}");
      });
      expect(outcome).toBe("aborted");
      expect((await rawRequest(proxyUrl, { method: "GET", path: "/healthz" })).status).toBe(200);
    });
  });

  it("logs a client that leaves before upstream answers as a departure, never as an upstream failure", async () => {
    const neverAnswer = (): void => undefined;
    await withProxy(neverAnswer, async (proxyUrl, upstream, logLines) => {
      await new Promise<void>((resolve) => {
        const req = request(new URL("/v1/messages", proxyUrl), { method: "POST", headers: CLI_HEADERS });
        req.on("error", () => resolve());
        req.end("{}");
        const leave = setInterval(() => {
          if (upstream.requests.length > 0) {
            clearInterval(leave);
            req.destroy();
            resolve();
          }
        }, STREAM_PAUSE_MS);
      });
      const deadline = Date.now() + RAW_EXCHANGE_TIMEOUT_MS;
      while (
        !logLines.some((line) => line["event"] === "anthropic_proxy_client_departed") &&
        Date.now() < deadline
      ) {
        await new Promise((resolve) => setTimeout(resolve, STREAM_PAUSE_MS));
      }
      expect(logLines.some((line) => line["event"] === "anthropic_proxy_client_departed")).toBe(true);
      expect(logLines.some((line) => line["event"] === "anthropic_proxy_upstream_failed")).toBe(false);
    });
  });
});

describe("the Anthropic key holder serves nothing but the model calls", () => {
  it("answers its health route and refuses every other route without reaching upstream", async () => {
    await withProxy(streamingAnswer, async (proxyUrl, upstream) => {
      expect((await rawRequest(proxyUrl, { method: "GET", path: "/healthz" })).status).toBe(200);
      for (const [method, path] of [
        ["GET", "/v1/models"],
        ["POST", "/v1/files"],
        ["POST", "/v1/messages/batches"],
        ["GET", "/v1/messages"],
        ["POST", "/v1/complete"],
      ] as const) {
        const reply = await rawRequest(proxyUrl, { method, path, headers: CLI_HEADERS, body: "{}" });
        expect(reply.status).toBe(404);
        expect(JSON.parse(reply.body)).toMatchObject({ type: "error", error: { type: "not_found_error" } });
      }
      // Sent raw: an HTTP client would resolve the dot segments before the key holder saw them.
      const traversal = await rawExchange(
        proxyUrl,
        "POST /v1/messages/../organizations HTTP/1.1\r\nHost: x\r\ncontent-length: 2\r\nConnection: close\r\n\r\n{}",
      );
      expect(traversal).toBe("HTTP/1.1 404 Not Found");
      expect(upstream.requests).toHaveLength(0);
    });
  });

  it("refuses a declared length over the ceiling before reading a byte or reaching upstream", async () => {
    await withProxy(
      streamingAnswer,
      async (proxyUrl, upstream) => {
        const reply = await rawRequest(proxyUrl, {
          method: "POST",
          path: "/v1/messages",
          headers: CLI_HEADERS,
          body: "x".repeat(SMALL_CEILING_BYTES + 1),
        });
        expect(reply.status).toBe(413);
        expect(JSON.parse(reply.body)).toMatchObject({ type: "error", error: { type: "request_too_large" } });
        expect(upstream.requests).toHaveLength(0);
      },
      { maxRequestBytes: SMALL_CEILING_BYTES },
    );
  });

  it("cuts off an undeclared body that runs over the ceiling, and upstream never gets a whole request", async () => {
    await withProxy(
      streamingAnswer,
      async (proxyUrl, upstream) => {
        const chunk = "x".repeat(SMALL_CEILING_BYTES / 2);
        const reply = await rawRequest(proxyUrl, {
          method: "POST",
          path: "/v1/messages",
          headers: { ...CLI_HEADERS, "transfer-encoding": "chunked" },
          chunks: [chunk, chunk, chunk],
        });
        expect(reply.status).toBe(413);
        expect(upstream.requests).toHaveLength(0);
      },
      { maxRequestBytes: SMALL_CEILING_BYTES },
    );
  });
});

describe("the Anthropic key holder fails in the API's own error shape and never leaks the key", () => {
  it("answers an unreachable upstream with a 502 api_error the CLI can read", async () => {
    const closed = await startUpstream(streamingAnswer);
    const deadUrl = closed.url;
    await closed.close();
    await withProxy(
      streamingAnswer,
      async (proxyUrl, _upstream, logLines) => {
        const reply = await rawRequest(proxyUrl, {
          method: "POST",
          path: "/v1/messages",
          headers: CLI_HEADERS,
          body: "{}",
        });
        expect(reply.status).toBe(502);
        expect(JSON.parse(reply.body)).toMatchObject({ type: "error", error: { type: "api_error" } });
        expect(logLines.some((line) => line["event"] === "anthropic_proxy_upstream_failed")).toBe(true);
      },
      { upstreamUrl: deadUrl },
    );
  });

  it("aborts the upstream request when the client leaves mid-stream", async () => {
    const hold = (_req: SeenRequest, res: ServerResponse): void => {
      res.writeHead(200, { "content-type": "text/event-stream" });
      res.write(STREAM_HEAD);
    };
    await withProxy(hold, async (proxyUrl, upstream) => {
      await new Promise<void>((resolve, reject) => {
        const req = request(
          new URL("/v1/messages", proxyUrl),
          { method: "POST", headers: CLI_HEADERS },
          (res) => {
            res.once("data", () => {
              req.destroy();
              resolve();
            });
          },
        );
        req.on("error", () => undefined);
        req.end("{}");
        const noFirstChunk = setTimeout(() => reject(new Error("no first chunk")), 5_000);
        req.on("close", () => clearTimeout(noFirstChunk));
      });
      const deadline = Date.now() + 5_000;
      while (!upstream.closedEarly() && Date.now() < deadline) {
        await new Promise((resolve) => setTimeout(resolve, 20));
      }
      expect(upstream.closedEarly()).toBe(true);
    });
  });

  it("logs each call against the run's execution id, and never the key", async () => {
    await withProxy(streamingAnswer, async (proxyUrl, _upstream, logLines) => {
      await rawRequest(proxyUrl, {
        method: "POST",
        path: "/v1/messages",
        headers: CLI_HEADERS,
        body: JSON.stringify({ model: MODEL, max_tokens: 8, stream: true, messages: [] }),
      });
      const requestLines = logLines.filter((line) => line["event"] === "anthropic_proxy_request");
      expect(requestLines).toHaveLength(1);
      expect(requestLines[0]).toMatchObject({
        executionId: "run-42",
        status: 200,
        requestId: "req_upstream_1",
      });
      expect(JSON.stringify(logLines)).not.toContain(API_KEY);
    });
  });
});
