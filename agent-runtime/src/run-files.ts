import { createHash, type Hash, randomUUID } from "node:crypto";
import type { Stats } from "node:fs";
import {
  chmodSync,
  closeSync,
  constants,
  copyFileSync,
  lstatSync,
  mkdirSync,
  openSync,
  readdirSync,
  renameSync,
  rmSync,
  writeFileSync,
  writeSync,
} from "node:fs";
import type { FileHandle } from "node:fs/promises";
import { open, readdir } from "node:fs/promises";
import { dirname, extname, join } from "node:path";
import { type ChargeBudget, createChargeBudget } from "./byte-budget.js";
import { mapWithConcurrency } from "./concurrency.js";
import { MAX_ARTIFACTS_MANIFEST_BYTES, MAX_PUBLISHED_OUT_BYTES, MAX_PUBLISHED_OUT_FILES } from "./config.js";
import { hasErrorCode } from "./errors.js";
import {
  closeSizedFile,
  openedPathMatches,
  openSizedFile,
  openSizedFileOrUndefined,
  readSizedFile,
  readSizedRange,
  type SizedFile,
} from "./fs-read.js";
import { type WalkedFile, walkRegularFiles } from "./fs-walk.js";
import { sha256Hex } from "./hash.js";
import { measureHtmlInertJson } from "./html-inert-json.js";
import { createNarrower, type Narrower } from "./narrow.js";
import { claimCreatedDirs } from "./private-fs.js";
import { areSafePathSegments, assertSafePathSegment } from "./safe-path.js";
import type { RemoveTreeFn } from "./setpriv.js";
import { TRACE_FILE_NAME, TRACE_SEQ_FILE_NAME, tracePackDirOf } from "./trace.js";
import { FINAL_RESPONSE_FILE_NAME, WORKSPACE_OUT_DIR } from "./workspace-layout.js";

export const ARTIFACTS_MANIFEST_NAME = "artifacts-manifest.json";

const RUNTIME_OWNED_ATTEMPT_FILES: readonly string[] = [ARTIFACTS_MANIFEST_NAME, FINAL_RESPONSE_FILE_NAME];

const PUBLISH_STAGING_PREFIX = ".publish-";

function publishedOutOversizedError(maxBytes: number): Error {
  return new Error(`published out/ exceeds the ${maxBytes}-byte cap`);
}

export class RetainedCapExceededError extends Error {}

function retainedCapExceededError(maxBytes: number): Error {
  return new RetainedCapExceededError(
    `published out/ exceeds the run's declared ${maxBytes}-byte retained-output cap`,
  );
}

function publishedOutTooManyFilesError(maxFiles: number): Error {
  return new Error(`published out/ exceeds the ${maxFiles}-file cap`);
}

function artifactsManifestOversizedError(maxBytes: number): Error {
  return new Error(`published artifacts manifest exceeds the ${maxBytes}-byte cap`);
}

export const SUPERVISOR_READABLE_DIR_MODE = 0o755;
export const SUPERVISOR_READABLE_FILE_MODE = 0o644;

export function artifactAttemptDirOf(artifactsRoot: string, executionId: string, attempt: number): string {
  return join(artifactsRoot, executionId, `attempt-${attempt}`);
}

function artifactsManifestPathOf(attemptDir: string): string {
  return join(attemptDir, ARTIFACTS_MANIFEST_NAME);
}

type TreeModes = { dirMode: number; fileMode: number };

type CopiedFile = { relativePath: string; sizeBytes: number; sha256: string };

const COPY_CHUNK_BYTES = 1024 * 1024;

function copyFileHashed(
  source: SizedFile,
  targetPath: string,
  fileMode: number,
): { sizeBytes: number; sha256: string } {
  const hash = createHash("sha256");
  const target = openSync(targetPath, "w", fileMode);
  try {
    let sizeBytes = 0;
    while (sizeBytes < source.sizeBytes) {
      const chunk = readSizedRange(
        source.fd,
        sizeBytes,
        Math.min(COPY_CHUNK_BYTES, source.sizeBytes - sizeBytes),
      );
      if (chunk.byteLength === 0) {
        break;
      }
      hash.update(chunk);
      writeSyncAll(target, chunk, chunk.byteLength);
      sizeBytes += chunk.byteLength;
    }
    return { sizeBytes, sha256: hash.digest("hex") };
  } finally {
    closeSync(target);
  }
}

