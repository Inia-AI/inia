import { describe, expect, it } from "bun:test";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Readable, Writable } from "node:stream";
import {
  CARGO_FD,
  CARGO_LENGTH_PREFIX_BYTES,
  type CargoByteReader,
  CargoChannelError,
  type CargoSource,
  createCargoReceiver,
  writeCargo,
} from "./document-channel.js";
import { sha256Hex } from "./hash.js";

const SCENARIO_REPORT_DEADLINE_MS = 15_000;

const SCENARIO_REPORT_POLL_MS = 20;

// Above the scenarios' own report deadline, so a runtime that regresses fails on the scenario's
// verdict ("hung") rather than on the runner's default timeout.
const CARGO_DEATH_TEST_TIMEOUT_MS = SCENARIO_REPORT_DEADLINE_MS + 5_000;

function sourceOf(bytes: Buffer): CargoSource {
  return { sizeBytes: bytes.byteLength, open: () => Readable.from([bytes]) };
}

function chunkedSourceOf(bytes: Buffer, chunkBytes: number): CargoSource {
  const chunks: Buffer[] = [];
  for (let offset = 0; offset < bytes.byteLength; offset += chunkBytes) {
    chunks.push(bytes.subarray(offset, offset + chunkBytes));
  }
  return { sizeBytes: bytes.byteLength, open: () => Readable.from(chunks) };
}

function collectingWritable(): { writable: Writable; written: () => Buffer } {
  const parts: Buffer[] = [];
  const writable = new Writable({
    write(chunk: Buffer, _encoding, callback) {
      parts.push(Buffer.from(chunk));
      callback();
    },
  });
  return { writable, written: () => Buffer.concat(parts) };
}

function readerOver(bytes: Buffer, readChunkBytes = 7): CargoByteReader {
  let offset = 0;
  let closed = false;
  return {
    read: (into) => {
      if (closed) {
        return Promise.reject(new Error("read after close"));
      }
      const take = Math.min(into.byteLength, readChunkBytes, bytes.byteLength - offset);
      bytes.copy(into, 0, offset, offset + take);
      offset += take;
      return Promise.resolve(take);
    },
    close: () => {
      closed = true;
    },
  };
}

async function receiveAll(
  bytes: Buffer,
  sizes: number[],
): Promise<{ received: Buffer[]; reader: CargoByteReader }> {
  const reader = readerOver(bytes);
  const receiver = createCargoReceiver(reader);
  const received: Buffer[] = [];
  for (const sizeBytes of sizes) {
    const parts: Buffer[] = [];
    await receiver.next(sizeBytes, (chunk) => {
      parts.push(Buffer.from(chunk));
    });
    received.push(Buffer.concat(parts));
  }
  return { received, reader };
}

