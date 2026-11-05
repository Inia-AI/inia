import { describe, expect, it } from "bun:test";
import {
  grayscalePng,
  IMAGE_SIDE_CEILING_PX,
  imageDimensionsOf,
  OVERSIZE_IMAGE_HEIGHT_PX,
  OVERSIZE_IMAGE_WIDTH_PX,
  oversizeProbePng,
} from "./probe-image.js";

const JPEG_SOI = [0xff, 0xd8];
const JPEG_APP0 = [
  0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
];
const JPEG_MARKER_PADDING = [0xff];

function jpegHeaderWithDimensions(width: number, height: number): Buffer {
  const sof0 = [0xff, 0xc0, 0x00, 0x11, 0x08, height >> 8, height & 0xff, width >> 8, width & 0xff, 0x03];
  return Buffer.from([...JPEG_SOI, ...JPEG_APP0, ...JPEG_MARKER_PADDING, ...sof0]);
}

describe("the oversize probe image", () => {
  it("is a PNG that exceeds the model's pixel ceiling on exactly one side", () => {
    expect(imageDimensionsOf(oversizeProbePng())).toEqual({
      width: OVERSIZE_IMAGE_WIDTH_PX,
      height: OVERSIZE_IMAGE_HEIGHT_PX,
    });
    expect(OVERSIZE_IMAGE_WIDTH_PX).toBeLessThanOrEqual(IMAGE_SIDE_CEILING_PX);
    expect(OVERSIZE_IMAGE_HEIGHT_PX).toBeGreaterThan(IMAGE_SIDE_CEILING_PX);
  });

  it("stays small in bytes despite its pixel count", () => {
    expect(oversizeProbePng().byteLength).toBeLessThan(4096);
  });
});

describe("imageDimensionsOf", () => {
  it("reads a PNG's IHDR", () => {
    expect(imageDimensionsOf(grayscalePng({ width: 3, height: 5 }))).toEqual({ width: 3, height: 5 });
  });

  it("reads a JPEG's start-of-frame segment past leading segments and marker padding", () => {
    expect(imageDimensionsOf(jpegHeaderWithDimensions(923, 1999))).toEqual({ width: 923, height: 1999 });
  });

  it("answers undefined for bytes that are neither", () => {
    expect(imageDimensionsOf(Buffer.from("not an image"))).toBeUndefined();
    expect(imageDimensionsOf(Buffer.alloc(0))).toBeUndefined();
    expect(imageDimensionsOf(Buffer.from([...JPEG_SOI, ...JPEG_APP0]))).toBeUndefined();
  });
});
