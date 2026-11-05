import { describe, expect, it } from "bun:test";
import { execFile } from "node:child_process";
import { chmodSync, mkdtempSync, rmSync, statSync } from "node:fs";
import { createServer, type Server } from "node:http";
import { connect } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { promisify } from "node:util";

const OBSERVATION_WINDOW_MS = 1000;
const REQUEST = "GET / HTTP/1.1\r\nhost: localhost\r\n\r\n";
const HEAD_END = "\r\n\r\n";
const REQUEST_WITH_DEL_IN_HEADER_VALUE = "GET / HTTP/1.1\r\nhost: localhost\r\nx-odd: a\x7fb\r\n\r\n";
const RESPONSE_LARGER_THAN_SEND_WINDOW_BYTES = 16 * 1024 * 1024;
const UNREACHABLE_PROXY = "http://127.0.0.1:1";
const SUBPROCESS_TIMEOUT_MS = 15_000;

const FETCH_THROUGH_DELETED_PROXY_SCRIPT = `
const { createServer } = require("node:http");
const server = createServer((_req, res) => {
  res.writeHead(200, { "content-length": "2" });
  res.end("ok");
});
const attempt = (url) =>
  fetch(url, { signal: AbortSignal.timeout(2000) }).then(
    (response) => "reached:" + response.status,
    () => "failed",
  );
server.listen(0, "127.0.0.1", async () => {
  const url = "http://127.0.0.1:" + server.address().port + "/";
  const whileSet = await attempt(url);
  delete process.env.HTTP_PROXY;
  delete process.env.http_proxy;
  const afterDelete = await attempt(url);
  console.log(JSON.stringify({ whileSet, afterDelete }));
  process.exit(0);
});
`;

// Shaped like the run child: stdin held open, then it ends through its final write's callback
// (deliverFinalFrame), never by letting the event loop drain.
const DEAD_STDOUT_CHILD = `
process.stdin.on("data", () => {});
const chunk = "x".repeat(65536);
let written = 0;
const write = () => {
  if (++written < 200) {
    process.stdout.write(chunk);
    setImmediate(write);
    return;
  }
  require("node:fs").writeSync(2, "work-done");
  process.stdout.write("final", (error) => process.exit(error ? 3 : 0));
};
write();
`;

// Parent side in a plain-bun subprocess: under accumulated runner state, child exit events have
// failed to arrive (docs/operations/bun-vs-node.md, the pipe-write item).
const DEAD_STDOUT_SCRIPT = `
const { spawn } = require("node:child_process");
const child = ${JSON.stringify(DEAD_STDOUT_CHILD)};
const run = (prefix) =>
  new Promise((resolve) => {
    const proc = spawn(process.execPath, ["-e", prefix + child], { stdio: ["pipe", "pipe", "pipe"] });
    let stderr = "";
    proc.stderr.on("data", (data) => {
      stderr += data;
    });
    proc.stdout.once("data", () => proc.stdout.destroy());
    proc.on("close", (code) =>
      resolve({
        workDone: stderr.includes("work-done"),
        endedThroughItsFinalWrite: code === 0 || code === 3,
        handlerCalled: stderr.includes("handler-called"),
      }),
    );
  });
(async () => {
  const unhandled = await run("");
  const listened = await run('process.stdout.on("error", () => {});');
  const handled = await run('process.on("uncaughtException", () => require("node:fs").writeSync(2, "handler-called"));');
  console.log(JSON.stringify({ unhandled, listened, handled }));
})();
`;

async function listening(server: Server): Promise<number> {
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  if (address === null || typeof address === "string") {
    throw new Error("no port");
  }
  return address.port;
}

