import { afterAll, describe, expect, it } from "bun:test";
import {
  appendFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  renameSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { MAX_ARTIFACTS_MANIFEST_BYTES, MAX_PUBLISHED_OUT_FILES } from "./config.js";
import { closeSizedFile, readSizedFile } from "./fs-read.js";
import { sha256Hex } from "./hash.js";
import {
  ARTIFACTS_MANIFEST_NAME,
  artifactAttemptDirOf,
  contentTypeOf,
  createRunFilesView,
  discardPublishedArtifacts,
  publishRunArtifacts,
  RetainedCapExceededError,
  type RunFilesView,
} from "./run-files.js";
import { removeOwnTree } from "./run-files-test-helpers.js";
import { FINAL_RESPONSE_FILE_NAME } from "./workspace-layout.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "run-files-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

let caseId = 0;

function newCase(): { artifactsRoot: string; workspacePath: string } {
  caseId += 1;
  const base = join(TEST_ROOT, `case-${caseId}`);
  const artifactsRoot = join(base, "artifacts");
  const workspacePath = join(base, "run-under-test");
  mkdirSync(artifactsRoot, { recursive: true });
  mkdirSync(workspacePath, { recursive: true });
  return { artifactsRoot, workspacePath };
}

function publish(input: { artifactsRoot: string; workspacePath: string }): string {
  publishRunArtifacts({
    artifactsRoot: input.artifactsRoot,
    executionId: "e-1",
    attempt: 1,
    workspacePath: input.workspacePath,
  });
  return join(artifactAttemptDirOf(input.artifactsRoot, "e-1", 1), "out");
}

describe("publishRunArtifacts", () => {
  it("mirrors the out/ tree, nested dirs included, into the per-run attempt area", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out", "tables"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "# report");
    writeFileSync(join(workspacePath, "out", "tables", "q1.csv"), "a,b\n");

    const publishedOut = publish({ artifactsRoot, workspacePath });
    expect(readFileSync(join(publishedOut, "report.md"), "utf8")).toBe("# report");
    expect(readFileSync(join(publishedOut, "tables", "q1.csv"), "utf8")).toBe("a,b\n");
  });

  it("publishes files and dirs readable by other users, so the supervisor can serve them", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out", "tables"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "# report", { mode: 0o600 });
    writeFileSync(join(workspacePath, "out", "tables", "q1.csv"), "a,b\n", { mode: 0o600 });

    const publishedOut = publish({ artifactsRoot, workspacePath });
    for (const dir of [
      join(artifactsRoot, "e-1"),
      artifactAttemptDirOf(artifactsRoot, "e-1", 1),
      publishedOut,
      join(publishedOut, "tables"),
    ]) {
      expect(statSync(dir).mode & 0o055).toBe(0o055);
    }
    expect(statSync(join(publishedOut, "report.md")).mode & 0o044).toBe(0o044);
    expect(statSync(join(publishedOut, "tables", "q1.csv")).mode & 0o044).toBe(0o044);
  });

  it("republishing mirrors the current out/ exactly, dropping files deleted since the last publish", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "draft.md"), "v1");
    const publishedOut = publish({ artifactsRoot, workspacePath });

    rmSync(join(workspacePath, "out", "draft.md"));
    writeFileSync(join(workspacePath, "out", "final.md"), "v2");
    publish({ artifactsRoot, workspacePath });

    expect(existsSync(join(publishedOut, "draft.md"))).toBe(false);
    expect(readFileSync(join(publishedOut, "final.md"), "utf8")).toBe("v2");
  });

  it("copies only regular files, never symlinks", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "secret.txt"), "outside out/");
    symlinkSync(join(workspacePath, "secret.txt"), join(workspacePath, "out", "link.txt"));
    writeFileSync(join(workspacePath, "out", "real.txt"), "real");

    const publishedOut = publish({ artifactsRoot, workspacePath });
    expect(existsSync(join(publishedOut, "link.txt"))).toBe(false);
    expect(readFileSync(join(publishedOut, "real.txt"), "utf8")).toBe("real");
  });

  it("publishes nothing when out/ is a symlink rather than a real directory", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "elsewhere"), { recursive: true });
    writeFileSync(join(workspacePath, "elsewhere", "file.txt"), "x");
    symlinkSync(join(workspacePath, "elsewhere"), join(workspacePath, "out"));

    const publishedOut = publish({ artifactsRoot, workspacePath });
    expect(existsSync(publishedOut)).toBe(false);
  });

  it("clears a prior publish when out/ no longer exists", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "draft.md"), "v1");
    const publishedOut = publish({ artifactsRoot, workspacePath });

    rmSync(join(workspacePath, "out"), { recursive: true, force: true });
    publish({ artifactsRoot, workspacePath });
    expect(existsSync(publishedOut)).toBe(false);
  });

  it("is a no-op when the run never created an out/ dir", () => {
    const { artifactsRoot, workspacePath } = newCase();
    const publishedOut = publish({ artifactsRoot, workspacePath });
    expect(existsSync(publishedOut)).toBe(false);
  });

  it("refuses an out/ tree whose total bytes exceed the published-size cap", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out", "tables"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "x".repeat(40));
    writeFileSync(join(workspacePath, "out", "tables", "q1.csv"), "y".repeat(40));
    expect(() =>
      publishRunArtifacts({
        artifactsRoot,
        executionId: "e-1",
        attempt: 1,
        workspacePath,
        maxTotalBytes: 64,
      }),
    ).toThrow(/cap/);
  });

  it("keeps the last good mirror and its manifest when a republish fails partway", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "draft.md"), "the good draft");
    const publishedOut = publish({ artifactsRoot, workspacePath });
    const manifestPath = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), ARTIFACTS_MANIFEST_NAME);
    const goodManifest = readFileSync(manifestPath, "utf8");

    writeFileSync(join(workspacePath, "out", "huge.bin"), "x".repeat(4096));
    expect(() =>
      publishRunArtifacts({
        artifactsRoot,
        executionId: "e-1",
        attempt: 1,
        workspacePath,
        maxTotalBytes: 64,
      }),
    ).toThrow(/cap/);

    expect(readFileSync(join(publishedOut, "draft.md"), "utf8")).toBe("the good draft");
    expect(readFileSync(manifestPath, "utf8")).toBe(goodManifest);
    expect(existsSync(join(publishedOut, "huge.bin"))).toBe(false);
    expect(readdirSync(artifactAttemptDirOf(artifactsRoot, "e-1", 1)).sort()).toEqual([
      ARTIFACTS_MANIFEST_NAME,
      "out",
    ]);
  });

  it("publishes a tree exactly at the published-size cap", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "x".repeat(64));
    publishRunArtifacts({
      artifactsRoot,
      executionId: "e-1",
      attempt: 1,
      workspacePath,
      maxTotalBytes: 64,
    });
    const publishedOut = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), "out");
    expect(readFileSync(join(publishedOut, "report.md"), "utf8")).toBe("x".repeat(64));
  });

  it("refuses an out/ tree over the run's declared retained-output cap with the declared-cap error", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "x".repeat(80));
    expect(() =>
      publishRunArtifacts({
        artifactsRoot,
        executionId: "e-1",
        attempt: 1,
        workspacePath,
        declaredCapBytes: 64,
      }),
    ).toThrow(RetainedCapExceededError);
  });

  it("reports the service publish cap, not the declared-cap error, when the declaration sits above it", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "x".repeat(80));
    let caught: unknown;
    try {
      publishRunArtifacts({
        artifactsRoot,
        executionId: "e-1",
        attempt: 1,
        workspacePath,
        maxTotalBytes: 64,
        declaredCapBytes: 100,
      });
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(Error);
    expect(caught).not.toBeInstanceOf(RetainedCapExceededError);
    expect(String(caught)).toMatch(/64-byte cap/);
  });

  it("publishes a tree exactly at the declared retained-output cap", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "x".repeat(64));
    publishRunArtifacts({
      artifactsRoot,
      executionId: "e-1",
      attempt: 1,
      workspacePath,
      declaredCapBytes: 64,
    });
    const publishedOut = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), "out");
    expect(readFileSync(join(publishedOut, "report.md"), "utf8")).toBe("x".repeat(64));
  });

  it("refuses an out/ tree with more files than the file-count cap", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    for (const name of ["a.txt", "b.txt", "c.txt"]) {
      writeFileSync(join(workspacePath, "out", name), "x");
    }
    expect(() =>
      publishRunArtifacts({
        artifactsRoot,
        executionId: "e-1",
        attempt: 1,
        workspacePath,
        maxTotalFiles: 2,
      }),
    ).toThrow(/2-file cap/);
  });

  it("publishes a tree exactly at the file-count cap", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "a.txt"), "a");
    writeFileSync(join(workspacePath, "out", "b.txt"), "b");
    publishRunArtifacts({
      artifactsRoot,
      executionId: "e-1",
      attempt: 1,
      workspacePath,
      maxTotalFiles: 2,
    });
    const publishedOut = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), "out");
    expect(readdirSync(publishedOut).sort()).toEqual(["a.txt", "b.txt"]);
  });

  it("enforces the real file-count cap when no override is given", () => {
    const { artifactsRoot, workspacePath } = newCase();
    const outDir = join(workspacePath, "out");
    mkdirSync(outDir, { recursive: true });
    for (let i = 0; i <= MAX_PUBLISHED_OUT_FILES; i += 1) {
      writeFileSync(join(outDir, `f-${i}`), "");
    }
    expect(() =>
      publishRunArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1, workspacePath }),
    ).toThrow(new RegExp(`${MAX_PUBLISHED_OUT_FILES}-file cap`));
  });

  it("refuses a publish whose manifest would exceed the manifest byte cap", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", `${"long-name-".repeat(10)}.md`), "x");
    expect(() =>
      publishRunArtifacts({
        artifactsRoot,
        executionId: "e-1",
        attempt: 1,
        workspacePath,
        maxManifestBytes: 64,
      }),
    ).toThrow(/manifest exceeds the 64-byte cap/);
  });

  it("caps the manifest on the rendering the listing serves, not on its raw bytes", () => {
    // `<`/`>`/`&` are legal in a filename and each escapes to six bytes on render.
    const { artifactsRoot, workspacePath } = newCase();
    const name = `${"<&>".repeat(8)}.md`;
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", name), "x");
    const rawManifestBytes = Buffer.byteLength(
      JSON.stringify([{ path: `out/${name}`, sizeBytes: 1, sha256: sha256Hex(Buffer.from("x")) }]),
    );
    expect(() =>
      publishRunArtifacts({
        artifactsRoot,
        executionId: "e-1",
        attempt: 1,
        workspacePath,
        maxManifestBytes: rawManifestBytes,
      }),
    ).toThrow(new RegExp(`manifest exceeds the ${rawManifestBytes}-byte cap`));
  });

  it("publishes escapable filenames whose rendering fits the manifest cap", () => {
    const { artifactsRoot, workspacePath } = newCase();
    const name = "q1 <draft> & final.csv";
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", name), "a,b\n");
    publishRunArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1, workspacePath });
    const manifestPath = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), ARTIFACTS_MANIFEST_NAME);
    expect(readFileSync(manifestPath, "utf8")).toContain(`out/${name}`);
  });

  it("publishes when the manifest is exactly at the manifest byte cap", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "a.md"), "x");
    const exactManifestBytes = Buffer.byteLength(
      JSON.stringify([{ path: "out/a.md", sizeBytes: 1, sha256: sha256Hex(Buffer.from("x")) }]),
    );
    publishRunArtifacts({
      artifactsRoot,
      executionId: "e-1",
      attempt: 1,
      workspacePath,
      maxManifestBytes: exactManifestBytes,
    });
    const publishedOut = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), "out");
    expect(readFileSync(join(publishedOut, "a.md"), "utf8")).toBe("x");
  });
});

