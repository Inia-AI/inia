import { describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { walkRegularFiles } from "./fs-walk.js";

// tmpfs returns directory entries newest-first, so creating files in sorted order hands the walk
// a reversed listing — the one filesystem where "just use readdir order" is guaranteed wrong.
const TMPFS_ROOT = "/dev/shm";
const ORDER_ROOT = statSync(TMPFS_ROOT, { throwIfNoEntry: false })?.isDirectory() ? TMPFS_ROOT : tmpdir();

describe("walkRegularFiles ordering", () => {
  it("visits files in byte order of their relative path, whatever order the directory returns them in", () => {
    const root = mkdtempSync(join(ORDER_ROOT, "walk-order-"));
    const sorted = ["a.md", "b/a.txt", "b/c/deep.txt", "tables/q1.csv", "tables/q9.csv", "z.md"];
    for (const relativePath of sorted) {
      mkdirSync(join(root, relativePath, ".."), { recursive: true });
      writeFileSync(join(root, relativePath), relativePath);
    }
    const seen: string[] = [];
    walkRegularFiles(root, (file) => seen.push(file.relativePath));
    expect(seen).toEqual(sorted);
  });

  it("is measured on a filesystem whose raw listing is not already sorted", () => {
    const root = mkdtempSync(join(ORDER_ROOT, "walk-raw-"));
    for (const name of ["a", "b", "c"]) {
      writeFileSync(join(root, name), name);
    }
    expect(readdirSync(root)).not.toEqual(["a", "b", "c"]);
  });
});
