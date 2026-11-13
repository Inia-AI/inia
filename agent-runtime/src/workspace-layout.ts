import { spawnSync } from "node:child_process";
import { chmodSync, mkdirSync, readdirSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { hasErrorCode } from "./errors.js";
import { groupMemberArgs } from "./setpriv.js";

export const WORKSPACE_DOCUMENTS_DIR = "docs";
export const WORKSPACE_OUT_DIR = "out";

export const RESERVED_WORKSPACE_DIRS: readonly string[] = [WORKSPACE_DOCUMENTS_DIR, WORKSPACE_OUT_DIR];

export const TRACES_DIR_NAME = "traces";
export const ARTIFACTS_DIR_NAME = "artifacts";

export const FINAL_RESPONSE_FILE_NAME = "final-response.json";

const RETAINED_ROOT_MODE = 0o711;

const RUN_SUBTREE_MODE = 0o770;

const SEALED_RUN_SUBTREE_MODE = 0o700;

export type RetainedRoot = { name: string; mode: number };

export const RETAINED_WORKSPACE_ROOTS: readonly RetainedRoot[] = [
  { name: TRACES_DIR_NAME, mode: RETAINED_ROOT_MODE },
  { name: ARTIFACTS_DIR_NAME, mode: RETAINED_ROOT_MODE },
];

export type RetainedRootPath = { path: string; mode: number };

export function retainedRootsUnder(workspaceRoot: string): RetainedRootPath[] {
  return RETAINED_WORKSPACE_ROOTS.map((root) => ({ path: join(workspaceRoot, root.name), mode: root.mode }));
}

// Owned by the supervisor and shared only with the run's uid through its group: no other pool uid
// can list, read or write it, even from outside the sandbox. Claiming an existing dir opens it again.
export function claimRunDir(runDir: string, runUid: number): void {
  mkdirSync(runDir, { mode: SEALED_RUN_SUBTREE_MODE, recursive: true });
  if (statSync(runDir).gid !== runUid) {
    giveGroupTo(runDir, runUid);
  }
  chmodSync(runDir, RUN_SUBTREE_MODE);
  if (!isOwnDirectory(runDir, RUN_SUBTREE_MODE, runUid)) {
    throw new Error(
      `run subtree ${runDir} must be a directory owned by this process and shared with uid ${runUid}`,
    );
  }
}

export function sealRunDir(runDir: string): void {
  try {
    chmodSync(runDir, SEALED_RUN_SUBTREE_MODE);
  } catch (error) {
    if (!hasErrorCode(error, "ENOENT")) {
      throw error;
    }
  }
}

// Pool uids are reused, so every run dir shared with a uid closes before that uid runs again.
export function sealRunDirsSharedWith(rootPath: string, uid: number): void {
  sealRunDirsWhere(rootPath, (gid) => gid === uid);
}

// A pool starts with every uid free, and a workspace that outlives the container (a pod's
// emptyDir) keeps the dirs an earlier pool left open.
export function sealEveryRunDir(rootPath: string): void {
  sealRunDirsWhere(rootPath, () => true);
}

function sealRunDirsWhere(rootPath: string, sharedWith: (gid: number) => boolean): void {
  for (const name of runDirNamesUnder(rootPath)) {
    const runDir = join(rootPath, name);
    const stats = statSync(runDir, { throwIfNoEntry: false });
    if (stats !== undefined && sharedWith(stats.gid) && (stats.mode & 0o777) !== SEALED_RUN_SUBTREE_MODE) {
      sealRunDir(runDir);
    }
  }
}

function runDirNamesUnder(rootPath: string): string[] {
  try {
    return readdirSync(rootPath, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name);
  } catch (error) {
    if (hasErrorCode(error, "ENOENT")) {
      return [];
    }
    throw error;
  }
}

// The purge removes an attempt as the uid that wrote it, from inside the attempt's sealed run dir.
export const attemptRunDirAccess = {
  openTo: (attemptPath: string, uid: number): void => claimRunDir(dirname(attemptPath), uid),
  close: (attemptPath: string): void => sealRunDir(dirname(attemptPath)),
};

function giveGroupTo(path: string, gid: number): void {
  const result = spawnSync("setpriv", [...groupMemberArgs(gid), "--", "chgrp", String(gid), path], {
    encoding: "utf8",
  });
  if (result.status !== 0) {
    throw new Error(`could not give group ${gid} to ${path}: ${result.stderr.trim()}`);
  }
}

function isOwnDirectory(path: string, mode: number, gid?: number): boolean {
  const stats = statSync(path);
  const ownUid = process.getuid?.();
  return (
    stats.isDirectory() &&
    (stats.mode & 0o7777) === mode &&
    (gid === undefined || stats.gid === gid) &&
    (ownUid === undefined || stats.uid === ownUid)
  );
}

export function claimRetainedRoot(rootPath: string, mode: number = RETAINED_ROOT_MODE): void {
  mkdirSync(rootPath, { recursive: true, mode });
  const chmodResult = spawnSync("chmod", [mode.toString(8), rootPath]);
  if (chmodResult.status !== 0) {
    throw new Error(`could not set mode ${mode.toString(8)} on retained root ${rootPath}`);
  }
  if (!isOwnDirectory(rootPath, mode)) {
    throw new Error(`retained root ${rootPath} must be a directory owned by this process`);
  }
}
