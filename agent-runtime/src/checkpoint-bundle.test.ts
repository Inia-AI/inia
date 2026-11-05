import { afterAll, describe, expect, it } from "bun:test";
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { gzipSync } from "node:zlib";
import {
  CHECKPOINT_BUNDLE_FORMAT,
  CheckpointBundleError,
  composeCheckpointVersionStamp,
  decodeCheckpointManifest,
  restoreSessionCheckpoint,
  serializeSessionCheckpoint,
} from "./checkpoint-bundle.js";
import { EXPECTED_CLAUDE_CODE_VERSION } from "./claude-code-pin.js";
import { RESERVED_WORKSPACE_DIRS } from "./workspace-layout.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "checkpoint-bundle-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

let caseId = 0;

function newCase(): { workspaceRoot: string; configDir: string; workspacePath: string } {
  caseId += 1;
  const base = join(TEST_ROOT, `case-${caseId}`);
  const workspaceRoot = join(base, "workspaces");
  const configDir = join(base, "config");
  const workspacePath = join(workspaceRoot, "run-under-test");
  mkdirSync(configDir, { recursive: true });
  mkdirSync(workspacePath, { recursive: true });
  return { workspaceRoot, configDir, workspacePath };
}

function modeOf(path: string): number {
  return statSync(path).mode & 0o777;
}

describe("the reserved workspace areas", () => {
  it("names the staged docs and mirrored out areas — skills live in the plugin dir, not the workspace", () => {
    expect(RESERVED_WORKSPACE_DIRS).toEqual(["docs", "out"]);
  });
});

describe("serializeSessionCheckpoint and restoreSessionCheckpoint", () => {
  it("round-trips the session dir and workspace, nested paths included, to identical bytes", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    mkdirSync(join(configDir, "projects", "-workspaces-run-x"), { recursive: true });
    writeFileSync(join(configDir, "projects", "-workspaces-run-x", "session.jsonl"), '{"type":"user"}\n');
    writeFileSync(join(configDir, "settings.json"), "{}");
    writeFileSync(join(workspacePath, "draft.md"), "# partial report");

    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(manifest.sessionId).toBe("s-1");
    expect(manifest.workspacePath).toBe(workspacePath);

    rmSync(configDir, { recursive: true, force: true });
    rmSync(workspacePath, { recursive: true, force: true });
    restoreSessionCheckpoint(manifest, { configDir, reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS });

    expect(readFileSync(join(configDir, "projects", "-workspaces-run-x", "session.jsonl"), "utf8")).toBe(
      '{"type":"user"}\n',
    );
    expect(readFileSync(join(configDir, "settings.json"), "utf8")).toBe("{}");
    expect(readFileSync(join(workspacePath, "draft.md"), "utf8")).toBe("# partial report");
  });

  it("recreates the run's dirs private (0700) and its files owner-only (0600)", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    mkdirSync(join(configDir, "projects"), { recursive: true });
    writeFileSync(join(configDir, "projects", "session.jsonl"), "{}\n");
    writeFileSync(join(workspacePath, "draft.md"), "# partial report");
    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    rmSync(configDir, { recursive: true, force: true });
    rmSync(workspacePath, { recursive: true, force: true });
    restoreSessionCheckpoint(manifest, { configDir, reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS });
    expect(modeOf(workspacePath)).toBe(0o700);
    expect(modeOf(configDir)).toBe(0o700);
    expect(modeOf(join(configDir, "projects"))).toBe(0o700);
    expect(modeOf(join(configDir, "projects", "session.jsonl"))).toBe(0o600);
    expect(modeOf(join(workspacePath, "draft.md"))).toBe(0o600);
  });

  it("clears a stranded workspace dir rather than failing, and never adopts the files in it", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    writeFileSync(join(workspacePath, "draft.md"), "# checkpointed");
    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    rmSync(configDir, { recursive: true, force: true });
    chmodSync(workspacePath, 0o700);
    writeFileSync(join(workspacePath, "stranded.md"), "left by the dead child");
    expect(existsSync(workspacePath)).toBe(true);

    restoreSessionCheckpoint(manifest, { configDir, reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS });

    expect(readFileSync(join(workspacePath, "draft.md"), "utf8")).toBe("# checkpointed");
    expect(existsSync(join(workspacePath, "stranded.md"))).toBe(false);
    expect(modeOf(workspacePath)).toBe(0o700);
  });

  it("refuses a leftover that is not this uid's own private run dir", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    rmSync(configDir, { recursive: true, force: true });
    chmodSync(workspacePath, 0o755);
    expect(() =>
      restoreSessionCheckpoint(manifest, { configDir, reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS }),
    ).toThrow(CheckpointBundleError);

    rmSync(workspacePath, { recursive: true, force: true });
    mkdirSync(join(workspaceRoot, "elsewhere"), { recursive: true, mode: 0o700 });
    symlinkSync(join(workspaceRoot, "elsewhere"), workspacePath);
    expect(() =>
      restoreSessionCheckpoint(manifest, { configDir, reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS }),
    ).toThrow(CheckpointBundleError);
  });

  it("recreates an empty workspace dir even when the bundle carries no workspace files", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    rmSync(workspacePath, { recursive: true, force: true });
    restoreSessionCheckpoint(
      decodeCheckpointManifest(bytes, { workspaceRoot, reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS }),
      { configDir, reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS },
    );
    expect(existsSync(workspacePath)).toBe(true);
  });

  it("excludes the staged docs/ area from the bundle, so documents never count against the cap", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    writeFileSync(join(workspacePath, "draft.md"), "# partial report");
    mkdirSync(join(workspacePath, "docs", "nested"), { recursive: true });
    writeFileSync(join(workspacePath, "docs", "big.pdf"), "x".repeat(4096));
    writeFileSync(join(workspacePath, "docs", "nested", "more.pdf"), "y".repeat(4096));

    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      maxBytes: 2048,
    });
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(Object.keys(manifest.files).sort()).toEqual(["config/settings.json", "workspace/draft.md"]);
  });

  it("excludes the out/ deliverables area from the bundle, so drafts never count against the cap", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    writeFileSync(join(workspacePath, "draft-notes.md"), "# notes");
    mkdirSync(join(workspacePath, "out", "tables"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "x".repeat(4096));
    writeFileSync(join(workspacePath, "out", "tables", "q1.csv"), "y".repeat(4096));

    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      maxBytes: 2048,
    });
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(Object.keys(manifest.files).sort()).toEqual(["config/settings.json", "workspace/draft-notes.md"]);
  });

  it("excludes a top-level workspace entry named docs even when it is a plain file", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    writeFileSync(join(workspacePath, "docs"), "not a directory");
    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(Object.keys(manifest.files)).toEqual(["config/settings.json"]);
  });

  it("refuses to serialize a bundle larger than the byte cap", () => {
    const { configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    writeFileSync(join(workspacePath, "big.bin"), "x".repeat(200));
    expect(() =>
      serializeSessionCheckpoint({
        sessionId: "s-1",
        workspacePath,
        configDir,
        reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
        maxBytes: 64,
      }),
    ).toThrow(/exceeds/);
  });

  it("stops at the file that busts the cap, so an oversized bundle never builds its manifest string", () => {
    const { configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "x".repeat(1024 * 1024));
    // In the workspace area, which is collected after the config area, so the order here is the
    // serializer's own and not the filesystem's. Unreadable, so reaching it would throw
    // something other than the cap error: the cap is spent on the config file above, and no
    // manifest string is ever built.
    const unreadable = join(workspacePath, "draft.md");
    writeFileSync(unreadable, "y");
    chmodSync(unreadable, 0o000);
    expect(() =>
      serializeSessionCheckpoint({
        sessionId: "s-1",
        workspacePath,
        configDir,
        reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
        maxBytes: 4096,
      }),
    ).toThrow(/exceeds/);
    chmodSync(unreadable, 0o600);
  });

  it("refuses to serialize when the session dir does not exist", () => {
    const { workspacePath } = newCase();
    expect(() =>
      serializeSessionCheckpoint({
        sessionId: "s-1",
        workspacePath,
        configDir: join(TEST_ROOT, "absent"),
        reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      }),
    ).toThrow(CheckpointBundleError);
  });
});