function seedPublishedRun(input: { artifactsRoot: string; traceRoot: string; workspacePath: string }): {
  view: RunFilesView;
} {
  mkdirSync(join(input.workspacePath, "out", "tables"), { recursive: true });
  writeFileSync(join(input.workspacePath, "out", "report.md"), "# report");
  writeFileSync(join(input.workspacePath, "out", "tables", "q1.csv"), "a,b\n");
  publishRunArtifacts({
    artifactsRoot: input.artifactsRoot,
    executionId: "e-1",
    attempt: 1,
    workspacePath: input.workspacePath,
  });
  const packDir = join(input.traceRoot, "e-1", "attempt-1");
  mkdirSync(join(packDir, "sub"), { recursive: true });
  writeFileSync(join(packDir, "trace.ndjson"), '{"type":"envelope"}\n');
  writeFileSync(join(packDir, "trace.seq"), "7\n");
  writeFileSync(join(packDir, "sub", "agent-researcher.jsonl"), '{"type":"user"}\n');
  return {
    view: createRunFilesView({
      artifactsRoot: input.artifactsRoot,
      traceRoot: input.traceRoot,
      removeTree: removeOwnTree,
    }),
  };
}

function newViewCase(): { artifactsRoot: string; traceRoot: string; workspacePath: string } {
  const { artifactsRoot, workspacePath } = newCase();
  const traceRoot = join(artifactsRoot, "..", "traces");
  mkdirSync(traceRoot, { recursive: true });
  return { artifactsRoot, traceRoot, workspacePath };
}

