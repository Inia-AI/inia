import { createAdmissionQueue } from "./admission-queue.js";
import { startAnthropicProxy } from "./anthropic-proxy-process.js";
import { createLocalCheckpointStore } from "./checkpoint-store.js";
import { assertCliTmpDirExecutable, cliTmpDirOf, probeExecCapability } from "./cli-tmp-exec.js";
import { loadServerConfig, MS_PER_MINUTE, UID_POOL_BASE, UID_POOL_SIZE, WORKSPACE_ROOT } from "./config.js";
import { ConfigError } from "./config-error.js";
import {
  envConfigProvider,
  resolveConfigValues,
  SETTINGS_DIRECTORIES,
  settingsFileProvider,
} from "./config-provider.js";
import { createLocalDocumentStore } from "./document-store.js";
import { toError } from "./errors.js";
import { createLogger, type Logger } from "./logger.js";
import type { LoopbackProcessHandle } from "./loopback-process.js";
import { assertMemoryFeedsUidPool, readCgroupMemoryLimit } from "./memory-pairing.js";
import type { ModelProviderConfig } from "./model-provider.js";
import { assertPidsFeedUidPool, readCgroupPidsLimit } from "./pids-pairing.js";
import { createRunSpawner, createUidSweeper } from "./run-spawner.js";
import { startServer } from "./server.js";
import { createUidPool } from "./uid-pool.js";
import { createUidReclaimer } from "./uid-reclaimer.js";
import { claimRetainedRoot, retainedRootsUnder } from "./workspace-layout.js";

const log = createLogger();

const SHUTDOWN_FORCE_AFTER_MS = 30_000;
const LOOPBACK_PROCESS_DEATH_EXIT_CODE = 1;

// Before the run API listens: a run admitted with no loopback process to talk to would fail on its
// first model call, and one that dies later takes the supervisor down with it — the container's
// restart policy brings both back together (ADR 0137, ADR 0151).
function startModelLoopback(
  modelProvider: ModelProviderConfig,
  deps: {
    supervisorEnv: Record<string, string | undefined>;
    logger: Logger;
    onDeath: (reason: string) => void;
  },
): Promise<LoopbackProcessHandle | undefined> {
  const { supervisorEnv, logger, onDeath } = deps;
  switch (modelProvider.provider) {
    case "bedrock":
    case "foundry":
      return Promise.resolve(undefined);
    case "anthropic":
      return startAnthropicProxy({
        proxy: modelProvider.proxy,
        supervisorEnv,
        logger,
        onExit: () => onDeath("anthropic_proxy_exited"),
      });
  }
}

process.on("unhandledRejection", (reason) => {
  log.error("unhandled_rejection", { error: toError(reason) });
  process.exit(1);
});

process.on("uncaughtException", (error) => {
  log.error("uncaught_exception", { error });
  process.exit(1);
});

