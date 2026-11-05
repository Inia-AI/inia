import { closeSync, mkdirSync, openSync, writeFileSync, writeSync } from "node:fs";
import { dirname, join } from "node:path";
import { type ChargeBudget, createChargeBudget } from "./byte-budget.js";
import { yieldToEventLoop } from "./concurrency.js";
import {
  MAX_DOCUMENT_META_FIELD_CHARS,
  MAX_DOCUMENTS_PER_RUN,
  MAX_RECARRIED_OUT_FILES,
  MAX_RECARRIED_OUT_PATH_CHARS,
  MAX_SKILL_ASSETS_PER_SKILL,
  MAX_SKILL_DESCRIPTION_CHARS,
} from "./config.js";
import type { CargoReceiver } from "./document-channel.js";
import { createSha256Stream, SHA256_HEX_PATTERN } from "./hash.js";
import type { Narrower } from "./narrow.js";
import { PRIVATE_DIR_MODE, PRIVATE_FILE_MODE } from "./private-fs.js";
import { areSafePathSegments, isSafePathSegment } from "./safe-path.js";
import { assertUniqueSkillIdentities, type StagedSkillFile } from "./skill-plugin.js";
import { WORKSPACE_DOCUMENTS_DIR, WORKSPACE_OUT_DIR } from "./workspace-layout.js";

export class RunDocumentBudgetError extends Error {}

export type SkillAssetPayload = {
  name: string;
  contentBase64: string;
  sha256?: string;
};

export type RunDocumentPayload = {
  name: string;
  contentBase64: string;
  sha256?: string;
  mediaType?: string;
  documentClass?: string;
  description?: string;
  assets?: SkillAssetPayload[];
};

type DocumentMeta = {
  mediaType?: string;
  documentClass?: string;
  description?: string;
};

export function copyDocumentMeta(source: {
  mediaType?: string | undefined;
  documentClass?: string | undefined;
  description?: string | undefined;
}): DocumentMeta {
  return {
    ...(source.mediaType === undefined ? {} : { mediaType: source.mediaType }),
    ...(source.documentClass === undefined ? {} : { documentClass: source.documentClass }),
    ...(source.description === undefined ? {} : { description: source.description }),
  };
}

export type SkillFilePayload = Pick<
  RunDocumentPayload,
  "name" | "contentBase64" | "sha256" | "description" | "assets"
>;

export type RunAsset = {
  name: string;
  bytes: Uint8Array;
  sha256: string;
};

export type RunDocument = {
  name: string;
  bytes: Uint8Array;
  sha256: string;
  mediaType?: string;
  documentClass?: string;
  description?: string;
  assets?: RunAsset[];
};

export const DOCUMENTS_AREA = "documents";
export const SKILLS_AREA = "skills";
// A continuation's re-carried out/ files. Unlike the other areas, names are out/-relative
// paths, so every parse boundary validates them per segment instead of as one name.
export const RECARRIED_OUT_AREA = "recarried_out";
export type StagedInputArea = typeof DOCUMENTS_AREA | typeof SKILLS_AREA | typeof RECARRIED_OUT_AREA;
export const STAGED_INPUT_AREAS: readonly StagedInputArea[] = [
  DOCUMENTS_AREA,
  SKILLS_AREA,
  RECARRIED_OUT_AREA,
];

export type StagedInput = RunDocument & { area: StagedInputArea };

export const DOCUMENT_NAME_PATTERN = /^[A-Za-z0-9._ -]{1,200}$/;
export const DOCUMENTS_MANIFEST_NAME = "manifest.json";

const RUN_DOCUMENT_FIELDS: readonly string[] = [
  "name",
  "contentBase64",
  "mediaType",
  "documentClass",
  "sha256",
];

const SKILL_FILE_FIELDS: readonly string[] = ["name", "contentBase64", "sha256", "description", "assets"];

const SKILL_ASSET_FIELDS: readonly string[] = ["name", "contentBase64", "sha256"];

const RESERVED_DOCUMENT_NAMES: readonly string[] = [".", "..", DOCUMENTS_MANIFEST_NAME];

// Rejects the area's own manifest name too, since a document named the same is joined
// into the same area dir and would otherwise collide with it.
export function isStagedInputName(name: string): boolean {
  return (
    isSafePathSegment(name) && DOCUMENT_NAME_PATTERN.test(name) && !RESERVED_DOCUMENT_NAMES.includes(name)
  );
}

// A re-carried path is relative to out/ and may be nested; every segment passes the same
// gate the other staged areas use, and the char bound is what the frame-cap proof stands on.
export function isRecarriedOutPath(path: string): boolean {
  return path.length <= MAX_RECARRIED_OUT_PATH_CHARS && areSafePathSegments(path.split("/"));
}