describe("discardPublishedArtifacts", () => {
  it("removes the published mirror and manifest, forfeiting the last good publish", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "draft.md"), "the good draft");
    const publishedOut = publish({ artifactsRoot, workspacePath });
    const manifestPath = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), ARTIFACTS_MANIFEST_NAME);
    expect(existsSync(manifestPath)).toBe(true);

    discardPublishedArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1 });
    expect(existsSync(publishedOut)).toBe(false);
    expect(existsSync(manifestPath)).toBe(false);
  });

  it("is a no-op when nothing was ever published", () => {
    const { artifactsRoot } = newCase();
    expect(() => discardPublishedArtifacts({ artifactsRoot, executionId: "e-1", attempt: 1 })).not.toThrow();
  });
});

describe("publish-time manifest", () => {
  it("writes a manifest of path, size, and sha beside the published tree", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out", "tables"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "report.md"), "# report");
    writeFileSync(join(workspacePath, "out", "tables", "q1.csv"), "a,b\n");
    publish({ artifactsRoot, workspacePath });
    const manifestPath = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), "artifacts-manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8")) as unknown;
    expect(manifest).toEqual([
      { path: "out/report.md", sizeBytes: 8, sha256: sha256Hex(Buffer.from("# report")) },
      { path: "out/tables/q1.csv", sizeBytes: 4, sha256: sha256Hex(Buffer.from("a,b\n")) },
    ]);
  });

  it("clears a stale manifest when a republish finds no out/ dir", () => {
    const { artifactsRoot, workspacePath } = newCase();
    mkdirSync(join(workspacePath, "out"), { recursive: true });
    writeFileSync(join(workspacePath, "out", "draft.md"), "v1");
    publish({ artifactsRoot, workspacePath });
    rmSync(join(workspacePath, "out"), { recursive: true, force: true });
    publish({ artifactsRoot, workspacePath });
    const manifestPath = join(artifactAttemptDirOf(artifactsRoot, "e-1", 1), "artifacts-manifest.json");
    expect(existsSync(manifestPath)).toBe(false);
  });
});

