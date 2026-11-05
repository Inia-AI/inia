import { afterAll, describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { Readable } from "node:stream";
import { createLocalDocumentStore, DocumentIntegrityError } from "./document-store.js";
import { sha256Hex } from "./hash.js";
import type { StagedInput } from "./run-documents.js";

const ROOTS: string[] = [];

function newRoot(): string {
  const root = join(mkdtempSync(join(tmpdir(), "document-store-tests-")), "store");
  ROOTS.push(root);
  return root;
}

afterAll(() => {
  for (const root of ROOTS) {
    rmSync(join(root, ".."), { recursive: true, force: true });
  }
});

function documentOf(text: string, name = "report.pdf", extra: Partial<StagedInput> = {}): StagedInput {
  const bytes = new TextEncoder().encode(text);
  return { name, bytes, sha256: sha256Hex(bytes), area: "documents", ...extra };
}

async function readAll(open: () => Readable): Promise<Buffer> {
  const parts: Buffer[] = [];
  for await (const chunk of open()) {
    parts.push(Buffer.isBuffer(chunk) ? Buffer.from(chunk) : Buffer.from(String(chunk)));
  }
  return Buffer.concat(parts);
}

describe("local document store", () => {
  it("creates the store root private to its owner at construction", () => {
    const root = newRoot();
    createLocalDocumentStore(root);
    expect(statSync(root).mode & 0o777).toBe(0o700);
  });

  it("refuses a pre-existing store root that is open to other uids", () => {
    const root = newRoot();
    mkdirSync(root, { recursive: true, mode: 0o755 });
    expect(() => createLocalDocumentStore(root)).toThrow(/store root/);
  });

  it("round-trips documents as streamable sources with sizes, shas, metadata, and order intact", async () => {
    const store = createLocalDocumentStore(newRoot());
    const documents = [
      documentOf("first body", "a.pdf", { mediaType: "application/pdf", documentClass: "AnnualReport" }),
      documentOf("second body", "b.txt"),
    ];
    await store.save("e-1", documents);
    const opened = await store.openInputs("e-1");
    expect(opened).toHaveLength(2);
    expect(opened?.[0]?.name).toBe("a.pdf");
    expect(opened?.[0]?.mediaType).toBe("application/pdf");
    expect(opened?.[0]?.documentClass).toBe("AnnualReport");
    expect(opened?.[0]?.sizeBytes).toBe(10);
    expect(opened?.[0]?.sha256).toBe(documents[0]?.sha256 ?? "");
    const firstOpen = opened?.[0]?.open;
    if (firstOpen === undefined) {
      throw new Error("expected an openable source");
    }
    expect((await readAll(firstOpen)).toString("utf8")).toBe("first body");
    // A source opens as many times as cold resumes need it.
    expect((await readAll(firstOpen)).toString("utf8")).toBe("first body");
    const secondOpen = opened?.[1]?.open;
    if (secondOpen === undefined) {
      throw new Error("expected an openable source");
    }
    expect((await readAll(secondOpen)).toString("utf8")).toBe("second body");
  });

  it("round-trips the area tag, keeping documents and skill files apart", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    await store.save("e-1", [
      documentOf("doc body", "a.pdf"),
      documentOf("# skill", "grading.md", { area: "skills" }),
    ]);
    const opened = await store.openInputs("e-1");
    expect(opened?.map((input) => [input.name, input.area])).toEqual([
      ["a.pdf", "documents"],
      ["grading.md", "skills"],
    ]);
    const manifest = JSON.parse(readFileSync(join(root, "e-1", "manifest.json"), "utf8")) as {
      area?: string;
    }[];
    expect(manifest.map((entry) => entry.area)).toEqual(["documents", "skills"]);
  });

  it("round-trips a skill file's description, so a cold resume re-registers it identically", async () => {
    const store = createLocalDocumentStore(newRoot());
    await store.save("e-1", [
      documentOf("# skill", "grading.md", { area: "skills", description: "How to grade" }),
      documentOf("doc body", "a.pdf"),
    ]);
    const opened = await store.openInputs("e-1");
    expect(opened?.[0]?.description).toBe("How to grade");
    expect(opened?.[1]?.description).toBeUndefined();
  });

  it("round-trips a skill file's assets as sized streamable sources, so cold resume re-stages them", async () => {
    const store = createLocalDocumentStore(newRoot());
    const assetBytes = new TextEncoder().encode("print('check')");
    const skill = documentOf("# skill body", "verify.md", {
      area: "skills",
      assets: [{ name: "verify.py", bytes: assetBytes, sha256: sha256Hex(assetBytes) }],
    });
    await store.save("e-1", [documentOf("doc"), skill]);
    const opened = await store.openInputs("e-1");
    expect(opened?.[0]?.assets).toBeUndefined();
    const asset = opened?.[1]?.assets?.[0];
    expect(asset?.name).toBe("verify.py");
    expect(asset?.sha256).toBe(sha256Hex(assetBytes));
    expect(asset?.sizeBytes).toBe(assetBytes.byteLength);
    const assetOpen = asset?.open;
    if (assetOpen === undefined) {
      throw new Error("expected an openable asset source");
    }
    expect((await readAll(assetOpen)).equals(Buffer.from(assetBytes))).toBe(true);
  });

  it("fails when a stored asset's bytes are not the size its manifest declares", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    const assetBytes = new TextEncoder().encode("print('check')");
    await store.save("e-1", [
      documentOf("# skill body", "verify.md", {
        area: "skills",
        assets: [{ name: "verify.py", bytes: assetBytes, sha256: sha256Hex(assetBytes) }],
      }),
    ]);
    const assetFile = readdirSync(join(root, "e-1")).find((name) => name.includes("asset"));
    writeFileSync(join(root, "e-1", assetFile ?? "!"), "truncated!");
    expect(store.openInputs("e-1")).rejects.toThrow(DocumentIntegrityError);
  });

  it("fails when a manifest asset entry is not a plain name", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    const assetBytes = new TextEncoder().encode("x");
    await store.save("e-1", [
      documentOf("# skill body", "verify.md", {
        area: "skills",
        assets: [{ name: "verify.py", bytes: assetBytes, sha256: sha256Hex(assetBytes) }],
      }),
    ]);
    const manifestPath = join(root, "e-1", "manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8")) as {
      assets?: { name: string }[];
    }[];
    const entry = manifest.find((item) => item.assets !== undefined);
    if (entry?.assets?.[0] !== undefined) {
      entry.assets[0].name = "../escape.py";
    }
    writeFileSync(manifestPath, JSON.stringify(manifest));
    expect(store.openInputs("e-1")).rejects.toThrow(DocumentIntegrityError);
  });

  it("fails when a manifest entry names an unknown area", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    await store.save("e-1", [documentOf("pristine")]);
    const manifestPath = join(root, "e-1", "manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8")) as Record<string, unknown>[];
    const entry = manifest[0];
    if (entry === undefined) {
      throw new Error("expected a manifest entry");
    }
    entry["area"] = "outside";
    writeFileSync(manifestPath, JSON.stringify(manifest));
    await expect(store.openInputs("e-1")).rejects.toBeInstanceOf(DocumentIntegrityError);
  });

  it("fails when a manifest entry names a file that is not a plain name", async () => {
    for (const name of ["../escape.pdf", "nested/report.pdf", "a\\b.pdf", ".", "..", "manifest.json", ""]) {
      const root = newRoot();
      const store = createLocalDocumentStore(root);
      await store.save("e-1", [documentOf("pristine")]);
      const manifestPath = join(root, "e-1", "manifest.json");
      const manifest = JSON.parse(readFileSync(manifestPath, "utf8")) as Record<string, unknown>[];
      const entry = manifest[0];
      if (entry === undefined) {
        throw new Error("expected a manifest entry");
      }
      entry["name"] = name;
      writeFileSync(manifestPath, JSON.stringify(manifest));
      await expect(store.openInputs("e-1")).rejects.toBeInstanceOf(DocumentIntegrityError);
    }
  });

  it("misses without throwing for a run that never saved documents", async () => {
    const store = createLocalDocumentStore(newRoot());
    expect(await store.openInputs("missing")).toBeUndefined();
  });

  it("refuses executionIds that are not safe path segments, dots and traversal included", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    for (const hostile of [".", "..", "../../escape", "a/b", ""]) {
      await expect(store.save(hostile, [documentOf("x")])).rejects.toThrow(/safe path segment/);
      await expect(store.openInputs(hostile)).rejects.toThrow(/safe path segment/);
      await expect(store.delete(hostile)).rejects.toThrow(/safe path segment/);
    }
    expect(readdirSync(root)).toHaveLength(0);
  });

  it("replaces a run's documents wholesale on a second save", async () => {
    const store = createLocalDocumentStore(newRoot());
    await store.save("e-1", [documentOf("old", "old.pdf"), documentOf("gone", "gone.pdf")]);
    await store.save("e-1", [documentOf("new", "new.pdf")]);
    const opened = await store.openInputs("e-1");
    expect(opened).toHaveLength(1);
    expect(opened?.[0]?.name).toBe("new.pdf");
  });

  it("stores bytes owner-only on disk", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    await store.save("e-1", [documentOf("private bytes")]);
    const runDir = join(root, "e-1");
    expect(readdirSync(runDir).length).toBeGreaterThan(0);
    for (const entry of readdirSync(runDir)) {
      expect(statSync(join(runDir, entry)).mode & 0o777).toBe(0o600);
    }
  });

  it("fails when a stored bytes file is not the size its manifest declares", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    await store.save("e-1", [documentOf("pristine")]);
    const runDir = join(root, "e-1");
    const bytesFile = readdirSync(runDir).find((name) => name.endsWith(".doc"));
    if (bytesFile === undefined) {
      throw new Error("expected a raw document bytes file");
    }
    writeFileSync(join(runDir, bytesFile), "truncated to a different size");
    await expect(store.openInputs("e-1")).rejects.toBeInstanceOf(DocumentIntegrityError);
  });

  it("fails when a stored bytes file is missing entirely", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    await store.save("e-1", [documentOf("pristine")]);
    const runDir = join(root, "e-1");
    const bytesFile = readdirSync(runDir).find((name) => name.endsWith(".doc"));
    rmSync(join(runDir, bytesFile ?? "!"));
    await expect(store.openInputs("e-1")).rejects.toBeInstanceOf(DocumentIntegrityError);
  });

  it("carries each document's sha256 and size in the manifest, with no per-doc sidecar files", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    const document = documentOf("pristine");
    await store.save("e-1", [document]);
    const runDir = join(root, "e-1");
    expect(readdirSync(runDir).filter((name) => name.endsWith(".sha256"))).toEqual([]);
    const manifest = JSON.parse(readFileSync(join(runDir, "manifest.json"), "utf8")) as {
      sha256?: string;
      sizeBytes?: number;
    }[];
    expect(manifest[0]?.sha256).toBe(document.sha256);
    expect(manifest[0]?.sizeBytes).toBe(document.bytes.byteLength);
  });

  it("fails when the manifest's sha or size entry is missing or junk", async () => {
    for (const corruption of [
      { field: "sha256", value: undefined },
      { field: "sha256", value: "not a hash" },
      { field: "sizeBytes", value: undefined },
      { field: "sizeBytes", value: -1 },
      { field: "sizeBytes", value: 1.5 },
    ]) {
      const root = newRoot();
      const store = createLocalDocumentStore(root);
      await store.save("e-1", [documentOf("pristine")]);
      const manifestPath = join(root, "e-1", "manifest.json");
      const manifest = JSON.parse(readFileSync(manifestPath, "utf8")) as Record<string, unknown>[];
      const entry = manifest[0];
      if (entry === undefined) {
        throw new Error("expected a manifest entry");
      }
      if (corruption.value === undefined) {
        delete entry[corruption.field];
      } else {
        entry[corruption.field] = corruption.value;
      }
      writeFileSync(manifestPath, JSON.stringify(manifest));
      await expect(store.openInputs("e-1")).rejects.toBeInstanceOf(DocumentIntegrityError);
    }
  });

  it("fails when the run manifest is junk", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    await store.save("e-1", [documentOf("pristine")]);
    writeFileSync(join(root, "e-1", "manifest.json"), '{"not": "a manifest"}');
    await expect(store.openInputs("e-1")).rejects.toBeInstanceOf(DocumentIntegrityError);
  });

  it("removes its staging dir when a save fails partway", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    const poisoned = documentOf("body");
    Object.defineProperty(poisoned, "bytes", {
      get(): Uint8Array {
        throw new Error("bytes unavailable");
      },
    });
    await expect(store.save("e-1", [poisoned])).rejects.toThrow(/bytes unavailable/);
    expect(readdirSync(root).filter((name) => name.startsWith(".staging-"))).toEqual([]);
  });

  it("sweeps staging leftovers from a crashed predecessor at construction", () => {
    const root = newRoot();
    mkdirSync(root, { recursive: true, mode: 0o700 });
    mkdirSync(join(root, ".staging-leftover"), { mode: 0o700 });
    writeFileSync(join(root, ".staging-leftover", "0.doc"), "orphaned bytes");
    createLocalDocumentStore(root);
    expect(readdirSync(root).filter((name) => name.startsWith(".staging-"))).toEqual([]);
  });

  it("deletes a run's documents so a later open misses, and tolerates a missing run", async () => {
    const store = createLocalDocumentStore(newRoot());
    await store.save("e-1", [documentOf("x")]);
    await store.delete("e-1");
    expect(await store.openInputs("e-1")).toBeUndefined();
    await expect(store.delete("e-1")).resolves.toBeUndefined();
  });
});