export const NAME_GATE_BY_AREA: Record<StagedInputArea, (name: string) => boolean> = {
  [DOCUMENTS_AREA]: isStagedInputName,
  [SKILLS_AREA]: isStagedInputName,
  [RECARRIED_OUT_AREA]: isRecarriedOutPath,
};
// No quantified groups: they exhaust the engine's backtracking budget past a few MB and
// report no match, indistinguishable from invalid content.
const BASE64_ALPHABET_PATTERN = /^[A-Za-z0-9+/]*={0,2}$/;
const BASE64_INTERIOR_CHUNK_PATTERN = /^[A-Za-z0-9+/]*$/;
const BASE64_GROUP_LENGTH = 4;
const BYTES_PER_BASE64_GROUP = 3;
// Measured: ~4-7ms of validate/decode/hash per 2 MiB of base64 on the dev container,
// against 455-487ms for a single unchunked block; larger chunks scale linearly, smaller
// ones only add yield overhead.
export const BASE64_DECODE_CHUNK_CHARS = 2 * 1024 * 1024;

// Chunked on base64's four-character quantum so the walk accepts exactly what one
// whole-string BASE64_ALPHABET_PATTERN pass accepts: interior chunks are bare alphabet,
// only the final chunk may end in padding.
async function isCanonicalBase64(value: string): Promise<boolean> {
  if (value.length % BASE64_GROUP_LENGTH !== 0) {
    return false;
  }
  for (let offset = 0; offset < value.length; offset += BASE64_DECODE_CHUNK_CHARS) {
    if (offset > 0) {
      await yieldToEventLoop();
    }
    const chunk = value.slice(offset, offset + BASE64_DECODE_CHUNK_CHARS);
    const isFinalChunk = offset + BASE64_DECODE_CHUNK_CHARS >= value.length;
    if (!(isFinalChunk ? BASE64_ALPHABET_PATTERN : BASE64_INTERIOR_CHUNK_PATTERN).test(chunk)) {
      return false;
    }
  }
  return true;
}

// allocUnsafe because zero-filling a budget-sized buffer is itself a hundreds-of-MB
// synchronous block; every byte is overwritten below or the length assertion throws
// before the buffer escapes.
async function decodeBase64InChunks(
  contentBase64: string,
  decodedLength: number,
): Promise<{ bytes: Buffer; sha256: string }> {
  const bytes = Buffer.allocUnsafe(decodedLength);
  const hash = createSha256Stream();
  let bytesWritten = 0;
  for (let offset = 0; offset < contentBase64.length; offset += BASE64_DECODE_CHUNK_CHARS) {
    if (offset > 0) {
      await yieldToEventLoop();
    }
    const written = bytes.write(
      contentBase64.slice(offset, offset + BASE64_DECODE_CHUNK_CHARS),
      bytesWritten,
      "base64",
    );
    hash.update(bytes.subarray(bytesWritten, bytesWritten + written));
    bytesWritten += written;
  }
  if (bytesWritten !== decodedLength) {
    throw new Error(`decoded ${bytesWritten} bytes where canonical base64 projected ${decodedLength}`);
  }
  return { bytes, sha256: hash.hexDigest() };
}

export function parseBoundedMetaField(
  record: Record<string, unknown>,
  key: "mediaType" | "documentClass",
  parse: Narrower,
  label: string,
): string | undefined {
  const value = parse.optionalString(record, key, label);
  if (value !== undefined && value.length > MAX_DOCUMENT_META_FIELD_CHARS) {
    parse.fail(`${label}.${key} must not exceed ${MAX_DOCUMENT_META_FIELD_CHARS} characters`);
  }
  return value;
}

function parseDeclaredSha256(
  record: Record<string, unknown>,
  parse: Narrower,
  label: string,
): string | undefined {
  const declared = parse.optionalString(record, "sha256", label);
  if (declared === undefined) {
    return undefined;
  }
  const normalized = declared.toLowerCase();
  if (!SHA256_HEX_PATTERN.test(normalized)) {
    parse.fail(`${label}.sha256 must be 64 hex characters`);
  }
  return normalized;
}

type ParsePayloadOptions = { requireSha256?: boolean };

// nameKey names the wire field AND the word in the refusal messages; the parsed value
// always lands on the internal `name` property, whatever the wire called it.
type PayloadFieldSpec = {
  field: string;
  allowedFields: readonly string[];
  kind: "document" | "skill" | "recarried_out";
  maxEntries: number;
  nameKey: "name" | "path";
  nameGate: (candidate: string) => boolean;
  nameRule: string;
};

