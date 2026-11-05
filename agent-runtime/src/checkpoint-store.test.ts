import { afterAll, describe, expect, it } from "bun:test";
import {
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  utimesSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  CheckpointIntegrityError,
  type CheckpointKey,
  checkpointKeyOf,
  createLocalCheckpointStore,
  sealCheckpoint,
  terminalCheckpointKeyOf,
} from "./checkpoint-store.js";
import { MAX_CHECKPOINT_BUNDLE_BYTES } from "./config.js";
import { closeSizedFile, readSizedFile } from "./fs-read.js";
import { sha256Hex } from "./hash.js";

const ROOTS: string[] = [];

function newRoot(): string {
  const root = join(mkdtempSync(join(tmpdir(), "checkpoint-store-tests-")), "store");
  ROOTS.push(root);
  return root;
}

afterAll(() => {
  for (const root of ROOTS) {
    rmSync(join(root, ".."), { recursive: true, force: true });
  }
});

const KEY: CheckpointKey = { executionId: "e-1", attempt: 1, segment: 0 };

function bundleOf(text: string): ReturnType<typeof sealCheckpoint> {
  return sealCheckpoint(new TextEncoder().encode(text));
}

describe("sealCheckpoint", () => {
  it("stamps the bundle with the sha256 of its bytes", () => {
    const bundle = bundleOf("session state");
    expect(bundle.sha256).toBe(sha256Hex(new TextEncoder().encode("session state")));
    expect(bundle.sha256).toMatch(/^[0-9a-f]{64}$/);
  });
});

describe("local checkpoint store", () => {
  it("creates the store root private to its owner at construction, before any run exists", () => {
    const root = newRoot();
    createLocalCheckpointStore(root);
    expect(statSync(root).mode & 0o777).toBe(0o700);
  });

  it("refuses a pre-existing store root that is open to other uids", () => {
    const root = newRoot();
    mkdirSync(root, { recursive: true, mode: 0o755 });
    expect(() => createLocalCheckpointStore(root)).toThrow(/store root/);
  });

  it("sweeps staging leftovers from a crashed predecessor at construction", () => {
    const root = newRoot();
    mkdirSync(root, { recursive: true, mode: 0o700 });
    writeFileSync(join(root, ".staging-leftover"), "orphaned bundle bytes");
    createLocalCheckpointStore(root);
    expect(readdirSync(root).filter((name) => name.startsWith(".staging-"))).toEqual([]);
  });

  it("writes bundles readable by nobody else", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(KEY, bundleOf("private session state"));
    const fileNames = readdirSync(root);
    expect(fileNames.length).toBeGreaterThan(0);
    for (const fileName of fileNames) {
      expect(statSync(join(root, fileName)).mode & 0o777).toBe(0o600);
    }
  });

  it("stores the bundle bytes raw on disk, with no encoded envelope around them", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    const bundle = bundleOf("raw session bytes");
    await store.save(KEY, bundle);
    const bytesFile = readdirSync(root).find((name) => name.endsWith(".checkpoint"));
    if (bytesFile === undefined) {
      throw new Error("expected a raw checkpoint bytes file");
    }
    expect(new Uint8Array(readFileSync(join(root, bytesFile)))).toEqual(new Uint8Array(bundle.bytes));
  });

  it("round-trips a bundle through save and load, bytes and hash intact", async () => {
    const store = createLocalCheckpointStore(newRoot());
    const bundle = bundleOf("the session snapshot");
    await store.save(KEY, bundle);
    const loaded = await store.load(KEY);
    expect(loaded).toBeDefined();
    expect(new TextDecoder().decode(loaded?.bytes)).toBe("the session snapshot");
    expect(loaded?.sha256).toBe(bundle.sha256);
  });

  it("misses without throwing for a key that was never saved", async () => {
    const store = createLocalCheckpointStore(newRoot());
    expect(await store.load(KEY)).toBeUndefined();
  });

  it("deletes a checkpoint so a later load misses", async () => {
    const store = createLocalCheckpointStore(newRoot());
    await store.save(KEY, bundleOf("x"));
    await store.delete(KEY);
    expect(await store.load(KEY)).toBeUndefined();
  });

  it("treats deleting a missing key as a no-op", async () => {
    const store = createLocalCheckpointStore(newRoot());
    await expect(store.delete(KEY)).resolves.toBeUndefined();
  });

  it("keeps checkpoints apart by executionId, attempt, and segment", async () => {
    const store = createLocalCheckpointStore(newRoot());
    await store.save(KEY, bundleOf("first"));
    await store.save({ ...KEY, segment: 1 }, bundleOf("second"));
    await store.save({ ...KEY, attempt: 2 }, bundleOf("third"));
    await store.save({ ...KEY, executionId: "e-2" }, bundleOf("fourth"));
    expect(new TextDecoder().decode((await store.load(KEY))?.bytes)).toBe("first");
    expect(new TextDecoder().decode((await store.load({ ...KEY, segment: 1 }))?.bytes)).toBe("second");
    expect(new TextDecoder().decode((await store.load({ ...KEY, attempt: 2 }))?.bytes)).toBe("third");
    expect(new TextDecoder().decode((await store.load({ ...KEY, executionId: "e-2" }))?.bytes)).toBe(
      "fourth",
    );
  });

  it("fails a load with CheckpointIntegrityError when the stored bytes were corrupted", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(KEY, bundleOf("pristine"));
    const bytesFile = readdirSync(root).find((name) => name.endsWith(".checkpoint"));
    if (bytesFile === undefined) {
      throw new Error("expected a raw checkpoint bytes file");
    }
    writeFileSync(join(root, bytesFile), "tampered");
    await expect(store.load(KEY)).rejects.toBeInstanceOf(CheckpointIntegrityError);
  });

  it("fails a load with CheckpointIntegrityError when the hash sidecar is gone or junk", async () => {
    for (const corrupt of [
      (path: string): void => rmSync(path),
      (path: string): void => writeFileSync(path, "not a hash"),
    ]) {
      const root = newRoot();
      const store = createLocalCheckpointStore(root);
      await store.save(KEY, bundleOf("pristine"));
      const shaFile = readdirSync(root).find((name) => name.endsWith(".sha256"));
      if (shaFile === undefined) {
        throw new Error("expected a checkpoint hash sidecar");
      }
      corrupt(join(root, shaFile));
      await expect(store.load(KEY)).rejects.toBeInstanceOf(CheckpointIntegrityError);
    }
  });

  it("refuses to save a bundle larger than the byte cap", async () => {
    const store = createLocalCheckpointStore(newRoot());
    const oversized = sealCheckpoint(new Uint8Array(MAX_CHECKPOINT_BUNDLE_BYTES + 1));
    await expect(store.save(KEY, oversized)).rejects.toThrow(/exceeds/);
  });

  it("stores the declared digest as given, and catches the mismatch on the way back out", async () => {
    const store = createLocalCheckpointStore(newRoot());
    const bundle = { bytes: new TextEncoder().encode("payload"), sha256: "0".repeat(64) };
    await store.save(KEY, bundle);
    await expect(store.load(KEY)).rejects.toBeInstanceOf(CheckpointIntegrityError);
  });

  it("keeps hostile executionIds inside the store root", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save({ executionId: "../../escape", attempt: 1, segment: 0 }, bundleOf("x"));
    expect(readdirSync(root)).toHaveLength(2);
    expect(
      new TextDecoder().decode(
        (await store.load({ executionId: "../../escape", attempt: 1, segment: 0 }))?.bytes,
      ),
    ).toBe("x");
  });
});

