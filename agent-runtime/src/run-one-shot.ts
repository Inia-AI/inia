import { closeSync, read } from "node:fs";
import { createAsyncQueue } from "./async-queue.js";
import { CARGO_FD, type CargoByteReader } from "./document-channel.js";
import { toError } from "./errors.js";
import { createLogger, stderrOnlySink } from "./logger.js";
import { biasOomScoreAdj } from "./oom.js";
import { deliverFinalFrame, runChild } from "./run-child.js";
import { createLineSplitter } from "./run-wire.js";
import { probeSandbox } from "./sandbox-probe.js";

function createStdinLines(): { next(): Promise<string> } {
  const queue = createAsyncQueue<string>();
  const feed = createLineSplitter((line) => queue.push(line));
  process.stdin.setEncoding("utf8");
  process.stdin.on("data", (chunk: string) => {
    try {
      feed(chunk);
    } catch (error) {
      queue.fail(toError(error));
    }
  });
  process.stdin.on("end", () => queue.fail(new Error("stdin closed before the expected frame arrived")));
  process.stdin.on("error", (error: Error) => queue.fail(error));
  return queue;
}

// Idempotent close: runChild closes after staging on every path, and a double close must
// not turn a failing run into an EBADF crash.
function createCargoFdReader(): CargoByteReader {
  let closed = false;
  return {
    read: (into) =>
      new Promise((resolve, reject) => {
        read(CARGO_FD, into, 0, into.byteLength, null, (error, bytesRead) => {
          if (error === null) {
            resolve(bytesRead);
          } else {
            reject(error);
          }
        });
      }),
    close: () => {
      if (!closed) {
        closed = true;
        closeSync(CARGO_FD);
      }
    },
  };
}

const oneShotLog = createLogger({ sink: stderrOnlySink });

if (!biasOomScoreAdj()) {
  oneShotLog.warn("oom_bias_failed", {
    detail: "this run child is no likelier to be OOM-killed than the supervisor hosting every run",
  });
}

process.stdout.on("error", (error: Error) => oneShotLog.error("frame_channel_errored", { error }));

const response = await runChild({
  lines: createStdinLines(),
  writeFrame: (frame) => process.stdout.write(`${JSON.stringify(frame)}\n`),
  env: process.env,
  probeSandboxFn: probeSandbox,
  cargo: createCargoFdReader(),
  onTerminationRequested: (cleanup) =>
    process.on("SIGTERM", () => {
      cleanup();
      process.exit(0);
    }),
});
// The stdin listener keeps the event loop alive forever, so the child must exit itself.
deliverFinalFrame({
  response,
  write: (line, callback) => process.stdout.write(line, callback),
  exit: (code) => process.exit(code),
});
