import { describe, expect, it } from "bun:test";
import { execFile } from "node:child_process";
import { join } from "node:path";
import { promisify } from "node:util";
import { anthropicProxyEnv } from "./anthropic-proxy-process.js";
import { freePort } from "./loopback-test-helpers.js";
import { isRecord } from "./narrow.js";

const API_KEY = "sk-ant-api03-the-real-key";
const PROCESS_DRIVER_TIMEOUT_MS = 60_000;

// A plain bun subprocess, as the gateway shim's driver is: the full-suite runner state ADR 0121
// records never reproduces there and dies with it.
async function bootInSubprocess(apiKey: string): Promise<Record<string, unknown>> {
  const port = await freePort();
  const modulePath = join(import.meta.dir, "anthropic-proxy-process.ts");
  const script = `
    const m = await import(${JSON.stringify(modulePath)});
    const exits = [];
    const report = { exits };
    try {
      const handle = await m.startAnthropicProxy({
        proxy: { apiKey: ${JSON.stringify(apiKey)} },
        supervisorEnv: { PATH: process.env.PATH },
        port: ${port},
        onExit: (exit) => exits.push(exit),
      });
      report.started = true;
      report.pid = handle.pid;
      const health = await fetch("http://127.0.0.1:${port}/healthz");
      report.healthStatus = health.status;
      await handle.stop();
    } catch (error) {
      report.started = false;
      report.startError = error instanceof Error ? error.message : String(error);
    }
    console.log(JSON.stringify(report));
    process.exit(0);
  `;
  const { stdout } = await promisify(execFile)("bun", ["-e", script], { encoding: "utf8", timeout: 45_000 });
  const lines = stdout.trim().split("\n");
  const parsed: unknown = JSON.parse(lines[lines.length - 1] ?? "{}");
  return isRecord(parsed) ? parsed : {};
}

describe("the Anthropic key holder is handed nothing it does not need", () => {
  it("is handed exactly the key and its listen address — never the supervisor's env, a proxy variable or AUTH_TOKEN", () => {
    expect(
      anthropicProxyEnv({
        proxy: { apiKey: API_KEY },
        host: "127.0.0.1",
        port: 8131,
        supervisorEnv: {
          PATH: "/usr/bin",
          LOG_LEVEL: "debug",
          AUTH_TOKEN: "runtime-token",
          HTTPS_PROXY: "http://proxy.corp:3128",
          ANTHROPIC_MODEL: "claude-sonnet-5-5",
          AWS_ACCESS_KEY_ID: "AKIA",
        },
      }),
    ).toEqual({
      PATH: "/usr/bin",
      LOG_LEVEL: "debug",
      ANTHROPIC_API_KEY: API_KEY,
      AGENT_RUNTIME_LOOPBACK_HOST: "127.0.0.1",
      AGENT_RUNTIME_LOOPBACK_PORT: "8131",
    });
  });
});

describe("the real Anthropic key holder under the supervisor", () => {
  it(
    "boots to ready, answers its health route, and stops on demand",
    async () => {
      const report = await bootInSubprocess(API_KEY);
      expect(report["startError"]).toBeUndefined();
      expect(report["started"]).toBe(true);
      expect(report["pid"]).toBeGreaterThan(0);
      expect(report["healthStatus"]).toBe(200);
      expect(report["exits"]).toEqual([]);
    },
    PROCESS_DRIVER_TIMEOUT_MS,
  );

  it(
    "fails boot, not the run, when it has no key — it exits before readiness",
    async () => {
      const report = await bootInSubprocess("");
      expect(report["started"]).toBe(false);
      expect(report["startError"]).toMatch(
        /Anthropic key holder exited before it was ready \(code 1, signal null\)/,
      );
    },
    PROCESS_DRIVER_TIMEOUT_MS,
  );
});
