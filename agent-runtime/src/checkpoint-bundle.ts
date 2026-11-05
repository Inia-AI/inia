import { existsSync, lstatSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, normalize } from "node:path";
import { gunzipSync, gzipSync } from "node:zlib";
import { type ChargeBudget, createChargeBudget } from "./byte-budget.js";
import { EXPECTED_CLAUDE_CODE_VERSION } from "./claude-code-pin.js";
import { type BuildIdentity, MAX_CHECKPOINT_BUNDLE_BYTES } from "./config.js";
import { hasErrorCode } from "./errors.js";
import { closeSizedFile, openSizedFile, readSizedFile } from "./fs-read.js";
import { walkRegularFiles } from "./fs-walk.js";
import { createNarrower, type Narrower } from "./narrow.js";
import { PRIVATE_DIR_MODE, PRIVATE_FILE_MODE } from "./private-fs.js";
import { areSafePathSegments } from "./safe-path.js";

export class CheckpointBundleError extends Error {}

const parse: Narrower = createNarrower((message) => new CheckpointBundleError(message));

// Bumped when a stored bundle's layout stops being readable by the code that reads it, so a
// platform holding an older bundle can refuse it rather than restore it into a shape the
// sidecar no longer understands.
export const CHECKPOINT_BUNDLE_FORMAT = "1";

// Four parts because four things can each make a stored bundle unusable on restore: the
// bundle's own layout, the sidecar that wrote it, the SDK whose session transcript it holds,
// and the model that session ran on.
export type CheckpointVersionStamp = {
  bundleFormat: string;
  sidecar: BuildIdentity;
  sdk: string;
  model: string;
};

export type CheckpointManifest = {
  sessionId: string;
  workspacePath: string;
  files: Record<string, string>;
  version?: CheckpointVersionStamp;
};

export function composeCheckpointVersionStamp(input: {
  sidecar: BuildIdentity;
  model: string;
}): CheckpointVersionStamp {
  return {
    bundleFormat: CHECKPOINT_BUNDLE_FORMAT,
    sidecar: input.sidecar,
    sdk: EXPECTED_CLAUDE_CODE_VERSION,
    model: input.model,
  };
}

const CONFIG_AREA = "config";
const WORKSPACE_AREA = "workspace";

function oversizedError(maxBytes: number): CheckpointBundleError {
  return new CheckpointBundleError(`checkpoint bundle exceeds the ${maxBytes}-byte cap`);
}

function base64LengthOf(byteLength: number): number {
  return Math.ceil(byteLength / 3) * 4;
}

// `"<path>":"<base64>",` — the punctuation each entry adds to the manifest string on top of
// the two values themselves.
const JSON_ENTRY_PUNCTUATION_BYTES = 4;

// Charging inside the visitor is what bounds the walk: `budget.charge` throws, and the throw
// propagates out of `walkRegularFiles`, so a workspace far over the cap stops being enumerated
// at the file that busts it rather than being read into memory in full first.
function collectArea(input: {
  dir: string;
  area: string;
  skipTopLevel: readonly string[];
  files: Record<string, string>;
  budget: ChargeBudget;
}): void {
  walkRegularFiles(
    input.dir,
    (file) => {
      const path = `${input.area}/${file.relativePath}`;
      // Charged from the same descriptor the bytes are read from, so the run can't grow the file between charge and read.
      const opened = openSizedFile(file.absolutePath);
      try {
        // The entry's own manifest cost is charged with its bytes, so the cap is spent as the
        // manifest is built rather than measured after the whole string exists.
        input.budget.charge(
          base64LengthOf(opened.sizeBytes) +
            Buffer.byteLength(JSON.stringify(path)) +
            JSON_ENTRY_PUNCTUATION_BYTES,
        );
        input.files[path] = readSizedFile(opened).toString("base64");
      } finally {
        closeSizedFile(opened);
      }
    },
    { skipTopLevel: input.skipTopLevel },
  );
}

