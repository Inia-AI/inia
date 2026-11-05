import { afterEach, beforeEach, describe, expect, it } from "bun:test";
import { spawnSync } from "node:child_process";
import {
  appendFileSync,
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
import type { HookJSONOutput, PreToolUseHookInput, SyncHookJSONOutput } from "@anthropic-ai/claude-agent-sdk";
import { sha256Hex } from "./hash.js";
import {
  createReadGateHook,
  DENIED_SYSTEM_ROOT_REASON,
  evaluateReadRequest,
  MAX_DENIAL_LISTED_DOCUMENTS,
  type ReadMirror,
} from "./read-gate.js";
import { WORKSPACE_DOCUMENTS_DIR } from "./workspace-layout.js";

function hookSpecificOutput(output: HookJSONOutput): SyncHookJSONOutput["hookSpecificOutput"] {
  if ("async" in output) {
    throw new Error("expected a synchronous hook output");
  }
  return output.hookSpecificOutput;
}

const DOCUMENT_NAME = "accounts.pdf";
const DOCUMENT_BYTES = Buffer.from("%PDF-1.7 the staged accounts document");

let scratchRoot: string;
let workspacePath: string;
let docsDir: string;
let stagedDocumentPath: string;
let mirror: ReadMirror;
let outsidePath: string;

beforeEach(() => {
  scratchRoot = mkdtempSync(join(tmpdir(), "read-gate-"));
  workspacePath = join(scratchRoot, "run-a");
  docsDir = join(workspacePath, WORKSPACE_DOCUMENTS_DIR);
  mkdirSync(docsDir, { recursive: true });
  stagedDocumentPath = join(docsDir, DOCUMENT_NAME);
  writeFileSync(stagedDocumentPath, DOCUMENT_BYTES);
  const mirrorDir = join(scratchRoot, "doc-mirror");
  mkdirSync(mirrorDir);
  mirror = {
    dir: mirrorDir,
    documents: [{ name: DOCUMENT_NAME, sha256: sha256Hex(DOCUMENT_BYTES), sizeBytes: DOCUMENT_BYTES.length }],
  };
  outsidePath = join(scratchRoot, "outside.txt");
  writeFileSync(outsidePath, "secret");
});

afterEach(() => {
  rmSync(scratchRoot, { recursive: true, force: true });
});

function readInput(filePath: string): { file_path: string } {
  return { file_path: filePath };
}

function approve(filePath: string): string {
  const decision = evaluateReadRequest(readInput(filePath), workspacePath, mirror);
  if (!decision.allowed) {
    throw new Error(`expected approval, got: ${decision.reason}`);
  }
  return decision.resolvedPath;
}

describe("evaluateReadRequest", () => {
  it("rewrites an approved document read to a mirror copy outside the workspace", () => {
    const resolvedPath = approve(stagedDocumentPath);
    expect(resolvedPath).toBe(join(mirror.dir, DOCUMENT_NAME));
    expect(readFileSync(resolvedPath)).toEqual(DOCUMENT_BYTES);
    expect(resolvedPath.startsWith(workspacePath)).toBe(false);
  });

  it("never yields the swapped target after the docs directory is replaced by a symlink post-approval", () => {
    const resolvedPath = approve(stagedDocumentPath);
    const swappedIn = Buffer.from("swapped-in secret");
    const attackerDir = join(scratchRoot, "attacker");
    mkdirSync(attackerDir);
    writeFileSync(join(attackerDir, DOCUMENT_NAME), swappedIn);
    rmSync(docsDir, { recursive: true, force: true });
    symlinkSync(attackerDir, docsDir);
    expect(resolvedPath).toBe(join(mirror.dir, DOCUMENT_NAME));
    expect(readFileSync(resolvedPath)).toEqual(DOCUMENT_BYTES);
    // A re-evaluation is allowed to fail closed OR keep resolving to the cached mirror copy —
    // either is safe, so this deliberately doesn't assert a single outcome.
    const again = evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, mirror);
    if (again.allowed) {
      expect(again.resolvedPath).toBe(resolvedPath);
      expect(readFileSync(again.resolvedPath)).toEqual(DOCUMENT_BYTES);
    }
    expect(readFileSync(resolvedPath)).toEqual(DOCUMENT_BYTES);
  });

  it("keeps serving the mirror copy after the workspace source is overwritten", () => {
    const resolvedPath = approve(stagedDocumentPath);
    writeFileSync(stagedDocumentPath, "tampered after first read");
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, mirror)).toEqual({
      allowed: true,
      resolvedPath,
    });
    expect(readFileSync(resolvedPath)).toEqual(DOCUMENT_BYTES);
  });

  it("refuses a first read whose workspace bytes no longer match the staged digest", () => {
    writeFileSync(stagedDocumentPath, "tampered before any read");
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, mirror).allowed).toBe(false);
  });

  it("refuses a staged doc grown past its staged size before reading it, by the size rule", () => {
    appendFileSync(stagedDocumentPath, Buffer.alloc(64 * 1024, 0x41));
    const decision = evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, mirror);
    expect(decision).toEqual({
      allowed: false,
      reason: `${join(docsDir, DOCUMENT_NAME)} no longer matches its staged size and will not be read`,
    });
    expect(existsSync(join(mirror.dir, DOCUMENT_NAME))).toBe(false);
  });

  it("refuses a first read whose docs directory already points outside the workspace", () => {
    const attackerDir = join(scratchRoot, "attacker");
    mkdirSync(attackerDir);
    writeFileSync(join(attackerDir, DOCUMENT_NAME), "outside bytes");
    rmSync(docsDir, { recursive: true, force: true });
    symlinkSync(attackerDir, docsDir);
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, mirror).allowed).toBe(false);
  });

  it("approves a read addressed directly at the cached mirror copy", () => {
    const resolvedPath = approve(stagedDocumentPath);
    expect(evaluateReadRequest(readInput(resolvedPath), workspacePath, mirror)).toEqual({
      allowed: true,
      resolvedPath,
    });
  });

  it("refuses a workspace file that is not a staged document", () => {
    const filePath = join(workspacePath, "report.pdf");
    writeFileSync(filePath, "pdf");
    expect(evaluateReadRequest(readInput(filePath), workspacePath, mirror).allowed).toBe(false);
    const unstagedInDocs = join(docsDir, "unstaged.pdf");
    writeFileSync(unstagedInDocs, "pdf");
    expect(evaluateReadRequest(readInput(unstagedInDocs), workspacePath, mirror).allowed).toBe(false);
  });

  it("refuses the docs manifest even though staging writes it beside the documents", () => {
    const manifestPath = join(docsDir, "manifest.json");
    writeFileSync(manifestPath, "[]");
    expect(evaluateReadRequest(readInput(manifestPath), workspacePath, mirror).allowed).toBe(false);
  });

  it("refuses every read when the run has no staged documents", () => {
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, undefined).allowed).toBe(false);
    const empty: ReadMirror = { dir: mirror.dir, documents: [] };
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, empty).allowed).toBe(false);
  });

  it("names the staged documents Read will serve when a workspace file is not staged", () => {
    const unstagedInDocs = join(docsDir, "unstaged.pdf");
    writeFileSync(unstagedInDocs, "pdf");
    const servedName = join(WORKSPACE_DOCUMENTS_DIR, DOCUMENT_NAME);
    expect(evaluateReadRequest(readInput(unstagedInDocs), workspacePath, mirror)).toEqual({
      allowed: false,
      reason: `${unstagedInDocs} is not one of this run's staged input documents; Read serves only: ${servedName}`,
    });
  });

  it("caps the named staged documents in a denial and counts the remainder", () => {
    const documents = Array.from({ length: MAX_DENIAL_LISTED_DOCUMENTS + 5 }, (_, index) => ({
      name: `doc-${String(index).padStart(2, "0")}.pdf`,
      sha256: sha256Hex(DOCUMENT_BYTES),
      sizeBytes: DOCUMENT_BYTES.length,
    }));
    const wide: ReadMirror = { dir: mirror.dir, documents };
    const unstagedInDocs = join(docsDir, "unstaged.pdf");
    writeFileSync(unstagedInDocs, "pdf");
    const decision = evaluateReadRequest(readInput(unstagedInDocs), workspacePath, wide);
    if (decision.allowed) {
      throw new Error("expected a denial");
    }
    expect(decision.reason).toContain(join(WORKSPACE_DOCUMENTS_DIR, "doc-00.pdf"));
    expect(decision.reason).toContain(join(WORKSPACE_DOCUMENTS_DIR, "doc-19.pdf"));
    expect(decision.reason).not.toContain("doc-20.pdf");
    expect(decision.reason).toEndWith(", and 5 more");
  });

  it("says the run has no staged documents rather than dangling an empty list", () => {
    const noDocumentsReason = `${stagedDocumentPath} is not one of this run's staged input documents, and this run has no staged input documents — Read serves nothing else`;
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, undefined)).toEqual({
      allowed: false,
      reason: noDocumentsReason,
    });
    const empty: ReadMirror = { dir: mirror.dir, documents: [] };
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, empty)).toEqual({
      allowed: false,
      reason: noDocumentsReason,
    });
  });

  it("judges the path alone, so extra Read parameters neither allow nor deny", () => {
    expect(
      evaluateReadRequest({ file_path: stagedDocumentPath, pages: "1-5" }, workspacePath, mirror).allowed,
    ).toBe(true);
    expect(evaluateReadRequest({ file_path: outsidePath, pages: "1-5" }, workspacePath, mirror).allowed).toBe(
      false,
    );
  });

  it("refuses a document reached through a symlinked intermediate directory outside the workspace", () => {
    const outsideDir = join(scratchRoot, "elsewhere");
    mkdirSync(outsideDir);
    writeFileSync(join(outsideDir, DOCUMENT_NAME), DOCUMENT_BYTES);
    symlinkSync(outsideDir, join(workspacePath, "shortcut"));
    const decision = evaluateReadRequest(
      readInput(join(workspacePath, "shortcut", DOCUMENT_NAME)),
      workspacePath,
      mirror,
    );
    expect(decision.allowed).toBe(false);
  });

  it("refuses a docs symlink whose target resolves into /proc, by the denied-root rule", () => {
    rmSync(stagedDocumentPath);
    symlinkSync("/proc/self/environ", stagedDocumentPath);
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, mirror)).toEqual({
      allowed: false,
      reason: DENIED_SYSTEM_ROOT_REASON,
    });
  });

  it("contains reads when the workspace root is itself reached through a symlink", () => {
    const linkedRoot = join(scratchRoot, "linked-root");
    symlinkSync(workspacePath, linkedRoot);
    const throughLink = evaluateReadRequest(
      readInput(join(linkedRoot, WORKSPACE_DOCUMENTS_DIR, DOCUMENT_NAME)),
      linkedRoot,
      mirror,
    );
    expect(throughLink).toEqual({ allowed: true, resolvedPath: join(mirror.dir, DOCUMENT_NAME) });
    expect(evaluateReadRequest(readInput(outsidePath), linkedRoot, mirror).allowed).toBe(false);
  });

  it("refuses the workspace directory, the docs directory, and any non-regular file", () => {
    expect(evaluateReadRequest(readInput(workspacePath), workspacePath, mirror).allowed).toBe(false);
    expect(evaluateReadRequest(readInput(docsDir), workspacePath, mirror).allowed).toBe(false);
    rmSync(stagedDocumentPath);
    const mkfifo = spawnSync("mkfifo", [stagedDocumentPath]);
    expect(mkfifo.status).toBe(0);
    expect(statSync(stagedDocumentPath).isFIFO()).toBe(true);
    expect(evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, mirror).allowed).toBe(false);
  });

  it("refuses a staged-document name reached from outside the workspace", () => {
    const decision = evaluateReadRequest(readInput(outsidePath), workspacePath, mirror);
    expect(decision.allowed).toBe(false);
  });

  it("refuses /proc/self/environ by the denied-root rule, not merely by containment", () => {
    expect(evaluateReadRequest(readInput("/proc/self/environ"), workspacePath, mirror)).toEqual({
      allowed: false,
      reason: DENIED_SYSTEM_ROOT_REASON,
    });
  });

  it("refuses /proc, /sys, and /dev paths even when they do not exist, by the denied-root rule", () => {
    for (const denied of ["/proc/1/cmdline", "/sys/kernel/hostname", "/dev/mem", "/proc", "/sys", "/dev"]) {
      expect(evaluateReadRequest(readInput(denied), workspacePath, mirror)).toEqual({
        allowed: false,
        reason: DENIED_SYSTEM_ROOT_REASON,
      });
    }
  });

  it("refuses a lexical traversal that lands under /proc before resolution, by the denied-root rule", () => {
    expect(
      evaluateReadRequest(
        readInput(join(workspacePath, "..", "..", "..", "proc", "self", "environ")),
        workspacePath,
        mirror,
      ),
    ).toEqual({ allowed: false, reason: DENIED_SYSTEM_ROOT_REASON });
  });

  it("refuses dot-dot traversal escaping the workspace", () => {
    const decision = evaluateReadRequest(
      readInput(join(workspacePath, "..", "outside.txt")),
      workspacePath,
      mirror,
    );
    expect(decision.allowed).toBe(false);
  });

  it("refuses a relative path", () => {
    const decision = evaluateReadRequest(readInput(DOCUMENT_NAME), workspacePath, mirror);
    expect(decision.allowed).toBe(false);
  });

  it("refuses a staged document whose workspace copy is missing (resolution fails closed)", () => {
    rmSync(stagedDocumentPath);
    const decision = evaluateReadRequest(readInput(stagedDocumentPath), workspacePath, mirror);
    expect(decision.allowed).toBe(false);
  });

  it("refuses when the workspace root itself cannot be resolved", () => {
    const decision = evaluateReadRequest(readInput(stagedDocumentPath), join(scratchRoot, "gone"), mirror);
    expect(decision.allowed).toBe(false);
  });

  it("refuses malformed tool input", () => {
    for (const input of [undefined, null, "path", 7, [], {}, { file_path: 3 }, { file_path: "" }]) {
      const decision = evaluateReadRequest(input, workspacePath, mirror);
      expect(decision.allowed).toBe(false);
    }
  });
});

