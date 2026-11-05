import { describe, expect, it } from "bun:test";
import { execFile } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { promisify } from "node:util";
import { EXPECTED_CLAUDE_CODE_VERSION } from "../claude-code-pin.js";
import { IMAGE_SIDE_CEILING_PX } from "./probe-image.js";
import {
  ASSET_EXECUTED_PROBE,
  ASSETS_READONLY_PROBE,
  ASSETS_WRITABLE_MARKER,
  BWRAP_ARGV_FIXTURE_PATH,
  DIRECT_EGRESS_REFUSED_PROBE,
  EGRESS_OPEN_MARKER,
  PLUGIN_INVISIBLE_PROBE,
  PROXY_EGRESS_REFUSED_PROBE,
  parseBwrapArgvFixture,
  parseScriptedRunReport,
  SCRATCH_INVISIBLE_PROBE,
  SCRIPTED_FINAL_TEXT,
  SCRIPTED_PROVIDER_FLAG_PREFIX,
  SCRIPTED_RUN_MARKER,
  type ScriptedRunReport,
  SKILL_ASSETS_SCRATCH_DIR,
  TMP_WRITABLE_PROBE,
} from "./scripted-run-contract.js";
import {
  describePersistentUserNamespaceFailure,
  describeUserNamespaceFlake,
  isUserNamespaceCreationFlake,
  retryOnUserNamespaceFlake,
  USERNS_FLAKE_MAX_ATTEMPTS,
  UserNamespaceFlakePersistedError,
} from "./userns-flake.js";

const execFileAsync = promisify(execFile);

// bwrap refuses capable-but-not-setuid processes, so the driver runs behind the same
// caps-clearing hop as the eval runner.
const CAPABILITY_CLEARING_ARGS = ["--inh-caps=-all", "--ambient-caps=-all", "--"];
const DRIVER_PATH = join(import.meta.dir, "scripted-run-driver.ts");
const SCRIPTED_RUN_TIMEOUT_MS = 180000;

const RUNS_UNDER_QEMU = process.env["EMULATED_PLATFORM"] === "true";

const OVERSIZE_IMAGE_REFUSAL = "Unable to resize image";

// Under emulation bwrap cannot build the sandbox (QEMU has no user namespaces; Rosetta refuses
// the first bind with ENOSYS), so the SDK's spawn is refused by bwrap itself before the command
// runs and the tool result is bwrap's own stderr. That prefix is the shared shape across
// emulators; the exact text is emulator-dependent and is logged rather than pinned.
const BWRAP_REFUSAL_PREFIX = "bwrap: ";

// The load-bearing sandbox flags (sandbox/README.md): present regardless of what the
// fixture says, so a bad fixture regeneration cannot silently drop them.
const REQUIRED_ARGV_FLAGS: readonly string[] = ["--unshare-net", "--new-session", "--die-with-parent"];
const REQUIRED_UNSETENV_VARS: readonly string[] = [
  "AWS_ACCESS_KEY_ID",
  "AWS_SECRET_ACCESS_KEY",
  "AWS_SESSION_TOKEN",
  "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI",
  "AWS_CONTAINER_CREDENTIALS_FULL_URI",
  "AWS_EC2_METADATA_SERVICE_ENDPOINT",
];

async function runDriver(reportPath: string, extraArgs: readonly string[]): Promise<ScriptedRunReport> {
  await execFileAsync(
    "setpriv",
    [...CAPABILITY_CLEARING_ARGS, "bun", DRIVER_PATH, reportPath, ...extraArgs],
    {
      encoding: "utf8",
      timeout: SCRIPTED_RUN_TIMEOUT_MS,
    },
  );
  return parseScriptedRunReport(JSON.parse(readFileSync(reportPath, "utf8")));
}

function argvHasPair(argv: readonly string[], flag: string, value: string): boolean {
  return argv.some((arg, index) => arg === flag && argv[index + 1] === value);
}