describe("terminal checkpoint keys", () => {
  it("keeps a run's terminal bundle apart from every pause segment", async () => {
    const store = createLocalCheckpointStore(newRoot());
    const pause = bundleOf("pause segment zero");
    const terminal = bundleOf("finished state");
    await store.save(checkpointKeyOf("e-1", 1, 0), pause);
    await store.save(terminalCheckpointKeyOf("e-1", 1), terminal);
    expect((await store.load(checkpointKeyOf("e-1", 1, 0)))?.sha256).toBe(pause.sha256);
    expect((await store.load(terminalCheckpointKeyOf("e-1", 1)))?.sha256).toBe(terminal.sha256);
    await store.delete(checkpointKeyOf("e-1", 1, 0));
    expect((await store.load(terminalCheckpointKeyOf("e-1", 1)))?.sha256).toBe(terminal.sha256);
  });

  it("keeps terminal bundles apart by attempt", async () => {
    const store = createLocalCheckpointStore(newRoot());
    await store.save(terminalCheckpointKeyOf("e-1", 1), bundleOf("first attempt"));
    await store.save(terminalCheckpointKeyOf("e-1", 2), bundleOf("second attempt"));
    expect((await store.load(terminalCheckpointKeyOf("e-1", 1)))?.bytes).toEqual(
      bundleOf("first attempt").bytes,
    );
    expect((await store.load(terminalCheckpointKeyOf("e-1", 2)))?.bytes).toEqual(
      bundleOf("second attempt").bytes,
    );
  });
});

