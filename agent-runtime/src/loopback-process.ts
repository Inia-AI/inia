// The supervisor's half of a loopback model process — the gateway shim (ADR 0137) or the Anthropic
// key holder (ADR 0151): spawned at boot under the same caps-clearing setpriv hop as a run child
// but as its own uid outside the pool, handed exactly its settings, polled to readiness, and
// reported when it dies.
import { spawn } from "node:child_process";
import { connect } from "node:net";
import { UID_POOL_BASE } from "./config.js";
import { messageOf } from "./errors.js";
import { createLogger, type Logger } from "./logger.js";
import { HEALTH_PATH, HTTP_OK } from "./loopback-http.js";
import { LOOPBACK_HOST_ENV_VAR, LOOPBACK_PORT_ENV_VAR } from "./model-provider.js";
import { privilegeDropArgs, signalRunArgv } from "./setpriv.js";
import { isPidDeadOrZombie } from "./uid-processes.js";

// Directly below the pool: never leased to a run, never swept as run residue. The modes are
// exclusive, so one uid serves whichever loopback process the mode starts.
export const LOOPBACK_PROCESS_UID = UID_POOL_BASE - 1;
const READY_TIMEOUT_MS = 15_000;
const READY_POLL_MS = 100;
// Per probe attempt: a process that accepts the connection and never answers must not outlast the
// ready deadline, which is only checked between attempts.
const PROBE_TIMEOUT_MS = 1_000;
const STOP_GRACE_MS = 5_000;
// What a loopback process inherits from the supervisor beside its own settings; everything else in
// its environment is authored, never spread — which keeps proxy variables, AUTH_TOKEN and every
// other supervisor secret out by construction rather than by denylist.
const INHERITED_ENV_VARS: readonly string[] = ["PATH", "LOG_LEVEL"];

export type LoopbackExit = { code: number | null; signal: string | null };

export type LoopbackChild = {
  pid?: number | undefined;
  on(event: "exit", listener: (code: number | null, signal: NodeJS.Signals | null) => void): unknown;
  on(event: "error", listener: (error: Error) => void): unknown;
};

export type SpawnLoopbackFn = (command: string, args: string[], env: Record<string, string>) => LoopbackChild;

const DEATH_POLL_MS = 100;

const spawnLoopbackProcess: SpawnLoopbackFn = (command, args, env) =>
  spawn(command, args, { env, stdio: ["ignore", "inherit", "inherit"] });

export type LoopbackProcessHandle = {
  pid: number;
  port: number;
  stop(): Promise<void>;
  signal(signal: "KILL" | "TERM"): void;
};

// The log events derive from the name — `<name>_started`, `_exited`, `_child_error` — so each
// process keeps the event names its consumers already read.
export type LoopbackProcessName = "anthropic_proxy";

export type LoopbackProcessSpec = {
  name: LoopbackProcessName;
  entry: string;
  label: string;
  env: Record<string, string>;
  host: string;
  port: number;
};

export type StartLoopbackProcessDeps = {
  onExit: (exit: LoopbackExit) => void;
  uid?: number;
  logger?: Logger;
  readyTimeoutMs?: number;
  probeTimeoutMs?: number;
  spawnFn?: SpawnLoopbackFn;
};

export function loopbackProcessEnv(input: {
  supervisorEnv: Record<string, string | undefined>;
  host: string;
  port: number;
  settings: Record<string, string>;
}): Record<string, string> {
  const inherited: Record<string, string> = {};
  for (const name of INHERITED_ENV_VARS) {
    const value = input.supervisorEnv[name];
    if (value !== undefined) {
      inherited[name] = value;
    }
  }
  return {
    ...inherited,
    ...input.settings,
    [LOOPBACK_HOST_ENV_VAR]: input.host,
    [LOOPBACK_PORT_ENV_VAR]: String(input.port),
  };
}

export function loopbackProcessArgv(uid: number, entry: string): string[] {
  return ["setpriv", ...privilegeDropArgs(uid), "--", "bun", entry];
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// A raw socket, not an HTTP client: Bun's clients read HTTP_PROXY from the process env, and the
// supervisor's env on a corporate network legitimately carries one (measured by the test that
// sets it). Loopback must never be probed through a proxy.
export function probeLoopbackHealth(
  host: string,
  port: number,
  timeoutMs: number = PROBE_TIMEOUT_MS,
): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = connect({ host, port }, () => {
      socket.write(`GET ${HEALTH_PATH} HTTP/1.1\r\nHost: ${host}\r\nConnection: close\r\n\r\n`);
    });
    // The status line is the whole answer; Bun's server may never close the socket on its own.
    let head = "";
    const settle = (): void => {
      clearTimeout(timer);
      resolve(head.startsWith(`HTTP/1.1 ${HTTP_OK}`));
      socket.destroy();
    };
    const timer = setTimeout(settle, timeoutMs);
    socket.setEncoding("utf8");
    socket.on("data", (chunk: string) => {
      head += chunk;
      if (head.includes("\r\n")) {
        settle();
      }
    });
    socket.on("close", settle);
    socket.on("error", () => {
      clearTimeout(timer);
      resolve(false);
    });
  });
}

