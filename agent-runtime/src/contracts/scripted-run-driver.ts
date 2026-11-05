// Entry point (like src/run-one-shot.ts it may read process.env): drives one real SDK run —
// real CLI subprocess, real bwrap sandbox — against the scripted Anthropic stub, so the whole
// SDK contract this runtime rides on is exercised at zero model cost. Spawned by
// sdk-scripted-run.test.ts through a caps-clearing setpriv hop; writes a JSON report to the
// path in argv[2]. With --update-fixture it also rewrites bwrap-argv.fixture.json. With
// --provider=foundry the CLI runs in its Microsoft Foundry mode against the same stub.
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { sha256Hex } from "../hash.js";
import { DEFERRED_TOOL_LOADING_OFF, MODEL_PROVIDER_ENV_VAR, type ModelProvider } from "../model-provider.js";
import { isRecord } from "../narrow.js";
import { claimReadMirror } from "../read-gate.js";
import { buildRunEnv, buildTraceEnvelope } from "../run-child.js";
import { FIRST_ATTEMPT } from "../run-registry.js";
import { runReport } from "../run-report.js";
import { createRestoredSessionCostReader } from "../session-cost-state.js";
import { stageSkillPlugin } from "../skill-plugin.js";
import { createRunTrace, TRACE_FILE_NAME } from "../trace.js";
import { WORKSPACE_DOCUMENTS_DIR } from "../workspace-layout.js";
import { startScriptedAnthropicStub } from "./anthropic-stub.js";
import {
  INVOCATION_SEPARATOR,
  normalizeBwrapArgv,
  parseNulDelimitedInvocations,
  sandboxInvocations,
} from "./bwrap-argv.js";
import { OVERSIZE_IMAGE_NAME, oversizeProbePng } from "./probe-image.js";
import {
  ASSET_EXECUTED_PROBE,
  ASSETS_READONLY_PROBE,
  ASSETS_WRITABLE_MARKER,
  BWRAP_ARGV_FIXTURE_PATH,
  DIRECT_EGRESS_REFUSED_PROBE,
  EGRESS_OPEN_MARKER,
  FOUNDRY_MODEL_PIN,
  PLUGIN_INVISIBLE_PROBE,
  PROXY_EGRESS_REFUSED_PROBE,
  SCRATCH_INVISIBLE_PROBE,
  SCRIPTED_ASSET_NAME,
  SCRIPTED_FINAL_TEXT,
  SCRIPTED_PROVIDER_FLAG_PREFIX,
  SCRIPTED_RUN_MARKER,
  SCRIPTED_SKILL_NAME,
  type ScriptedRunReport,
  SKILL_ASSETS_SCRATCH_DIR,
  TMP_WRITABLE_PROBE,
} from "./scripted-run-contract.js";

const SCRIPTED_EXECUTION_ID = "sdk-scripted-run";
const RESUMED_EXECUTION_ID = "sdk-scripted-run-resumed";
const RESUMED_PROMPT = "Continue: reply with the final text.";
const SCRIPTED_DEADLINE_MS = 150000;
const SCRIPTED_STUB_MODEL = "scripted-stub-model";

const CAP_AMB_LINE = /^CapAmb:\s*([0-9a-fA-F]+)\s*$/m;

function assertNoAmbientCapabilities(): void {
  const capAmbHex = readFileSync("/proc/self/status", "utf8").match(CAP_AMB_LINE)?.[1];
  if (capAmbHex === undefined || BigInt(`0x${capAmbHex}`) !== 0n) {
    throw new Error(
      `this process holds ambient capabilities (CapAmb ${capAmbHex ?? "unreadable"}); bwrap refuses ` +
        "capable-but-not-setuid processes — spawn the driver through setpriv --inh-caps=-all --ambient-caps=-all",
    );
  }
}

type TraceSummary = {
  initClaudeCodeVersion: string | undefined;
  modelProvider: string | undefined;
  toolCalls: { name: string; errored: boolean }[];
  usageRecords: number;
  recordTypes: Record<string, number>;
  resultTotalCostUsd: number | undefined;
};