const ORDINARY_NAME_RULE = `an ordinary file name matching ${DOCUMENT_NAME_PATTERN.source}`;
const RECARRIED_OUT_NAME_RULE = `a relative path of safe segments, at most ${MAX_RECARRIED_OUT_PATH_CHARS} characters`;

const DOCUMENT_PAYLOAD_SPEC: PayloadFieldSpec = {
  field: "documents",
  allowedFields: RUN_DOCUMENT_FIELDS,
  kind: "document",
  maxEntries: MAX_DOCUMENTS_PER_RUN,
  nameKey: "name",
  nameGate: isStagedInputName,
  nameRule: ORDINARY_NAME_RULE,
};

const SKILL_PAYLOAD_SPEC: PayloadFieldSpec = {
  field: "skillFiles",
  allowedFields: SKILL_FILE_FIELDS,
  kind: "skill",
  maxEntries: MAX_DOCUMENTS_PER_RUN,
  nameKey: "name",
  nameGate: isStagedInputName,
  nameRule: ORDINARY_NAME_RULE,
};

const RECARRIED_OUT_PAYLOAD_FIELDS: readonly string[] = ["path", "contentBase64", "sha256"];

const RECARRIED_OUT_PAYLOAD_SPEC: PayloadFieldSpec = {
  field: "continuation.outFiles",
  allowedFields: RECARRIED_OUT_PAYLOAD_FIELDS,
  kind: "recarried_out",
  maxEntries: MAX_RECARRIED_OUT_FILES,
  nameKey: "path",
  nameGate: isRecarriedOutPath,
  nameRule: RECARRIED_OUT_NAME_RULE,
};

function parseSkillDescription(
  record: Record<string, unknown>,
  parse: Narrower,
  label: string,
): string | undefined {
  const description = parse.optionalString(record, "description", label);
  if (description !== undefined && description.length > MAX_SKILL_DESCRIPTION_CHARS) {
    parse.fail(`${label}.description must not exceed ${MAX_SKILL_DESCRIPTION_CHARS} characters`);
  }
  return description;
}

function parseSkillAssets(
  record: Record<string, unknown>,
  parse: Narrower,
  label: string,
  options: ParsePayloadOptions,
): SkillAssetPayload[] | undefined {
  const value = record["assets"];
  if (value === undefined) {
    return undefined;
  }
  const items = parse.requireArray(value, `${label}.assets`);
  // Empty means absent: [] and omission behave identically everywhere downstream, so they
  // must fingerprint identically too — a caller normalizing one into the other across a
  // retry must never earn a payload_conflict for it.
  if (items.length === 0) {
    return undefined;
  }
  if (items.length > MAX_SKILL_ASSETS_PER_SKILL) {
    parse.fail(`${label}.assets must not carry more than ${MAX_SKILL_ASSETS_PER_SKILL} entries`);
  }
  const seenNames = new Set<string>();
  return items.map((item, index) => {
    const assetLabel = `${label}.assets[${index}]`;
    const assetRecord = parse.asRecord(item, assetLabel);
    parse.rejectUnknownFields(assetRecord, SKILL_ASSET_FIELDS, assetLabel);
    const name = parse.requireString(assetRecord, "name", assetLabel);
    if (!isStagedInputName(name)) {
      parse.fail(`${assetLabel}.name must be an ordinary file name matching ${DOCUMENT_NAME_PATTERN.source}`);
    }
    if (seenNames.has(name)) {
      parse.fail(`${label}.assets must not repeat the name "${name}"`);
    }
    seenNames.add(name);
    const contentBase64 = parse.requireString(assetRecord, "contentBase64", assetLabel);
    const sha256 = parseDeclaredSha256(assetRecord, parse, assetLabel);
    if (options.requireSha256 === true && sha256 === undefined) {
      parse.fail(`${assetLabel}.sha256 must be present`);
    }
    return { name, contentBase64, ...(sha256 === undefined ? {} : { sha256 }) };
  });
}

// A name that is a directory prefix of another (a beside a/b) cannot stage: the first
// lands as a regular file exactly where the second needs a directory. Caught at the parse
// boundary so the conflict answers 400 instead of crashing the child's staging — the same
// class the path-length cap closes. Inert for flat areas: a name without "/" has no ancestors.
function trackNameNesting(
  spec: PayloadFieldSpec,
  parse: Narrower,
): {
  note(name: string): void;
} {
  const acceptedNames = new Set<string>();
  const nestedNameByAncestor = new Map<string, string>();
  return {
    note(name) {
      const nested = nestedNameByAncestor.get(name);
      if (nested !== undefined) {
        parse.fail(`${spec.field} must not nest the ${spec.nameKey} "${nested}" inside "${name}"`);
      }
      const ancestors = name.split("/");
      ancestors.pop();
      let prefix: string | undefined;
      for (const segment of ancestors) {
        prefix = prefix === undefined ? segment : `${prefix}/${segment}`;
        if (acceptedNames.has(prefix)) {
          parse.fail(`${spec.field} must not nest the ${spec.nameKey} "${name}" inside "${prefix}"`);
        }
        if (!nestedNameByAncestor.has(prefix)) {
          nestedNameByAncestor.set(prefix, name);
        }
      }
      acceptedNames.add(name);
    },
  };
}

