import { afterAll, describe, expect, it } from "bun:test";
import { spawnSync } from "node:child_process";
import {
  appendFileSync,
  mkdirSync,
  mkdtempSync,
  rmSync,
  symlinkSync,
  truncateSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { hasErrorCode } from "./errors.js";
import {
  closeSizedFile,
  NotARegularFileError,
  OpenedPathMismatchError,
  openSizedFile,
  readSizedFile,
} from "./fs-read.js";
import { walkRegularFiles } from "./fs-walk.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "fs-read-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

let caseId = 0;

function newFile(contents: string): string {
  caseId += 1;
  const path = join(TEST_ROOT, `case-${caseId}.bin`);
  writeFileSync(path, contents);
  return path;
}

describe("openSizedFile and readSizedFile", () => {
  it("reports the size and returns exactly those bytes", () => {
    const path = newFile("the whole file");
    const file = openSizedFile(path);
    try {
      expect(file.sizeBytes).toBe(14);
      expect(readSizedFile(file).toString("utf8")).toBe("the whole file");
    } finally {
      closeSizedFile(file);
    }
  });

  it("never reads past the size it reported, however much the file grew after", () => {
    const path = newFile("small");
    const file = openSizedFile(path);
    try {
      appendFileSync(path, "x".repeat(10_000));
      expect(file.sizeBytes).toBe(5);
      expect(readSizedFile(file).toString("utf8")).toBe("small");
    } finally {
      closeSizedFile(file);
    }
  });

  it("returns only the surviving bytes when the file shrank after it was opened", () => {
    const path = newFile("keep this, drop that");
    const file = openSizedFile(path);
    try {
      truncateSync(path, 10);
      expect(readSizedFile(file).toString("utf8")).toBe("keep this,");
    } finally {
      closeSizedFile(file);
    }
  });

  it("fails to open a file that is not there rather than reporting an empty one", () => {
    expect(() => openSizedFile(join(TEST_ROOT, "never-written.bin"))).toThrow();
  });
});

describe("openSizedFile against a run-controlled tree", () => {
  function catchOpen(path: string): unknown {
    try {
      openSizedFile(path);
    } catch (error) {
      return error;
    }
    return undefined;
  }

  it("refuses a symlink instead of reading through it to bytes outside the tree", () => {
    const outside = newFile("bytes from outside the tree");
    caseId += 1;
    const link = join(TEST_ROOT, `link-${caseId}`);
    symlinkSync(outside, link);
    expect(hasErrorCode(catchOpen(link), "ELOOP")).toBe(true);
  });

  it("refuses a path the walk saw as a regular file that a swap turned into a symlink", () => {
    const outside = newFile("bytes from outside the tree");
    const dir = mkdtempSync(join(TEST_ROOT, "swap-"));
    const walked = join(dir, "deliverable.md");
    writeFileSync(walked, "in-tree bytes");
    const seen: string[] = [];
    walkRegularFiles(dir, (file) => seen.push(file.absolutePath));
    expect(seen).toEqual([walked]);
    rmSync(walked);
    symlinkSync(outside, walked);
    expect(hasErrorCode(catchOpen(walked), "ELOOP")).toBe(true);
  });

  it("refuses a file reached through a symlinked directory component, not just a symlinked leaf", () => {
    const elsewhere = mkdtempSync(join(TEST_ROOT, "elsewhere-"));
    writeFileSync(join(elsewhere, "deliverable.md"), "bytes from outside the tree");
    const tree = mkdtempSync(join(TEST_ROOT, "linked-parent-"));
    symlinkSync(elsewhere, join(tree, "sub"));
    expect(catchOpen(join(tree, "sub", "deliverable.md"))).toBeInstanceOf(OpenedPathMismatchError);
  });

  it("refuses a walked path whose directory a swap turned into a symlink before the open", () => {
    const elsewhere = mkdtempSync(join(TEST_ROOT, "swap-target-"));
    writeFileSync(join(elsewhere, "deliverable.md"), "bytes from outside the tree");
    const tree = mkdtempSync(join(TEST_ROOT, "dir-swap-"));
    mkdirSync(join(tree, "sub"));
    writeFileSync(join(tree, "sub", "deliverable.md"), "in-tree bytes");
    const seen: string[] = [];
    walkRegularFiles(tree, (file) => seen.push(file.absolutePath));
    expect(seen).toEqual([join(tree, "sub", "deliverable.md")]);
    rmSync(join(tree, "sub"), { recursive: true });
    symlinkSync(elsewhere, join(tree, "sub"));
    expect(catchOpen(join(tree, "sub", "deliverable.md"))).toBeInstanceOf(OpenedPathMismatchError);
  });

  it("refuses a directory swapped in where a regular file was walked", () => {
    caseId += 1;
    const dir = join(TEST_ROOT, `as-a-directory-${caseId}`);
    mkdirSync(dir);
    expect(catchOpen(dir)).toBeInstanceOf(NotARegularFileError);
  });

  it("refuses a device node, whose size and bytes no reader could reconcile", () => {
    expect(catchOpen("/dev/null")).toBeInstanceOf(NotARegularFileError);
  });

  it("refuses a writerless fifo instead of blocking on it until some deadline fires", () => {
    caseId += 1;
    const fifo = join(TEST_ROOT, `fifo-${caseId}`);
    const mkfifo = spawnSync("mkfifo", [fifo]);
    expect(mkfifo.status).toBe(0);
    expect(catchOpen(fifo)).toBeInstanceOf(NotARegularFileError);
  });
});
