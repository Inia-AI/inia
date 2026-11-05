import { type Dirent, readdirSync } from "node:fs";
import { join } from "node:path";
import { hasErrorCode } from "./errors.js";

export type WalkedFile = { relativePath: string; absolutePath: string };

export type WalkOptions = {
  skipTopLevel?: readonly string[];
  missingRootOk?: boolean;
};

// Symlinks and other special entries are excluded here, once, for every consumer — a
// symlink could smuggle in bytes from outside the walked tree.
export function walkRegularFiles(
  rootDir: string,
  visit: (file: WalkedFile) => void,
  options: WalkOptions = {},
): void {
  walk(rootDir, "", visit, options.skipTopLevel ?? [], options.missingRootOk === true);
}

function walk(
  rootDir: string,
  prefix: string,
  visit: (file: WalkedFile) => void,
  skipTopLevel: readonly string[],
  missingRootOk: boolean,
): void {
  let entries: Dirent<string>[];
  try {
    entries = readdirSync(join(rootDir, prefix), { withFileTypes: true });
  } catch (error) {
    if (prefix === "" && missingRootOk && hasErrorCode(error, "ENOENT", "ENOTDIR")) {
      return;
    }
    throw error;
  }
  for (const entry of entries.sort(byName)) {
    const relativePath = prefix === "" ? entry.name : `${prefix}/${entry.name}`;
    if (prefix === "" && skipTopLevel.includes(relativePath)) {
      continue;
    }
    if (entry.isDirectory()) {
      walk(rootDir, relativePath, visit, skipTopLevel, missingRootOk);
    } else if (entry.isFile()) {
      visit({ relativePath, absolutePath: join(rootDir, relativePath) });
    }
  }
}

function byName(a: Dirent<string>, b: Dirent<string>): number {
  if (a.name === b.name) {
    return 0;
  }
  return a.name < b.name ? -1 : 1;
}