function parseStagedPayloads(
  value: unknown,
  parse: Narrower,
  spec: PayloadFieldSpec,
  options: ParsePayloadOptions,
): RunDocumentPayload[] {
  const items = parse.requireArray(value, spec.field);
  if (items.length > spec.maxEntries) {
    parse.fail(`${spec.field} must not carry more than ${spec.maxEntries} entries`);
  }
  const seenNames = new Set<string>();
  const nesting = trackNameNesting(spec, parse);
  const payloads = items.map((item, index) => {
    const label = `${spec.field}[${index}]`;
    const record = parse.asRecord(item, label);
    parse.rejectUnknownFields(record, spec.allowedFields, label);
    const name = parse.requireString(record, spec.nameKey, label);
    if (!spec.nameGate(name)) {
      parse.fail(`${label}.${spec.nameKey} must be ${spec.nameRule}`);
    }
    if (seenNames.has(name)) {
      parse.fail(`${spec.field} must not repeat the ${spec.nameKey} "${name}"`);
    }
    seenNames.add(name);
    nesting.note(name);
    const contentBase64 = parse.requireString(record, "contentBase64", label);
    const sha256 = parseDeclaredSha256(record, parse, label);
    if (options.requireSha256 === true && sha256 === undefined) {
      parse.fail(`${label}.sha256 must be present`);
    }
    const withMetadata = spec.kind === "document";
    const mediaType = withMetadata ? parseBoundedMetaField(record, "mediaType", parse, label) : undefined;
    const documentClass = withMetadata
      ? parseBoundedMetaField(record, "documentClass", parse, label)
      : undefined;
    const description = spec.kind === "skill" ? parseSkillDescription(record, parse, label) : undefined;
    const assets = spec.kind === "skill" ? parseSkillAssets(record, parse, label, options) : undefined;
    return {
      name,
      contentBase64,
      ...(sha256 === undefined ? {} : { sha256 }),
      ...copyDocumentMeta({ mediaType, documentClass, description }),
      ...(assets === undefined ? {} : { assets }),
    };
  });
  if (spec.kind === "skill") {
    assertUniqueSkillIdentities(payloads, (problem) => {
      const label = `${spec.field}[${problem.index}]`;
      parse.fail(
        problem.kind === "no-identity"
          ? `${label}.name keeps no skill identity after normalization`
          : `${label}.name "${problem.name}" collides with "${problem.priorName}" ` +
              `on the skill identity "${problem.identity}"`,
      );
    });
  }
  return payloads;
}

export function parseRunDocumentPayloads(
  value: unknown,
  parse: Narrower,
  options: ParsePayloadOptions = {},
): RunDocumentPayload[] {
  return parseStagedPayloads(value, parse, DOCUMENT_PAYLOAD_SPEC, options);
}

export function parseSkillFilePayloads(
  value: unknown,
  parse: Narrower,
  options: ParsePayloadOptions = {},
): SkillFilePayload[] {
  return parseStagedPayloads(value, parse, SKILL_PAYLOAD_SPEC, options);
}

// The digest is required, not optional like a document's: the platform holds each published
// file's sha256, and a continuation must never restore a workspace nobody can describe. The
// wire's `path` lands on the internal `name` — downstream, a re-carried file is a RunDocument
// whose name happens to be an out/-relative path.
export function parseRecarriedOutPayloads(value: unknown, parse: Narrower): RunDocumentPayload[] {
  return parseStagedPayloads(value, parse, RECARRIED_OUT_PAYLOAD_SPEC, { requireSha256: true });
}

export type StagedAssetMeta = {
  name: string;
  sha256: string;
  sizeBytes: number;
};

export type StagedFileMeta = {
  name: string;
  sha256: string;
  sizeBytes: number;
  mediaType?: string;
  documentClass?: string;
  description?: string;
  assets?: StagedAssetMeta[];
};

const DOCUMENT_META_FIELDS: readonly string[] = ["name", "sha256", "sizeBytes", "mediaType", "documentClass"];

const SKILL_META_FIELDS: readonly string[] = ["name", "sha256", "sizeBytes", "description", "assets"];

const SKILL_ASSET_META_FIELDS: readonly string[] = ["name", "sha256", "sizeBytes"];