// Synchronous on purpose: both call sites run in the per-run child after its SDK stream is torn
// down, never in the supervisor that hosts every run, so yielding between files would buy no
// interleaving and measurably slow the walk.
//
// The cap bounds the uncompressed manifest, not the stored bytes: gzip cannot grow a manifest
// whose bulk is base64 (measured: 64 MiB of base64 over random bytes compresses to about
// 48 MiB), so every downstream size proof stays an upper bound compression cannot invalidate.
export function serializeSessionCheckpoint(input: {
  sessionId: string;
  workspacePath: string;
  configDir: string;
  reservedWorkspaceDirs: readonly string[];
  version?: CheckpointVersionStamp;
  maxBytes?: number;
}): Uint8Array {
  const maxBytes = input.maxBytes ?? MAX_CHECKPOINT_BUNDLE_BYTES;
  if (!existsSync(input.configDir)) {
    throw new CheckpointBundleError(`session dir ${input.configDir} does not exist`);
  }
  if (!existsSync(input.workspacePath)) {
    throw new CheckpointBundleError(`workspace ${input.workspacePath} does not exist`);
  }
  const files: Record<string, string> = {};
  const budget = createChargeBudget(maxBytes, oversizedError);
  const manifest: CheckpointManifest = {
    sessionId: input.sessionId,
    workspacePath: input.workspacePath,
    files,
    ...(input.version === undefined ? {} : { version: input.version }),
  };
  // What the manifest costs with no entries in it, charged before any file so the entries are
  // charged against what is actually left.
  budget.charge(Buffer.byteLength(JSON.stringify(manifest)));
  collectArea({ dir: input.configDir, area: CONFIG_AREA, skipTopLevel: [], files, budget });
  collectArea({
    dir: input.workspacePath,
    area: WORKSPACE_AREA,
    skipTopLevel: input.reservedWorkspaceDirs,
    files,
    budget,
  });
  return gzipSync(new TextEncoder().encode(JSON.stringify(manifest)));
}

function assertSafeRelativePath(path: string, reservedWorkspaceDirs: readonly string[]): string[] {
  const [area, ...segments] = path.split("/");
  if (area !== CONFIG_AREA && area !== WORKSPACE_AREA) {
    throw new CheckpointBundleError(`file path "${path}" is outside the config and workspace areas`);
  }
  if (!areSafePathSegments(segments)) {
    throw new CheckpointBundleError(`file path "${path}" is not a safe relative path`);
  }
  // Tested against the resolved path, not the literal segment — a dot-prefixed path could otherwise smuggle files into docs/ or out/ at restore.
  const normalized = normalize(segments.join("/"));
  const resolvedSegments = normalized.split("/");
  if (normalized.startsWith("/") || !areSafePathSegments(resolvedSegments)) {
    throw new CheckpointBundleError(`file path "${path}" is not a safe relative path`);
  }
  const first = resolvedSegments[0];
  if (area === WORKSPACE_AREA && first !== undefined && reservedWorkspaceDirs.includes(first)) {
    throw new CheckpointBundleError(`file path "${path}" is inside the reserved ${first}/ area`);
  }
  return [area, ...resolvedSegments];
}

const RUN_DIR_PREFIX = "run-";

// The manifest crosses a process boundary and is untrusted: unconstrained, its workspacePath could aim recursive writes and rm -rf anywhere the uid can reach.
function assertContainedWorkspacePath(workspacePath: string, workspaceRoot: string): void {
  const prefix = `${workspaceRoot}/`;
  const name = workspacePath.startsWith(prefix) ? workspacePath.slice(prefix.length) : "";
  if (name === "" || name.includes("/") || !name.startsWith(RUN_DIR_PREFIX)) {
    throw new CheckpointBundleError(
      `checkpoint workspacePath must name a ${RUN_DIR_PREFIX}* dir directly under ${workspaceRoot}`,
    );
  }
}

export type ManifestConstraints = {
  workspaceRoot: string;
  reservedWorkspaceDirs: readonly string[];
};

function parseVersionStamp(value: unknown): CheckpointVersionStamp {
  const record = parse.asRecord(value, "checkpoint manifest.version");
  const sidecar = parse.asRecord(record["sidecar"], "checkpoint manifest.version.sidecar");
  return {
    bundleFormat: parse.requireString(record, "bundleFormat", "checkpoint manifest.version"),
    sidecar: {
      version: parse.requireString(sidecar, "version", "checkpoint manifest.version.sidecar"),
      revision: parse.requireString(sidecar, "revision", "checkpoint manifest.version.sidecar"),
    },
    sdk: parse.requireString(record, "sdk", "checkpoint manifest.version"),
    model: parse.requireString(record, "model", "checkpoint manifest.version"),
  };
}