describe("cargo framing", () => {
  it("round-trips several entries through the length-prefixed stream, small reads included", async () => {
    const docs = [Buffer.from("first document body"), Buffer.alloc(0), Buffer.from("third")];
    const { writable, written } = collectingWritable();
    await writeCargo(docs.map(sourceOf), writable);
    const wire = written();
    expect(wire.byteLength).toBe(
      docs.reduce((total, doc) => total + CARGO_LENGTH_PREFIX_BYTES + doc.byteLength, 0),
    );
    const { received } = await receiveAll(
      wire,
      docs.map((doc) => doc.byteLength),
    );
    expect(received.map((entry) => entry.toString("utf8"))).toEqual(docs.map((doc) => doc.toString("utf8")));
  });

  it("delivers a multi-chunk source without ever concatenating it into one buffer", async () => {
    const body = Buffer.alloc(64 * 1024, 7);
    const { writable, written } = collectingWritable();
    await writeCargo([chunkedSourceOf(body, 1000)], writable);
    const { received } = await receiveAll(written(), [body.byteLength]);
    expect(received[0]?.equals(body)).toBe(true);
  });

  it("rejects an entry whose length prefix disagrees with the declared size", async () => {
    const { writable, written } = collectingWritable();
    await writeCargo([sourceOf(Buffer.from("abc"))], writable);
    const receiver = createCargoReceiver(readerOver(written()));
    await expect(receiver.next(4, () => {})).rejects.toThrow(CargoChannelError);
  });

  it("rejects a stream that ends before the declared bytes arrive", async () => {
    const { writable, written } = collectingWritable();
    await writeCargo([sourceOf(Buffer.from("abcdef"))], writable);
    const truncated = written().subarray(0, CARGO_LENGTH_PREFIX_BYTES + 3);
    const receiver = createCargoReceiver(readerOver(truncated));
    await expect(receiver.next(6, () => {})).rejects.toThrow(CargoChannelError);
  });

  it("rejects a stream that ends before a whole length prefix arrives", async () => {
    const receiver = createCargoReceiver(readerOver(Buffer.from([0, 0, 0])));
    await expect(receiver.next(1, () => {})).rejects.toThrow(CargoChannelError);
  });

  it("fails the write when a source yields a different byte count than it declared", async () => {
    const { writable } = collectingWritable();
    const lyingSource: CargoSource = { sizeBytes: 10, open: () => Readable.from([Buffer.from("abc")]) };
    await expect(writeCargo([lyingSource], writable)).rejects.toThrow(CargoChannelError);
  });

  it("fails the write when the destination reports an error instead of crashing the caller", async () => {
    const writable = new Writable({
      write(_chunk, _encoding, callback) {
        callback(Object.assign(new Error("broken pipe"), { code: "EPIPE" }));
      },
    });
    writable.on("error", () => {});
    await expect(writeCargo([sourceOf(Buffer.from("abc"))], writable)).rejects.toThrow(CargoChannelError);
  });

  it("honours destination backpressure: never writes the next chunk before the last one is accepted", async () => {
    let inFlight = 0;
    let maxInFlight = 0;
    const writable = new Writable({
      highWaterMark: 1,
      write(_chunk: Buffer, _encoding, callback) {
        inFlight += 1;
        maxInFlight = Math.max(maxInFlight, inFlight);
        setTimeout(() => {
          inFlight -= 1;
          callback();
        }, 1);
      },
    });
    const body = Buffer.alloc(8 * 1024, 3);
    await writeCargo([chunkedSourceOf(body, 512)], writable);
    expect(maxInFlight).toBe(1);
  });
});

