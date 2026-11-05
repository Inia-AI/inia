// A tiny in-memory image vocabulary for the contract net: a synthetic PNG whose pixel
// dimensions exceed the model's 2000×2000 ceiling (so the CLI must downscale it before the
// stub ever sees it), and header-only dimension readers for what comes back.
import { deflateSync } from "node:zlib";

export const IMAGE_SIDE_CEILING_PX = 2000;
export const OVERSIZE_IMAGE_NAME = "oversize-probe.png";
export const OVERSIZE_IMAGE_WIDTH_PX = 100;
export const OVERSIZE_IMAGE_HEIGHT_PX = IMAGE_SIDE_CEILING_PX + 100;

export type ImageDimensions = { width: number; height: number };

// What the stub sees of an image block inside a tool_result: enough to judge the downscale
// without holding the bytes.
export type ToolResultImage = {
  mediaType: string;
  byteLength: number;
  dimensions: ImageDimensions | undefined;
};

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const PNG_IHDR_WIDTH_OFFSET = 16;
const PNG_IHDR_HEIGHT_OFFSET = 20;
const PNG_BIT_DEPTH_8 = 8;
const PNG_COLOR_TYPE_GRAYSCALE = 0;
const PNG_FILTER_NONE = 0;
const PNG_IHDR_LENGTH = 13;
const CRC32_POLYNOMIAL = 0xedb88320;
const CRC32_INITIAL = 0xffffffff;

const JPEG_MARKER_PREFIX = 0xff;
const JPEG_SOI_LENGTH = 2;
const JPEG_SOF_HEIGHT_OFFSET = 5;
const JPEG_SOF_WIDTH_OFFSET = 7;
const JPEG_SEGMENT_HEADER_LENGTH = 2;
const JPEG_START_OF_FRAME_MARKERS: ReadonlySet<number> = new Set([
  0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf,
]);

const CRC_TABLE = buildCrcTable();

function buildCrcTable(): Uint32Array {
  const table = new Uint32Array(256);
  for (let n = 0; n < table.length; n += 1) {
    let c = n;
    for (let k = 0; k < 8; k += 1) {
      c = (c & 1) === 1 ? CRC32_POLYNOMIAL ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c >>> 0;
  }
  return table;
}

function crc32(bytes: Buffer): number {
  let crc = CRC32_INITIAL;
  for (const byte of bytes) {
    crc = (CRC_TABLE[(crc ^ byte) & 0xff] ?? 0) ^ (crc >>> 8);
  }
  return (crc ^ CRC32_INITIAL) >>> 0;
}

function pngChunk(type: string, data: Buffer): Buffer {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length, 0);
  const typeAndData = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(typeAndData), 0);
  return Buffer.concat([length, typeAndData, crc]);
}

export function grayscalePng(dimensions: ImageDimensions): Buffer {
  const ihdr = Buffer.alloc(PNG_IHDR_LENGTH);
  ihdr.writeUInt32BE(dimensions.width, 0);
  ihdr.writeUInt32BE(dimensions.height, 4);
  ihdr[8] = PNG_BIT_DEPTH_8;
  ihdr[9] = PNG_COLOR_TYPE_GRAYSCALE;
  // Zero-filled rows: every row opens with the "none" filter byte and holds black pixels.
  const rows = Buffer.alloc((dimensions.width + 1) * dimensions.height, PNG_FILTER_NONE);
  return Buffer.concat([
    PNG_SIGNATURE,
    pngChunk("IHDR", ihdr),
    pngChunk("IDAT", deflateSync(rows)),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

export function oversizeProbePng(): Buffer {
  return grayscalePng({ width: OVERSIZE_IMAGE_WIDTH_PX, height: OVERSIZE_IMAGE_HEIGHT_PX });
}

function pngDimensionsOf(bytes: Buffer): ImageDimensions | undefined {
  if (
    bytes.length < PNG_IHDR_HEIGHT_OFFSET + 4 ||
    !bytes.subarray(0, PNG_SIGNATURE.length).equals(PNG_SIGNATURE)
  ) {
    return undefined;
  }
  return {
    width: bytes.readUInt32BE(PNG_IHDR_WIDTH_OFFSET),
    height: bytes.readUInt32BE(PNG_IHDR_HEIGHT_OFFSET),
  };
}

function jpegDimensionsOf(bytes: Buffer): ImageDimensions | undefined {
  if (bytes.length < JPEG_SOI_LENGTH || bytes[0] !== JPEG_MARKER_PREFIX || bytes[1] !== 0xd8) {
    return undefined;
  }
  let offset = JPEG_SOI_LENGTH;
  while (offset + JPEG_SOF_WIDTH_OFFSET + 2 <= bytes.length) {
    if (bytes[offset] !== JPEG_MARKER_PREFIX) {
      return undefined;
    }
    const marker = bytes[offset + 1];
    if (marker === undefined) {
      return undefined;
    }
    if (marker === JPEG_MARKER_PREFIX) {
      offset += 1;
      continue;
    }
    if (JPEG_START_OF_FRAME_MARKERS.has(marker)) {
      return {
        height: bytes.readUInt16BE(offset + JPEG_SOF_HEIGHT_OFFSET),
        width: bytes.readUInt16BE(offset + JPEG_SOF_WIDTH_OFFSET),
      };
    }
    offset += JPEG_SEGMENT_HEADER_LENGTH + bytes.readUInt16BE(offset + JPEG_SEGMENT_HEADER_LENGTH);
  }
  return undefined;
}

export function imageDimensionsOf(bytes: Buffer): ImageDimensions | undefined {
  return pngDimensionsOf(bytes) ?? jpegDimensionsOf(bytes);
}
