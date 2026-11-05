// Reads this repository's closed modules: the paths each list under closed-modules/ names, and
// the blocks their markers fence in a file that stays. The docs gate checks only the lines that
// ship, and the export takes every module out of the public tree. A module is the paths its list
// names, plus every block its markers fence.
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

const LIST_FOLDER = "closed-modules";
const LIST_SUFFIX = ".txt";
const MARKER_PATTERN = /^\s*(?:\/\/|#|<!--|@\*)\s*closed-module ([a-z0-9-]+): (start|end)\s*(?:-->|\*@)?\s*$/;

function readList(root: string, module: string): string[] {
  return readFileSync(join(root, LIST_FOLDER, `${module}${LIST_SUFFIX}`), "utf8")
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line !== "" && !line.startsWith("#"));
}

export function closedModules(root: string): string[] {
  const folder = join(root, LIST_FOLDER);
  if (!existsSync(folder)) {
    return [];
  }
  return readdirSync(folder)
    .filter((name) => name.endsWith(LIST_SUFFIX))
    .map((name) => name.slice(0, -LIST_SUFFIX.length));
}

export function closedPaths(root: string): string[] {
  return closedModules(root).flatMap((module) => readList(root, module));
}

export function isClosedPath(closed: readonly string[], file: string): boolean {
  return closed.some((path) => file === path || file.startsWith(`${path}/`));
}

// The lines that stay once every closed module is stripped, numbered from 1. It refuses a fence
// that the export refuses: a module with no list, an end with no start, a start inside the same
// module's block, and a block that never ends.
export function openLines(
  text: string,
  file: string,
  modules: ReadonlySet<string>,
): { number: number; text: string }[] {
  const open: { module: string; start: number }[] = [];
  const kept: { number: number; text: string }[] = [];
  for (const [index, line] of text.split("\n").entries()) {
    const number = index + 1;
    const [, module, edge] = line.match(MARKER_PATTERN) ?? [];
    if (module === undefined) {
      if (open.length === 0) {
        kept.push({ number, text: line });
      }
      continue;
    }
    if (!modules.has(module)) {
      throw new Error(
        `${file}: line ${number} names closed module "${module}", which has no list in ${LIST_FOLDER}/`,
      );
    }
    const same = open.find((block) => block.module === module);
    if (edge === "start") {
      if (same !== undefined) {
        throw new Error(
          `${file}: line ${number} starts a closed-module ${module} block inside the one that starts at line ${same.start}`,
        );
      }
      open.push({ module, start: number });
      continue;
    }
    if (same === undefined) {
      throw new Error(`${file}: line ${number} ends a closed-module ${module} block that never started`);
    }
    const innermost = open.at(-1);
    if (innermost !== undefined && innermost.module !== module) {
      throw new Error(
        `${file}: line ${number} ends a closed-module ${module} block while the closed-module ${innermost.module} block that starts at line ${innermost.start} is still open`,
      );
    }
    open.pop();
  }
  const [unclosed] = open;
  if (unclosed !== undefined) {
    throw new Error(
      `${file}: a closed-module ${unclosed.module} block starts at line ${unclosed.start} and never ends`,
    );
  }
  return kept;
}
