import { describe, expect, it } from "bun:test";
import { EventEmitter } from "node:events";
import { chmodSync, existsSync, lstatSync, mkdirSync, mkdtempSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { UID_POOL_BASE, UID_POOL_SIZE } from "./config.js";
import {
  createPoolUidTreeRemover,
  privilegeDropArgs,
  RunAsUidError,
  runAsUid,
  signalRunArgv,
} from "./setpriv.js";
import { createUidPool, type UidPool, UidPoolExhaustedError } from "./uid-pool.js";
import {
  attemptRunDirAccess,
  claimRetainedRoot,
  claimRunDir,
  sealRunDirsSharedWith,
} from "./workspace-layout.js";

describe("privilegeDropArgs", () => {
  it("drops to exactly the given uid and gid with every capability cleared", () => {
    expect(privilegeDropArgs(20001)).toEqual([
      "--reuid=20001",
      "--regid=20001",
      "--clear-groups",
      "--inh-caps=-all",
      "--ambient-caps=-all",
    ]);
  });
});

describe("runAsUid", () => {
  it("runs the command under the pool uid and resolves on success", async () => {
    await expect(runAsUid(UID_POOL_BASE, ["true"])).resolves.toBeUndefined();
  });

  it("rejects with the command's stderr when it fails", async () => {
    await expect(runAsUid(UID_POOL_BASE, ["sh", "-c", "echo denied >&2; exit 3"])).rejects.toThrow(/denied/);
  });

  it("names the signal when the command was killed by one, which has no exit code to report", async () => {
    const failure = await runAsUid(UID_POOL_BASE, ["sh", "-c", "kill -TERM $$"]).then(
      () => undefined,
      (error: unknown) => error,
    );
    expect(failure).toBeInstanceOf(RunAsUidError);
    expect(failure instanceof RunAsUidError ? failure.signal : undefined).toBe("SIGTERM");
    expect(failure instanceof Error ? failure.message : "").toMatch(/killed by SIGTERM/);
  });
});

describe("runAsUid when the process's events are lost", () => {
  it("rejects instead of waiting forever for a process that died silently", async () => {
    const silent = Object.assign(new EventEmitter(), { pid: 2_147_483_000, stderr: undefined });
    const failure = await runAsUid(UID_POOL_BASE, ["find"], {
      spawnProcess: () => silent,
      processEnd: { isDead: () => true, pollMs: 1, graceMs: 10 },
    }).then(
      () => undefined,
      (error: unknown) => error,
    );
    expect(failure).toBeInstanceOf(RunAsUidError);
    expect(failure instanceof Error ? failure.message : "").toMatch(
      /neither its exit nor its close event arrived/,
    );
  });

  it("rejects when the process cannot be spawned", async () => {
    await expect(
      runAsUid(UID_POOL_BASE, ["find"], {
        spawnProcess: () => {
          throw new Error("spawn EMFILE");
        },
      }),
    ).rejects.toThrow(/EMFILE/);
  });
});

describe("createPoolUidTreeRemover", () => {
  function fullPool(): UidPool {
    return createUidPool({ baseUid: UID_POOL_BASE, size: UID_POOL_SIZE });
  }

  function seededTree(): string {
    const root = mkdtempSync(join(tmpdir(), "pool-rm-"));
    const tree = join(root, "e-1");
    mkdirSync(join(tree, "attempt-1"), { recursive: true });
    writeFileSync(join(tree, "attempt-1", "report.md"), "# report");
    return tree;
  }

  it("removes a tree owned by this process directly, without dropping privileges", async () => {
    const asUidCalls: number[] = [];
    const removeTree = createPoolUidTreeRemover({
      pool: fullPool(),
      runAsUidFn: (uid) => {
        asUidCalls.push(uid);
        return Promise.resolve();
      },
    });
    const tree = seededTree();
    await removeTree(tree);
    expect(existsSync(tree)).toBe(false);
    expect(asUidCalls).toEqual([]);
  });

  it("removes a pool-uid-owned tree by re-running rm under that uid", async () => {
    const asUidCalls: { uid: number; argv: string[] }[] = [];
    const tree = seededTree();
    const removeTree = createPoolUidTreeRemover({
      pool: fullPool(),
      ownerUidOf: () => Promise.resolve(UID_POOL_BASE + 3),
      runAsUidFn: (uid, argv) => {
        asUidCalls.push({ uid, argv });
        return Promise.resolve();
      },
    });
    await removeTree(tree);
    expect(asUidCalls).toEqual([{ uid: UID_POOL_BASE + 3, argv: ["rm", "-rf", tree] }]);
  });

  it("refuses a tree owned outside the pool range, touching nothing", async () => {
    const asUidCalls: number[] = [];
    const tree = seededTree();
    const removeTree = createPoolUidTreeRemover({
      pool: fullPool(),
      ownerUidOf: () => Promise.resolve(0),
      runAsUidFn: (uid) => {
        asUidCalls.push(uid);
        return Promise.resolve();
      },
    });
    await expect(removeTree(tree)).rejects.toThrow(/outside the run uid pool/);
    expect(asUidCalls).toEqual([]);
    expect(existsSync(tree)).toBe(true);
  });

  it("removes a tree owned by the pool's last slot, whatever the pool's size", async () => {
    const asUidCalls: number[] = [];
    const tree = seededTree();
    const removeTree = createPoolUidTreeRemover({
      pool: fullPool(),
      ownerUidOf: () => Promise.resolve(UID_POOL_BASE + UID_POOL_SIZE - 1),
      runAsUidFn: (uid) => {
        asUidCalls.push(uid);
        return Promise.resolve();
      },
    });
    await removeTree(tree);
    expect(asUidCalls).toEqual([UID_POOL_BASE + UID_POOL_SIZE - 1]);
  });

  it("refuses the uid one past the pool's last slot", async () => {
    const tree = seededTree();
    const removeTree = createPoolUidTreeRemover({
      pool: fullPool(),
      ownerUidOf: () => Promise.resolve(UID_POOL_BASE + UID_POOL_SIZE),
      runAsUidFn: () => Promise.resolve(),
    });
    await expect(removeTree(tree)).rejects.toThrow(/outside the run uid pool/);
  });

  it("treats a missing tree as already removed", async () => {
    const removeTree = createPoolUidTreeRemover({
      pool: fullPool(),
      runAsUidFn: () => Promise.reject(new Error("must not be called")),
    });
    const missing = join(mkdtempSync(join(tmpdir(), "pool-rm-missing-")), "e-none");
    await expect(removeTree(missing)).resolves.toBeUndefined();
  });

  it("waits while the owner uid is leased to a live run, then removes before the uid is leased again", async () => {
    const pool = createUidPool({ baseUid: UID_POOL_BASE, size: 1 });
    const liveRunUid = pool.acquire();
    const asUidCalls: number[] = [];
    let leasableDuringRemoval: boolean | undefined;
    const removeTree = createPoolUidTreeRemover({
      pool,
      ownerUidOf: () => Promise.resolve(liveRunUid),
      runAsUidFn: (uid) => {
        asUidCalls.push(uid);
        leasableDuringRemoval = pool.available() > 0;
        return Promise.resolve();
      },
    });
    const removal = removeTree(seededTree());
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(asUidCalls).toEqual([]);
    pool.release(liveRunUid);
    expect(() => pool.acquire()).toThrow(UidPoolExhaustedError);
    await removal;
    expect(asUidCalls).toEqual([liveRunUid]);
    expect(leasableDuringRemoval).toBe(false);
    expect(pool.acquire()).toBe(liveRunUid);
  });

  it("removes a tree a real pool uid owns, as that uid, only once the uid's lease has ended", async () => {
    const pool = createUidPool({ baseUid: UID_POOL_BASE, size: 1 });
    const runUid = pool.acquire();
    const root = mkdtempSync(join(tmpdir(), "pool-rm-real-"));
    chmodSync(root, 0o777);
    const tree = join(root, "attempt-1");
    await runAsUid(runUid, ["sh", "-c", `mkdir -m 700 ${tree} && echo report > ${tree}/report.md`]);
    expect(lstatSync(tree).uid).toBe(runUid);
    const removal = createPoolUidTreeRemover({ pool })(tree);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(existsSync(tree)).toBe(true);
    pool.release(runUid);
    await removal;
    expect(existsSync(tree)).toBe(false);
    expect(pool.available()).toBe(1);
  });

  it("removes a pool uid's attempt inside a sealed run subtree as that uid, and seals the subtree again", async () => {
    const pool = createUidPool({ baseUid: UID_POOL_BASE, size: 1 });
    const runUid = pool.acquire();
    const area = mkdtempSync(join(tmpdir(), "pool-rm-sealed-"));
    chmodSync(area, 0o755);
    const root = join(area, "artifacts");
    claimRetainedRoot(root);
    const runDir = join(root, "e-1");
    claimRunDir(runDir, runUid);
    const tree = join(runDir, "attempt-1");
    await runAsUid(runUid, ["sh", "-c", `mkdir -m 755 ${tree} && echo report > ${tree}/report.md`]);
    sealRunDirsSharedWith(root, runUid);
    pool.release(runUid);
    const remove = createPoolUidTreeRemover({ pool, parentAccess: attemptRunDirAccess });
    await remove(tree);
    expect(existsSync(tree)).toBe(false);
    expect(statSync(runDir).mode & 0o777).toBe(0o700);
  });

  it("closes the run dir again when opening it to the owner uid fails partway", async () => {
    const tree = seededTree();
    const closed: string[] = [];
    const remove = createPoolUidTreeRemover({
      pool: createUidPool({ baseUid: UID_POOL_BASE, size: 1 }),
      ownerUidOf: () => Promise.resolve(UID_POOL_BASE),
      runAsUidFn: () => Promise.resolve(),
      parentAccess: {
        openTo: () => {
          throw new Error("run subtree is not shared with the owner uid");
        },
        close: (path) => {
          closed.push(path);
        },
      },
    });
    await expect(remove(tree)).rejects.toThrow("run subtree is not shared with the owner uid");
    expect(closed).toEqual([tree]);
  });

  it("refuses a tree whose owner changed while it waited for the owner uid", async () => {
    const owners = [UID_POOL_BASE, UID_POOL_BASE + 1];
    const tree = seededTree();
    const removeTree = createPoolUidTreeRemover({
      pool: fullPool(),
      ownerUidOf: () => Promise.resolve(owners.shift()),
      runAsUidFn: () => Promise.reject(new Error("must not be called")),
    });
    await expect(removeTree(tree)).rejects.toThrow(/its owner changed from uid/);
    expect(existsSync(tree)).toBe(true);
  });

  it("skips a tree that disappeared while it waited for the owner uid", async () => {
    const pool = createUidPool({ baseUid: UID_POOL_BASE, size: 1 });
    const liveRunUid = pool.acquire();
    const owners: (number | undefined)[] = [liveRunUid, undefined];
    const removeTree = createPoolUidTreeRemover({
      pool,
      ownerUidOf: () => Promise.resolve(owners.shift()),
      runAsUidFn: () => Promise.reject(new Error("must not be called")),
    });
    const removal = removeTree(seededTree());
    pool.release(liveRunUid);
    await expect(removal).resolves.toBeUndefined();
    expect(pool.available()).toBe(1);
  });
});

describe("signalRunArgv", () => {
  it("signals as the run's own uid, which the supervisor may not signal directly", () => {
    const argv = signalRunArgv(20003, "KILL");
    expect(argv[0]).toBe("setpriv");
    expect(argv).toContain("--reuid=20003");
    expect(argv).toContain("--inh-caps=-all");
    expect(argv.slice(argv.indexOf("--") + 1)).toEqual(["pkill", "-KILL", "-u", "20003"]);
  });

  it("carries the requested signal", () => {
    expect(signalRunArgv(20001, "TERM")).toContain("-TERM");
  });
});