function expectSdkContractHeld(report: ScriptedRunReport): void {
  expect(report.output).toBe(SCRIPTED_FINAL_TEXT);
  expect(report.sessionId).not.toBe("");
  expect(report.traceInitClaudeCodeVersion).toBe(EXPECTED_CLAUDE_CODE_VERSION);

  // The model-visible tool surface is the locked-down set and nothing else; the scripted run
  // registers one skill, so Skill joins Bash and Read.
  expect(report.toolNamesOffered).toEqual(["Bash", "Read", "Skill"]);
  expect(report.messageRequests).toBe(3);

  // Usage derivation from the raw stream held (the output_tokens placeholder trap).
  expect(report.traceUsageRecords).toBeGreaterThan(0);
  expect(report.outputTokens).toBeGreaterThan(0);

  expect(report.bwrapInvocations).toBe(1);

  for (const flag of REQUIRED_ARGV_FLAGS) {
    expect(report.bwrapArgvRaw).toContain(flag);
  }
  expect(argvHasPair(report.bwrapArgvRaw, "--tmpfs", "/tmp")).toBe(true);

  // The assets dir must enter the sandbox read-only: as --ro-bind, and never as a
  // writable --bind. Asserted against the normalized argv so a bad fixture
  // regeneration cannot silently drop the property.
  const assetsDirToken = `<SCRATCH>/${SKILL_ASSETS_SCRATCH_DIR}`;
  expect(argvHasPair(report.bwrapArgvNormalized, "--ro-bind", assetsDirToken)).toBe(true);
  expect(argvHasPair(report.bwrapArgvNormalized, "--bind", assetsDirToken)).toBe(false);
  for (const name of REQUIRED_UNSETENV_VARS) {
    expect(argvHasPair(report.bwrapArgvRaw, "--unsetenv", name)).toBe(true);
  }

  // The full normalized argv is pinned as a fixture: any drift in the SDK's bwrap
  // translation fails here and is adopted only by a conscious regeneration
  // (bun src/contracts/scripted-run-driver.ts --update-fixture, via a rw src mount).
  expect(existsSync(BWRAP_ARGV_FIXTURE_PATH)).toBe(true);
  const fixture = parseBwrapArgvFixture(JSON.parse(readFileSync(BWRAP_ARGV_FIXTURE_PATH, "utf8")));
  expect(report.bwrapArgvNormalized).toEqual(fixture);

  expectResumedLegReportsOnlyItsOwnSpend(report);
}

// The CLI restores a resumed session's cost-state, so the second leg's SDK figure is cumulative.
// The two legs' reported costs must add up to what the session actually cost, never more.
function expectResumedLegReportsOnlyItsOwnSpend(report: ScriptedRunReport): void {
  const resumed = report.resumedLeg;
  expect(resumed.sessionId).toBe(report.sessionId);
  expect(report.sdkCostUsd).toBeGreaterThan(0);
  expect(resumed.restoredCostUsd).toBeCloseTo(report.sdkCostUsd, 9);
  expect(resumed.sdkCostUsd).toBeGreaterThan(report.sdkCostUsd);
  expect(resumed.estimatedCostUsd).toBeCloseTo(resumed.sdkCostUsd - resumed.restoredCostUsd, 9);
  expect(report.estimatedCostUsd + resumed.estimatedCostUsd).toBeCloseTo(resumed.sdkCostUsd, 9);
}

function expectBwrapRefusedUnderEmulation(report: ScriptedRunReport): void {
  const toolResultText = report.toolResultTexts.join("\n");
  console.error(`emulated scripted run: bwrap refused the sandboxed tool call: ${toolResultText}`);
  expect(report.traceToolCalls).toContainEqual({ name: "Bash", errored: true });
  expect(toolResultText).toContain(BWRAP_REFUSAL_PREFIX);
  expect(toolResultText).not.toContain(SCRIPTED_RUN_MARKER);
  expect(toolResultText).not.toContain(EGRESS_OPEN_MARKER);
}