function preToolUseInput(toolName: string, toolInput: unknown): PreToolUseHookInput {
  return {
    hook_event_name: "PreToolUse",
    tool_name: toolName,
    tool_input: toolInput,
    tool_use_id: "toolu_1",
    session_id: "session-1",
    transcript_path: "/dev/null",
    cwd: workspacePath,
  };
}

describe("createReadGateHook", () => {
  it("denies a Read whose path escapes the workspace, with a reason", async () => {
    const hook = createReadGateHook(workspacePath, mirror);
    const output = await hook(preToolUseInput("Read", readInput(outsidePath)), "toolu_1", {
      signal: new AbortController().signal,
    });
    expect(hookSpecificOutput(output)).toMatchObject({
      hookEventName: "PreToolUse",
      permissionDecision: "deny",
    });
  });

  it("allows a staged-document Read and rewrites file_path to the mirror copy", async () => {
    const hook = createReadGateHook(workspacePath, mirror);
    const output = await hook(
      preToolUseInput("Read", { file_path: stagedDocumentPath, pages: "2-4" }),
      "toolu_1",
      { signal: new AbortController().signal },
    );
    expect(hookSpecificOutput(output)).toEqual({
      hookEventName: "PreToolUse",
      permissionDecision: "allow",
      updatedInput: { file_path: join(mirror.dir, DOCUMENT_NAME), pages: "2-4" },
    });
    expect(readFileSync(join(mirror.dir, DOCUMENT_NAME))).toEqual(DOCUMENT_BYTES);
  });

  it("forwards only the Read tool's own parameters, dropping anything else the model sent", async () => {
    const hook = createReadGateHook(workspacePath, mirror);
    const output = await hook(
      preToolUseInput("Read", {
        file_path: stagedDocumentPath,
        offset: 2,
        limit: 10,
        pages: "1-3",
        sandbox: false,
        smuggled: "../../etc/passwd",
      }),
      "toolu_1",
      { signal: new AbortController().signal },
    );
    expect(hookSpecificOutput(output)).toEqual({
      hookEventName: "PreToolUse",
      permissionDecision: "allow",
      updatedInput: { file_path: join(mirror.dir, DOCUMENT_NAME), offset: 2, limit: 10, pages: "1-3" },
    });
  });

  it("omits Read parameters the model did not send rather than inventing them", async () => {
    const hook = createReadGateHook(workspacePath, mirror);
    const output = await hook(preToolUseInput("Read", { file_path: stagedDocumentPath }), "toolu_1", {
      signal: new AbortController().signal,
    });
    expect(hookSpecificOutput(output)).toEqual({
      hookEventName: "PreToolUse",
      permissionDecision: "allow",
      updatedInput: { file_path: join(mirror.dir, DOCUMENT_NAME) },
    });
  });

  it("denies a Read with malformed input", async () => {
    const hook = createReadGateHook(workspacePath, mirror);
    const output = await hook(preToolUseInput("Read", { nope: true }), "toolu_1", {
      signal: new AbortController().signal,
    });
    expect(hookSpecificOutput(output)).toMatchObject({ permissionDecision: "deny" });
  });

  it("expresses no opinion on other tools", async () => {
    const hook = createReadGateHook(workspacePath, mirror);
    const output = await hook(preToolUseInput("Bash", { command: "ls" }), "toolu_1", {
      signal: new AbortController().signal,
    });
    expect(output).toEqual({});
  });

  it("expresses no opinion on non-PreToolUse events", async () => {
    const hook = createReadGateHook(workspacePath, mirror);
    const output = await hook(
      {
        hook_event_name: "SessionEnd",
        reason: "other",
        session_id: "session-1",
        transcript_path: "/dev/null",
        cwd: workspacePath,
      },
      undefined,
      { signal: new AbortController().signal },
    );
    expect(output).toEqual({});
  });
});