function summarizeTracePack(packDir: string): TraceSummary {
  const lines = readFileSync(join(packDir, TRACE_FILE_NAME), "utf8")
    .split("\n")
    .filter((line) => line !== "");
  const summary: TraceSummary = {
    initClaudeCodeVersion: undefined,
    modelProvider: undefined,
    toolCalls: [],
    usageRecords: 0,
    recordTypes: {},
    resultTotalCostUsd: undefined,
  };
  for (const line of lines) {
    const parsed: unknown = JSON.parse(line);
    if (!isRecord(parsed)) {
      continue;
    }
    const recordType = typeof parsed["type"] === "string" ? parsed["type"] : "unknown";
    summary.recordTypes[recordType] = (summary.recordTypes[recordType] ?? 0) + 1;
    if (parsed["type"] === "envelope" && typeof parsed["modelProvider"] === "string") {
      summary.modelProvider = parsed["modelProvider"];
    }
    if (parsed["type"] === "usage") {
      summary.usageRecords += 1;
    }
    if (parsed["type"] === "tool_call") {
      const name = parsed["toolName"];
      if (typeof name === "string") {
        summary.toolCalls.push({ name, errored: parsed["errorClass"] !== null });
      }
    }
    if (parsed["type"] === "message" && isRecord(parsed["message"])) {
      const message = parsed["message"];
      if (message["type"] === "system" && typeof message["claude_code_version"] === "string") {
        summary.initClaudeCodeVersion = message["claude_code_version"];
      }
      if (message["type"] === "result" && typeof message["total_cost_usd"] === "number") {
        summary.resultTotalCostUsd = message["total_cost_usd"];
      }
    }
  }
  return summary;
}

function providerOf(argv: readonly string[]): ModelProvider {
  const flag = argv.find((arg) => arg.startsWith(SCRIPTED_PROVIDER_FLAG_PREFIX));
  const value = flag?.slice(SCRIPTED_PROVIDER_FLAG_PREFIX.length);
  if (value === undefined || value === "bedrock") {
    return "bedrock";
  }
  if (value === "foundry") {
    return "foundry";
  }
  throw new Error(
    `usage: ${SCRIPTED_PROVIDER_FLAG_PREFIX}bedrock|foundry (the other modes have their own supervised runs)`,
  );
}

// The historical driver env is direct-API shaped (the stub is the API); the Foundry variant goes
// through the production buildRunEnv for that mode, then adds what only a stub run needs: the
// CLI's auth skip, which the production config refuses, because no Azure identity chain can
// resolve here.
function subprocessEnvFor(input: {
  provider: ModelProvider;
  shimDir: string;
  privateHome: string;
  stubBaseUrl: string;
}): Record<string, string> {
  const path = `${input.shimDir}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin`;
  if (input.provider === "foundry") {
    return {
      ...buildRunEnv({
        env: {
          PATH: path,
          [MODEL_PROVIDER_ENV_VAR]: "foundry",
          CLAUDE_CODE_USE_FOUNDRY: "1",
          ANTHROPIC_FOUNDRY_BASE_URL: input.stubBaseUrl,
          ANTHROPIC_MODEL: FOUNDRY_MODEL_PIN,
          ANTHROPIC_DEFAULT_HAIKU_MODEL: FOUNDRY_MODEL_PIN,
          CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
        },
        privateHome: input.privateHome,
        executionId: SCRIPTED_EXECUTION_ID,
      }),
      CLAUDE_CODE_SKIP_FOUNDRY_AUTH: "1",
    };
  }
  return {
    PATH: path,
    HOME: input.privateHome,
    CLAUDE_CONFIG_DIR: join(input.privateHome, ".claude"),
    ANTHROPIC_BASE_URL: input.stubBaseUrl,
    ANTHROPIC_API_KEY: "scripted-stub-key",
    ANTHROPIC_MODEL: SCRIPTED_STUB_MODEL,
    ANTHROPIC_DEFAULT_HAIKU_MODEL: SCRIPTED_STUB_MODEL,
    CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: "1",
    CLAUDE_CODE_DISABLE_AUTO_MEMORY: "1",
    ...DEFERRED_TOOL_LOADING_OFF,
  };
}

