import { describe, expect, it } from "bun:test";
import { chmodSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { ConfigError } from "./config-error.js";
import { assertCredentialsFileReadable } from "./credentials-file.js";

function credentialsFile(mode: number): string {
  const path = join(mkdtempSync(join(tmpdir(), "credentials-file-")), "credentials.json");
  writeFileSync(path, "{}");
  chmodSync(path, mode);
  return path;
}

describe("assertCredentialsFileReadable", () => {
  it("accepts a file every run's uid can read", () => {
    expect(() => assertCredentialsFileReadable(credentialsFile(0o444))).not.toThrow();
  });

  it("refuses a missing file, so the runtime does not start without the host's credentials", () => {
    expect(() => assertCredentialsFileReadable(join(tmpdir(), "no-such-credentials.json"))).toThrow(
      ConfigError,
    );
  });

  it("refuses a file only its owner can read, which no run's uid could use", () => {
    expect(() => assertCredentialsFileReadable(credentialsFile(0o400))).toThrow(ConfigError);
  });
});