describe("decodeCheckpointManifest", () => {
  const WORKSPACE_ROOT = "/workspaces";

  function decode(manifest: unknown): void {
    decodeCheckpointManifest(new TextEncoder().encode(JSON.stringify(manifest)), {
      workspaceRoot: WORKSPACE_ROOT,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
  }

  it("rejects bytes that are not JSON", () => {
    expect(() =>
      decodeCheckpointManifest(new TextEncoder().encode("not json"), {
        workspaceRoot: WORKSPACE_ROOT,
        reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      }),
    ).toThrow(CheckpointBundleError);
  });

  it("rejects manifests missing the session identity", () => {
    for (const bad of [
      { workspacePath: "/workspaces/run-a", files: {} },
      { sessionId: "", workspacePath: "/workspaces/run-a", files: {} },
      { sessionId: "s", files: {} },
      { sessionId: "s", workspacePath: "relative/path", files: {} },
      { sessionId: "s", workspacePath: "/workspaces/run-a", files: "none" },
      { sessionId: "s", workspacePath: "/workspaces/run-a", files: { "config/a": 7 } },
    ]) {
      expect(() => decode(bad)).toThrow(CheckpointBundleError);
    }
  });

  it("rejects a workspacePath that is not a run dir directly under the workspace root", () => {
    for (const workspacePath of [
      "/etc",
      "/workspaces",
      "/workspaces/",
      "/workspaces/../app",
      "/workspaces/run-a/nested",
      "/workspaces/checkpoints",
      "/workspaces/..",
      "/workspacesX/run-a",
      "/app/run-a",
    ]) {
      const manifest = { sessionId: "s", workspacePath, files: {} };
      expect(() => decode(manifest)).toThrow(CheckpointBundleError);
    }
  });

  it("rejects a manifest that smuggles files into the reserved docs/ and out/ areas", () => {
    for (const path of [
      "workspace/docs",
      "workspace/docs/evil.pdf",
      "workspace/docs/nested/evil.pdf",
      "workspace/out",
      "workspace/out/forged-report.md",
      "workspace/out/nested/forged.csv",
    ]) {
      const manifest = { sessionId: "s", workspacePath: "/workspaces/run-a", files: { [path]: "aGk=" } };
      expect(() => decode(manifest)).toThrow(CheckpointBundleError);
    }
  });

  it("rejects a dot-segment path that normalizes into a reserved area", () => {
    for (const path of [
      "workspace/./docs/evil.pdf",
      "workspace/././out/forged-report.md",
      "workspace/./docs",
      "workspace/.",
      "config/./settings.json",
    ]) {
      const manifest = { sessionId: "s", workspacePath: "/workspaces/run-a", files: { [path]: "aGk=" } };
      expect(() => decode(manifest)).toThrow(CheckpointBundleError);
    }
  });

  it("rejects file paths that try to escape their area", () => {
    for (const path of [
      "config/../../etc/passwd",
      "workspace/../escape",
      "/etc/passwd",
      "other/file",
      "config//x",
      "config/",
    ]) {
      const manifest = { sessionId: "s", workspacePath: "/workspaces/run-a", files: { [path]: "aGk=" } };
      expect(() => decode(manifest)).toThrow(CheckpointBundleError);
    }
  });
});

describe("the bundle version stamp", () => {
  it("round-trips all four parts, naming this build's format, SDK pin and the run's model", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      version: composeCheckpointVersionStamp({
        sidecar: { version: "1.2.3", revision: "abc123" },
        model: "a-model",
      }),
    });
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(manifest.version).toEqual({
      bundleFormat: CHECKPOINT_BUNDLE_FORMAT,
      sidecar: { version: "1.2.3", revision: "abc123" },
      sdk: EXPECTED_CLAUDE_CODE_VERSION,
      model: "a-model",
    });
  });

  it("reads a bundle written before stamps existed as unstamped, rather than refusing it", () => {
    const stampless = {
      sessionId: "s-1",
      workspacePath: "/workspaces/run-a",
      files: {},
    };
    const manifest = decodeCheckpointManifest(new TextEncoder().encode(JSON.stringify(stampless)), {
      workspaceRoot: "/workspaces",
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(manifest.version).toBeUndefined();
  });

  it("refuses a stamp that is present but incomplete, so a half-written stamp cannot read as a real one", () => {
    const halfStamped = {
      sessionId: "s-1",
      workspacePath: "/workspaces/run-a",
      files: {},
      version: { bundleFormat: "1", sdk: "2.0.0", model: "a-model" },
    };
    expect(() =>
      decodeCheckpointManifest(new TextEncoder().encode(JSON.stringify(halfStamped)), {
        workspaceRoot: "/workspaces",
        reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
      }),
    ).toThrow(CheckpointBundleError);
  });
});

describe("bundle bytes at rest", () => {
  it("writes gzip and reads it back to the same manifest", () => {
    const { workspaceRoot, configDir, workspacePath } = newCase();
    writeFileSync(join(configDir, "settings.json"), "{}");
    writeFileSync(join(workspacePath, "draft.md"), "z".repeat(4096));
    const bytes = serializeSessionCheckpoint({
      sessionId: "s-1",
      workspacePath,
      configDir,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(Array.from(bytes.slice(0, 2))).toEqual([0x1f, 0x8b]);
    const manifest = decodeCheckpointManifest(bytes, {
      workspaceRoot,
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(Buffer.from(manifest.files["workspace/draft.md"] ?? "", "base64").toString()).toBe(
      "z".repeat(4096),
    );
  });

  it("still reads an uncompressed bundle, so one written by an earlier sidecar stays restorable", () => {
    const raw = {
      sessionId: "s-1",
      workspacePath: "/workspaces/run-a",
      files: { "config/settings.json": Buffer.from("{}").toString("base64") },
    };
    const manifest = decodeCheckpointManifest(new TextEncoder().encode(JSON.stringify(raw)), {
      workspaceRoot: "/workspaces",
      reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS,
    });
    expect(manifest.sessionId).toBe("s-1");
  });

  it("refuses to inflate past the cap, so a bundle from outside this process cannot exhaust memory", () => {
    // Inflates to valid JSON, so without the bound this decodes cleanly rather than failing for
    // an unrelated reason — the refusal has to be the inflate's, and the message says which.
    const bomb = gzipSync(
      Buffer.from(
        JSON.stringify({
          sessionId: "s-1",
          workspacePath: "/workspaces/run-a",
          files: { "config/big": "A".repeat(64 * 1024) },
        }),
      ),
    );
    expect(() =>
      decodeCheckpointManifest(
        bomb,
        { workspaceRoot: "/workspaces", reservedWorkspaceDirs: RESERVED_WORKSPACE_DIRS },
        1024,
      ),
    ).toThrow(/could not be decompressed/);
  });
});