describe("open", () => {
  it("hands back an open descriptor on the stored bytes, and the digest the store wrote", async () => {
    const store = createLocalCheckpointStore(newRoot());
    const bundle = bundleOf("finished state");
    await store.save(terminalCheckpointKeyOf("e-1", 1), bundle);
    const opened = await store.open(terminalCheckpointKeyOf("e-1", 1));
    expect(opened?.sha256).toBe(bundle.sha256);
    expect(opened?.file.sizeBytes).toBe(bundle.bytes.byteLength);
    if (opened === undefined) {
      throw new Error("unreachable");
    }
    expect(readSizedFile(opened.file)).toEqual(Buffer.from(bundle.bytes));
    closeSizedFile(opened.file);
  });

  it("answers undefined for a run with no bundle, so an absent one is not an error", async () => {
    const store = createLocalCheckpointStore(newRoot());
    expect(await store.open(terminalCheckpointKeyOf("absent", 1))).toBeUndefined();
  });

  it("refuses a bundle whose digest sidecar is gone, rather than serving bytes nothing vouches for", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(terminalCheckpointKeyOf("e-1", 1), bundleOf("finished state"));
    for (const name of readdirSync(root).filter((entry) => entry.endsWith(".sha256"))) {
      rmSync(join(root, name));
    }
    await expect(store.open(terminalCheckpointKeyOf("e-1", 1))).rejects.toBeInstanceOf(
      CheckpointIntegrityError,
    );
  });
});

describe("reapOrphans", () => {
  const AN_HOUR_MS = 60 * 60 * 1000;

  function backdate(root: string, ms: number): void {
    const when = new Date(Date.now() - ms);
    for (const name of readdirSync(root)) {
      utimesSync(join(root, name), when, when);
    }
  }

  it("removes an aged file whose run no code remembers, and its digest sidecar with it", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(terminalCheckpointKeyOf("e-1", 1), bundleOf("stranded"));
    backdate(root, 2 * AN_HOUR_MS);
    expect(await store.reapOrphans({ isLive: () => false, olderThanMs: AN_HOUR_MS })).toBe(1);
    expect(readdirSync(root)).toEqual([]);
  });

  it("spares a file belonging to a run the supervisor still knows about", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(terminalCheckpointKeyOf("e-1", 1), bundleOf("live run"));
    backdate(root, 2 * AN_HOUR_MS);
    expect(await store.reapOrphans({ isLive: (id) => id === "e-1", olderThanMs: AN_HOUR_MS })).toBe(0);
    expect((await store.load(terminalCheckpointKeyOf("e-1", 1)))?.bytes).toEqual(bundleOf("live run").bytes);
  });

  it("spares a file younger than the age, so a run that has only just published keeps its bundle", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(terminalCheckpointKeyOf("e-1", 1), bundleOf("just written"));
    expect(await store.reapOrphans({ isLive: () => false, olderThanMs: AN_HOUR_MS })).toBe(0);
    expect(readdirSync(root)).toHaveLength(2);
  });

  it("removes a digest sidecar whose bundle is already gone, which a torn save leaves behind", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(terminalCheckpointKeyOf("e-1", 1), bundleOf("torn"));
    // Exactly what a supervisor death between the two writes leaves: the digest, and no bytes.
    for (const name of readdirSync(root).filter((entry) => !entry.endsWith(".sha256"))) {
      rmSync(join(root, name));
    }
    backdate(root, 2 * AN_HOUR_MS);
    expect(await store.reapOrphans({ isLive: () => false, olderThanMs: AN_HOUR_MS })).toBe(1);
    expect(readdirSync(root)).toEqual([]);
  });

  it("skips an entry it cannot remove and keeps sweeping the rest", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(terminalCheckpointKeyOf("e-1", 1), bundleOf("a real bundle"));
    // A directory wearing a bundle's name: it stats fine and then refuses to be removed, which
    // is the shape of any entry the sweep cannot handle.
    mkdirSync(join(root, "e-2.1.-1.checkpoint"));
    backdate(root, 2 * AN_HOUR_MS);
    const skipped: string[] = [];
    const reaped = await store.reapOrphans({
      isLive: () => false,
      olderThanMs: AN_HOUR_MS,
      onSkipped: (name, error) => skipped.push(`${name}|${error.message.slice(0, 6)}`),
    });
    expect(reaped).toBe(1);
    expect(skipped).toHaveLength(1);
    expect(readdirSync(root)).toEqual(["e-2.1.-1.checkpoint"]);
  });

  it("recovers the executionId from a file name even when the id carries its own dots", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    await store.save(terminalCheckpointKeyOf("e.1.2", 1), bundleOf("dotted id"));
    backdate(root, 2 * AN_HOUR_MS);
    const seen: string[] = [];
    await store.reapOrphans({
      isLive: (id) => {
        seen.push(id);
        return true;
      },
      olderThanMs: AN_HOUR_MS,
    });
    expect(seen).toEqual(["e.1.2"]);
  });

  it("answers empty when the store root has gone, rather than failing the sweep", async () => {
    const root = newRoot();
    const store = createLocalCheckpointStore(root);
    rmSync(root, { recursive: true, force: true });
    expect(await store.reapOrphans({ isLive: () => false, olderThanMs: 0 })).toBe(0);
  });
});
