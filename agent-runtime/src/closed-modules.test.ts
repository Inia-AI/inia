import { afterEach, describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { closedModules, closedPaths, isClosedPath, openLines } from "./closed-modules.js";

const roots: string[] = [];

function tree(files: Record<string, string>): string {
  const root = mkdtempSync(join(tmpdir(), "closed-modules-"));
  roots.push(root);
  for (const [path, text] of Object.entries(files)) {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), text);
  }
  return root;
}

afterEach(() => {
  for (const root of roots.splice(0)) {
    rmSync(root, { recursive: true, force: true });
  }
});

describe("closedModules", () => {
  it("names each module that has a list", () => {
    const root = tree({
      "closed-modules/demo.txt": "",
      "closed-modules/other.txt": "",
      "closed-modules/README.md": "",
    });

    expect(closedModules(root).sort()).toEqual(["demo", "other"]);
  });
});

describe("isClosedPath", () => {
  it("holds for a listed path and for every path under a listed folder, and for nothing else", () => {
    const closed = ["docs/research", "CLAUDE.md"];

    expect(isClosedPath(closed, "CLAUDE.md")).toBe(true);
    expect(isClosedPath(closed, "docs/research/spike-log.md")).toBe(true);
    expect(isClosedPath(closed, "docs/research-notes.md")).toBe(false);
    expect(isClosedPath(closed, "docs/README.md")).toBe(false);
  });
});

describe("closedPaths", () => {
  it("names every path that any module's list names", () => {
    const root = tree({
      "closed-modules/demo.txt": "# the demo module\nsrc/demo.ts\n",
      "closed-modules/other.txt": "docs/other.md\n",
    });

    expect(closedPaths(root).sort()).toEqual(["docs/other.md", "src/demo.ts"]);
  });

  it("names nothing in a tree with no closed modules, as in the public tree", () => {
    expect(closedPaths(tree({ "src/main.ts": "" }))).toEqual([]);
  });
});

describe("openLines", () => {
  it("keeps the lines outside every module's blocks, with their line numbers", () => {
    const text = [
      "a",
      "<!-- closed-module demo: start -->",
      "b",
      "<!-- closed-module demo: end -->",
      "c",
      "  # closed-module other: start",
      "d",
      "  # closed-module other: end",
      "Prose that names `// closed-module demo: start` stays.",
    ].join("\n");

    expect(openLines(text, "page.md", new Set(["demo", "other"]))).toEqual([
      { number: 1, text: "a" },
      { number: 5, text: "c" },
      { number: 9, text: "Prose that names `// closed-module demo: start` stays." },
    ]);
  });

  it("drops a block of one module nested inside another's", () => {
    const text = [
      "a",
      "<!-- closed-module demo: start -->",
      "b",
      "<!-- closed-module other: start -->",
      "c",
      "<!-- closed-module other: end -->",
      "d",
      "<!-- closed-module demo: end -->",
      "e",
    ].join("\n");

    expect(openLines(text, "page.md", new Set(["demo", "other"]))).toEqual([
      { number: 1, text: "a" },
      { number: 9, text: "e" },
    ]);
  });

  it("refuses blocks of two modules that cross, naming the block still open", () => {
    const text = [
      "<!-- closed-module demo: start -->",
      "<!-- closed-module other: start -->",
      "<!-- closed-module demo: end -->",
      "<!-- closed-module other: end -->",
    ].join("\n");

    expect(() => openLines(text, "page.md", new Set(["demo", "other"]))).toThrow(
      "page.md: line 3 ends a closed-module demo block while the closed-module other block that starts at line 2 is still open",
    );
  });

  it("refuses a block that never ends, so it cannot hide the rest of the file", () => {
    expect(() =>
      openLines("a\n<!-- closed-module demo: start -->\nb\n", "page.md", new Set(["demo"])),
    ).toThrow("page.md: a closed-module demo block starts at line 2 and never ends");
  });

  it("refuses a marker for a module with no list, as in the public tree", () => {
    expect(() =>
      openLines(
        "<!-- closed-module demo: start -->\nb\n<!-- closed-module demo: end -->\n",
        "page.md",
        new Set(),
      ),
    ).toThrow('page.md: line 1 names closed module "demo", which has no list in closed-modules/');
  });
});