describe("recarried out/ area", () => {
  it("round-trips a re-carried out/ entry whose name is a nested path", async () => {
    const store = createLocalDocumentStore(newRoot());
    await store.save("e-1", [
      documentOf("annual report", "report.pdf"),
      documentOf("print('check')", "checks/model.py", { area: "recarried_out" }),
    ]);
    const opened = await store.openInputs("e-1");
    expect(opened?.map((input) => [input.area, input.name])).toEqual([
      ["documents", "report.pdf"],
      ["recarried_out", "checks/model.py"],
    ]);
    const outEntry = opened?.[1];
    expect(outEntry === undefined ? "" : (await readAll(outEntry.open)).toString("utf8")).toBe(
      "print('check')",
    );
  });

  it("refuses a stored manifest whose recarried_out name walks out of the area", async () => {
    const root = newRoot();
    const store = createLocalDocumentStore(root);
    await store.save("e-1", [documentOf("body", "draft.md", { area: "recarried_out" })]);
    const manifestPath = join(root, "e-1", "manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8")) as Record<string, unknown>[];
    const entry = manifest[0];
    if (entry !== undefined) {
      entry["name"] = "../escape.md";
    }
    writeFileSync(manifestPath, JSON.stringify(manifest));
    await expect(store.openInputs("e-1")).rejects.toThrow(DocumentIntegrityError);
  });
});