export function parseCheckpointManifest(
  value: unknown,
  constraints: ManifestConstraints,
): CheckpointManifest {
  const record = parse.asRecord(value, "checkpoint manifest");
  const sessionId = parse.requireString(record, "sessionId", "checkpoint manifest");
  const workspacePath = parse.requireString(record, "workspacePath", "checkpoint manifest");
  if (!workspacePath.startsWith("/")) {
    parse.fail("checkpoint manifest must carry an absolute workspacePath");
  }
  assertContainedWorkspacePath(workspacePath, constraints.workspaceRoot);
  const rawFiles = parse.asRecord(record["files"], "checkpoint manifest.files");
  const files: Record<string, string> = {};
  for (const [path, content] of Object.entries(rawFiles)) {
    assertSafeRelativePath(path, constraints.reservedWorkspaceDirs);
    if (typeof content !== "string") {
      parse.fail(`file "${path}" must carry base64 content`);
    }
    files[path] = content;
  }
  // Optional on purpose: a bundle written before the stamp existed reads back as unstamped
  // rather than failing, and the platform treats an absent stamp as its own answer.
  const version = parse.optionalWith(record, "version", parseVersionStamp, "checkpoint manifest");
  return { sessionId, workspacePath, files, ...(version === undefined ? {} : { version }) };
}

// Sniffed rather than assumed: a bundle now outlives the sidecar that wrote it — it goes into
// platform custody and comes back to a later build — so the reader has to answer for the
// uncompressed form this repo used to write.
function inflateIfCompressed(bytes: Uint8Array, maxBytes: number): Uint8Array {
  if (bytes[0] !== 0x1f || bytes[1] !== 0x8b) {
    return bytes;
  }
  try {
    // Bounded because the bytes arrive from outside this process — the platform holds the bundle
    // between runs — and the child that inflates one is memory-budgeted like every other.
    return gunzipSync(bytes, { maxOutputLength: maxBytes });
  } catch (error) {
    throw new CheckpointBundleError("checkpoint bundle bytes could not be decompressed", {
      cause: error,
    });
  }
}

export function decodeCheckpointManifest(
  bytes: Uint8Array,
  constraints: ManifestConstraints,
  maxBytes: number = MAX_CHECKPOINT_BUNDLE_BYTES,
): CheckpointManifest {
  const inflated = inflateIfCompressed(bytes, maxBytes);
  let parsed: unknown;
  try {
    parsed = JSON.parse(new TextDecoder().decode(inflated));
  } catch {
    throw new CheckpointBundleError("checkpoint bundle bytes are not valid JSON");
  }
  return parseCheckpointManifest(parsed, constraints);
}

function assertReclaimableWorkspace(workspacePath: string): void {
  const stats = lstatSync(workspacePath);
  const ownUid = process.getuid?.();
  if (
    !stats.isDirectory() ||
    (stats.mode & 0o777) !== PRIVATE_DIR_MODE ||
    (ownUid !== undefined && stats.uid !== ownUid)
  ) {
    throw new CheckpointBundleError(
      `workspace dir ${workspacePath} already exists and is not this run's own private dir; refusing to restore into it`,
    );
  }
}

export function restoreSessionCheckpoint(
  manifest: CheckpointManifest,
  target: { configDir: string; reservedWorkspaceDirs: readonly string[] },
): void {
  mkdirSync(target.configDir, { recursive: true, mode: PRIVATE_DIR_MODE });
  try {
    // Deliberately not recursive: the parent must already exist, keeping this to exactly one run-* dir under the workspace root.
    mkdirSync(manifest.workspacePath, { mode: PRIVATE_DIR_MODE });
  } catch (error) {
    if (!hasErrorCode(error, "EEXIST")) {
      throw error;
    }
    // A killed paused child strands its workspace, and cold resume can't retry (its answer/token are spent) —
    // reclaim only a leftover that's provably this uid's own dir, and only by clearing it, never writing into it.
    assertReclaimableWorkspace(manifest.workspacePath);
    rmSync(manifest.workspacePath, { recursive: true, force: true });
    mkdirSync(manifest.workspacePath, { mode: PRIVATE_DIR_MODE });
  }
  for (const [path, base64] of Object.entries(manifest.files)) {
    const [area, ...segments] = assertSafeRelativePath(path, target.reservedWorkspaceDirs);
    const rootDir = area === CONFIG_AREA ? target.configDir : manifest.workspacePath;
    const absolute = join(rootDir, ...segments);
    mkdirSync(dirname(absolute), { recursive: true, mode: PRIVATE_DIR_MODE });
    writeFileSync(absolute, Buffer.from(base64, "base64"), { mode: PRIVATE_FILE_MODE });
  }
}