function expectFoundryProviderTaken(report: ScriptedRunReport): void {
  expect(report.provider).toBe("foundry");
  expect(report.traceModelProvider).toBe("foundry");
  expect(report.requestPaths.every((path) => path.startsWith("/v1/messages"))).toBe(true);
  expect(report.costUpdates).toBeGreaterThan(0);
}

describe("the SDK contract, exercised end-to-end against the scripted stub", () => {
  it(
    "runs the real CLI through one sandboxed tool call and the argv fixture holds",
    async () => {
      const scratch = mkdtempSync(join(tmpdir(), "scripted-run-test-"));
      const reportPath = join(scratch, "report.json");
      try {
        const runDriverOnce = (): Promise<ScriptedRunReport> => runDriver(reportPath, []);
        if (RUNS_UNDER_QEMU) {
          // The scripted run completes under emulation: the SDK still builds and invokes the
          // fixture-pinned argv, bwrap refuses it, and the stub-scripted model carries on
          // regardless. Whether the sandboxed command executes is a property of the emulator,
          // not the SDK, so this gate pins the SDK contract and records the refusal it saw.
          const report = await runDriverOnce();
          expectSdkContractHeld(report);
          expectBwrapRefusedUnderEmulation(report);
          return;
        }
        // The SDK's apply-seccomp helper intermittently loses a thread race on shared CI
        // runners and its nested unshare(CLONE_NEWUSER) returns EINVAL. The retry is keyed to that
        // exact signature and logged loudly; every other failure still goes red, and a persistent
        // EINVAL goes red under its own name. See src/contracts/userns-flake.ts and
        // sandbox/README.md.
        const readDiagnosticFile = (path: string): string => readFileSync(path, "utf8");
        const report = await retryOnUserNamespaceFlake({
          run: runDriverOnce,
          isFlake: (candidate) => isUserNamespaceCreationFlake(candidate.toolResultTexts),
          maxAttempts: USERNS_FLAKE_MAX_ATTEMPTS,
          onFlake: (attempt, candidate) => {
            console.error(
              describeUserNamespaceFlake({
                attempt,
                maxAttempts: USERNS_FLAKE_MAX_ATTEMPTS,
                toolResultTexts: candidate.toolResultTexts,
                readDiagnosticFile,
              }),
            );
          },
          exhausted: (lastReport) =>
            new UserNamespaceFlakePersistedError(
              describePersistentUserNamespaceFailure({
                maxAttempts: USERNS_FLAKE_MAX_ATTEMPTS,
                toolResultTexts: lastReport.toolResultTexts,
                readDiagnosticFile,
              }),
            ),
        });

        expectSdkContractHeld(report);

        // The tool round-trip: Bash executed inside the sandbox and its output returned
        // to the model — including the probes for the undocumented denyRead-/tmp behaviour
        // ADR 0065 requires re-measuring on every SDK bump.
        const toolResultText = report.toolResultTexts.join("\n");
        expect(toolResultText).toContain(SCRIPTED_RUN_MARKER);
        expect(toolResultText).toContain(TMP_WRITABLE_PROBE);
        expect(toolResultText).toContain(SCRATCH_INVISIBLE_PROBE);

        // The skill-asset surface, measured live: a staged asset script executes
        // from inside the sandbox, its directory refuses writes (the ro-bind, not file
        // modes — the probe creates a fresh file), and the plugin dir carrying SKILL.md
        // stays invisible to sandboxed Bash.
        expect(toolResultText).toContain(ASSET_EXECUTED_PROBE);
        expect(toolResultText).toContain(ASSETS_READONLY_PROBE);
        expect(toolResultText).not.toContain(ASSETS_WRITABLE_MARKER);
        expect(toolResultText).toContain(PLUGIN_INVISIBLE_PROBE);

        // "No network for executed code", measured: a direct connect dies in the unshared
        // namespace, and the SDK's own proxy plumbing refuses an external fetch under the
        // empty allowlist. Any EGRESS_OPEN marker means executed code reached the network.
        expect(toolResultText).toContain(DIRECT_EGRESS_REFUSED_PROBE);
        expect(toolResultText).toContain(PROXY_EGRESS_REFUSED_PROBE);
        expect(toolResultText).not.toContain(EGRESS_OPEN_MARKER);
        expect(report.traceToolCalls).toContainEqual({ name: "Bash", errored: false });

        // The Read of a staged image over the model's 2000 px ceiling comes back as
        // an image block downscaled to fit, never the CLI's "Unable to resize image" refusal —
        // which is what a missing sharp native module produced. The resize runs in the CLI
        // parent: the bwrap invocation count the shared contract pins stays at the one Bash call.
        expect(toolResultText).not.toContain(OVERSIZE_IMAGE_REFUSAL);
        expect(report.traceToolCalls).toContainEqual({ name: "Read", errored: false });
        expect(report.toolResultImages).toHaveLength(1);
        const [servedImage] = report.toolResultImages;
        expect(servedImage?.byteLength).toBeGreaterThan(0);
        expect(servedImage?.dimensions).toBeDefined();
        expect(servedImage?.dimensions?.width).toBeLessThanOrEqual(IMAGE_SIDE_CEILING_PX);
        expect(servedImage?.dimensions?.height).toBeLessThanOrEqual(IMAGE_SIDE_CEILING_PX);

        expect(report.provider).toBe("bedrock");
        expect(report.traceModelProvider).toBe("bedrock");
        expect(report.costUpdates).toBeGreaterThan(0);
      } finally {
        rmSync(scratch, { recursive: true, force: true });
      }
    },
    SCRIPTED_RUN_TIMEOUT_MS,
  );

  it(
    "runs the real CLI in its Microsoft Foundry mode against the same stub, sandbox shape unchanged",
    async () => {
      const scratch = mkdtempSync(join(tmpdir(), "scripted-foundry-test-"));
      const reportPath = join(scratch, "report.json");
      try {
        const runDriverOnce = (): Promise<ScriptedRunReport> =>
          runDriver(reportPath, [`${SCRIPTED_PROVIDER_FLAG_PREFIX}foundry`]);
        if (RUNS_UNDER_QEMU) {
          const report = await runDriverOnce();
          expectSdkContractHeld(report);
          expectBwrapRefusedUnderEmulation(report);
          expectFoundryProviderTaken(report);
          return;
        }
        const readDiagnosticFile = (path: string): string => readFileSync(path, "utf8");
        const report = await retryOnUserNamespaceFlake({
          run: runDriverOnce,
          isFlake: (candidate) => isUserNamespaceCreationFlake(candidate.toolResultTexts),
          maxAttempts: USERNS_FLAKE_MAX_ATTEMPTS,
          onFlake: (attempt, candidate) => {
            console.error(
              describeUserNamespaceFlake({
                attempt,
                maxAttempts: USERNS_FLAKE_MAX_ATTEMPTS,
                toolResultTexts: candidate.toolResultTexts,
                readDiagnosticFile,
              }),
            );
          },
          exhausted: (lastReport) =>
            new UserNamespaceFlakePersistedError(
              describePersistentUserNamespaceFailure({
                maxAttempts: USERNS_FLAKE_MAX_ATTEMPTS,
                toolResultTexts: lastReport.toolResultTexts,
                readDiagnosticFile,
              }),
            ),
        });

        // The SDK contract — stream narrowing, tool surface, usage derivation, and the exact
        // bwrap argv — is provider-independent: the same fixture as the default run, so a
        // provider mode can never loosen the lockdown.
        expectSdkContractHeld(report);

        expectFoundryProviderTaken(report);

        expect(report.toolResultTexts.join("\n")).toContain(SCRIPTED_RUN_MARKER);
        expect(report.toolResultTexts.join("\n")).toContain(DIRECT_EGRESS_REFUSED_PROBE);
        expect(report.traceToolCalls).toContainEqual({ name: "Bash", errored: false });
      } finally {
        rmSync(scratch, { recursive: true, force: true });
      }
    },
    SCRIPTED_RUN_TIMEOUT_MS,
  );
});