function writeSyncAll(fd: number, chunk: Buffer, length: number): void {
  let written = 0;
  while (written < length) {
    written += writeSync(fd, chunk, written, length - written);
  }
}

type DirEnsurer = (dir: string) => void;

function createDirEnsurer(targetDir: string, dirMode: number): DirEnsurer {
  const createdDirs = new Set<string>();
  const ensureDir = (dir: string): void => {
    if (createdDirs.has(dir)) {
      return;
    }
    if (dir !== targetDir) {
      ensureDir(dirname(dir));
    }
    mkdirSync(dir, { recursive: true });
    chmodSync(dir, dirMode);
    createdDirs.add(dir);
  };
  return ensureDir;
}

type PublishCaps = { bytes: ChargeBudget; files: ChargeBudget };

function copyRegularTree(
  sourceDir: string,
  targetDir: string,
  modes: TreeModes,
  caps?: PublishCaps,
): CopiedFile[] {
  const copied: CopiedFile[] = [];
  const ensureDir = createDirEnsurer(targetDir, modes.dirMode);
  ensureDir(targetDir);
  walkRegularFiles(sourceDir, (file) => {
    caps?.files.charge(1);
    const source = openSizedFile(file.absolutePath);
    try {
      // Charged before copying, so an over-cap tree fails before its bytes
      // land in the retained area.
      caps?.bytes.charge(source.sizeBytes);
      const target = join(targetDir, file.relativePath);
      ensureDir(dirname(target));
      const { sizeBytes, sha256 } = copyFileHashed(source, target, modes.fileMode);
      chmodSync(target, modes.fileMode);
      copied.push({ relativePath: file.relativePath, sizeBytes, sha256 });
    } finally {
      closeSizedFile(source);
    }
  });
  return copied;
}

// Restage is a copy only — hashing here would be paid on every cold resume and thrown away.
function mirrorRegularTree(sourceDir: string, targetDir: string, modes: TreeModes): void {
  const ensureDir = createDirEnsurer(targetDir, modes.dirMode);
  ensureDir(targetDir);
  walkRegularFiles(sourceDir, (file) => {
    const target = join(targetDir, file.relativePath);
    ensureDir(dirname(target));
    copyFileSync(file.absolutePath, target);
    chmodSync(target, modes.fileMode);
  });
}

function statDirectoryOrUndefined(path: string): ReturnType<typeof lstatSync> | undefined {
  try {
    const stats = lstatSync(path);
    return stats.isDirectory() ? stats : undefined;
  } catch (error) {
    if (hasErrorCode(error, "ENOENT")) {
      return undefined;
    }
    throw error;
  }
}