function signalAsOwner(uid: number, signal: "KILL" | "TERM"): void {
  const [command, ...args] = signalRunArgv(uid, signal);
  if (command === undefined) {
    return;
  }
  spawn(command, args, { stdio: "ignore" }).on("error", () => undefined);
}

export async function startLoopbackProcess(
  spec: LoopbackProcessSpec,
  deps: StartLoopbackProcessDeps,
): Promise<LoopbackProcessHandle> {
  const log = deps.logger ?? createLogger();
  const { host, port } = spec;
  const events = {
    started: `${spec.name}_started`,
    exited: `${spec.name}_exited`,
    childError: `${spec.name}_child_error`,
  } as const;
  const uid = deps.uid ?? LOOPBACK_PROCESS_UID;
  const readyTimeoutMs = deps.readyTimeoutMs ?? READY_TIMEOUT_MS;
  const probeTimeoutMs = deps.probeTimeoutMs ?? PROBE_TIMEOUT_MS;
  const [command, ...args] = loopbackProcessArgv(uid, spec.entry);
  if (command === undefined) {
    throw new Error(`${spec.label} argv is empty`);
  }
  const spawnFn = deps.spawnFn ?? spawnLoopbackProcess;
  const child = spawnFn(command, args, spec.env);
  let exit: LoopbackExit | undefined;
  let spawnFailure: string | undefined;
  let stopping = false;
  let ready = false;
  // An error before any pid exists is a failed spawn. After one, it is the runner-state class
  // ADR 0121 records (Bun reports EBADF from epoll_ctl on a child that is alive and listening,
  // and may then never deliver `exit`): readiness and the /proc death poll decide, not the event.
  child.on("error", (error) => {
    if (child.pid === undefined) {
      spawnFailure = messageOf(error);
      exit = { code: null, signal: null };
      log.error(events.childError, { error });
      log.error(events.exited, exit);
      return;
    }
    log.warn(events.childError, { error });
  });
  child.on("exit", (code, signal) => {
    // The same runner-state class: an exit carrying no status for a pid /proc still shows alive
    // is not a death.
    if (code === null && signal === null && child.pid !== undefined && !isPidDeadOrZombie(child.pid)) {
      log.warn(events.childError, { error: new Error("exit event without status for a live pid") });
      return;
    }
    exit = { code, signal };
    if (stopping) {
      return;
    }
    log.error(events.exited, { code, signal });
    // Before readiness the start itself reports the death, with its status; onExit is the
    // supervisor's post-boot drain and would discard that diagnostic.
    if (ready) {
      deps.onExit(exit);
    }
  });
  const healthUrl = `http://${host}:${port}${HEALTH_PATH}`;
  const deadline = Date.now() + readyTimeoutMs;
  while (exit === undefined && !(await probeLoopbackHealth(host, port, probeTimeoutMs))) {
    if (Date.now() > deadline) {
      stopping = true;
      signalAsOwner(uid, "KILL");
      throw new Error(`${spec.label} did not become ready on ${healthUrl} within ${readyTimeoutMs} ms`);
    }
    await sleep(READY_POLL_MS);
  }
  if (exit !== undefined) {
    const cause =
      spawnFailure === undefined
        ? `code ${String(exit.code)}, signal ${String(exit.signal)}`
        : `spawn failed: ${spawnFailure}`;
    throw new Error(`${spec.label} exited before it was ready (${cause})`);
  }
  ready = true;
  const pid = child.pid ?? 0;
  log.info(events.started, { pid, port });
  // Death is the exit event or a /proc state, never the event alone (ADR 0121).
  const deadWithin = async (timeoutMs: number): Promise<boolean> => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (exit !== undefined || isPidDeadOrZombie(pid)) {
        return true;
      }
      await sleep(DEATH_POLL_MS);
    }
    return exit !== undefined || isPidDeadOrZombie(pid);
  };
  return {
    pid,
    port,
    signal: (signal) => signalAsOwner(uid, signal),
    stop: async () => {
      if (exit !== undefined) {
        return;
      }
      stopping = true;
      signalAsOwner(uid, "TERM");
      if (await deadWithin(STOP_GRACE_MS)) {
        return;
      }
      signalAsOwner(uid, "KILL");
      await deadWithin(STOP_GRACE_MS);
    },
  };
}
