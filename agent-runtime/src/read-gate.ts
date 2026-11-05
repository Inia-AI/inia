import { randomUUID } from "node:crypto";
import { existsSync, realpathSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { basename, dirname, isAbsolute, join, relative, resolve, sep } from "node:path";
import type { HookCallback, HookJSONOutput } from "@anthropic-ai/claude-agent-sdk";
import { assertUnderSandboxDeniedRoot } from "./config.js";
import { closeSizedFile, openSizedFile, readSizedFile } from "./fs-read.js";
import { sha256Hex } from "./hash.js";
import { isRecord } from "./narrow.js";
import { claimPrivateRoot, PRIVATE_FILE_MODE, STAGING_PREFIX } from "./private-fs.js";
import { WORKSPACE_DOCUMENTS_DIR } from "./workspace-layout.js";

export const READ_TOOL_NAME = "Read";

export const READ_MIRROR_DIR_NAME = "doc-mirror";

const DENIED_SYSTEM_ROOTS: readonly string[] = ["/proc", "/sys", "/dev"];

export const DENIED_SYSTEM_ROOT_REASON = "reading /proc, /sys, or /dev is not allowed";

export type StagedDocumentDigest = { name: string; sha256: string; sizeBytes: number };

export type ReadMirror = {
  dir: string;
  documents: readonly StagedDocumentDigest[];
};

export function claimReadMirror(privateHome: string, documents: readonly StagedDocumentDigest[]): ReadMirror {
  const dir = join(privateHome, READ_MIRROR_DIR_NAME);
  assertUnderSandboxDeniedRoot(dir, "the document read mirror");
  claimPrivateRoot(dir, "document read mirror");
  return { dir, documents };
}

export type ReadGateDecision = { allowed: true; resolvedPath: string } | { allowed: false; reason: string };

function deny(reason: string): ReadGateDecision {
  return { allowed: false, reason };
}

function isUnderDeniedSystemRoot(path: string): boolean {
  return DENIED_SYSTEM_ROOTS.some((root) => path === root || path.startsWith(`${root}${sep}`));
}

function isWithin(root: string, candidate: string): boolean {
  const relation = relative(root, candidate);
  if (relation === "") {
    return true;
  }
  return relation !== ".." && !relation.startsWith(`..${sep}`) && !isAbsolute(relation);
}

function mirrorDocumentRead(
  document: StagedDocumentDigest,
  mirrorDir: string,
  workspacePath: string,
  resolvedWorkspace: string | undefined,
): ReadGateDecision {
  const mirrorPath = join(mirrorDir, document.name);
  // Only this gate writes the mirror, atomically (below), so an existing entry is always finished.
  if (existsSync(mirrorPath)) {
    return { allowed: true, resolvedPath: mirrorPath };
  }
  let workspace = resolvedWorkspace;
  if (workspace === undefined) {
    try {
      workspace = realpathSync(workspacePath);
    } catch {
      return deny(`the run workspace at ${workspacePath} is not resolvable`);
    }
  }
  const sourcePath = join(workspace, WORKSPACE_DOCUMENTS_DIR, document.name);
  let bytes: Buffer;
  try {
    const source = openSizedFile(sourcePath);
    try {
      // Check fstat size before reading bytes: docs/ is Bash-writable, so a run could grow its own copy into an unbounded read.
      if (source.sizeBytes !== document.sizeBytes) {
        return deny(`${sourcePath} no longer matches its staged size and will not be read`);
      }
      bytes = readSizedFile(source);
    } finally {
      closeSizedFile(source);
    }
  } catch {
    return deny(`${sourcePath} is not readable as a staged document`);
  }
  if (sha256Hex(bytes) !== document.sha256) {
    return deny(`${sourcePath} does not match the staged document's bytes`);
  }
  // Write-then-rename: a torn write must never leave a truncated file for the existsSync fast path above to serve forever.
  const stagingPath = join(mirrorDir, `${STAGING_PREFIX}${randomUUID()}`);
  try {
    writeFileSync(stagingPath, bytes, { mode: PRIVATE_FILE_MODE });
    renameSync(stagingPath, mirrorPath);
  } catch {
    rmSync(stagingPath, { force: true });
    return deny(`the staged document ${document.name} could not be mirrored for reading`);
  }
  return { allowed: true, resolvedPath: mirrorPath };
}

export const MAX_DENIAL_LISTED_DOCUMENTS = 20;

const NO_STAGED_DOCUMENTS_SUFFIX =
  "is not one of this run's staged input documents, and this run has no staged input documents — Read serves nothing else";

function denyNotStaged(filePath: string, mirror: ReadMirror | undefined): ReadGateDecision {
  const documents = mirror?.documents ?? [];
  if (documents.length === 0) {
    return deny(`${filePath} ${NO_STAGED_DOCUMENTS_SUFFIX}`);
  }
  const listed = documents
    .slice(0, MAX_DENIAL_LISTED_DOCUMENTS)
    .map((document) => join(WORKSPACE_DOCUMENTS_DIR, document.name));
  const unlisted = documents.length - listed.length;
  const remainder = unlisted > 0 ? `, and ${unlisted} more` : "";
  const served = `${listed.join(", ")}${remainder}`;
  return deny(`${filePath} is not one of this run's staged input documents; Read serves only: ${served}`);
}

export function evaluateReadRequest(
  toolInput: unknown,
  workspacePath: string,
  mirror: ReadMirror | undefined,
): ReadGateDecision {
  if (!isRecord(toolInput)) {
    return deny("Read input must be an object");
  }
  const filePath = toolInput["file_path"];
  if (typeof filePath !== "string" || filePath === "") {
    return deny("file_path is required and must be a non-empty string");
  }
  if (!isAbsolute(filePath)) {
    return deny("file_path must be an absolute path naming a staged document in the run workspace");
  }
  const requested = resolve(filePath);
  if (isUnderDeniedSystemRoot(requested)) {
    return deny(DENIED_SYSTEM_ROOT_REASON);
  }
  if (mirror === undefined) {
    return denyNotStaged(filePath, mirror);
  }
  const mirrored = mirror.documents.find((document) => requested === join(mirror.dir, document.name));
  if (mirrored !== undefined) {
    return mirrorDocumentRead(mirrored, mirror.dir, workspacePath, undefined);
  }
  let resolvedWorkspace: string;
  let resolvedPath: string;
  try {
    resolvedWorkspace = realpathSync(workspacePath);
    resolvedPath = realpathSync(filePath);
  } catch {
    return deny(`${filePath} does not resolve to a staged document in the run workspace`);
  }
  if (isUnderDeniedSystemRoot(resolvedPath)) {
    return deny(DENIED_SYSTEM_ROOT_REASON);
  }
  if (!isWithin(resolvedWorkspace, resolvedPath)) {
    return deny(`${filePath} resolves outside the run workspace`);
  }
  if (dirname(resolvedPath) !== join(resolvedWorkspace, WORKSPACE_DOCUMENTS_DIR)) {
    return denyNotStaged(filePath, mirror);
  }
  const name = basename(resolvedPath);
  const document = mirror.documents.find((candidate) => candidate.name === name);
  if (document === undefined) {
    return denyNotStaged(filePath, mirror);
  }
  // `resolvedPath` only classifies which document is requested; never read it directly —
  // a path component could be swapped for a symlink between this classify and the copy's open.
  return mirrorDocumentRead(document, mirror.dir, workspacePath, resolvedWorkspace);
}

// Allowlist, not a spread: an unrecognized key (e.g. `__proto__`) would reach the tool unreasoned-about.
const FORWARDED_READ_PARAMS: readonly string[] = ["offset", "limit", "pages"];

function forwardedReadParams(toolInput: unknown): Record<string, unknown> {
  if (!isRecord(toolInput)) {
    return {};
  }
  const forwarded: Record<string, unknown> = {};
  for (const name of FORWARDED_READ_PARAMS) {
    if (Object.hasOwn(toolInput, name)) {
      forwarded[name] = toolInput[name];
    }
  }
  return forwarded;
}

export function createReadGateHook(workspacePath: string, mirror: ReadMirror | undefined): HookCallback {
  return (input): Promise<HookJSONOutput> => {
    if (input.hook_event_name !== "PreToolUse" || input.tool_name !== READ_TOOL_NAME) {
      return Promise.resolve({});
    }
    const decision = evaluateReadRequest(input.tool_input, workspacePath, mirror);
    if (!decision.allowed) {
      return Promise.resolve({
        hookSpecificOutput: {
          hookEventName: "PreToolUse",
          permissionDecision: "deny",
          permissionDecisionReason: decision.reason,
        },
      });
    }
    return Promise.resolve({
      hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "allow",
        updatedInput: { ...forwardedReadParams(input.tool_input), file_path: decision.resolvedPath },
      },
    });
  };
}