export function publishRunArtifacts(input: {
  artifactsRoot: string;
  executionId: string;
  attempt: number;
  workspacePath: string;
  maxTotalBytes?: number;
  maxTotalFiles?: number;
  maxManifestBytes?: number;
  declaredCapBytes?: number;
}): void {
  const serviceCapBytes = input.maxTotalBytes ?? MAX_PUBLISHED_OUT_BYTES;
  // The declared cap covers artifacts plus trace, so out/ alone already exceeding it is refused
  // here rather than waiting for the trace-inclusive measurement to catch the same overshoot.
  const declaredCapBytes = input.declaredCapBytes;
  const declaredCapBinds = declaredCapBytes !== undefined && declaredCapBytes <= serviceCapBytes;
  const maxTotalBytes = declaredCapBinds ? declaredCapBytes : serviceCapBytes;
  const oversizedError = declaredCapBinds ? retainedCapExceededError : publishedOutOversizedError;
  const maxTotalFiles = input.maxTotalFiles ?? MAX_PUBLISHED_OUT_FILES;
  const maxManifestBytes = input.maxManifestBytes ?? MAX_ARTIFACTS_MANIFEST_BYTES;
  const outDir = join(input.workspacePath, WORKSPACE_OUT_DIR);
  const attemptDir = artifactAttemptDirOf(input.artifactsRoot, input.executionId, input.attempt);
  const publishedOutDir = join(attemptDir, WORKSPACE_OUT_DIR);
  const manifestPath = artifactsManifestPathOf(attemptDir);
  if (statDirectoryOrUndefined(outDir) === undefined) {
    discardPublishedArtifacts(input);
    return;
  }
  claimCreatedDirs(attemptDir, SUPERVISOR_READABLE_DIR_MODE);
  sweepPublishStaging(attemptDir);
  const stagingDir = join(attemptDir, `${PUBLISH_STAGING_PREFIX}${randomUUID()}`);
  try {
    mkdirSync(stagingDir, { mode: SUPERVISOR_READABLE_DIR_MODE });
    chmodSync(stagingDir, SUPERVISOR_READABLE_DIR_MODE);
    const stagedOutDir = join(stagingDir, WORKSPACE_OUT_DIR);
    const stagedManifestPath = artifactsManifestPathOf(stagingDir);
    const copied = copyRegularTree(
      outDir,
      stagedOutDir,
      { dirMode: SUPERVISOR_READABLE_DIR_MODE, fileMode: SUPERVISOR_READABLE_FILE_MODE },
      {
        bytes: createChargeBudget(maxTotalBytes, oversizedError),
        files: createChargeBudget(maxTotalFiles, publishedOutTooManyFilesError),
      },
    );
    const manifest = copied.map((file) => ({
      path: `${WORKSPACE_OUT_DIR}/${file.relativePath}`,
      sizeBytes: file.sizeBytes,
      sha256: file.sha256,
    }));
    // Measured on the escaped rendering, not the raw JSON: `<`, `>`, `&` each
    // become a six-byte escape, so a raw-bytes cap would sanction six times over.
    const { json: manifestJson, renderedBytes } = measureHtmlInertJson(manifest);
    if (renderedBytes > maxManifestBytes) {
      throw artifactsManifestOversizedError(maxManifestBytes);
    }
    writeFileSync(stagedManifestPath, manifestJson, { mode: SUPERVISOR_READABLE_FILE_MODE });
    chmodSync(stagedManifestPath, SUPERVISOR_READABLE_FILE_MODE);
    // Old mirror removed only once the replacement is staged; manifest renamed
    // last so the listing is never stale between the two renames.
    rmSync(manifestPath, { force: true });
    rmSync(publishedOutDir, { recursive: true, force: true });
    renameSync(stagedOutDir, publishedOutDir);
    renameSync(stagedManifestPath, manifestPath);
  } finally {
    rmSync(stagingDir, { recursive: true, force: true });
  }
}

// Manifest removed first, so a listing never names files the empty publish already dropped.
export function discardPublishedArtifacts(input: {
  artifactsRoot: string;
  executionId: string;
  attempt: number;
}): void {
  const attemptDir = artifactAttemptDirOf(input.artifactsRoot, input.executionId, input.attempt);
  rmSync(artifactsManifestPathOf(attemptDir), { force: true });
  rmSync(join(attemptDir, WORKSPACE_OUT_DIR), { recursive: true, force: true });
}

function sweepPublishStaging(attemptDir: string): void {
  for (const entry of readdirSync(attemptDir)) {
    if (entry.startsWith(PUBLISH_STAGING_PREFIX)) {
      rmSync(join(attemptDir, entry), { recursive: true, force: true });
    }
  }
}

const RESTAGED_DIR_MODE = 0o700;
const RESTAGED_FILE_MODE = 0o600;

// out/ never rides the checkpoint bundle — restaged from the published mirror on cold resume.
export function restagePublishedOut(input: {
  artifactsRoot: string;
  executionId: string;
  attempt: number;
  workspacePath: string;
}): void {
  const publishedOutDir = join(
    artifactAttemptDirOf(input.artifactsRoot, input.executionId, input.attempt),
    WORKSPACE_OUT_DIR,
  );
  if (statDirectoryOrUndefined(publishedOutDir) === undefined) {
    return;
  }
  mirrorRegularTree(publishedOutDir, join(input.workspacePath, WORKSPACE_OUT_DIR), {
    dirMode: RESTAGED_DIR_MODE,
    fileMode: RESTAGED_FILE_MODE,
  });
}

export type RunFileKind = "artifact" | "trace";

export type RunFileEntry = {
  path: string;
  sizeBytes: number;
  sha256: string;
  kind: RunFileKind;
};