function parseSizeBytes(record: Record<string, unknown>, parse: Narrower, label: string): number {
  const sizeBytes = parse.requireNonNegativeNumber(record, "sizeBytes", label);
  if (!Number.isSafeInteger(sizeBytes)) {
    parse.fail(`${label}.sizeBytes must be a whole number of bytes`);
  }
  return sizeBytes;
}

export function parseRequiredSha256(record: Record<string, unknown>, parse: Narrower, label: string): string {
  const sha256 = parseDeclaredSha256(record, parse, label);
  if (sha256 === undefined) {
    parse.fail(`${label}.sha256 must be present`);
  }
  return sha256;
}

function parseSkillAssetMetas(
  record: Record<string, unknown>,
  parse: Narrower,
  label: string,
): StagedAssetMeta[] | undefined {
  const value = record["assets"];
  if (value === undefined) {
    return undefined;
  }
  const items = parse.requireArray(value, `${label}.assets`);
  if (items.length === 0) {
    return undefined;
  }
  if (items.length > MAX_SKILL_ASSETS_PER_SKILL) {
    parse.fail(`${label}.assets must not carry more than ${MAX_SKILL_ASSETS_PER_SKILL} entries`);
  }
  const seenNames = new Set<string>();
  return items.map((item, index) => {
    const assetLabel = `${label}.assets[${index}]`;
    const assetRecord = parse.asRecord(item, assetLabel);
    parse.rejectUnknownFields(assetRecord, SKILL_ASSET_META_FIELDS, assetLabel);
    const name = parse.requireString(assetRecord, "name", assetLabel);
    if (!isStagedInputName(name)) {
      parse.fail(`${assetLabel}.name must be an ordinary file name matching ${DOCUMENT_NAME_PATTERN.source}`);
    }
    if (seenNames.has(name)) {
      parse.fail(`${label}.assets must not repeat the name "${name}"`);
    }
    seenNames.add(name);
    return {
      name,
      sha256: parseRequiredSha256(assetRecord, parse, assetLabel),
      sizeBytes: parseSizeBytes(assetRecord, parse, assetLabel),
    };
  });
}

function parseStagedMetas(value: unknown, parse: Narrower, spec: PayloadFieldSpec): StagedFileMeta[] {
  const items = parse.requireArray(value, spec.field);
  if (items.length > spec.maxEntries) {
    parse.fail(`${spec.field} must not carry more than ${spec.maxEntries} entries`);
  }
  const seenNames = new Set<string>();
  const nesting = trackNameNesting(spec, parse);
  const metas = items.map((item, index) => {
    const label = `${spec.field}[${index}]`;
    const record = parse.asRecord(item, label);
    parse.rejectUnknownFields(record, spec.allowedFields, label);
    const name = parse.requireString(record, spec.nameKey, label);
    if (!spec.nameGate(name)) {
      parse.fail(`${label}.${spec.nameKey} must be ${spec.nameRule}`);
    }
    if (seenNames.has(name)) {
      parse.fail(`${spec.field} must not repeat the ${spec.nameKey} "${name}"`);
    }
    seenNames.add(name);
    nesting.note(name);
    const withMetadata = spec.kind === "document";
    const mediaType = withMetadata ? parseBoundedMetaField(record, "mediaType", parse, label) : undefined;
    const documentClass = withMetadata
      ? parseBoundedMetaField(record, "documentClass", parse, label)
      : undefined;
    const description = spec.kind === "skill" ? parseSkillDescription(record, parse, label) : undefined;
    const assets = spec.kind === "skill" ? parseSkillAssetMetas(record, parse, label) : undefined;
    return {
      name,
      sha256: parseRequiredSha256(record, parse, label),
      sizeBytes: parseSizeBytes(record, parse, label),
      ...copyDocumentMeta({ mediaType, documentClass, description }),
      ...(assets === undefined ? {} : { assets }),
    };
  });
  if (spec.kind === "skill") {
    assertUniqueSkillIdentities(metas, (problem) => {
      const label = `${spec.field}[${problem.index}]`;
      parse.fail(
        problem.kind === "no-identity"
          ? `${label}.name keeps no skill identity after normalization`
          : `${label}.name "${problem.name}" collides with "${problem.priorName}" ` +
              `on the skill identity "${problem.identity}"`,
      );
    });
  }
  return metas;
}

const DOCUMENT_META_SPEC: PayloadFieldSpec = {
  field: "documents",
  allowedFields: DOCUMENT_META_FIELDS,
  kind: "document",
  maxEntries: MAX_DOCUMENTS_PER_RUN,
  nameKey: "name",
  nameGate: isStagedInputName,
  nameRule: ORDINARY_NAME_RULE,
};

