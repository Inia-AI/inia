// The anthropic mode end to end in the production shape: the real spawner leases a uid and spawns
// the real run child, which stages a document over fd 3, probes the sandbox, and runs the real CLI
// — whose every model call crosses the real key holder (in this process, on the fixed loopback
// port the child's env is authored with) to a scripted Anthropic API stub. Zero model cost. The
// sandboxed command looks for the key everywhere a run can look, and must find only the placeholder.
import { describe, expect, it } from "bun:test";
import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { startAnthropicProxyServer } from "../anthropic-proxy.js";
import { UID_POOL_BASE } from "../config.js";
import { createLocalDocumentStore } from "../document-store.js";
import { RUNTIME_ENVIRONMENT_SECTION } from "../environment-section.js";
import { sha256Hex } from "../hash.js";
import { createLogger } from "../logger.js";
import { jsonLineSink, traceEnvelopeOf, traceResultMessageOf } from "../loopback-test-helpers.js";
import {
  ANTHROPIC_PROXY_PLACEHOLDER_API_KEY,
  buildProviderSubprocessEnv,
  LOOPBACK_HOST,
  LOOPBACK_PORT,
} from "../model-provider.js";
import { createRunSpawner } from "../run-spawner.js";
import { runAsUid } from "../setpriv.js";
import { createUidPool } from "../uid-pool.js";
import { estimateUsageCostUsd, resultModelUsageOf, ZERO_TOKEN_USAGE } from "../usage-cost.js";
import { claimRetainedRoot, retainedRootsUnder } from "../workspace-layout.js";
import { startScriptedAnthropicStub } from "./anthropic-stub.js";
import { SCRIPTED_FINAL_TEXT, SCRIPTED_RUN_MARKER } from "./scripted-run-contract.js";

const SUPERVISED_RUN_TIMEOUT_MS = 180000;
const EXECUTION_ID = "anthropic-supervised-run";
const MODEL = "claude-sonnet-5-5";
// Split so the whole key never appears in the command text the model sends: the probe joins the
// halves inside the sandbox, where only a leaked key could match them.
const KEY_HEAD = "sk-ant-api03-SUPERVISED";
const KEY_TAIL = "RUNKEY720";
const API_KEY = `${KEY_HEAD}-${KEY_TAIL}`;
const DOCUMENT_NAME = "brief.txt";
const CALLER_SYSTEM_PROMPT = "ENG855_CALLER_SYSTEM_PROMPT: you are a scripted analyst.";
const DOCUMENT_TEXT = "ENG720_DOCUMENT_BODY staged over fd 3";
const KEY_IN_ENV = "ENG720_KEY_IN_ENV";
const KEY_NOT_IN_ENV = "ENG720_KEY_NOT_IN_ENV";
const KEY_IN_PROC = "ENG720_KEY_IN_PROC";
const KEY_NOT_IN_PROC = "ENG720_KEY_NOT_IN_PROC";
const KEY_SEEN_PREFIX = "ENG720_KEY_SEEN=";
const PROXY_UNREACHABLE_PROBE = "ENG720_PROXY_UNREACHABLE";
const PROXY_REACHABLE_MARKER = "ENG720_PROXY_REACHABLE";
const PROXY_VIA_EGRESS_UNREACHABLE_PROBE = "ENG720_PROXY_VIA_EGRESS_UNREACHABLE";
const PROXY_VIA_EGRESS_REACHABLE_MARKER = "ENG720_PROXY_VIA_EGRESS_REACHABLE";

const RUNS_UNDER_QEMU = process.env["EMULATED_PLATFORM"] === "true";

