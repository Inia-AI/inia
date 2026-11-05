import { describe, expect, it } from "bun:test";
import { createServer, type IncomingMessage } from "node:http";
import { connect, type Socket } from "node:net";

type RequestState = "unread" | "unread-resumed" | "partially-consumed" | "fully-consumed";
type CloseKind = "client-fin" | "client-rst" | "server-destroy";

const OBSERVATION_WINDOW_MS = 1500;
const BODY_BYTES = 4096;

function reachState(req: IncomingMessage, state: RequestState): Promise<void> {
  if (state === "unread") {
    return Promise.resolve();
  }
  if (state === "unread-resumed") {
    req.socket.resume();
    return Promise.resolve();
  }
  return new Promise<void>((resolve) => {
    // Listener stays attached — the stream keeps consuming, which is what
    // separates "partially consumed" from "abandoned".
    req.on("data", () => resolve());
    req.on("end", () => resolve());
  });
}

async function emitsCloseOn(state: RequestState, kind: CloseKind): Promise<boolean> {
  let serverSocket: Socket | undefined;
  let closeEmitted = false;
  const reachedState = Promise.withResolvers<void>();

  const server = createServer((req) => {
    serverSocket = req.socket;
    req.socket.once("close", () => {
      closeEmitted = true;
    });
    void reachState(req, state).then(() => reachedState.resolve());
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", () => resolve()));
  const address = server.address();
  if (address === null || typeof address === "string") {
    throw new Error("no port");
  }

  const sentBytes = state === "fully-consumed" ? BODY_BYTES : BODY_BYTES / 2;
  const client = connect(address.port, "127.0.0.1", () => {
    client.write(
      "POST / HTTP/1.1\r\nhost: localhost\r\ncontent-type: application/json\r\n" +
        `content-length: ${BODY_BYTES}\r\n\r\n${"b".repeat(sentBytes)}`,
    );
  });
  client.on("error", () => {});

  await reachedState.promise;
  if (kind === "client-fin") {
    client.end();
  } else if (kind === "client-rst") {
    client.resetAndDestroy();
  } else {
    serverSocket?.destroy();
  }

  await new Promise((resolve) => setTimeout(resolve, OBSERVATION_WINDOW_MS));
  client.destroy();
  await new Promise<void>((resolve) => {
    server.closeAllConnections();
    server.close(() => resolve());
  });
  return closeEmitted;
}

// Pins the matrix the admission wait's eviction rests on: a Bun upgrade that changes any cell
// here must fail this test.
describe("Bun socket close delivery per request state", () => {
  const expectations: { state: RequestState; kind: CloseKind; closeEmitted: boolean }[] = [
    { state: "unread", kind: "client-fin", closeEmitted: true },
    { state: "unread", kind: "client-rst", closeEmitted: true },
    { state: "unread", kind: "server-destroy", closeEmitted: true },
    { state: "unread-resumed", kind: "client-fin", closeEmitted: true },
    { state: "unread-resumed", kind: "client-rst", closeEmitted: true },
    { state: "unread-resumed", kind: "server-destroy", closeEmitted: true },
    { state: "partially-consumed", kind: "client-fin", closeEmitted: true },
    { state: "partially-consumed", kind: "client-rst", closeEmitted: true },
    { state: "partially-consumed", kind: "server-destroy", closeEmitted: true },
    { state: "fully-consumed", kind: "client-fin", closeEmitted: true },
    { state: "fully-consumed", kind: "client-rst", closeEmitted: true },
    { state: "fully-consumed", kind: "server-destroy", closeEmitted: true },
  ];

  for (const { state, kind, closeEmitted } of expectations) {
    it(`${state} + ${kind} ${closeEmitted ? "emits" : "never emits"} socket close`, async () => {
      expect(await emitsCloseOn(state, kind)).toBe(closeEmitted);
    });
  }
});
