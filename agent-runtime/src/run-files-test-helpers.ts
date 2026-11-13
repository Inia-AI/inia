import { chmodSync, existsSync, readdirSync, rmSync } from "node:fs";
import { rm } from "node:fs/promises";
import { join } from "node:path";
import { type RemoveTreeFn, runAsUid } from "./setpriv.js";
import { claimRunDir, retainedRootsUnder } from "./workspace-layout.js";

const LISTABLE_ROOT_MODE = 0o755;

// Test trees are owned by the test's own uid, so a plain rm is what the pool remover would run.
export const removeOwnTree: RemoveTreeFn = (path) => rm(path, { recursive: true, force: true });

// The retained roots are unlistable and a settled run's subtrees are closed to its uid; a test that
// sweeps up as that uid opens both first.
function shareRetainedRunsWith(workspaceRoot: string, uid: number): void {
  for (const root of retainedRootsUnder(workspaceRoot)) {
    if (!existsSync(root.path)) {
      continue;
    }
    chmodSync(root.path, LISTABLE_ROOT_MODE);
    for (const executionId of readdirSync(root.path)) {
      claimRunDir(join(root.path, executionId), uid);
    }
  }
}

// The run's uid owns the retained trace and artifact dirs it wrote; only that uid can remove them,
// exactly as the deployed purge does through setpriv.
export async function removeRunResidue(input: {
  scratch: string;
  workspaceRoot: string;
  tmpRoot: string;
  uid: number;
}): Promise<void> {
  try {
    shareRetainedRunsWith(input.workspaceRoot, input.uid);
    await runAsUid(input.uid, [
      "find",
      input.workspaceRoot,
      input.tmpRoot,
      "-user",
      String(input.uid),
      "-delete",
    ]);
  } finally {
    rmSync(input.scratch, { recursive: true, force: true });
  }
}