async function main(): Promise<void> {
  let server: ReturnType<typeof startServer> | undefined;
  let runtimeLog = log;
  let loopbackProcess: LoopbackProcessHandle | undefined;

  const stopLoopbackProcessThenExit = (exitCode: number): void => {
    const exit = (): void => process.exit(exitCode);
    if (loopbackProcess === undefined) {
      exit();
      return;
    }
    loopbackProcess.stop().then(exit, exit);
  };

  // A loopback process that dies before the server exists is a boot failure; after, it drains the
  // runs exactly as SIGTERM does (ADR 0137).
  const shutdown = (signal: string, exitCode: number): void => {
    if (server === undefined) {
      stopLoopbackProcessThenExit(exitCode);
      return;
    }
    const listening = server;
    runtimeLog.warn("shutdown_started", { signal });
    listening.drainRuns();
    listening.close(() => stopLoopbackProcessThenExit(exitCode));
    listening.closeIdleConnections();
    setTimeout(() => {
      runtimeLog.error("shutdown_forced", { signal });
      listening.closeAllConnections();
      stopLoopbackProcessThenExit(1);
    }, SHUTDOWN_FORCE_AFTER_MS).unref();
  };

  try {
    const configValues = await resolveConfigValues([
      envConfigProvider(process.env),
      settingsFileProvider(process.env[SETTINGS_DIRECTORIES]),
    ]);
    const config = loadServerConfig(configValues);
    runtimeLog = createLogger({ level: config.logLevel });
    assertMemoryFeedsUidPool(readCgroupMemoryLimit(), configValues["AGENT_RUNTIME_MEMORY_CHECK_DISABLED"]);
    assertPidsFeedUidPool(readCgroupPidsLimit());
    const cliTmpDir = cliTmpDirOf(config.subprocessEnv);
    assertCliTmpDirExecutable(
      cliTmpDir,
      probeExecCapability(cliTmpDir),
      configValues["AGENT_RUNTIME_TMP_EXEC_CHECK_DISABLED"],
    );
    if (config.modelProvider.provider === "bedrock" && "ambient" in config.modelProvider.identity) {
      runtimeLog.warn("ambient_credentials", {
        detail:
          "the host's AWS credentials are in every run child's environment, where a sandbox or " +
          "read-gate slip can exfiltrate them. Break-glass only: trusted documents, never a " +
          "deployed environment.",
      });
    }
    loopbackProcess = await startModelLoopback(config.modelProvider, {
      supervisorEnv: configValues,
      logger: runtimeLog,
      onDeath: (reason) => shutdown(reason, LOOPBACK_PROCESS_DEATH_EXIT_CODE),
    });
    const checkpointStore = createLocalCheckpointStore(config.checkpointRoot);
    const documentStore = createLocalDocumentStore(config.documentRoot);
    for (const root of retainedRootsUnder(WORKSPACE_ROOT)) {
      claimRetainedRoot(root.path, root.mode);
    }
    const pool = createUidPool({ baseUid: UID_POOL_BASE, size: UID_POOL_SIZE });
    const admission = createAdmissionQueue(
      { poolSize: UID_POOL_SIZE, ...config.admissionClasses },
      {
        freeSlots: () => pool.available(),
        onUnbalancedChatRelease: () => {
          runtimeLog.warn("admission_chat_release_unbalanced", {
            detail: "a chat slot was returned with none held; the count stays at zero",
          });
        },
      },
    );
    pool.onFreed(() => admission.notifySlotFreed());
    const uidSweeper = createUidSweeper({ workspaceRoot: WORKSPACE_ROOT, log: runtimeLog });
    const runReportFn = createRunSpawner({
      pool,
      uidSweeper,
      workspaceRoot: WORKSPACE_ROOT,
      subprocessEnv: config.subprocessEnv,
      modelProvider: config.modelProvider,
      buildIdentity: config.buildIdentity,
      deadlineMs: config.fuses.deadlineMs.defaultValue,
      maxBudgetUsd: config.fuses.maxBudgetUsd.defaultValue,
      maxTurns: config.fuses.maxTurns.defaultValue,
      checkpointStore,
      documentStore,
      documentBudgetBytes: config.documentBudgetBytes,
      pauseGraceMs: config.pauseGraceMinutes * MS_PER_MINUTE,
      logger: runtimeLog,
    });
    createUidReclaimer({
      pool,
      sweepWhenIdle: (uid) => uidSweeper.sweepWhenIdle(uid),
      log: runtimeLog,
    }).start();
    server = startServer(config, {
      runReportFn,
      uidPool: pool,
      checkpointStore,
      documentStore,
      admission,
      logger: runtimeLog,
    });
  } catch (error) {
    await loopbackProcess?.stop();
    if (error instanceof ConfigError) {
      log.error("configuration_error", { error });
      process.exit(1);
    }
    throw error;
  }

  server.on("error", (error) => {
    runtimeLog.error("server_error", { error });
    process.exit(1);
  });

  process.on("SIGTERM", () => shutdown("SIGTERM", 0));
  process.on("SIGINT", () => shutdown("SIGINT", 0));
}

await main();