describe("anthropic mode, supervised: spawner → child → CLI → key holder → Anthropic API", () => {
  it(
    "completes a scripted run whose every model call carried the key only from the key holder onward",
    async () => {
      // A dedicated pool uid, as in deployment: the spawner kills every straggler of the run's
      // uid once the child exits, so the test process must never share it.
      const scratch = mkdtempSync(join(tmpdir(), "anthropic-supervised-"));
      const workspaceRoot = join(scratch, "workspaces");
      const tmpRoot = join(scratch, "tmp");
      mkdirSync(workspaceRoot);
      mkdirSync(tmpRoot);
      execFileSync("chmod", ["1777", scratch, workspaceRoot, tmpRoot]);
      const toolCommand = [
        `cat docs/${DOCUMENT_NAME}`,
        `echo ${SCRIPTED_RUN_MARKER}`,
        `K="${KEY_HEAD}-""${KEY_TAIL}"`,
        `env | grep -q -F "$K" && echo ${KEY_IN_ENV} || echo ${KEY_NOT_IN_ENV}`,
        `grep -l -s -F "$K" /proc/*/environ >/dev/null && echo ${KEY_IN_PROC} || echo ${KEY_NOT_IN_PROC}`,
        `echo ${KEY_SEEN_PREFIX}$(printenv ANTHROPIC_API_KEY)`,
        `timeout 5 bash -c 'echo > /dev/tcp/${LOOPBACK_HOST}/${LOOPBACK_PORT}' 2>/dev/null && echo ${PROXY_REACHABLE_MARKER} || echo ${PROXY_UNREACHABLE_PROBE}`,
        // curl honours the proxy variables the SDK sets inside the sandbox, so this measures the
        // egress bridge's answer to a loopback target, not only the unshared namespace.
        `timeout 10 curl -s -m 5 -o /dev/null http://${LOOPBACK_HOST}:${LOOPBACK_PORT}/healthz 2>/dev/null && echo ${PROXY_VIA_EGRESS_REACHABLE_MARKER} || echo ${PROXY_VIA_EGRESS_UNREACHABLE_PROBE}`,
      ].join("; ");
      const stub = await startScriptedAnthropicStub({ toolCommand, finalText: SCRIPTED_FINAL_TEXT });
      const proxyLines: Record<string, unknown>[] = [];
      const proxy = await startAnthropicProxyServer({
        apiKey: API_KEY,
        upstreamUrl: stub.baseUrl,
        host: LOOPBACK_HOST,
        port: LOOPBACK_PORT,
        logger: createLogger({ level: "debug", sink: jsonLineSink(proxyLines) }),
      });
      const supervisorLines: Record<string, unknown>[] = [];
      try {
        for (const root of retainedRootsUnder(workspaceRoot)) {
          claimRetainedRoot(root.path, root.mode);
        }
        const documentBytes = new TextEncoder().encode(DOCUMENT_TEXT);
        const documentStore = createLocalDocumentStore(join(scratch, "documents"));
        await documentStore.save(EXECUTION_ID, [
          {
            name: DOCUMENT_NAME,
            bytes: documentBytes,
            sha256: sha256Hex(documentBytes),
            area: "documents",
            mediaType: "text/plain",
          },
        ]);
        const spawnRun = createRunSpawner({
          pool: createUidPool({ baseUid: UID_POOL_BASE, size: 1 }),
          workspaceRoot,
          tmpRoot,
          // What loadServerConfig hands the spawner in deployment: the image env, the real key
          // among it, through the anthropic allowlist. HOME rides it because the pool uid has no
          // passwd entry and the SDK resolves a home directory at import.
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
          documentStore,
          logger: createLogger({ level: "debug", sink: jsonLineSink(supervisorLines) }),
        });
        const run = spawnRun(
          { executionId: EXECUTION_ID, prompt: "Run the Bash tool exactly once, then stop." },
          {
            deadlineMs: SUPERVISED_RUN_TIMEOUT_MS - 30_000,
            maxBudgetUsd: 1,
            maxTurns: 5,
            systemPrompt: CALLER_SYSTEM_PROMPT,
          },
        );
        if (RUNS_UNDER_QEMU) {
          await expect(run).rejects.toThrow();
          return;
        }
        const result = await run;

        expect(result.output).toBe(SCRIPTED_FINAL_TEXT);
        expect(result.usage?.outputTokens ?? 0).toBeGreaterThan(0);
        const framesFromChild = supervisorLines
          .filter((line) => line["event"] === "wire_frame" && line["direction"] === "from_child")
          .map((line) => line["frame"]);
        expect(framesFromChild.filter((frame) => frame === "final")).toEqual(["final"]);

        // The tool ran inside the sandbox, read the staged document, and found the placeholder
        // where the key would be — in its environment, in every /proc environ it can see — and
        // could not reach the key holder at all.
        const toolResultText = stub.observations.toolResultTexts.join("\n");
        expect(toolResultText).toContain(DOCUMENT_TEXT);
        expect(toolResultText).toContain(SCRIPTED_RUN_MARKER);
        expect(toolResultText).toContain(KEY_NOT_IN_ENV);
        expect(toolResultText).not.toContain(KEY_IN_ENV);
        expect(toolResultText).toContain(KEY_NOT_IN_PROC);
        expect(toolResultText).not.toContain(KEY_IN_PROC);
        expect(toolResultText).toContain(`${KEY_SEEN_PREFIX}${ANTHROPIC_PROXY_PLACEHOLDER_API_KEY}`);
        expect(toolResultText).not.toContain(API_KEY);
        expect(toolResultText).toContain(PROXY_UNREACHABLE_PROBE);
        expect(toolResultText).not.toContain(PROXY_REACHABLE_MARKER);
        expect(toolResultText).toContain(PROXY_VIA_EGRESS_UNREACHABLE_PROBE);
        expect(toolResultText).not.toContain(PROXY_VIA_EGRESS_REACHABLE_MARKER);

        // Every model call opened its system prompt with the runtime's environment section, and the
        // caller's own system prompt followed it.
        expect(stub.observations.systemTexts.length).toBe(stub.observations.messageRequests);
        for (const systemText of stub.observations.systemTexts) {
          const sectionAt = systemText.indexOf(RUNTIME_ENVIRONMENT_SECTION);
          expect(sectionAt).toBeGreaterThanOrEqual(0);
          expect(systemText.indexOf(CALLER_SYSTEM_PROMPT)).toBeGreaterThan(sectionAt);
        }

        // Every model call reached the API through the key holder, carrying the real key there and
        // nowhere before it; the key holder bound each one to this run.
        expect(stub.observations.messageRequests).toBeGreaterThanOrEqual(2);
        expect(stub.observations.toolNamesOffered).toEqual(["Bash", "Read"]);
        // The run declares no effort; the runtime's own high for Sonnet 5.5 reaches every request.
        expect([...new Set(stub.observations.requestEfforts)]).toEqual(["high"]);
        expect(stub.observations.requestApiKeys.every((key) => key === API_KEY)).toBe(true);
        expect(stub.observations.requestPaths.every((path) => path.startsWith("/v1/messages"))).toBe(true);
        const proxyRequests = proxyLines.filter((line) => line["event"] === "anthropic_proxy_request");
        expect(proxyRequests.length).toBe(stub.observations.requestPaths.length);
        expect(proxyRequests.every((line) => line["executionId"] === EXECUTION_ID)).toBe(true);
        expect(proxyRequests.every((line) => line["status"] === 200)).toBe(true);
        expect(JSON.stringify(proxyLines)).not.toContain(API_KEY);
        expect(JSON.stringify(supervisorLines)).not.toContain(API_KEY);

        expect(result.tracePath).toBeDefined();
        const envelope = result.tracePath === undefined ? undefined : traceEnvelopeOf(result.tracePath);
        expect(envelope?.["modelProvider"]).toBe("anthropic");
        expect(envelope?.["modelPin"]).toBe(MODEL);
        expect(envelope?.["reasoning"]).toMatchObject({ effort: "high", effortSource: "runtime_default" });
        expect(JSON.stringify(envelope)).not.toContain(API_KEY);

        // The CLI's budget fuse spends the SDK's own cost figure, so that figure must price Sonnet 5.5
        // at the runtime's rates: SDK 0.3.281 priced it as Opus 5.5 and stopped a run short of its cap.
        const resultMessage =
          result.tracePath === undefined ? undefined : traceResultMessageOf(result.tracePath);
        const modelUsage = resultModelUsageOf(resultMessage);
        expect([...(modelUsage?.keys() ?? [])]).toEqual([MODEL]);
        const sonnetUsage = modelUsage?.get(MODEL) ?? ZERO_TOKEN_USAGE;
        expect(sonnetUsage.outputTokens).toBeGreaterThan(0);
        expect(resultMessage?.["total_cost_usd"]).toBeCloseTo(estimateUsageCostUsd(MODEL, sonnetUsage), 12);
      } finally {
        await proxy.close();
        await stub.close();
        try {
          await runAsUid(UID_POOL_BASE, [
            "find",
            workspaceRoot,
            tmpRoot,
            "-user",
            String(UID_POOL_BASE),
            "-delete",
          ]);
        } finally {
          rmSync(scratch, { recursive: true, force: true });
        }
      }
    },
    SUPERVISED_RUN_TIMEOUT_MS,
  );
});
