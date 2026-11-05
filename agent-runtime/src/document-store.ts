import { createReadStream } from "node:fs";
import { mkdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { join } from "node:path";
import type { Readable } from "node:stream";
import { mapWithConcurrency } from "./concurrency.js";
import { hasErrorCode } from "./errors.js";
import { SHA256_HEX_PATTERN } from "./hash.js";
import { createNarrower, type Narrower } from "./narrow.js";
import { claimPrivateRoot, PRIVATE_DIR_MODE, PRIVATE_FILE_MODE, publishAtomically } from "./private-fs.js";
import {
  copyDocumentMeta,
  DOCUMENTS_MANIFEST_NAME,
  isStagedInputName,
  NAME_GATE_BY_AREA,
  parseBoundedMetaField,
  STAGED_INPUT_AREAS,
  type StagedInput,
  type StagedInputArea,
} from "./run-documents.js";
import { assertSafePathSegment } from "./safe-path.js";

export class DocumentIntegrityError extends Error {}

export type StoredInputAsset = {
  name: string;
  sha256: string;
  sizeBytes: number;
  open(): Readable;
};

// A staged input readable as a stream, so the supervisor moves store bytes one chunk at
// a time and never materializes a document. The size check at open time catches a
// truncated or missing store file; byte-level integrity is the child's job — it hashes
// the streamed bytes against the manifest sha256 while staging.
export type StoredInput = {
  area: StagedInputArea;
  name: string;
  sha256: string;
  sizeBytes: number;
  mediaType?: string;
  documentClass?: string;
  description?: string;
  open(): Readable;
  assets?: StoredInputAsset[];
};

// Deleted at consumer complete or retention reap, never at terminal — the platform may
// still re-stage from here for a cold resume after a run has already settled.
export type DocumentStore = {
  save(executionId: string, inputs: StagedInput[]): Promise<void>;
  openInputs(executionId: string): Promise<StoredInput[] | undefined>;
  delete(executionId: string): Promise<void>;
};

const parse: Narrower = createNarrower((message) => new DocumentIntegrityError(message));

const BYTES_SUFFIX = ".doc";
const ASSET_BYTES_SUFFIX = ".asset";
const STORE_IO_CONCURRENCY = 6;

function assetBytesNameOf(index: number, assetIndex: number): string {
  return `${index}-${assetIndex}${ASSET_BYTES_SUFFIX}`;
}

type StoredAssetMeta = {
  name: string;
  sha256: string;
  sizeBytes: number;
};

type StoredDocumentMeta = {
  name: string;
  sha256: string;
  sizeBytes: number;
  area: StagedInputArea;
  mediaType?: string;
  documentClass?: string;
  description?: string;
  assets?: StoredAssetMeta[];
};

function asStagedInputArea(value: string, label: string): StagedInputArea {
  const area = STAGED_INPUT_AREAS.find((known) => known === value);
  if (area === undefined) {
    parse.fail(`${label}.area must be one of ${STAGED_INPUT_AREAS.join(", ")}`);
  }
  return area;
}

function parseStoredSha256(record: Record<string, unknown>, label: string): string {
  const sha256 = parse.requireString(record, "sha256", label).toLowerCase();
  if (!SHA256_HEX_PATTERN.test(sha256)) {
    parse.fail(`${label}.sha256 must be 64 hex characters`);
  }
  return sha256;
}

function parseStoredSizeBytes(record: Record<string, unknown>, label: string): number {
  const sizeBytes = parse.requireNonNegativeNumber(record, "sizeBytes", label);
  if (!Number.isSafeInteger(sizeBytes)) {
    parse.fail(`${label}.sizeBytes must be a whole number of bytes`);
  }
  return sizeBytes;
}

// Hashes live in the manifest, not per-doc sidecars like the checkpoint store uses:
// the whole staging dir lands in one rename, so bytes and hashes are already atomic.
function parseStoredManifest(value: unknown, runDir: string): StoredDocumentMeta[] {
  const items = parse.requireArray(value, `document manifest at ${runDir}`);
  return items.map((item, index) => {
    const label = `document manifest at ${runDir} entry ${index}`;
    const record = parse.asRecord(item, label);
    const mediaType = parseBoundedMetaField(record, "mediaType", parse, label);
    const documentClass = parseBoundedMetaField(record, "documentClass", parse, label);
    const description = parse.optionalString(record, "description", label);
    const area = asStagedInputArea(parse.requireString(record, "area", label), label);
    // Validated because it is joined into the staging area path on the way in.
    const name = parse.requireString(record, "name", label);
    if (!NAME_GATE_BY_AREA[area](name)) {
      parse.fail(`${label}.name is not a valid name for the ${area} area`);
    }
    const assets =
      record["assets"] === undefined ? undefined : parseStoredAssets(record["assets"], `${label}.assets`);
    return {
      name,
      sha256: parseStoredSha256(record, label),
      sizeBytes: parseStoredSizeBytes(record, label),
      area,
      ...copyDocumentMeta({ mediaType, documentClass, description }),
      ...(assets === undefined ? {} : { assets }),
    };
  });
}

// The names are joined into the child's assets staging dir on the way out, so they meet
// the same plain-name gate as the documents whose store they share.
function parseStoredAssets(value: unknown, label: string): StoredAssetMeta[] {
  return parse.requireArray(value, label).map((item, index) => {
    const assetLabel = `${label}[${index}]`;
    const record = parse.asRecord(item, assetLabel);
    const name = parse.requireString(record, "name", assetLabel);
    if (!isStagedInputName(name)) {
      parse.fail(`${assetLabel}.name is not an ordinary file name`);
    }
    return {
      name,
      sha256: parseStoredSha256(record, assetLabel),
      sizeBytes: parseStoredSizeBytes(record, assetLabel),
    };
  });
}

async function sizedSource(bytesPath: string, sizeBytes: number): Promise<{ open(): Readable }> {
  let measured: number;
  try {
    measured = (await stat(bytesPath)).size;
  } catch (error) {
    if (hasErrorCode(error, "ENOENT")) {
      throw new DocumentIntegrityError(`stored document at ${bytesPath} is missing`);
    }
    throw error;
  }
  if (measured !== sizeBytes) {
    throw new DocumentIntegrityError(
      `stored document at ${bytesPath} holds ${measured} bytes where its manifest declares ${sizeBytes}`,
    );
  }
  return { open: () => createReadStream(bytesPath) };
}

export function createLocalDocumentStore(root: string): DocumentStore {
  claimPrivateRoot(root, "document store");

  function runDirOf(executionId: string): string {
    assertSafePathSegment(executionId);
    return join(root, executionId);
  }

  return {
    async save(executionId, inputs) {
      const runDir = runDirOf(executionId);
      await publishAtomically(
        root,
        runDir,
        async (stagingDir) => {
          await mkdir(stagingDir, { mode: PRIVATE_DIR_MODE });
          const manifest = await mapWithConcurrency(
            inputs.map((input, index) => ({ input, index })),
            STORE_IO_CONCURRENCY,
            async ({ input, index }) => {
              await writeFile(join(stagingDir, `${index}${BYTES_SUFFIX}`), input.bytes, {
                mode: PRIVATE_FILE_MODE,
              });
              const assets = input.assets;
              if (assets !== undefined) {
                // Sequential on purpose: STORE_IO_CONCURRENCY already runs per-entry, and
                // fanning out per asset would multiply it.
                for (const [assetIndex, asset] of assets.entries()) {
                  await writeFile(join(stagingDir, assetBytesNameOf(index, assetIndex)), asset.bytes, {
                    mode: PRIVATE_FILE_MODE,
                  });
                }
              }
              return {
                name: input.name,
                sha256: input.sha256,
                sizeBytes: input.bytes.byteLength,
                area: input.area,
                ...copyDocumentMeta(input),
                ...(assets === undefined
                  ? {}
                  : {
                      assets: assets.map((asset) => ({
                        name: asset.name,
                        sha256: asset.sha256,
                        sizeBytes: asset.bytes.byteLength,
                      })),
                    }),
              };
            },
          );
          await writeFile(join(stagingDir, DOCUMENTS_MANIFEST_NAME), JSON.stringify(manifest), {
            mode: PRIVATE_FILE_MODE,
          });
        },
        { replace: true },
      );
    },

    async openInputs(executionId) {
      const runDir = runDirOf(executionId);
      let manifestBytes: Buffer;
      try {
        manifestBytes = await readFile(join(runDir, DOCUMENTS_MANIFEST_NAME));
      } catch (error) {
        if (hasErrorCode(error, "ENOENT")) {
          return undefined;
        }
        throw error;
      }
      let parsedManifest: unknown;
      try {
        parsedManifest = JSON.parse(manifestBytes.toString("utf8"));
      } catch {
        throw new DocumentIntegrityError(`document manifest at ${runDir} is not valid JSON`);
      }
      const manifest = parseStoredManifest(parsedManifest, runDir);
      return mapWithConcurrency(
        manifest.map((meta, index) => ({ meta, index })),
        STORE_IO_CONCURRENCY,
        async ({ meta, index }) => {
          const { assets: assetMetas, ...storedMeta } = meta;
          const source = await sizedSource(join(runDir, `${index}${BYTES_SUFFIX}`), meta.sizeBytes);
          if (assetMetas === undefined) {
            return { ...storedMeta, open: source.open };
          }
          const assets = await Promise.all(
            assetMetas.map(async (asset, assetIndex) => {
              const assetSource = await sizedSource(
                join(runDir, assetBytesNameOf(index, assetIndex)),
                asset.sizeBytes,
              );
              return { ...asset, open: assetSource.open };
            }),
          );
          return { ...storedMeta, open: source.open, assets };
        },
      );
    },

    async delete(executionId) {
      await rm(runDirOf(executionId), { recursive: true, force: true });
    },
  };
}
