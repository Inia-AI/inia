import { readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { createNarrower, type Narrower } from "../../src/narrow.js";

export class DriftWorkspaceError extends Error {}

const parse: Narrower = createNarrower((message) => new DriftWorkspaceError(message));

const DRIFT_DIR = fileURLToPath(new URL(".", import.meta.url));

export const DRIFT_RUNS_DIR = join(DRIFT_DIR, "..", "runs", "drift");

export const DRIFT_RECORDS_FILE_NAME = "records.json";

const PACKAGE_JSON_PATH = join(DRIFT_DIR, "..", "..", "package.json");

// The build identity env var is unset in dev builds; the harness stamps the source version.
export function readRuntimeVersion(packageJsonPath = PACKAGE_JSON_PATH): string {
  const manifest = parse.asRecord(JSON.parse(readFileSync(packageJsonPath, "utf8")), "package.json");
  return parse.requireString(manifest, "version", "package.json");
}

export function timestampSlug(date: Date): string {
  return date.toISOString().replaceAll(":", "-").replace(/\..*$/, "Z");
}