const SKILL_META_SPEC: PayloadFieldSpec = {
  field: "skillFiles",
  allowedFields: SKILL_META_FIELDS,
  kind: "skill",
  maxEntries: MAX_DOCUMENTS_PER_RUN,
  nameKey: "name",
  nameGate: isStagedInputName,
  nameRule: ORDINARY_NAME_RULE,
};

// The internal wire's re-carry shape is a plain StagedFileMeta: unlike the HTTP boundary,
// which calls the field `path`, both sides of the supervisor↔child wire speak `name`.
const RECARRIED_OUT_META_FIELDS: readonly string[] = ["name", "sha256", "sizeBytes"];

const RECARRIED_OUT_META_SPEC: PayloadFieldSpec = {
  field: "continuation.outFiles",
  allowedFields: RECARRIED_OUT_META_FIELDS,
  kind: "recarried_out",
  maxEntries: MAX_RECARRIED_OUT_FILES,
  nameKey: "name",
  nameGate: isRecarriedOutPath,
  nameRule: RECARRIED_OUT_NAME_RULE,
};

export function parseDocumentMetas(value: unknown, parse: Narrower): StagedFileMeta[] {
  return parseStagedMetas(value, parse, DOCUMENT_META_SPEC);
}

export function parseSkillFileMetas(value: unknown, parse: Narrower): StagedFileMeta[] {
  return parseStagedMetas(value, parse, SKILL_META_SPEC);
}

export function parseRecarriedOutMetas(value: unknown, parse: Narrower): StagedFileMeta[] {
  return parseStagedMetas(value, parse, RECARRIED_OUT_META_SPEC);
}

export type CargoManifestEntry = {
  kind: "document" | "skill_file" | "skill_asset" | "recarried_out";
  name: string;
  sha256: string;
  sizeBytes: number;
};

// The one definition of the cargo channel's entry order, derived from the frame metadata
// both sides already hold: documents first, then each skill file immediately followed by
// its assets, then a continuation's re-carried out/ files. The writer and the staging
// reader must walk exactly this list.
export function cargoManifestOf(
  documents: readonly StagedFileMeta[],
  skillFiles: readonly StagedFileMeta[],
  recarriedOut: readonly StagedFileMeta[] = [],
): CargoManifestEntry[] {
  return [
    ...documents.map(
      (meta): CargoManifestEntry => ({
        kind: "document",
        name: meta.name,
        sha256: meta.sha256,
        sizeBytes: meta.sizeBytes,
      }),
    ),
    ...skillFiles.flatMap((meta): CargoManifestEntry[] => [
      { kind: "skill_file", name: meta.name, sha256: meta.sha256, sizeBytes: meta.sizeBytes },
      ...(meta.assets ?? []).map(
        (asset): CargoManifestEntry => ({
          kind: "skill_asset",
          name: asset.name,
          sha256: asset.sha256,
          sizeBytes: asset.sizeBytes,
        }),
      ),
    ]),
    ...recarriedOut.map(
      (meta): CargoManifestEntry => ({
        kind: "recarried_out",
        name: meta.name,
        sha256: meta.sha256,
        sizeBytes: meta.sizeBytes,
      }),
    ),
  ];
}

function writeAllToFd(fd: number, chunk: Uint8Array): void {
  let offset = 0;
  while (offset < chunk.byteLength) {
    offset += writeSync(fd, chunk, offset, chunk.byteLength - offset);
  }
}

async function receiveToFile(
  receiver: CargoReceiver,
  meta: { name: string; sha256: string; sizeBytes: number },
  path: string,
  parse: Narrower,
  describe: string,
): Promise<void> {
  const hash = createSha256Stream();
  const fd = openSync(path, "wx", PRIVATE_FILE_MODE);
  try {
    await receiver.next(meta.sizeBytes, (chunk) => {
      hash.update(chunk);
      writeAllToFd(fd, chunk);
    });
  } finally {
    closeSync(fd);
  }
  if (hash.hexDigest() !== meta.sha256) {
    parse.fail(`${describe} "${meta.name}" bytes do not match the declared sha256`);
  }
}

async function receiveVerified(
  receiver: CargoReceiver,
  meta: { name: string; sha256: string; sizeBytes: number },
  parse: Narrower,
  describe: string,
): Promise<Buffer> {
  // One right-sized allocation up front: each received chunk is a small view over the
  // receiver's oversized scratch buffer, so retaining the views would pin the scratch
  // allocations for the whole receive — copying into the target frees them per chunk.
  const target = Buffer.allocUnsafe(meta.sizeBytes);
  let offset = 0;
  const hash = createSha256Stream();
  await receiver.next(meta.sizeBytes, (chunk) => {
    hash.update(chunk);
    target.set(chunk, offset);
    offset += chunk.byteLength;
  });
  if (hash.hexDigest() !== meta.sha256) {
    parse.fail(`${describe} "${meta.name}" bytes do not match the declared sha256`);
  }
  return target;
}