// resolve() hands back the opened descriptor, never a path to re-open: the containment
// verdict and the served bytes are one open's view, so a component swapped between the
// two cannot redirect the read. The caller owns closing `file`.
export type ResolvedRunFile = {
  file: SizedFile;
  contentType: string;
};

export type RetainedByteTotals = {
  artifactBytes: number;
  traceBytes: number;
};

export type RunFilesView = {
  list(executionId: string, attempt: number): Promise<RunFileEntry[]>;
  retainedBytes(executionId: string, attempt: number): Promise<RetainedByteTotals>;
  resolve(executionId: string, attempt: number, path: string): Promise<ResolvedRunFile | undefined>;
  purge(executionId: string): Promise<void>;
  purgeArtifacts(executionId: string): Promise<void>;
};

export const DEFAULT_CONTENT_TYPE = "application/octet-stream";

const CONTENT_TYPES_BY_EXTENSION: Record<string, string> = {
  ".css": "text/css",
  ".csv": "text/csv",
  ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  ".gif": "image/gif",
  ".html": "text/html",
  ".jpeg": "image/jpeg",
  ".jpg": "image/jpeg",
  ".json": "application/json",
  ".jsonl": "application/x-ndjson",
  ".md": "text/markdown",
  ".ndjson": "application/x-ndjson",
  ".pdf": "application/pdf",
  ".png": "image/png",
  ".pptx": "application/vnd.openxmlformats-officedocument.presentationml.presentation",
  ".svg": "image/svg+xml",
  ".txt": "text/plain",
  ".xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
};

export function contentTypeOf(path: string): string {
  return CONTENT_TYPES_BY_EXTENSION[extname(path).toLowerCase()] ?? DEFAULT_CONTENT_TYPE;
}

const TRACE_PATH_PREFIX = "trace";
const FILE_OPEN_CONCURRENCY = 6;

const parseManifest: Narrower = createNarrower(
  (message) => new Error(`published artifacts manifest is invalid: ${message}`),
);

type ManifestEntry = { path: string; sizeBytes: number; sha256: string };

function parseArtifactsManifest(raw: string): ManifestEntry[] {
  const items = parseManifest.requireArray(JSON.parse(raw) as unknown, "manifest");
  return items.map((item, index) => {
    const record = parseManifest.asRecord(item, `manifest[${index}]`);
    const path = parseManifest.requireString(record, "path", `manifest[${index}]`);
    if (!path.startsWith(`${WORKSPACE_OUT_DIR}/`)) {
      parseManifest.fail(`manifest[${index}].path must sit under ${WORKSPACE_OUT_DIR}/`);
    }
    return {
      path,
      sizeBytes: parseManifest.requireNumber(record, "sizeBytes", `manifest[${index}]`),
      sha256: parseManifest.requireString(record, "sha256", `manifest[${index}]`),
    };
  });
}

// Containment is by construction: the segments are validated (no "..", no separators), so
// the joined candidate sits under rootDir lexically, and openSizedFile's descriptor-identity
// check proves no component of it resolved through a symlink.
function resolveWithinRoot(rootDir: string, segments: readonly string[]): ResolvedRunFile | undefined {
  const candidate = join(rootDir, ...segments);
  const file = openSizedFileOrUndefined(candidate);
  return file === undefined ? undefined : { file, contentType: contentTypeOf(candidate) };
}

type HashCacheEntry = { sizeBytes: number; mtimeMs: number; sha256: string };

async function purgeRunDir(runDir: string, removeTree: RemoveTreeFn): Promise<void> {
  let attempts: string[];
  try {
    attempts = await readdir(runDir);
  } catch (error) {
    if (hasErrorCode(error, "ENOENT", "ENOTDIR")) {
      return;
    }
    throw error;
  }
  for (const attempt of attempts.sort()) {
    await removeTree(join(runDir, attempt));
  }
  await removeTree(runDir);
}

