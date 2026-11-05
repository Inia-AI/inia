import { readdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { MAX_CHECKPOINT_BUNDLE_BYTES } from "./config.js";
import { hasErrorCode, toError } from "./errors.js";
import { closeSizedFile, openSizedFileOrUndefined, type SizedFile } from "./fs-read.js";
import { SHA256_HEX_PATTERN, sha256Hex } from "./hash.js";
import { claimPrivateRoot, PRIVATE_FILE_MODE, publishAtomically } from "./private-fs.js";

export class CheckpointIntegrityError extends Error {}

export class CheckpointSizeError extends Error {}

export type CheckpointKey = {
  executionId: string;
  attempt: number;
  segment: number;
};

// The server (save) and the spawner (cold resume) both derive this key; a mismatch here makes a resume silently miss.
export function checkpointKeyOf(executionId: string, attempt: number, segment: number): CheckpointKey {
  return { executionId, attempt, segment };
}

// Pause segments count 0 upward, so the terminal bundle's slot can never collide with one.
export const TERMINAL_CHECKPOINT_SEGMENT = -1;

export function terminalCheckpointKeyOf(executionId: string, attempt: number): CheckpointKey {
  return { executionId, attempt, segment: TERMINAL_CHECKPOINT_SEGMENT };
}

export type CheckpointBundle = {
  bytes: Uint8Array;
  sha256: string;
};

// The bundle as an open descriptor plus the digest declared for it, for a caller that streams
// the bytes rather than holding them: 64 MiB resident per fetch, on the supervisor that hosts
// every run, is the cost `load` pays and this one does not.
export type OpenedCheckpoint = {
  file: SizedFile;
  sha256: string;
};

export type CheckpointStore = {
  save(key: CheckpointKey, bundle: CheckpointBundle): Promise<void>;
  load(key: CheckpointKey): Promise<CheckpointBundle | undefined>;
  open(key: CheckpointKey): Promise<OpenedCheckpoint | undefined>;
  delete(key: CheckpointKey): Promise<void>;
  // Files whose in-memory record died with a supervisor restart belong to no run and no code
  // would ever delete them; `isLive` is asked per execution id so a running run keeps its own.
  reapOrphans(input: {
    isLive: (executionId: string) => boolean;
    olderThanMs: number;
    onSkipped?: (name: string, error: Error) => void;
  }): Promise<number>;
};

export function sealCheckpoint(bytes: Uint8Array): CheckpointBundle {
  return { bytes, sha256: sha256Hex(bytes) };
}

const FILE_SUFFIX = ".checkpoint";
const HASH_SUFFIX = ".sha256";

// A sidecar with no bundle left has no mtime to judge; the pair is old enough when the half
// that is still there says so.
async function isOlderThan(path: string, oldestKeptMtimeMs: number): Promise<boolean> {
  try {
    return (await stat(path)).mtimeMs <= oldestKeptMtimeMs;
  } catch (error) {
    if (hasErrorCode(error, "ENOENT")) {
      return true;
    }
    throw error;
  }
}

function fileNameOf(key: CheckpointKey): string {
  return `${encodeURIComponent(key.executionId)}.${key.attempt}.${key.segment}${FILE_SUFFIX}`;
}

// Parsed from the right, because an executionId may itself contain dots: the last two
// dot-separated fields are the attempt and the segment, and everything before them is the
// encoded id.
// A stored bundle is a pair, and either half can be on disk without the other: `save` writes the
// digest first and the bytes second, so a supervisor that dies between the two leaves a sidecar
// with no bundle. Both names answer here, and both resolve to the bundle path, so the sweep
// removes the pair whichever half it met.
function bundleNameOf(name: string): string | undefined {
  const bundleName = name.endsWith(HASH_SUFFIX) ? name.slice(0, -HASH_SUFFIX.length) : name;
  return bundleName.endsWith(FILE_SUFFIX) ? bundleName : undefined;
}

function executionIdOfFileName(name: string): string | undefined {
  if (!name.endsWith(FILE_SUFFIX)) {
    return undefined;
  }
  const encodedId = name.slice(0, -FILE_SUFFIX.length).split(".").slice(0, -2).join(".");
  if (encodedId === "") {
    return undefined;
  }
  try {
    return decodeURIComponent(encodedId);
  } catch {
    return undefined;
  }
}

export function createLocalCheckpointStore(root: string): CheckpointStore {
  claimPrivateRoot(root, "checkpoint store");

  function pathOf(key: CheckpointKey): string {
    return join(root, fileNameOf(key));
  }

  function hashPathOf(key: CheckpointKey): string {
    return `${pathOf(key)}${HASH_SUFFIX}`;
  }

  // One place knows a stored bundle is a pair of files: the reaper deletes by path and `delete`
  // by key, and a third companion file would otherwise be forgotten by exactly the sweep that
  // exists to stop files being forgotten.
  async function removeBundleFilesAt(path: string): Promise<void> {
    await Promise.all([rm(path, { force: true }), rm(`${path}${HASH_SUFFIX}`, { force: true })]);
  }

  // The sidecar contract — its name, its trimming, and what an absent or malformed one means —
  // read once for both the load path and the fetch path.
  async function readDeclaredSha256(key: CheckpointKey): Promise<string> {
    let sha256: string;
    try {
      sha256 = (await readFile(hashPathOf(key), "utf8")).trim();
    } catch (error) {
      if (hasErrorCode(error, "ENOENT")) {
        throw new CheckpointIntegrityError(`stored checkpoint at ${pathOf(key)} has no sha256 sidecar`);
      }
      throw error;
    }
    if (!SHA256_HEX_PATTERN.test(sha256)) {
      throw new CheckpointIntegrityError(
        `stored checkpoint at ${pathOf(key)} has a malformed sha256 sidecar`,
      );
    }
    return sha256;
  }

  function writeAtomically(finalPath: string, contents: Uint8Array | string): Promise<void> {
    return publishAtomically(root, finalPath, (stagingPath) =>
      writeFile(stagingPath, contents, { mode: PRIVATE_FILE_MODE }),
    );
  }

  return {
    async save(key, bundle) {
      if (bundle.bytes.byteLength > MAX_CHECKPOINT_BUNDLE_BYTES) {
        throw new CheckpointSizeError(
          `refusing to save checkpoint for ${key.executionId}: ${bundle.bytes.byteLength} bytes ` +
            `exceeds the ${MAX_CHECKPOINT_BUNDLE_BYTES}-byte cap`,
        );
      }
      // Deliberately not re-hashed here. Every caller receives its bundle through
      // fromWireCheckpoint, which hashes the bytes as they cross the one boundary that can
      // corrupt them; hashing the same in-memory buffer a second time proves nothing about
      // that crossing and costs a full pass over up to 64 MiB on the supervisor's event loop.
      // Hash first, bytes second: a crash between the two leaves only a hash file, which load reports as a clean miss, not corruption.
      await writeAtomically(hashPathOf(key), bundle.sha256);
      await writeAtomically(pathOf(key), bundle.bytes);
    },

    async load(key) {
      const path = pathOf(key);
      let bytes: Buffer;
      try {
        bytes = await readFile(path);
      } catch (error) {
        if (hasErrorCode(error, "ENOENT")) {
          return undefined;
        }
        throw error;
      }
      const sha256 = await readDeclaredSha256(key);
      if (sha256Hex(bytes) !== sha256) {
        throw new CheckpointIntegrityError(`stored checkpoint at ${path} failed its sha256 integrity check`);
      }
      return { bytes, sha256 };
    },

    async open(key) {
      const opened = openSizedFileOrUndefined(pathOf(key));
      if (opened === undefined) {
        return undefined;
      }
      try {
        return { file: opened, sha256: await readDeclaredSha256(key) };
      } catch (error) {
        closeSizedFile(opened);
        throw error;
      }
    },

    async delete(key) {
      await removeBundleFilesAt(pathOf(key));
    },

    async reapOrphans({ isLive, olderThanMs, onSkipped = () => {} }) {
      let reaped = 0;
      let names: string[];
      try {
        names = await readdir(root);
      } catch (error) {
        if (hasErrorCode(error, "ENOENT")) {
          return reaped;
        }
        throw error;
      }
      const oldestKeptMtimeMs = Date.now() - olderThanMs;
      const bundleNames = new Set(names.map(bundleNameOf).filter((name) => name !== undefined));
      for (const bundleName of bundleNames) {
        const executionId = executionIdOfFileName(bundleName);
        if (executionId === undefined || isLive(executionId)) {
          continue;
        }
        const path = join(root, bundleName);
        // One unreadable entry must not abandon the rest of the sweep: this is the only thing
        // that reclaims a stranded file, so it skips what it cannot handle and carries on.
        try {
          if (await isOlderThan(path, oldestKeptMtimeMs)) {
            await removeBundleFilesAt(path);
            reaped += 1;
          }
        } catch (error) {
          onSkipped(bundleName, toError(error));
        }
      }
      return reaped;
    },
  };
}