async function main(): Promise<void> {
  assertNoAmbientCapabilities();
  const reportPath = process.argv.slice(2).find((arg) => !arg.startsWith("--"));
  const updateFixture = process.argv.includes("--update-fixture");
  const provider = providerOf(process.argv);
  if (reportPath === undefined && !updateFixture) {
    throw new Error(
      "usage: scripted-run-driver.ts <report.json> [--update-fixture] [--provider=bedrock|foundry]",
    );
  }

  const scratchRoot = mkdtempSync(join(tmpdir(), "scripted-run-"));
  const shimDir = join(scratchRoot, "shim");
  const argvLog = join(scratchRoot, "bwrap-argv.log");
  const privateHome = join(scratchRoot, "home");
  const configDir = join(privateHome, ".claude");
  const workspaceRoot = join(scratchRoot, "workspaces");
  const traceRoot = join(scratchRoot, "traces");
  mkdirSync(shimDir, { recursive: true });
  mkdirSync(configDir, { recursive: true });
  mkdirSync(workspaceRoot, { recursive: true });

  // One staged input document, an image over the model's pixel ceiling, exactly where a run
  // child stages documents and gated exactly as a run's Read is: the scripted Read
  // must come back as a downscaled image block, which is the CLI parent resizing it — a
  // resize dependency missing from the image surfaces here as a tool error, not an image.
  const workspacePath = mkdtempSync(join(workspaceRoot, "run-"));
  const documentsDir = join(workspacePath, WORKSPACE_DOCUMENTS_DIR);
  mkdirSync(documentsDir, { recursive: true });
  const oversizeImage = oversizeProbePng();
  const oversizeImagePath = join(documentsDir, OVERSIZE_IMAGE_NAME);
  writeFileSync(oversizeImagePath, oversizeImage);
  const readMirror = claimReadMirror(privateHome, [
    { name: OVERSIZE_IMAGE_NAME, sha256: sha256Hex(oversizeImage), sizeBytes: oversizeImage.byteLength },
  ]);

  // The shim writes NUL-delimited argv (immune to embedded whitespace) then execs the real
  // binary, so the sandboxed command still runs — same technique as spike 8.
  writeFileSync(
    join(shimDir, "bwrap"),
    [
      "#!/bin/bash",
      `{ printf '%s\\0' "$@"; printf '${INVOCATION_SEPARATOR}'; } >> ${argvLog}`,
      'exec /usr/bin/bwrap "$@"',
      "",
    ].join("\n"),
  );
  chmodSync(join(shimDir, "bwrap"), 0o755);

  // One real skill with one runnable asset, staged exactly as a run child stages them:
  // SKILL.md into the plugin under the private home, the asset into its own
  // scratch dir the sandbox must ro-bind.
  const skillAssetsDir = join(scratchRoot, SKILL_ASSETS_SCRATCH_DIR);
  const skillPlugin = stageSkillPlugin(
    privateHome,
    [
      {
        name: SCRIPTED_SKILL_NAME,
        description: "Scripted contract probe skill; the run never invokes it.",
        bytes: Buffer.from("# Probe skill\n"),
        assets: [{ name: SCRIPTED_ASSET_NAME, bytes: Buffer.from(`echo ${ASSET_EXECUTED_PROBE}\n`) }],
      },
    ],
    skillAssetsDir,
  );
  if (skillPlugin?.assetsPath === undefined) {
    throw new Error("the scripted probe skill failed to stage its asset");
  }

  // The scratch root itself exists inside the namespace as an ancestor of the workspace bind,
  // so the invisibility probe targets a scratch file that is not on the workspace path. The
  // egress probes measure "no network for executed code" per bump: a direct connect must die
  // in the unshared namespace, and the SDK's own proxy plumbing (http_proxy et al. point at
  // the in-namespace bridge) must refuse an external fetch under the empty allowlist. The
  // asset probes measure the skill-asset surface: the staged script executes from inside the
  // sandbox via $SKILL_ASSETS_DIR, a fresh-file write into its dir is refused by the mount
  // (not by file modes), and the plugin dir holding SKILL.md stays invisible.
  const toolCommand = [
    `echo ${SCRIPTED_RUN_MARKER}`,
    `touch /tmp/eng232-probe && echo ${TMP_WRITABLE_PROBE}`,
    `test -e ${join(shimDir, "bwrap")} || echo ${SCRATCH_INVISIBLE_PROBE}`,
    `timeout 5 bash -c 'echo > /dev/tcp/1.1.1.1/443' 2>/dev/null && echo ${EGRESS_OPEN_MARKER}-direct || echo ${DIRECT_EGRESS_REFUSED_PROBE}`,
    `timeout 10 wget -q -T 5 -t 1 -O /dev/null https://example.com 2>/dev/null && echo ${EGRESS_OPEN_MARKER}-proxy || echo ${PROXY_EGRESS_REFUSED_PROBE}`,
    `bash "$SKILL_ASSETS_DIR/${SCRIPTED_SKILL_NAME}/${SCRIPTED_ASSET_NAME}"`,
    `touch "$SKILL_ASSETS_DIR/${SCRIPTED_SKILL_NAME}/write-probe" 2>/dev/null && echo ${ASSETS_WRITABLE_MARKER} || echo ${ASSETS_READONLY_PROBE}`,
    `test -e "$HOME/skills" || echo ${PLUGIN_INVISIBLE_PROBE}`,
  ].join("; ");
  const stub = await startScriptedAnthropicStub({
    toolCommand,
    readFilePath: oversizeImagePath,
    finalText: SCRIPTED_FINAL_TEXT,
  });

  const subprocessEnv = subprocessEnvFor({ provider, shimDir, privateHome, stubBaseUrl: stub.baseUrl });

  const prompt = `Run the Bash tool exactly once, then Read ${oversizeImagePath}, then stop: ${toolCommand}`;
  const trace = createRunTrace({
    traceRoot,
    executionId: SCRIPTED_EXECUTION_ID,
    attempt: FIRST_ATTEMPT,
    configDir,
    // The child stamps the envelope from its own process env, where the supervisor named the
    // provider — never from the SDK env, which carries no runtime setting.
    envelope: buildTraceEnvelope({
      prompt,
      env: { ...subprocessEnv, [MODEL_PROVIDER_ENV_VAR]: provider },
      skills: skillPlugin.skillNames,
    }),
  });

  let costUpdates = 0;
  try {
    const result = await runReport(
      { executionId: SCRIPTED_EXECUTION_ID, prompt },
      {
        subprocessEnv,
        deadlineMs: SCRIPTED_DEADLINE_MS,
        maxBudgetUsd: 1,
        maxTurns: 5,
        workspaceRoot,
        workspacePath,
        readMirror,
        skillPlugin,
        trace,
        onCostUpdate: () => {
          costUpdates += 1;
        },
      },
    );

    if (!existsSync(argvLog)) {
      throw new Error(
        `the PATH shim never executed, so no bwrap argv was captured at ${argvLog}. The usual cause ` +
          "is a noexec mount over the scratch root (Docker mounts a tmpfs noexec unless told " +
          "otherwise): execvp skips an unexecutable shim and falls through to the real bwrap, so the " +
          "run succeeds while the capture stays empty.",
      );
    }
    const invocations = sandboxInvocations(parseNulDelimitedInvocations(readFileSync(argvLog, "utf8")));
    const firstInvocation = invocations[0] ?? [];
    const normalized = normalizeBwrapArgv(firstInvocation, { scratchRoot, workspacePath });
    const traceSummary = summarizeTracePack(trace.packDir);
    const firstLegObservations = structuredClone(stub.observations);

    // The same session resumed in a second leg, as a continuation resumes it. The CLI
    // restores the transcript's cost-state, so its total_cost_usd carries the first leg's spend
    // as well; the runtime must report only what this leg added.
    const restoredCostUsd = createRestoredSessionCostReader({ configDir, cwd: workspacePath })(
      result.sessionId,
    );
    const resumedTrace = createRunTrace({
      traceRoot,
      executionId: RESUMED_EXECUTION_ID,
      attempt: FIRST_ATTEMPT,
      configDir,
      envelope: buildTraceEnvelope({
        prompt: RESUMED_PROMPT,
        env: { ...subprocessEnv, [MODEL_PROVIDER_ENV_VAR]: provider },
        skills: skillPlugin.skillNames,
      }),
    });
    const resumed = await runReport(
      { executionId: RESUMED_EXECUTION_ID, prompt: RESUMED_PROMPT },
      {
        subprocessEnv,
        deadlineMs: SCRIPTED_DEADLINE_MS,
        maxBudgetUsd: 1,
        maxTurns: 5,
        workspaceRoot,
        workspacePath,
        readMirror,
        skillPlugin,
        trace: resumedTrace,
        resumeFrom: { sessionId: result.sessionId },
      },
    );
    const resumedTraceSummary = summarizeTracePack(resumedTrace.packDir);

    const report: ScriptedRunReport = {
      provider,
      output: result.output,
      sessionId: result.sessionId,
      outputTokens: result.usage?.outputTokens ?? 0,
      estimatedCostUsd: result.estimatedCostUsd,
      sdkCostUsd: traceSummary.resultTotalCostUsd ?? 0,
      resumedLeg: {
        sessionId: resumed.sessionId,
        estimatedCostUsd: resumed.estimatedCostUsd,
        sdkCostUsd: resumedTraceSummary.resultTotalCostUsd ?? 0,
        restoredCostUsd,
      },
      costUpdates,
      toolResultTexts: firstLegObservations.toolResultTexts,
      toolResultImages: firstLegObservations.toolResultImages,
      toolNamesOffered: firstLegObservations.toolNamesOffered,
      requestPaths: firstLegObservations.requestPaths,
      messageRequests: firstLegObservations.messageRequests,
      bwrapInvocations: invocations.length,
      bwrapArgvRaw: firstInvocation,
      bwrapArgvNormalized: normalized,
      traceInitClaudeCodeVersion: traceSummary.initClaudeCodeVersion,
      traceModelProvider: traceSummary.modelProvider,
      traceToolCalls: traceSummary.toolCalls,
      traceUsageRecords: traceSummary.usageRecords,
      traceRecordTypes: traceSummary.recordTypes,
    };
    if (reportPath !== undefined) {
      writeFileSync(reportPath, JSON.stringify(report, null, 2));
    }
    if (updateFixture) {
      writeFileSync(BWRAP_ARGV_FIXTURE_PATH, `${JSON.stringify(normalized, null, 2)}\n`);
    }
  } finally {
    await stub.close();
    rmSync(scratchRoot, { recursive: true, force: true });
  }
}

await main();