// The session bundle is the one cargo entry outside cargoManifestOf: it precedes the whole
// manifest, because the child restores the session before it stages anything. Buffered whole
// like a skill file — the decoder needs the bytes in memory — and bounded by the wire
// descriptor's own size cap rather than the shared document budget.
export async function receiveCheckpointBytes(
  receiver: CargoReceiver,
  descriptor: { sha256: string; sizeBytes: number },
  parse: Narrower,
): Promise<Buffer> {
  return receiveVerified(receiver, { name: "session bundle", ...descriptor }, parse, "checkpoint");
}

function documentsManifestOf(documents: readonly StagedFileMeta[]): Record<string, unknown>[] {
  return documents.map((meta) => ({
    name: meta.name,
    ...(meta.mediaType === undefined ? {} : { mediaType: meta.mediaType }),
    ...(meta.documentClass === undefined ? {} : { documentClass: meta.documentClass }),
    sizeBytes: meta.sizeBytes,
    sha256: meta.sha256,
  }));
}

// Documents stream straight from the cargo channel to the docs area — the child never
// holds more than one chunk of one. Skill files and their assets are buffered whole:
// SKILL.md composition needs the body in memory, and their sizes ride the same charged
// budget every input shares. Charged from declared sizes before any byte is pulled, so
// an over-budget staging never reads the overflow into memory.
export async function stageStreamedInputs(input: {
  documents: StagedFileMeta[];
  skillFiles: StagedFileMeta[];
  recarriedOut: StagedFileMeta[];
  receiver: CargoReceiver;
  parse: Narrower;
  budget: ChargeBudget;
  workspacePath: string;
}): Promise<StagedSkillFile[]> {
  const { documents, skillFiles, recarriedOut, receiver, parse, budget } = input;
  const manifest = cargoManifestOf(documents, skillFiles, recarriedOut);
  for (const entry of manifest) {
    budget.charge(entry.sizeBytes);
  }
  const docsDir = join(input.workspacePath, WORKSPACE_DOCUMENTS_DIR);
  if (documents.length > 0) {
    mkdirSync(docsDir, { mode: PRIVATE_DIR_MODE });
  }
  for (const meta of documents) {
    await receiveToFile(receiver, meta, join(docsDir, meta.name), parse, "document");
  }
  if (documents.length > 0) {
    writeFileSync(join(docsDir, DOCUMENTS_MANIFEST_NAME), JSON.stringify(documentsManifestOf(documents)), {
      mode: PRIVATE_FILE_MODE,
    });
  }
  const staged: StagedSkillFile[] = [];
  for (const meta of skillFiles) {
    const bytes = await receiveVerified(receiver, meta, parse, "skill file");
    const assets =
      meta.assets === undefined
        ? undefined
        : await (async () => {
            const received = [];
            for (const asset of meta.assets ?? []) {
              received.push({
                name: asset.name,
                bytes: await receiveVerified(receiver, asset, parse, "asset"),
              });
            }
            return received;
          })();
    staged.push({
      name: meta.name,
      ...(meta.description === undefined ? {} : { description: meta.description }),
      bytes,
      ...(assets === undefined ? {} : { assets }),
    });
  }
  // Unlike the docs area, out/ carries no manifest file: the re-carried set is described on
  // the wire and nowhere on disk, so the worker finds exactly its own files.
  if (recarriedOut.length > 0) {
    const outDir = join(input.workspacePath, WORKSPACE_OUT_DIR);
    mkdirSync(outDir, { recursive: true, mode: PRIVATE_DIR_MODE });
    for (const meta of recarriedOut) {
      const target = join(outDir, ...meta.name.split("/"));
      mkdirSync(dirname(target), { recursive: true, mode: PRIVATE_DIR_MODE });
      await receiveToFile(receiver, meta, target, parse, "re-carried out/ file");
    }
  }
  return staged;
}

function decodedLengthOf(contentBase64: string): number {
  const padding = contentBase64.endsWith("==") ? 2 : contentBase64.endsWith("=") ? 1 : 0;
  return (contentBase64.length / BASE64_GROUP_LENGTH) * BYTES_PER_BASE64_GROUP - padding;
}

