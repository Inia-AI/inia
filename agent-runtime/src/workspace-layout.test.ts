import { describe, expect, it } from "bun:test";
import { chmodSync, mkdtempSync, readFileSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { UID_POOL_BASE } from "./config.js";
import { LOOPBACK_PROCESS_UID } from "./loopback-process.js";
import { runAsUid } from "./setpriv.js";
import {
  claimRetainedRoot,
  claimRunDir,
  sealEveryRunDir,
  sealRunDirsSharedWith,
} from "./workspace-layout.js";

const RUN_UID = UID_POOL_BASE;
const OTHER_RUN_UID = UID_POOL_BASE + 1;
const EXECUTION_ID = "e-victim";

function retainedRoot(name: string): string {
  const area = mkdtempSync(join(tmpdir(), `${name}-`));
  chmodSync(area, 0o755);
  const root = join(area, "artifacts");
  claimRetainedRoot(root);
  return root;
}

function claimRunSubtree(root: string, executionId: string, uid: number): void {
  claimRunDir(join(root, executionId), uid);
}

async function publishAsRun(root: string): Promise<string> {
  const attemptDir = join(root, EXECUTION_ID, "attempt-1");
  const report = join(attemptDir, "report.md");
  await runAsUid(RUN_UID, [
    "sh",
    "-c",
    `mkdir -m 755 ${attemptDir} && echo report > ${report} && chmod 644 ${report}`,
  ]);
  return report;
}

describe("a run's subtree under a retained root", () => {
  it("lets the run's own uid write and read it", async () => {
    const root = retainedRoot("own-run");
    claimRunSubtree(root, EXECUTION_ID, RUN_UID);
    const report = await publishAsRun(root);
    await expect(runAsUid(RUN_UID, ["cat", report])).resolves.toBeUndefined();
  });

  it("refuses another run's uid a listing of the root and a listing, read or write of the subtree", async () => {
    const root = retainedRoot("other-run");
    claimRunSubtree(root, EXECUTION_ID, RUN_UID);
    const report = await publishAsRun(root);
    await expect(runAsUid(OTHER_RUN_UID, ["ls", root])).rejects.toThrow(/Permission denied/);
    await expect(runAsUid(OTHER_RUN_UID, ["ls", join(root, EXECUTION_ID)])).rejects.toThrow(
      /Permission denied/,
    );
    await expect(runAsUid(OTHER_RUN_UID, ["cat", report])).rejects.toThrow(/Permission denied/);
    await expect(
      runAsUid(OTHER_RUN_UID, ["sh", "-c", `echo planted > ${join(root, EXECUTION_ID, "planted")}`]),
    ).rejects.toThrow(/Permission denied/);
  });

  it("refuses the loopback model process's uid a read of a run's trace", async () => {
    const root = retainedRoot("loopback");
    claimRunSubtree(root, EXECUTION_ID, RUN_UID);
    const report = await publishAsRun(root);
    await expect(runAsUid(LOOPBACK_PROCESS_UID, ["cat", report])).rejects.toThrow(/Permission denied/);
  });

  it("refuses a run's uid an entry of its own directly under the root", async () => {
    const root = retainedRoot("root-entry");
    await expect(runAsUid(RUN_UID, ["mkdir", join(root, "e-squatted")])).rejects.toThrow(/Permission denied/);
  });

  it("closes every subtree shared with a uid to that uid, while the supervisor still reads it", async () => {
    const root = retainedRoot("sealed");
    claimRunSubtree(root, EXECUTION_ID, RUN_UID);
    claimRunSubtree(root, "e-earlier", RUN_UID);
    claimRunSubtree(root, "e-other", OTHER_RUN_UID);
    const report = await publishAsRun(root);
    sealRunDirsSharedWith(root, RUN_UID);
    await expect(runAsUid(RUN_UID, ["cat", report])).rejects.toThrow(/Permission denied/);
    expect(readFileSync(report, "utf8")).toBe("report\n");
    expect(statSync(join(root, "e-earlier")).mode & 0o777).toBe(0o700);
    expect(statSync(join(root, "e-other")).mode & 0o777).toBe(0o770);
  });

  it("closes every subtree under a root, whichever uid it was shared with, as a boot finds them", () => {
    const root = retainedRoot("boot");
    claimRunSubtree(root, EXECUTION_ID, RUN_UID);
    claimRunSubtree(root, "e-other", OTHER_RUN_UID);
    sealEveryRunDir(root);
    expect(statSync(join(root, EXECUTION_ID)).mode & 0o777).toBe(0o700);
    expect(statSync(join(root, "e-other")).mode & 0o777).toBe(0o700);
  });

  it("opens a sealed subtree again to the uid of the run that claims it next", async () => {
    const root = retainedRoot("reclaimed");
    claimRunSubtree(root, EXECUTION_ID, RUN_UID);
    sealRunDirsSharedWith(root, RUN_UID);
    claimRunSubtree(root, EXECUTION_ID, OTHER_RUN_UID);
    const runDir = join(root, EXECUTION_ID);
    expect(statSync(runDir).gid).toBe(OTHER_RUN_UID);
    await expect(runAsUid(OTHER_RUN_UID, ["mkdir", join(runDir, "attempt-2")])).resolves.toBeUndefined();
    await expect(runAsUid(RUN_UID, ["ls", runDir])).rejects.toThrow(/Permission denied/);
  });
});
