// The process frame both loopback entry points share — the gateway shim's and the Anthropic key
// holder's. Each entry point reads process.env and hands it here with the server it starts.
import { parseLogLevel } from "./config.js";
import { ConfigError } from "./config-error.js";
import { toError } from "./errors.js";
import { bestEffortSink, createLogger, type Logger, stdoutStderrSink } from "./logger.js";

type Env = Record<string, string | undefined>;

export async function runLoopbackEntry(
  env: Env,
  start: (env: Env, log: Logger) => Promise<{ close(): Promise<void> }>,
): Promise<void> {
  // Best-effort on purpose: stdout is a pipe the supervisor owns, and a pipe that dies under a
  // living parent (ADR 0129) must cost a log line, never the process.
  const log = createLogger({
    level: parseLogLevel(env["LOG_LEVEL"]),
    sink: bestEffortSink(stdoutStderrSink),
  });

  // Bun raises a dead stdout's EPIPE as a stream error after the write returns — outside any
  // try/catch the sink could hold (measured in the full suite). A log stream that dies costs its
  // lines, never the process.
  for (const stream of [process.stdout, process.stderr]) {
    stream.on("error", () => undefined);
  }

  process.on("unhandledRejection", (reason) => {
    log.error("unhandled_rejection", { error: toError(reason) });
    process.exit(1);
  });

  process.on("uncaughtException", (error) => {
    log.error("uncaught_exception", { error });
    process.exit(1);
  });

  try {
    const server = await start(env, log);
    const shutdown = (): void => {
      server.close().then(
        () => process.exit(0),
        () => process.exit(1),
      );
    };
    process.on("SIGTERM", shutdown);
    process.on("SIGINT", shutdown);
  } catch (error) {
    if (error instanceof ConfigError) {
      log.error("configuration_error", { error });
      process.exit(1);
    }
    throw error;
  }
}