function closed(server: Server): Promise<void> {
  return new Promise<void>((resolve) => {
    server.closeAllConnections();
    server.close(() => resolve());
  });
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function bodyBytesReceived(raw: Buffer): number {
  const headEnd = raw.indexOf(HEAD_END);
  return headEnd < 0 ? 0 : raw.length - headEnd - HEAD_END.length;
}

// Bun runtime facts the kept workarounds in docs/operations/bun-vs-node.md rest on. Each workaround stays;
// each pin here names the behaviour the workaround no longer has to cover, so a runtime that
// regresses it fails by name.
describe("Bun runtime facts", () => {
  it("throws on listen(NaN) instead of binding an ephemeral port (PORT validation stays)", async () => {
    const server = createServer();
    try {
      await expect(
        new Promise<void>((resolve, reject) => {
          server.once("error", reject);
          server.listen(Number.NaN, "127.0.0.1", () => resolve());
        }),
      ).rejects.toMatchObject({ code: "ERR_OUT_OF_RANGE" });
    } finally {
      await closed(server);
    }
  });

  it("keeps the sticky bit through fs.chmodSync (claimRetainedRoot's chmod shell-out stays)", () => {
    const dir = mkdtempSync(join(tmpdir(), "sticky-"));
    try {
      chmodSync(dir, 0o1777);
      expect((statSync(dir).mode & 0o7777).toString(8)).toBe("1777");
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  // allowHalfOpen keeps the client's write side open past the server's FIN, so the second
  // request really reaches the wire and "parses no second request" means what it says.
  it("FINs the socket after a connection: close response and parses no second request (the re-armed phase timer stays)", async () => {
    let requestsSeen = 0;
    const server = createServer((_req, res) => {
      requestsSeen += 1;
      res.writeHead(200, { connection: "close", "content-length": "2" });
      res.end("ok");
    });
    const port = await listening(server);
    const client = connect({ port, host: "127.0.0.1", allowHalfOpen: true }, () => client.write(REQUEST));
    client.on("data", () => {});
    client.on("error", () => {});
    try {
      const finSeen = await Promise.race([
        new Promise<boolean>((resolve) => client.once("end", () => resolve(true))),
        sleep(OBSERVATION_WINDOW_MS * 2).then(() => false),
      ]);
      expect(finSeen).toBe(true);
      client.write(REQUEST);
      await sleep(OBSERVATION_WINDOW_MS);
      expect(requestsSeen).toBe(1);
    } finally {
      client.destroy();
      await closed(server);
    }
  });

  it("leaves a client socket paused until something reads it (raw test clients must resume())", async () => {
    const server = createServer((_req, res) => {
      res.writeHead(200, { connection: "close", "content-length": "2" });
      res.end("ok");
    });
    const port = await listening(server);
    const client = connect(port, "127.0.0.1", () => client.write(REQUEST));
    client.on("error", () => {});
    let endSeen = false;
    client.on("end", () => {
      endSeen = true;
    });
    try {
      await sleep(OBSERVATION_WINDOW_MS);
      expect(client.readableFlowing).toBeNull();
      expect(endSeen).toBe(false);
    } finally {
      client.destroy();
      await closed(server);
    }
  });

  it("delivers a response larger than the send window whole when the socket is destroyed at finish (never touching a socket at completion stays)", async () => {
    const body = Buffer.alloc(RESPONSE_LARGER_THAN_SEND_WINDOW_BYTES, "x");
    const server = createServer((req, res) => {
      res.writeHead(200, { connection: "close", "content-length": String(body.length) });
      res.on("finish", () => req.socket.destroy());
      res.end(body);
    });
    const port = await listening(server);
    try {
      const received = await new Promise<number>((resolve) => {
        const chunks: Buffer[] = [];
        const client = connect(port, "127.0.0.1", () => client.write(REQUEST));
        client.on("data", (chunk: Buffer) => chunks.push(chunk));
        client.on("error", () => {});
        client.on("close", () => resolve(bodyBytesReceived(Buffer.concat(chunks))));
      });
      expect(received).toBe(body.length);
    } finally {
      await closed(server);
    }
  });

  it("refuses a DEL byte in a header value before any handler runs (the key holder's 502 for a header it cannot forward stays)", async () => {
    let handled = false;
    const server = createServer((_req, res) => {
      handled = true;
      res.writeHead(200, { connection: "close", "content-length": "0" });
      res.end();
    });
    server.on("clientError", (_error, socket) => socket.destroy());
    const port = await listening(server);
    const client = connect(port, "127.0.0.1", () => client.write(REQUEST_WITH_DEL_IN_HEADER_VALUE));
    client.on("error", () => {});
    client.resume();
    try {
      await new Promise<void>((resolve) => client.once("close", () => resolve()));
      expect(handled).toBe(false);
    } finally {
      client.destroy();
      await closed(server);
    }
  });

  it("keeps a child running through an unhandled stdout error, out of uncaughtException, to the end of its final write (frame_channel_errored stays for Node)", async () => {
    const { stdout } = await promisify(execFile)(process.execPath, ["-e", DEAD_STDOUT_SCRIPT], {
      encoding: "utf8",
      timeout: SUBPROCESS_TIMEOUT_MS,
    });
    // The final write's callback usually reports the EPIPE (exit 3), but Bun can report success
    // after the pipe died (exit 0).
    const ranToItsFinalWrite = { workDone: true, endedThroughItsFinalWrite: true, handlerCalled: false };
    expect(JSON.parse(stdout)).toEqual({
      unhandled: ranToItsFinalWrite,
      listened: ranToItsFinalWrite,
      handled: ranToItsFinalWrite,
    });
  });

  // In a subprocess: the proxy variables are never set on the runner (docs/operations/bun-vs-node.md).
  it("routes fetch through HTTP_PROXY while it is set and drops it once deleted (loopback probes stay on raw sockets)", async () => {
    const { stdout } = await promisify(execFile)(
      process.execPath,
      ["-e", FETCH_THROUGH_DELETED_PROXY_SCRIPT],
      {
        env: { ...process.env, HTTP_PROXY: UNREACHABLE_PROXY, http_proxy: UNREACHABLE_PROXY },
        encoding: "utf8",
        timeout: SUBPROCESS_TIMEOUT_MS,
      },
    );
    expect(JSON.parse(stdout)).toEqual({ whileSet: "failed", afterDelete: "reached:200" });
  });
});
