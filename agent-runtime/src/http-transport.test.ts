import { describe, expect, it } from "bun:test";
import { createServer, type ServerResponse } from "node:http";
import { countCompetitorTurns } from "./concurrency-test-helpers.js";
import {
  BODY_UTF8_DECODE_CHUNK_BYTES,
  decodeJsonBody,
  openResponse,
  sendSerializedJson,
} from "./http-transport.js";

function chunked(bytes: Buffer, chunkSize: number): Buffer[] {
  const chunks: Buffer[] = [];
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    chunks.push(Buffer.from(bytes.subarray(offset, offset + chunkSize)));
  }
  return chunks;
}

describe("decodeJsonBody", () => {
  it("parses multibyte content split mid-sequence across chunk boundaries", async () => {
    const value = { text: "héllo 🌍 你好 ".repeat(200) };
    const bytes = Buffer.from(JSON.stringify(value));
    for (const size of [1, 3, 7, 1024]) {
      expect(await decodeJsonBody(chunked(bytes, size))).toEqual(value);
    }
  });

  it("keeps a leading BOM, so a BOM-prefixed body still fails as invalid JSON", async () => {
    const bytes = Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from("{}")]);
    await expect(decodeJsonBody([bytes])).rejects.toThrow(SyntaxError);
  });

  it("replaces invalid utf8 exactly like the whole-buffer decode", async () => {
    const bytes = Buffer.concat([Buffer.from('{"a":"'), Buffer.from([0xff, 0xfe]), Buffer.from('"}')]);
    expect(await decodeJsonBody(chunked(bytes, 4))).toEqual(JSON.parse(bytes.toString("utf8")));
  });

  it("rejects an empty body the same way as empty-string JSON", async () => {
    await expect(decodeJsonBody([])).rejects.toThrow(SyntaxError);
  });

  it("empties the chunk list so body buffers do not outlive the parse", async () => {
    const chunks = chunked(Buffer.from('{"a":1}'), 2);
    await decodeJsonBody(chunks);
    expect(chunks).toHaveLength(0);
  });

  it("hands the event loop back while decoding a document-scale body", async () => {
    const batchCount = 4;
    const bytes = Buffer.from(`{"blob":"${"a".repeat(BODY_UTF8_DECODE_CHUNK_BYTES * batchCount)}"}`);
    const chunks = chunked(bytes, 64 * 1024);
    const turns = await countCompetitorTurns(() => decodeJsonBody(chunks));
    expect(turns).toBeGreaterThanOrEqual(batchCount);
  });
});

async function fetchHandled(handler: (res: ServerResponse) => void): Promise<Response> {
  const server = createServer((_req, res) => handler(res));
  await new Promise<void>((resolve) => {
    server.listen(0, "127.0.0.1", resolve);
  });
  const address = server.address();
  if (address === null || typeof address === "string") {
    throw new Error("server did not bind to a port");
  }
  try {
    return await fetch(`http://127.0.0.1:${address.port}/`, { headers: { connection: "close" } });
  } finally {
    server.close();
    server.closeAllConnections();
  }
}

describe("openResponse", () => {
  it("drops an undefined-valued header and still answers instead of destroying the socket", async () => {
    let wrote: boolean | undefined;
    const response = await fetchHandled((res) => {
      wrote = openResponse(res, 405, { allow: undefined, "retry-after": "5" });
      res.end();
    });
    expect(wrote).toBe(true);
    expect(response.status).toBe(405);
    expect(response.headers.get("allow")).toBeNull();
    expect(response.headers.get("retry-after")).toBe("5");
    expect(response.headers.get("x-content-type-options")).toBe("nosniff");
  });

  it("surfaces a malformed header value as a thrown error the caller can still answer — never a silent destroy", async () => {
    let caught: unknown;
    const response = await fetchHandled((res) => {
      try {
        openResponse(res, 200, { "x-detail": "split\r\nacross lines" });
        res.end();
      } catch (error) {
        caught = error;
        if (openResponse(res, 500, {})) {
          res.end();
        }
      }
    });
    expect(caught).toBeInstanceOf(TypeError);
    expect(response.status).toBe(500);
  });

  it("surfaces an invalid status code the same way — thrown, answerable, never a silent destroy", async () => {
    let caught: unknown;
    const response = await fetchHandled((res) => {
      try {
        openResponse(res, 99, {});
        res.end();
      } catch (error) {
        caught = error;
        if (openResponse(res, 500, {})) {
          res.end();
        }
      }
    });
    expect(caught).toBeInstanceOf(RangeError);
    expect(response.status).toBe(500);
  });
});

describe("sendSerializedJson", () => {
  it("declares content-length in bytes of the rendered body instead of relying on the runtime", async () => {
    const value = { text: "héllo 🌍 你好 </script> " };
    const response = await fetchHandled((res) => sendSerializedJson(res, 200, JSON.stringify(value)));
    const text = await response.text();
    expect(JSON.parse(text)).toEqual(value);
    expect(text.length).toBeLessThan(Buffer.byteLength(text));
    expect(response.headers.get("transfer-encoding")).toBeNull();
    expect(response.headers.get("content-length")).toBe(String(Buffer.byteLength(text)));
  });

  it("keeps a caller header from overriding the declared content-length", async () => {
    const response = await fetchHandled((res) =>
      sendSerializedJson(res, 200, JSON.stringify({ ok: true }), { "content-length": "1" }),
    );
    const text = await response.text();
    expect(response.headers.get("content-length")).toBe(String(Buffer.byteLength(text)));
  });

  it("keeps a caller header from overriding the declared content-length under any casing", async () => {
    const response = await fetchHandled((res) =>
      sendSerializedJson(res, 200, JSON.stringify({ ok: true }), { "Content-Length": "1" }),
    );
    const text = await response.text();
    expect(response.headers.get("content-length")).toBe(String(Buffer.byteLength(text)));
    expect(JSON.parse(text)).toEqual({ ok: true });
  });
});
