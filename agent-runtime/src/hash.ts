import { createHash } from "node:crypto";

export const SHA256_HEX_PATTERN = /^[0-9a-f]{64}$/;

export function sha256Hex(bytes: Uint8Array): string {
  return createHash("sha256").update(bytes).digest("hex");
}

export type Sha256Stream = {
  update: (bytes: Uint8Array) => void;
  hexDigest: () => string;
};

export function createSha256Stream(): Sha256Stream {
  const hash = createHash("sha256");
  return {
    update: (bytes) => {
      hash.update(bytes);
    },
    hexDigest: () => hash.digest("hex"),
  };
}

export function sha256HexOfJson(value: unknown): string {
  return sha256Hex(new TextEncoder().encode(JSON.stringify(value)));
}