// The staged-input decode pipeline for a value with its own byte cap instead of the shared
// budget — the continuation's session bundle as it arrives in the platform-facing HTTP
// body. The size projection is arithmetic on the length and the padding tail, valid
// whether or not the string is canonical — checked first so an over-cap payload refuses
// before the full-string canonical walk spends anything.
export async function decodeBoundedBase64(input: {
  contentBase64: string;
  label: string;
  maxBytes: number;
  parse: Narrower;
}): Promise<{ bytes: Buffer; sha256: string }> {
  const decodedLength = decodedLengthOf(input.contentBase64);
  if (decodedLength > input.maxBytes) {
    input.parse.fail(`${input.label} exceeds the ${input.maxBytes}-byte cap`);
  }
  if (!(await isCanonicalBase64(input.contentBase64))) {
    input.parse.fail(`${input.label} is not canonical base64`);
  }
  return decodeBase64InChunks(input.contentBase64, decodedLength);
}

export function createDocumentBudget(maxTotalBytes: number): ChargeBudget {
  return createChargeBudget(
    maxTotalBytes,
    (maxBytes) =>
      new RunDocumentBudgetError(
        `documents, skill files and re-carried out/ files together exceed ` +
          `the ${maxBytes}-byte decoded budget across the run`,
      ),
  );
}

// Control yields between chunks of a single document, not only between files — decoding
// a full budget is seconds of synchronous work on the one event loop every other run
// shares. Validation runs as a separate pass ahead of the charge so a document that is
// both non-canonical and over-budget answers 400, not 413.
export async function decodeRunDocuments(
  payloads: RunDocumentPayload[],
  parse: Narrower,
  budget: ChargeBudget,
  describe = "document",
): Promise<RunDocument[]> {
  const decoded: RunDocument[] = [];
  for (const payload of payloads) {
    await yieldToEventLoop();
    if (!(await isCanonicalBase64(payload.contentBase64))) {
      parse.fail(`${describe} "${payload.name}" contentBase64 is not canonical base64`);
    }
    const decodedLength = decodedLengthOf(payload.contentBase64);
    budget.charge(decodedLength);
    const { bytes, sha256 } = await decodeBase64InChunks(payload.contentBase64, decodedLength);
    if (payload.sha256 !== undefined && payload.sha256 !== sha256) {
      parse.fail(`${describe} "${payload.name}" bytes do not match the declared sha256`);
    }
    const assets = payload.assets === undefined ? undefined : await decodeSkillAssets(payload, parse, budget);
    decoded.push({
      name: payload.name,
      bytes,
      sha256,
      ...copyDocumentMeta(payload),
      ...(assets === undefined ? {} : { assets }),
    });
  }
  return decoded;
}

async function decodeSkillAssets(
  payload: RunDocumentPayload,
  parse: Narrower,
  budget: ChargeBudget,
): Promise<RunAsset[]> {
  const decoded: RunAsset[] = [];
  for (const asset of payload.assets ?? []) {
    await yieldToEventLoop();
    if (!(await isCanonicalBase64(asset.contentBase64))) {
      parse.fail(`asset "${asset.name}" of skill "${payload.name}" contentBase64 is not canonical base64`);
    }
    const decodedLength = decodedLengthOf(asset.contentBase64);
    budget.charge(decodedLength);
    const { bytes, sha256 } = await decodeBase64InChunks(asset.contentBase64, decodedLength);
    if (asset.sha256 !== undefined && asset.sha256 !== sha256) {
      parse.fail(`asset "${asset.name}" of skill "${payload.name}" bytes do not match the declared sha256`);
    }
    decoded.push({ name: asset.name, bytes, sha256 });
  }
  return decoded;
}

// Scalars only: a summary outlives the request, so it must never pin a run's
// payload bytes.
export type StagedAssetSummary = {
  name: string;
  sha256: string;
  sizeBytes: number;
};

export type StagedInputSummary = {
  area: StagedInputArea;
  name: string;
  sha256: string;
  sizeBytes: number;
  mediaType?: string;
  documentClass?: string;
  description?: string;
  assets?: StagedAssetSummary[];
};

export function summarizeStagedFileMetas(
  documents: readonly StagedFileMeta[],
  skillFiles: readonly StagedFileMeta[],
  recarriedOut: readonly StagedFileMeta[] = [],
): StagedInputSummary[] {
  return [
    ...documents.map((meta): StagedInputSummary => ({ area: DOCUMENTS_AREA, ...meta })),
    ...skillFiles.map((meta): StagedInputSummary => ({ area: SKILLS_AREA, ...meta })),
    ...recarriedOut.map((meta): StagedInputSummary => ({ area: RECARRIED_OUT_AREA, ...meta })),
  ].sort((left, right) => compareCodePoints(sortKeyOf(left), sortKeyOf(right)));
}

function sortKeyOf(summary: StagedInputSummary): string {
  return `${summary.area}/${summary.name}`;
}

function compareCodePoints(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}