describe("cargo over a real child process pipe", () => {
  const CHILD_SCRIPT = `
    const { read, closeSync, fstatSync } = require("node:fs");
    const { createHash } = require("node:crypto");
    const readOnce = (into) =>
      new Promise((resolve, reject) =>
        read(${CARGO_FD}, into, 0, into.length, null, (error, bytesRead) =>
          error ? reject(error) : resolve(bytesRead),
        ),
      );
    const main = async () => {
      const hash = createHash("sha256");
      let total = 0;
      const buffer = Buffer.alloc(64 * 1024);
      while (true) {
        const bytesRead = await readOnce(buffer);
        if (bytesRead === 0) break;
        hash.update(buffer.subarray(0, bytesRead));
        total += bytesRead;
      }
      closeSync(${CARGO_FD});
      let closed = false;
      try {
        fstatSync(${CARGO_FD});
      } catch {
        closed = true;
      }
      process.stdout.write(JSON.stringify({ total, sha256: hash.digest("hex"), closed }));
    };
    main().catch((error) => {
      process.stderr.write(String(error));
      process.exit(1);
    });
  `;

  it("streams bytes a real child reads from fd 3, and the child can close the fd behind itself", async () => {
    const child = spawn(process.execPath, ["-e", CHILD_SCRIPT], {
      stdio: ["ignore", "pipe", "inherit", "pipe"],
    });
    const cargo = child.stdio[CARGO_FD];
    if (!(cargo instanceof Writable)) {
      throw new Error("child stdio[3] is not writable");
    }
    cargo.on("error", () => {});
    const body = Buffer.alloc(4 * 1024 * 1024);
    for (let index = 0; index < body.byteLength; index += 1) {
      body[index] = index % 251;
    }
    let stdout = "";
    child.stdout?.setEncoding("utf8");
    child.stdout?.on("data", (chunk: string) => {
      stdout += chunk;
    });
    await writeCargo([chunkedSourceOf(body, 64 * 1024)], cargo);
    const [code] = (await once(child, "close")) as [number | null];
    expect(code).toBe(0);
    const report = JSON.parse(stdout) as { total: number; sha256: string; closed: boolean };
    expect(report.total).toBe(CARGO_LENGTH_PREFIX_BYTES + body.byteLength);
    expect(report.closed).toBe(true);
    const streamed = Buffer.concat([expectedPrefixOf(body.byteLength), body]);
    expect(report.sha256).toBe(sha256Hex(streamed));
  });

  it("survives the child dying mid-stream: the write fails, the test process does not", async () => {
    const child = spawn(process.execPath, ["-e", "process.exit(0)"], {
      stdio: ["ignore", "ignore", "inherit", "pipe"],
    });
    const cargo = child.stdio[CARGO_FD];
    if (!(cargo instanceof Writable)) {
      throw new Error("child stdio[3] is not writable");
    }
    cargo.on("error", () => {});
    await once(child, "close");
    const body = Buffer.alloc(8 * 1024 * 1024, 9);
    await expect(writeCargo([sourceOf(body)], cargo)).rejects.toThrow(CargoChannelError);
  });

  // A write pending under backpressure when the child dies must be settled by the pipe's owner
  // (exit-keyed abort + /proc death poll, as the spawner wires it). Run in a plain-bun
  // subprocess: that is the production shape, and a SIGKILLed child with a pending write has
  // wedged later spawning tests in the runner process, so the dirty state must die with it.
  it(
    "settles a write left pending at the child's death: rejects and releases the source",
    async () => {
      const channelModule = new URL("./document-channel.ts", import.meta.url).pathname;
      const uidProcessesModule = new URL("./uid-processes.ts", import.meta.url).pathname;
      const scenario = `
      const { spawn } = require("node:child_process");
      const { Readable } = require("node:stream");
      const { writeCargo, CargoChannelError } = require(${JSON.stringify(channelModule)});
      const { isPidDeadOrZombie } = require(${JSON.stringify(uidProcessesModule)});
      const main = async () => {
        const child = spawn(
          process.execPath,
          ["-e", "const b = Buffer.alloc(1024); require('node:fs').read(3, b, 0, 1024, null, () => {});"],
          { stdio: ["ignore", "ignore", "inherit", "pipe"] },
        );
        const cargo = child.stdio[3];
        cargo.on("error", () => {});
        const teardown = new AbortController();
        const tearDown = () => {
          teardown.abort();
        };
        child.on("exit", tearDown);
        child.on("close", () => cargo.destroy());
        const pid = child.pid;
        const deathPoll = setInterval(() => {
          if (pid !== undefined && isPidDeadOrZombie(pid)) {
            tearDown();
            clearInterval(deathPoll);
          }
        }, 20);
        deathPoll.unref();
        const body = Buffer.alloc(64 * 1024 * 1024, 7);
        let sourceDestroyed = false;
        const source = {
          sizeBytes: body.byteLength,
          open: () => {
            const stream = Readable.from([body]);
            stream.on("close", () => {
              sourceDestroyed = true;
            });
            return stream;
          },
        };
        const writing = writeCargo([source], cargo, { signal: teardown.signal });
        setTimeout(() => child.kill("SIGKILL"), 50);
        const outcome = await Promise.race([
          writing.then(
            () => "resolved",
            (error) => (error instanceof CargoChannelError ? "cargo-error" : "other-error"),
          ),
          new Promise((resolve) => setTimeout(() => resolve("hung"), 4000).unref()),
        ]);
        for (let turn = 0; turn < 200 && !sourceDestroyed; turn += 1) {
          await new Promise((resolve) => setTimeout(resolve, 5));
        }
        clearInterval(deathPoll);
        require("node:fs").writeFileSync(process.env["SCENARIO_REPORT_PATH"] ?? "", JSON.stringify({ outcome, sourceDestroyed }));
        process.exit(0);
      };
      main().catch((error) => {
        process.stderr.write(String(error));
        process.exit(1);
      });
    `;
      expect(await scenarioReport(scenario)).toEqual({ outcome: "cargo-error", sourceDestroyed: true });
    },
    CARGO_DEATH_TEST_TIMEOUT_MS,
  );

  it("settles a pending write when the writable is destroyed under it", async () => {
    const neverAccepting = new Writable({
      highWaterMark: 1,
      write() {},
    });
    neverAccepting.on("error", () => {});
    const body = Buffer.alloc(1024, 5);
    const writing = writeCargo([sourceOf(body)], neverAccepting);
    setTimeout(() => neverAccepting.destroy(), 20);
    await expect(writing).rejects.toThrow(CargoChannelError);
  });

  it("rejects immediately when asked to write to an already-destroyed pipe", async () => {
    const dead = new Writable({
      write(_chunk, _encoding, callback) {
        callback();
      },
    });
    dead.on("error", () => {});
    dead.destroy();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await expect(writeCargo([sourceOf(Buffer.from("abc"))], dead)).rejects.toThrow(CargoChannelError);
  });
});

