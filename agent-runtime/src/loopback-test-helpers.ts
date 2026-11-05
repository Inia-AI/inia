import { readFileSync } from "node:fs";
import { createServer } from "node:net";
import { join } from "node:path";
import { isRecord } from "./narrow.js";
import { TRACE_FILE_NAME } from "./trace.js";

export function freePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const server = createServer();
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      if (address === null || typeof address === "string") {
        reject(new Error("no port"));
        return;
      }
      const { port } = address;
      server.close(() => resolve(port));
    });
  });
}

export function jsonLineSink(sink: Record<string, unknown>[]): (line: string) => void {
  return (line) => {
    const parsed: unknown = JSON.parse(line);
    if (isRecord(parsed)) {
      sink.push(parsed);
    }
  };
}

function traceRecordsOf(packDir: string): Record<string, unknown>[] {
  const records: Record<string, unknown>[] = [];
  for (const line of readFileSync(join(packDir, TRACE_FILE_NAME), "utf8").split("\n")) {
    if (line === "") {
      continue;
    }
    const parsed: unknown = JSON.parse(line);
    if (isRecord(parsed)) {
      records.push(parsed);
    }
  }
  return records;
}

export function traceEnvelopeOf(packDir: string): Record<string, unknown> | undefined {
  return traceRecordsOf(packDir).find((record) => record["type"] === "envelope");
}

export function traceResultMessageOf(packDir: string): Record<string, unknown> | undefined {
  return traceRecordsOf(packDir)
    .map((record) => (record["type"] === "message" ? record["message"] : undefined))
    .find((message): message is Record<string, unknown> => isRecord(message) && message["type"] === "result");
}
