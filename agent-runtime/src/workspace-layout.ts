import { spawnSync } from "node:child_process";
import { mkdirSync, statSync } from "node:fs";
import { join } from "node:path";

export const WORKSPACE_DOCUMENTS_DIR = "docs";
export const WORKSPACE_OUT_DIR = "out";

export const RESERVED_WORKSPACE_DIRS: readonly string[] = [WORKSPACE_DOCUMENTS_DIR, WORKSPACE_OUT_DIR];

export const TRACES_DIR_NAME = "traces";
export const ARTIFACTS_DIR_NAME = "artifacts";

export const FINAL_RESPONSE_FILE_NAME = "final-response.json";

const RETAINED_ROOT_MODE = 0o1777;

export type RetainedRoot = { name: string; mode: number };

export const RETAINED_WORKSPACE_ROOTS: readonly RetainedRoot[] = [
  { name: TRACES_DIR_NAME, mode: RETAINED_ROOT_MODE },
  { name: ARTIFACTS_DIR_NAME, mode: RETAINED_ROOT_MODE },
];

export type RetainedRootPath = { path: string; mode: number };

export function retainedRootsUnder(workspaceRoot: string): RetainedRootPath[] {
  return RETAINED_WORKSPACE_ROOTS.map((root) => ({ path: join(workspaceRoot, root.name), mode: root.mode }));
}

// Claimed by the supervisor before the run's first child exists: the retained roots are
// world-writable, so a lazily-created per-execution dir would be first-writer-wins
// between mutually distrusting pool uids.
export function claimRunSubtree(rootPath: string, executionId: string): void {
  claimRetainedRoot(join(rootPath, executionId));
}

// Sticky and world-writable, so every pool uid can create its own subtree but none can
// remove another's.
export function claimRetainedRoot(rootPath: string, mode: number = RETAINED_ROOT_MODE): void {
  mkdirSync(rootPath, { recursive: true, mode });
  const chmodResult = spawnSync("chmod", [mode.toString(8), rootPath]);
  if (chmodResult.status !== 0) {
    throw new Error(`could not set mode ${mode.toString(8)} on retained root ${rootPath}`);
  }
  const rootStat = statSync(rootPath);
  const ownUid = process.getuid?.();
  if (
    !rootStat.isDirectory() ||
    (rootStat.mode & 0o7777) !== mode ||
    (ownUid !== undefined && rootStat.uid !== ownUid)
  ) {
    throw new Error(`retained root ${rootPath} must be a sticky directory owned by this process`);
  }
}