function expectedPrefixOf(sizeBytes: number): Buffer {
  const prefix = Buffer.alloc(CARGO_LENGTH_PREFIX_BYTES);
  prefix.writeBigUInt64BE(BigInt(sizeBytes));
  return prefix;
}

// The verdict travels via the filesystem, polled — never via the runner's stdio or 'close' event:
// under accumulated runner state child-event delivery has been observed to fail entirely, and a
// lost event must not fail a test.
async function scenarioReport(scenario: string): Promise<unknown> {
  const dir = mkdtempSync(join(tmpdir(), "cargo-scenario-"));
  const reportPath = join(dir, "report.json");
  const runner = spawn(process.execPath, ["-e", scenario], {
    stdio: ["ignore", "ignore", "inherit"],
    env: { ...process.env, SCENARIO_REPORT_PATH: reportPath },
  });
  const deadline = Date.now() + SCENARIO_REPORT_DEADLINE_MS;
  let report: unknown;
  while (report === undefined && Date.now() < deadline) {
    try {
      report = JSON.parse(readFileSync(reportPath, "utf8"));
    } catch {
      await new Promise((resolve) => setTimeout(resolve, SCENARIO_REPORT_POLL_MS));
    }
  }
  runner.kill("SIGKILL");
  rmSync(dir, { recursive: true, force: true });
  return report;
}

// The writable itself emits error/close within a millisecond of the child's death, so a pending
// write settles without any owner-driven teardown. The owner path above stays wired; these pins
// exist so a runtime that goes back to "never settles" is caught by name rather than by the abort
// race silently doing all the work.
describe("cargo pipe death", () => {
  it(
    "settles a write left pending at the child's death through the stream's own error, with no abort signal",
    async () => {
      const channelModule = new URL("./document-channel.ts", import.meta.url).pathname;
      const scenario = `
      const { spawn } = require("node:child_process");
      const { Readable } = require("node:stream");
      const { writeCargo, CargoChannelError } = require(${JSON.stringify(channelModule)});
      const main = async () => {
        const child = spawn(
          process.execPath,
          ["-e", "const b = Buffer.alloc(1024); require('node:fs').read(3, b, 0, 1024, null, () => {});"],
          { stdio: ["ignore", "ignore", "inherit", "pipe"] },
        );
        const cargo = child.stdio[3];
        cargo.on("error", () => {});
        const body = Buffer.alloc(64 * 1024 * 1024, 7);
        const source = { sizeBytes: body.byteLength, open: () => Readable.from([body]) };
        const writing = writeCargo([source], cargo);
        setTimeout(() => child.kill("SIGKILL"), 50);
        const outcome = await Promise.race([
          writing.then(
            () => "resolved",
            (error) => (error instanceof CargoChannelError ? "cargo-error" : "other-error"),
          ),
          new Promise((resolve) => setTimeout(() => resolve("hung"), 4000).unref()),
        ]);
        require("node:fs").writeFileSync(process.env["SCENARIO_REPORT_PATH"] ?? "", JSON.stringify({ outcome }));
        process.exit(0);
      };
      main().catch((error) => {
        process.stderr.write(String(error));
        process.exit(1);
      });
    `;
      expect(await scenarioReport(scenario)).toEqual({ outcome: "cargo-error" });
    },
    CARGO_DEATH_TEST_TIMEOUT_MS,
  );

  it(
    "calls a pending write's own callback with ECANCELED when the child reading the pipe dies",
    async () => {
      const scenario = `
      const { spawn } = require("node:child_process");
      const child = spawn(process.execPath, ["-e", "setInterval(() => {}, 1000)"], {
        stdio: ["ignore", "ignore", "inherit", "pipe"],
      });
      const cargo = child.stdio[3];
      cargo.on("error", () => {});
      let pending = false;
      const settled = new Promise((resolve) => {
        pending = !cargo.write(Buffer.alloc(1024 * 1024, 7), (error) => resolve(error ? String(error.code) : "accepted"));
      });
      child.kill("SIGKILL");
      Promise.race([
        settled,
        new Promise((resolve) => setTimeout(() => resolve("never-called"), 4000)),
      ]).then((callback) => {
        require("node:fs").writeFileSync(process.env["SCENARIO_REPORT_PATH"] ?? "", JSON.stringify({ pending, callback }));
        process.exit(0);
      });
    `;
      expect(await scenarioReport(scenario)).toEqual({ pending: true, callback: "ECANCELED" });
    },
    CARGO_DEATH_TEST_TIMEOUT_MS,
  );
});
