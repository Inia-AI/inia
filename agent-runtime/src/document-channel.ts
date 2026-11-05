import type { Readable, Writable } from "node:stream";

// The binary side-channel documents ride between supervisor and run child: the NDJSON
// frames on stdin carry metadata only, and each staged file's raw bytes cross this fd as
// one length-prefixed entry, in the canonical staging order both ends derive from the
// frame. One chunk in flight on either side is the whole point — never buffer an entry.
export const CARGO_FD = 3;
export const CARGO_LENGTH_PREFIX_BYTES = 8;
const READ_CHUNK_BYTES = 1024 * 1024;

export class CargoChannelError extends Error {}

export type CargoSource = { sizeBytes: number; open(): Readable };

export type CargoByteReader = {
  read(into: Buffer): Promise<number>;
  close(): void;
};

export type CargoReceiver = {
  next(expectedSizeBytes: number, sink: (chunk: Uint8Array) => void): Promise<void>;
};

function lengthPrefixOf(sizeBytes: number): Buffer {
  const prefix = Buffer.alloc(CARGO_LENGTH_PREFIX_BYTES);
  prefix.writeBigUInt64BE(BigInt(sizeBytes));
  return prefix;
}

export type WriteCargoOptions = {
  // Tripped by the pipe's owner when the peer is known dead (the spawner aborts on child
  // exit). This is the teardown signal the channel TRUSTS; the writable's own
  // 'close'/'error' events are raced as a bonus but never relied on.
  signal?: AbortSignal;
};

// The write callback is the backpressure signal: awaiting it keeps exactly one chunk in flight.
// It is not a liveness signal: a write left pending when the peer dies never fires it. So every
// write also races the writable's own close/error and an owner-held abort signal, tripped on the
// child's exit.
function writeChunk(writable: Writable, chunk: Uint8Array, signal: AbortSignal | undefined): Promise<void> {
  return new Promise((resolve, reject) => {
    if (writable.destroyed || writable.writableEnded || signal?.aborted === true) {
      reject(new CargoChannelError("cargo pipe is already closed"));
      return;
    }
    let settled = false;
    const settle = (outcome: () => void): void => {
      if (!settled) {
        settled = true;
        writable.off("close", onTornDown);
        writable.off("error", onFailed);
        signal?.removeEventListener("abort", onTornDown);
        outcome();
      }
    };
    const onTornDown = (): void => {
      settle(() => reject(new CargoChannelError("cargo pipe closed before the chunk was accepted")));
    };
    const onFailed = (error: Error): void => {
      settle(() => reject(new CargoChannelError(`cargo write failed: ${error.message}`)));
    };
    writable.once("close", onTornDown);
    writable.once("error", onFailed);
    signal?.addEventListener("abort", onTornDown, { once: true });
    writable.write(chunk, (error) => {
      if (error === undefined || error === null) {
        settle(resolve);
      } else {
        onFailed(error);
      }
    });
  });
}

// Resolves rather than rejects on a torn-down pipe: every entry's bytes were already
// accepted by the time end runs, and the child verifies delivery by sha256 regardless.
function endCargo(writable: Writable, signal: AbortSignal | undefined): Promise<void> {
  return new Promise((resolve) => {
    if (writable.destroyed || writable.writableEnded || signal?.aborted === true) {
      resolve();
      return;
    }
    let settled = false;
    const settle = (): void => {
      if (!settled) {
        settled = true;
        writable.off("close", settle);
        writable.off("error", settle);
        signal?.removeEventListener("abort", settle);
        resolve();
      }
    };
    writable.once("close", settle);
    writable.once("error", settle);
    signal?.addEventListener("abort", settle, { once: true });
    writable.end(settle);
  });
}

export async function writeCargo(
  entries: readonly CargoSource[],
  writable: Writable,
  options: WriteCargoOptions = {},
): Promise<void> {
  const signal = options.signal;
  for (const entry of entries) {
    await writeChunk(writable, lengthPrefixOf(entry.sizeBytes), signal);
    const source = entry.open();
    let sentBytes = 0;
    try {
      for await (const chunk of source) {
        const bytes = Buffer.isBuffer(chunk) ? chunk : Buffer.from(String(chunk));
        sentBytes += bytes.byteLength;
        if (sentBytes > entry.sizeBytes) {
          throw new CargoChannelError(
            `cargo source delivered more than its declared ${entry.sizeBytes} bytes`,
          );
        }
        await writeChunk(writable, bytes, signal);
      }
    } finally {
      source.destroy();
    }
    if (sentBytes !== entry.sizeBytes) {
      throw new CargoChannelError(
        `cargo source ended at ${sentBytes} bytes where it declared ${entry.sizeBytes}`,
      );
    }
  }
  await endCargo(writable, signal);
}

export function createCargoReceiver(reader: CargoByteReader): CargoReceiver {
  // Reads never over-ask: each asks for at most what the current prefix or body still
  // needs, so a read can never span into the next entry and no carry-over buffer exists.
  async function readMore(maxBytes: number): Promise<Buffer> {
    const into = Buffer.allocUnsafe(Math.min(maxBytes, READ_CHUNK_BYTES));
    const bytesRead = await reader.read(into);
    if (bytesRead === 0) {
      throw new CargoChannelError("cargo stream ended before the declared bytes arrived");
    }
    return into.subarray(0, bytesRead);
  }

  async function readExact(byteLength: number, sink: (chunk: Uint8Array) => void): Promise<void> {
    let remaining = byteLength;
    while (remaining > 0) {
      const chunk = await readMore(remaining);
      sink(chunk);
      remaining -= chunk.byteLength;
    }
  }

  async function readPrefix(): Promise<number> {
    const prefix = Buffer.alloc(CARGO_LENGTH_PREFIX_BYTES);
    let filled = 0;
    while (filled < CARGO_LENGTH_PREFIX_BYTES) {
      const chunk = await readMore(CARGO_LENGTH_PREFIX_BYTES - filled);
      prefix.set(chunk, filled);
      filled += chunk.byteLength;
    }
    const declared = prefix.readBigUInt64BE();
    if (declared > BigInt(Number.MAX_SAFE_INTEGER)) {
      throw new CargoChannelError("cargo length prefix exceeds the safe integer range");
    }
    return Number(declared);
  }

  return {
    async next(expectedSizeBytes, sink) {
      const prefixed = await readPrefix();
      if (prefixed !== expectedSizeBytes) {
        throw new CargoChannelError(
          `cargo entry carries a ${prefixed}-byte length prefix where the frame declared ${expectedSizeBytes} bytes`,
        );
      }
      await readExact(expectedSizeBytes, sink);
    },
  };
}
