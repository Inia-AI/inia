// ADR 0052 left one question to measure before MCP goes live: whether the CLI writes the run token,
// which rides the MCP server's Authorization header, into the session files a checkpoint captures.
// A scripted run in anthropic mode, with an MCP server configured, ends with a terminal checkpoint;
// the token must appear nowhere in it. Zero model cost.
import { describe, expect, it } from "bun:test";
import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync } from "node:fs";
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { startAnthropicProxyServer } from "../anthropic-proxy.js";
import { decodeCheckpointManifest } from "../checkpoint-bundle.js";
import type { CheckpointBundle } from "../checkpoint-store.js";
import { UID_POOL_BASE } from "../config.js";
import { createLocalDocumentStore } from "../document-store.js";
import { createLogger } from "../logger.js";
import { buildProviderSubprocessEnv, LOOPBACK_HOST, LOOPBACK_PORT } from "../model-provider.js";
import { isRecord } from "../narrow.js";
import { removeRunResidue } from "../run-files-test-helpers.js";
import { createRunSpawner } from "../run-spawner.js";
import { createUidPool } from "../uid-pool.js";
import { claimRetainedRoot, RESERVED_WORKSPACE_DIRS, retainedRootsUnder } from "../workspace-layout.js";
import { startScriptedAnthropicStub } from "./anthropic-stub.js";
import { SCRIPTED_FINAL_TEXT, SCRIPTED_RUN_MARKER } from "./scripted-run-contract.js";

const RUN_TIMEOUT_MS = 180000;
const EXECUTION_ID = "run-token-checkpoint";
const MODEL = "claude-sonnet-5-5";
const API_KEY = "sk-ant-api03-RUNTOKEN-CHECKPOINT";
const RUN_TOKEN = "ENG1038-RUN-TOKEN-0f3c9a7e5b";
const RUNS_UNDER_QEMU = process.env["EMULATED_PLATFORM"] === "true";

type McpStub = { url: string; authorizations: string[]; close: () => Promise<void> };

async function bodyOf(request: IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(String(chunk)));
  }
  return Buffer.concat(chunks).toString("utf8");
}

function parsedOrUndefined(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

// Just enough of the MCP streamable HTTP transport for the CLI to connect and list no tools. The
// gateway module's stub server is closed code, so the open tree cannot use it here.
async function answerAsMcpServer(request: IncomingMessage, response: ServerResponse): Promise<void> {
  if (request.method !== "POST") {
    response.writeHead(405).end();
    return;
  }
  const message = parsedOrUndefined(await bodyOf(request));
  if (!isRecord(message)) {
    response.writeHead(400).end();
    return;
  }
  if (!("id" in message)) {
    response.writeHead(202).end();
    return;
  }
  const result =
    message["method"] === "initialize"
      ? {
          protocolVersion: "2025-06-18",
          capabilities: { tools: {} },
          serverInfo: { name: "platform-stub", version: "0.0.0" },
        }
      : { tools: [] };
  response.writeHead(200, { "content-type": "application/json" });
  response.end(JSON.stringify({ jsonrpc: "2.0", id: message["id"], result }));
}

async function startMcpStub(): Promise<McpStub> {
  const authorizations: string[] = [];
  const server = createServer((request, response) => {
    authorizations.push(request.headers.authorization ?? "");
    answerAsMcpServer(request, response).catch(() => response.destroy());
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address() as AddressInfo;
  return {
    url: `http://127.0.0.1:${port}/mcp`,
    authorizations,
    close: () =>
      new Promise((resolve) => {
        server.close(() => resolve());
        server.closeAllConnections();
      }),
  };
}

function checkpointText(checkpoint: CheckpointBundle, workspaceRoot: string): string {
  const manifest = decodeCheckpointManifest(checkpoint.bytes, {
    workspaceRoot,
    reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
  });
  const files = Object.values(manifest.files).map((encoded) =>
    Buffer.from(encoded, "base64").toString("utf8"),
  );
  return [JSON.stringify({ ...manifest, files: Object.keys(manifest.files) }), ...files].join("\n");
}

describe("a terminal checkpoint of a run with an MCP server", () => {
  it(
    "holds no trace of the run token",
    async () => {
      const scratch = mkdtempSync(join(tmpdir(), "run-token-checkpoint-"));
      const workspaceRoot = join(scratch, "workspaces");
      const tmpRoot = join(scratch, "tmp");
      mkdirSync(workspaceRoot);
      mkdirSync(tmpRoot);
      execFileSync("chmod", ["1777", scratch, workspaceRoot, tmpRoot]);
      const stub = await startScriptedAnthropicStub({
        toolCommand: `echo ${SCRIPTED_RUN_MARKER}`,
        finalText: SCRIPTED_FINAL_TEXT,
      });
      const proxy = await startAnthropicProxyServer({
        apiKey: API_KEY,
        upstreamUrl: stub.baseUrl,
        host: LOOPBACK_HOST,
        port: LOOPBACK_PORT,
        logger: createLogger({ level: "error" }),
      });
      const mcp = await startMcpStub();
      const checkpoints: CheckpointBundle[] = [];
      try {
        for (const root of retainedRootsUnder(workspaceRoot)) {
          claimRetainedRoot(root.path, root.mode);
        }
        const spawnRun = createRunSpawner({
          pool: createUidPool({ baseUid: UID_POOL_BASE, size: 1 }),
          workspaceRoot,
          tmpRoot,
          subprocessEnv: buildProviderSubprocessEnv("anthropic", {
            PATH: process.env["PATH"],
            HOME: process.env["HOME"],
            NODE_PATH: process.env["NODE_PATH"],
            CLAUDE_CONFIG_DIR: process.env["CLAUDE_CONFIG_DIR"],
            ANTHROPIC_API_KEY: API_KEY,
            ANTHROPIC_MODEL: MODEL,
            ANTHROPIC_DEFAULT_HAIKU_MODEL: MODEL,
            CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
          }),
          modelProvider: { provider: "anthropic", proxy: { apiKey: API_KEY } },
          buildIdentity: { version: "0.0.0-test", revision: "testrev" },
          pauseGraceMs: 60_000,
          documentStore: createLocalDocumentStore(join(scratch, "documents")),
          logger: createLogger({ level: "error" }),
        });
        const run = spawnRun(
          { executionId: EXECUTION_ID, prompt: "Run the Bash tool exactly once, then stop." },
          {
            deadlineMs: RUN_TIMEOUT_MS - 30_000,
            maxBudgetUsd: 1,
            maxTurns: 5,
            mcpServerUrl: mcp.url,
            runToken: RUN_TOKEN,
            onTerminalCheckpoint: (input) => {
              checkpoints.push(input.checkpoint);
              return Promise.resolve();
            },
          },
        );
        if (RUNS_UNDER_QEMU) {
          await expect(run).rejects.toThrow();
          return;
        }
        expect((await run).output).toBe(SCRIPTED_FINAL_TEXT);
        expect(mcp.authorizations).toContain(`Bearer ${RUN_TOKEN}`);
        expect(checkpoints.length).toBe(1);
        const text = checkpoints.map((checkpoint) => checkpointText(checkpoint, workspaceRoot)).join("\n");
        expect(text).toContain(SCRIPTED_RUN_MARKER);
        expect(text).not.toContain(RUN_TOKEN);
      } finally {
        await proxy.close();
        await stub.close();
        await mcp.close();
        await removeRunResidue({ scratch, workspaceRoot, tmpRoot, uid: UID_POOL_BASE });
      }
    },
    RUN_TIMEOUT_MS,
  );
});