export function createRunFilesView(input: {
  artifactsRoot: string;
  traceRoot: string;
  removeTree: RemoveTreeFn;
}): RunFilesView {
  const { removeTree } = input;
  const traceHashCache = new Map<string, HashCacheEntry>();
  // trace.ndjson is append-only — the running hash context is resumed only on
  // growth, never on rewrite.
  const appendHashStates = new Map<string, { bytesHashed: number; context: Hash }>();

  async function hashAppendOnlyFile(
    absolutePath: string,
    handle: FileHandle,
    sizeBytes: number,
  ): Promise<string> {
    // Removed from the map while in use so two concurrent listings never share one Hash context.
    const state = appendHashStates.get(absolutePath);
    appendHashStates.delete(absolutePath);
    const resumable = state !== undefined && state.bytesHashed < sizeBytes;
    const context = resumable ? state.context : createHash("sha256");
    const startOffset = resumable ? state.bytesHashed : 0;
    const tailBytes = sizeBytes - startOffset;
    if (tailBytes > 0) {
      const tail = Buffer.alloc(tailBytes);
      const { bytesRead } = await handle.read(tail, 0, tailBytes, startOffset);
      context.update(tail.subarray(0, bytesRead));
    }
    const sha256 = context.copy().digest("hex");
    appendHashStates.set(absolutePath, { bytesHashed: sizeBytes, context });
    return sha256;
  }

  async function readAtSize(handle: FileHandle, sizeBytes: number): Promise<Buffer> {
    const bytes = Buffer.alloc(sizeBytes);
    const { bytesRead } = await handle.read(bytes, 0, sizeBytes, 0);
    return bytesRead === sizeBytes ? bytes : bytes.subarray(0, bytesRead);
  }

  async function hashedTraceEntry(file: WalkedFile, handle: FileHandle, stats: Stats): Promise<RunFileEntry> {
    const cached = traceHashCache.get(file.absolutePath);
    if (cached !== undefined && cached.sizeBytes === stats.size && cached.mtimeMs === stats.mtimeMs) {
      return {
        path: `${TRACE_PATH_PREFIX}/${file.relativePath}`,
        sizeBytes: cached.sizeBytes,
        sha256: cached.sha256,
        kind: "trace",
      };
    }
    const sha256 =
      file.relativePath === TRACE_FILE_NAME
        ? await hashAppendOnlyFile(file.absolutePath, handle, stats.size)
        : sha256Hex(await readAtSize(handle, stats.size));
    traceHashCache.set(file.absolutePath, { sizeBytes: stats.size, mtimeMs: stats.mtimeMs, sha256 });
    return {
      path: `${TRACE_PATH_PREFIX}/${file.relativePath}`,
      sizeBytes: stats.size,
      sha256,
      kind: "trace",
    };
  }

  async function listArtifacts(attemptDir: string): Promise<RunFileEntry[]> {
    const manifest = openSizedFileOrUndefined(artifactsManifestPathOf(attemptDir));
    if (manifest === undefined) {
      return [];
    }
    let raw: string;
    try {
      if (manifest.sizeBytes > MAX_ARTIFACTS_MANIFEST_BYTES) {
        throw artifactsManifestOversizedError(MAX_ARTIFACTS_MANIFEST_BYTES);
      }
      raw = readSizedFile(manifest).toString("utf8");
    } finally {
      closeSizedFile(manifest);
    }
    return parseArtifactsManifest(raw).map((entry) => ({ ...entry, kind: "artifact" }));
  }

  async function mapOpenRegularFiles<T>(
    rootDir: string,
    onOpen: (file: WalkedFile, handle: FileHandle, stats: Stats) => Promise<T>,
    isIncluded: (file: WalkedFile) => boolean = () => true,
  ): Promise<T[]> {
    const files: WalkedFile[] = [];
    walkRegularFiles(rootDir, (file) => files.push(file), { missingRootOk: true });
    const mapped = await mapWithConcurrency(files.filter(isIncluded), FILE_OPEN_CONCURRENCY, async (file) => {
      let handle: FileHandle;
      try {
        handle = await open(
          file.absolutePath,
          constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK,
        );
      } catch (error) {
        if (hasErrorCode(error, "ENOENT", "EISDIR", "ELOOP")) {
          return undefined;
        }
        throw error;
      }
      try {
        const stats = await handle.stat();
        return stats.isFile() && openedPathMatches(handle.fd, file.absolutePath)
          ? await onOpen(file, handle, stats)
          : undefined;
      } finally {
        await handle.close();
      }
    });
    return mapped.filter((value): value is Awaited<T> => value !== undefined);
  }

  const isTracePackFile = (file: WalkedFile): boolean => file.relativePath !== TRACE_SEQ_FILE_NAME;
  // Neither is a deliverable: the manifest is what the listing is built from, and the
  // persisted final response is the result's second channel. Both are the runtime's own
  // files, so neither counts against the caller's retained-output cap.
  const isRetainedArtifactFile = (file: WalkedFile): boolean =>
    !RUNTIME_OWNED_ATTEMPT_FILES.includes(file.relativePath);

  function listTraces(packDir: string): Promise<RunFileEntry[]> {
    return mapOpenRegularFiles(packDir, hashedTraceEntry, isTracePackFile);
  }

  async function residentBytes(rootDir: string, isIncluded?: (file: WalkedFile) => boolean): Promise<number> {
    const sizes = await mapOpenRegularFiles(
      rootDir,
      (_file, _handle, stats) => Promise.resolve(stats.size),
      isIncluded,
    );
    return sizes.reduce((total, size) => total + size, 0);
  }

  function evictCachedHashes(executionId: string): void {
    const prefix = `${join(input.traceRoot, executionId)}/`;
    for (const key of traceHashCache.keys()) {
      if (key.startsWith(prefix)) {
        traceHashCache.delete(key);
      }
    }
    for (const key of appendHashStates.keys()) {
      if (key.startsWith(prefix)) {
        appendHashStates.delete(key);
      }
    }
  }

  return {
    async list(executionId, attempt) {
      assertSafePathSegment(executionId);
      const [artifacts, traces] = await Promise.all([
        listArtifacts(artifactAttemptDirOf(input.artifactsRoot, executionId, attempt)),
        listTraces(tracePackDirOf(input.traceRoot, executionId, attempt)),
      ]);
      return [...artifacts, ...traces].sort((a, b) => (a.path < b.path ? -1 : 1));
    },

    // Measured from disk, never from the manifest: a kill anywhere in the publish
    // sequence leaves bytes resident that no manifest declares. The whole attempt dir is
    // counted, stranded .publish- staging included, except the manifest.
    async retainedBytes(executionId, attempt) {
      assertSafePathSegment(executionId);
      const attemptDir = artifactAttemptDirOf(input.artifactsRoot, executionId, attempt);
      const [artifactBytes, tracePackBytes] = await Promise.all([
        residentBytes(attemptDir, isRetainedArtifactFile),
        residentBytes(tracePackDirOf(input.traceRoot, executionId, attempt), isTracePackFile),
      ]);
      return { artifactBytes, traceBytes: tracePackBytes };
    },

    // out/ fetches answer only manifest-enumerated paths, so serving and the listing agree
    // on what a run's artifacts are; stranded bytes without a manifest stay charged but are
    // never served.
    async resolve(executionId, attempt, path) {
      assertSafePathSegment(executionId);
      const [area, ...rest] = path.split("/");
      if (!areSafePathSegments(rest)) {
        return undefined;
      }
      if (area === WORKSPACE_OUT_DIR) {
        const attemptDir = artifactAttemptDirOf(input.artifactsRoot, executionId, attempt);
        const enumerated = await listArtifacts(attemptDir);
        if (!enumerated.some((entry) => entry.path === path)) {
          return undefined;
        }
        return resolveWithinRoot(join(attemptDir, WORKSPACE_OUT_DIR), rest);
      }
      if (area === TRACE_PATH_PREFIX && path !== `${TRACE_PATH_PREFIX}/${TRACE_SEQ_FILE_NAME}`) {
        const packDir = tracePackDirOf(input.traceRoot, executionId, attempt);
        return resolveWithinRoot(packDir, rest);
      }
      return undefined;
    },

    async purge(executionId) {
      assertSafePathSegment(executionId);
      evictCachedHashes(executionId);
      await Promise.all([
        purgeRunDir(join(input.artifactsRoot, executionId), removeTree),
        purgeRunDir(join(input.traceRoot, executionId), removeTree),
      ]);
    },

    async purgeArtifacts(executionId) {
      assertSafePathSegment(executionId);
      await purgeRunDir(join(input.artifactsRoot, executionId), removeTree);
    },
  };
}