describe("createRunFilesView", () => {
  it("serves artifact hashes from the publish-time manifest instead of re-reading the bytes", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const publishedFile = join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), "out", "report.md");
    writeFileSync(publishedFile, "tampered after publish");
    const files = await view.list("e-1", 1);
    const artifact = files.find((file) => file.path === "out/report.md");
    expect(artifact?.sha256).toBe(sha256Hex(Buffer.from("# report")));
  });

  it("never lists the artifacts manifest itself", async () => {
    const seeded = seedPublishedRun(newViewCase());
    const files = await seeded.view.list("e-1", 1);
    expect(files.some((file) => file.path.includes("artifacts-manifest"))).toBe(false);
  });

  it("refuses to serve a listing from a manifest over the manifest byte cap", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const manifestPath = join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), ARTIFACTS_MANIFEST_NAME);
    writeFileSync(manifestPath, `[${" ".repeat(MAX_ARTIFACTS_MANIFEST_BYTES)}]`);
    expect(view.list("e-1", 1)).rejects.toThrow(/byte cap/);
  });

  it("counts published bytes from disk when a kill between the publish renames left no manifest", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    rmSync(join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), ARTIFACTS_MANIFEST_NAME));
    expect(await view.retainedBytes("e-1", 1)).toEqual({ artifactBytes: 12, traceBytes: 36 });
  });

  it("counts published bytes from disk, never from the manifest's declared sizes", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const manifestPath = join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), ARTIFACTS_MANIFEST_NAME);
    writeFileSync(manifestPath, '[{"path":"out/report.md","sizeBytes":1,"sha256":"0"}]');
    expect((await view.retainedBytes("e-1", 1)).artifactBytes).toBe(12);
  });

  it("counts a truncated or unparseable manifest's resident bytes all the same", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const manifestPath = join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), ARTIFACTS_MANIFEST_NAME);
    writeFileSync(manifestPath, '[{"path":"out/repo');
    expect((await view.retainedBytes("e-1", 1)).artifactBytes).toBe(12);
  });

  it("refuses out/ fetches when a kill between the publish renames left no manifest", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    rmSync(join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), ARTIFACTS_MANIFEST_NAME));
    expect(await view.resolve("e-1", 1, "out/report.md")).toBeUndefined();
    const trace = await view.resolve("e-1", 1, "trace/trace.ndjson");
    expect(trace).toBeDefined();
    if (trace !== undefined) {
      closeSizedFile(trace.file);
    }
  });

  it("refuses out/ paths the manifest does not enumerate, while still serving enumerated ones", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const publishedOut = join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), "out");
    writeFileSync(join(publishedOut, "planted.md"), "never published");
    expect(await view.resolve("e-1", 1, "out/planted.md")).toBeUndefined();
    const listed = await view.resolve("e-1", 1, "out/report.md");
    expect(listed).toBeDefined();
    if (listed !== undefined) {
      closeSizedFile(listed.file);
    }
  });

  it("counts bytes stranded in a publish staging dir left by a kill mid-copy", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const stagingDir = join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), ".publish-stranded");
    mkdirSync(join(stagingDir, "out"), { recursive: true });
    writeFileSync(join(stagingDir, "out", "partial.bin"), "half-copy");
    expect((await view.retainedBytes("e-1", 1)).artifactBytes).toBe(12 + 9);
  });

  it("counts a kill after the mirror teardown left only staging: no out/, no manifest, bytes still charged", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const attemptDir = artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1);
    rmSync(join(attemptDir, ARTIFACTS_MANIFEST_NAME));
    rmSync(join(attemptDir, "out"), { recursive: true });
    mkdirSync(join(attemptDir, ".publish-stranded", "out"), { recursive: true });
    writeFileSync(join(attemptDir, ".publish-stranded", "out", "partial.bin"), "half-copy");
    expect((await view.retainedBytes("e-1", 1)).artifactBytes).toBe(9);
  });

  it("lists no artifacts when the manifest is a symlink rather than a regular file", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const attemptDir = artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1);
    const manifestPath = join(attemptDir, ARTIFACTS_MANIFEST_NAME);
    const elsewhere = join(attemptDir, "planted.json");
    rmSync(manifestPath);
    writeFileSync(elsewhere, '[{"path":"out/planted.md","sizeBytes":1,"sha256":"0"}]');
    symlinkSync(elsewhere, manifestPath);
    const files = await view.list("e-1", 1);
    expect(files.every((file) => file.kind === "trace")).toBe(true);
  });

  it("refuses to resolve through a symlinked directory inside the retained area", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const outsideDir = join(seeded.artifactsRoot, "..", "outside");
    mkdirSync(outsideDir, { recursive: true });
    writeFileSync(join(outsideDir, "secret.txt"), "supervisor-only");
    symlinkSync(outsideDir, join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), "out", "link"));
    expect(await view.resolve("e-1", 1, "out/link/secret.txt")).toBeUndefined();
  });

  it("refuses to resolve through a directory swapped for a symlink even when its target stays inside the retained area", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const outDir = join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), "out");
    mkdirSync(join(outDir, "planted"));
    writeFileSync(join(outDir, "planted", "q1.csv"), "planted,bytes\n");
    rmSync(join(outDir, "tables"), { recursive: true });
    symlinkSync(join(outDir, "planted"), join(outDir, "tables"));
    expect(await view.resolve("e-1", 1, "out/tables/q1.csv")).toBeUndefined();
  });

  it("streams from the resolve-time descriptor: a directory swap after resolve cannot redirect the bytes", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const resolved = await view.resolve("e-1", 1, "out/report.md");
    if (resolved === undefined) {
      throw new Error("expected out/report.md to resolve");
    }
    try {
      const outDir = join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), "out");
      const hijacked = join(seeded.artifactsRoot, "..", "hijacked");
      mkdirSync(hijacked, { recursive: true });
      writeFileSync(join(hijacked, "report.md"), "bytes the swap points at");
      renameSync(outDir, `${outDir}-moved`);
      symlinkSync(hijacked, outDir);
      expect(readSizedFile(resolved.file).toString("utf8")).toBe("# report");
    } finally {
      closeSizedFile(resolved.file);
    }
  });

  it("lists artifacts and trace pack files in one manifest, hashed and kinded, sorted by path", async () => {
    const seeded = seedPublishedRun(newViewCase());
    const files = await seeded.view.list("e-1", 1);
    expect(files).toEqual([
      {
        path: "out/report.md",
        sizeBytes: 8,
        sha256: sha256Hex(Buffer.from("# report")),
        kind: "artifact",
      },
      {
        path: "out/tables/q1.csv",
        sizeBytes: 4,
        sha256: sha256Hex(Buffer.from("a,b\n")),
        kind: "artifact",
      },
      {
        path: "trace/sub/agent-researcher.jsonl",
        sizeBytes: 16,
        sha256: sha256Hex(Buffer.from('{"type":"user"}\n')),
        kind: "trace",
      },
      {
        path: "trace/trace.ndjson",
        sizeBytes: 20,
        sha256: sha256Hex(Buffer.from('{"type":"envelope"}\n')),
        kind: "trace",
      },
    ]);
  });

  it("counts exactly the bytes it lists, split by kind, without hashing them", async () => {
    const seeded = seedPublishedRun(newViewCase());
    const listed = await seeded.view.list("e-1", 1);
    const listedBytesOf = (kind: string): number =>
      listed.filter((file) => file.kind === kind).reduce((total, file) => total + file.sizeBytes, 0);
    // trace.seq is excluded from this count too, not just from the listing.
    const counted = await seeded.view.retainedBytes("e-1", 1);
    expect(counted).toEqual({
      artifactBytes: listedBytesOf("artifact"),
      traceBytes: listedBytesOf("trace"),
    });
    expect(counted.artifactBytes + counted.traceBytes).toBe(48);
  });

  it("counts nothing for a run that never published or traced", async () => {
    const { artifactsRoot, traceRoot } = newViewCase();
    const view = createRunFilesView({ artifactsRoot, traceRoot, removeTree: removeOwnTree });
    expect(await view.retainedBytes("e-absent", 1)).toEqual({ artifactBytes: 0, traceBytes: 0 });
  });

  it("purges only the artifacts on the punitive path, leaving the trace pack listed and fetchable", async () => {
    const seeded = seedPublishedRun(newViewCase());
    await seeded.view.purgeArtifacts("e-1");
    const files = await seeded.view.list("e-1", 1);
    expect(files.length).toBeGreaterThan(0);
    expect(files.every((file) => file.kind === "trace")).toBe(true);
    expect(await seeded.view.resolve("e-1", 1, "out/report.md")).toBeUndefined();
    const trace = await seeded.view.resolve("e-1", 1, "trace/trace.ndjson");
    expect(trace).toBeDefined();
    if (trace !== undefined) {
      closeSizedFile(trace.file);
    }
    expect(await seeded.view.retainedBytes("e-1", 1)).toEqual({ artifactBytes: 0, traceBytes: 36 });
    await seeded.view.purge("e-1");
    expect(await seeded.view.list("e-1", 1)).toEqual([]);
  });

  it("never lists the trace.seq counter sidecar", async () => {
    const seeded = seedPublishedRun(newViewCase());
    const files = await seeded.view.list("e-1", 1);
    expect(files.some((file) => file.path.includes("trace.seq"))).toBe(false);
  });

  it("keeps the trace.ndjson hash exact across appends and across a rewrite", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    const tracePath = join(seeded.traceRoot, "e-1", "attempt-1", "trace.ndjson");
    const listedTrace = async (): Promise<{ sizeBytes: number; sha256: string } | undefined> =>
      (await view.list("e-1", 1)).find((file) => file.path === "trace/trace.ndjson");
    const first = '{"type":"envelope"}\n';
    const second = '{"type":"tool_call"}\n';
    expect((await listedTrace())?.sha256).toBe(sha256Hex(Buffer.from(first)));
    appendFileSync(tracePath, second);
    const appended = await listedTrace();
    expect(appended?.sizeBytes).toBe(Buffer.byteLength(first + second));
    expect(appended?.sha256).toBe(sha256Hex(Buffer.from(first + second)));
    writeFileSync(tracePath, first);
    expect((await listedTrace())?.sha256).toBe(sha256Hex(Buffer.from(first)));
  });

  it("lists nothing for a run that never published or traced", async () => {
    const { artifactsRoot, traceRoot } = newViewCase();
    const view = createRunFilesView({ artifactsRoot, traceRoot, removeTree: removeOwnTree });
    expect(await view.list("e-absent", 1)).toEqual([]);
  });

  it("resolves a manifest path to an opened descriptor and content type, and nothing else", async () => {
    const seeded = seedPublishedRun(newViewCase());
    const resolved = await seeded.view.resolve("e-1", 1, "out/tables/q1.csv");
    expect(resolved?.contentType).toBe("text/csv");
    expect(resolved === undefined ? "" : readSizedFile(resolved.file).toString("utf8")).toBe("a,b\n");
    if (resolved !== undefined) {
      closeSizedFile(resolved.file);
    }
    for (const path of [
      "out/missing.md",
      "../out/report.md",
      "out/../report.md",
      "out/../../../../etc/passwd",
      "trace/trace.seq",
      "report.md",
      "",
    ]) {
      expect(await seeded.view.resolve("e-1", 1, path)).toBeUndefined();
    }
  });

  it("purges both retained areas for a run", async () => {
    const seeded = seedPublishedRun(newViewCase());
    await seeded.view.purge("e-1");
    expect(await seeded.view.list("e-1", 1)).toEqual([]);
  });

  it("purges through the injected tree remover, which owns cross-uid deletion", async () => {
    const { artifactsRoot, traceRoot } = newViewCase();
    mkdirSync(join(artifactsRoot, "e-1"), { recursive: true });
    mkdirSync(join(traceRoot, "e-1"), { recursive: true });
    const removed: string[] = [];
    const view = createRunFilesView({
      artifactsRoot,
      traceRoot,
      removeTree: (path) => {
        removed.push(path);
        return Promise.resolve();
      },
    });
    await view.purge("e-1");
    expect(removed.sort()).toEqual([join(artifactsRoot, "e-1"), join(traceRoot, "e-1")]);
  });

  it("removes each attempt subtree by its own owner before the supervisor-owned run dir", async () => {
    const { artifactsRoot, traceRoot } = newViewCase();
    mkdirSync(artifactAttemptDirOf(artifactsRoot, "e-1", 1), { recursive: true });
    mkdirSync(join(artifactsRoot, "e-1", "attempt-2"), { recursive: true });
    mkdirSync(join(traceRoot, "e-1", "attempt-1"), { recursive: true });
    const removed: string[] = [];
    const view = createRunFilesView({
      artifactsRoot,
      traceRoot,
      removeTree: (path) => {
        removed.push(path);
        rmSync(path, { recursive: true, force: true });
        return Promise.resolve();
      },
    });
    await view.purge("e-1");
    expect(removed.filter((path) => path.startsWith(artifactsRoot))).toEqual([
      artifactAttemptDirOf(artifactsRoot, "e-1", 1),
      join(artifactsRoot, "e-1", "attempt-2"),
      join(artifactsRoot, "e-1"),
    ]);
    expect(removed.filter((path) => path.startsWith(traceRoot))).toEqual([
      join(traceRoot, "e-1", "attempt-1"),
      join(traceRoot, "e-1"),
    ]);
    expect(existsSync(join(artifactsRoot, "e-1"))).toBe(false);
  });

  it("refuses executionIds that are not safe path segments", async () => {
    const { artifactsRoot, traceRoot } = newViewCase();
    const view = createRunFilesView({ artifactsRoot, traceRoot, removeTree: removeOwnTree });
    await expect(view.purge("..")).rejects.toThrow();
    await expect(view.list("..", 1)).rejects.toThrow();
  });
});

describe("contentTypeOf", () => {
  it("maps known extensions case-insensitively and falls back to octet-stream", () => {
    expect(contentTypeOf("out/report.PDF")).toBe("application/pdf");
    expect(contentTypeOf("out/report.docx")).toBe(
      "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    );
    expect(contentTypeOf("out/table.xlsx")).toBe(
      "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    );
    expect(contentTypeOf("out/data.json")).toBe("application/json");
    expect(contentTypeOf("trace/trace.ndjson")).toBe("application/x-ndjson");
    expect(contentTypeOf("trace/sub/agent-researcher.jsonl")).toBe("application/x-ndjson");
    expect(contentTypeOf("out/report.md")).toBe("text/markdown");
    expect(contentTypeOf("out/notes.txt")).toBe("text/plain");
    expect(contentTypeOf("out/table.csv")).toBe("text/csv");
    expect(contentTypeOf("out/unknown.zzz")).toBe("application/octet-stream");
    expect(contentTypeOf("out/no-extension")).toBe("application/octet-stream");
  });
});

describe("the persisted final response beside the published artifacts", () => {
  it("is never charged to the run's retained output nor listed as an artifact", async () => {
    const seeded = newViewCase();
    const { view } = seedPublishedRun(seeded);
    writeFileSync(
      join(artifactAttemptDirOf(seeded.artifactsRoot, "e-1", 1), FINAL_RESPONSE_FILE_NAME),
      "x".repeat(100),
    );
    expect((await view.retainedBytes("e-1", 1)).artifactBytes).toBe(12);
    const listed = await view.list("e-1", 1);
    expect(listed.some((file) => file.path.includes(FINAL_RESPONSE_FILE_NAME))).toBe(false);
  });
});
