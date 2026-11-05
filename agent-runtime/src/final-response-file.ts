import { randomUUID } from "node:crypto";
import { chmodSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { MAX_CHILD_FRAME_LENGTH } from "./config.js";
import { messageOf } from "./errors.js";
import { closeSizedFile, openSizedFileOrUndefined, readSizedFile } from "./fs-read.js";
import { claimCreatedDirs } from "./private-fs.js";
import {
  artifactAttemptDirOf,
  SUPERVISOR_READABLE_DIR_MODE,
  SUPERVISOR_READABLE_FILE_MODE,
} from "./run-files.js";
import { parseWireResponse, WireError, type WireResponse } from "./run-wire.js";
import { FINAL_RESPONSE_FILE_NAME } from "./workspace-layout.js";

export type FinalResponseLocation = {
  artifactsRoot: string;
  executionId: string;
  attempt: number;
};

export class FinalResponseUnreadableError extends Error {}

const STAGING_PREFIX = ".final-response-";

export function finalResponsePathOf(location: FinalResponseLocation): string {
  return join(
    artifactAttemptDirOf(location.artifactsRoot, location.executionId, location.attempt),
    FINAL_RESPONSE_FILE_NAME,
  );
}

// Staged then renamed, so the final name never holds a partial write: a child killed
// mid-write leaves a staging file the supervisor never reads, not a truncated response.
export function persistFinalResponse(input: FinalResponseLocation & { response: WireResponse }): void {
  const attemptDir = artifactAttemptDirOf(input.artifactsRoot, input.executionId, input.attempt);
  claimCreatedDirs(attemptDir, SUPERVISOR_READABLE_DIR_MODE);
  const stagingPath = join(attemptDir, `${STAGING_PREFIX}${randomUUID()}`);
  try {
    writeFileSync(stagingPath, JSON.stringify(input.response), { mode: SUPERVISOR_READABLE_FILE_MODE });
    chmodSync(stagingPath, SUPERVISOR_READABLE_FILE_MODE);
    renameSync(stagingPath, finalResponsePathOf(input));
  } finally {
    rmSync(stagingPath, { force: true });
  }
}

// Absent is undefined; present but unreadable — over the cap, not JSON, not a wire
// response — throws, because a supervisor that treated a corrupt result as "never
// written" would report a death the child did not die.
export function readPersistedFinalResponse(
  location: FinalResponseLocation,
  maxBytes: number = MAX_CHILD_FRAME_LENGTH,
): WireResponse | undefined {
  const path = finalResponsePathOf(location);
  const file = openSizedFileOrUndefined(path);
  if (file === undefined) {
    return undefined;
  }
  try {
    if (file.sizeBytes > maxBytes) {
      throw new FinalResponseUnreadableError(
        `persisted final response at ${path} is ${file.sizeBytes} bytes, over the ${maxBytes}-byte cap`,
      );
    }
    const text = readSizedFile(file).toString("utf8");
    let body: unknown;
    try {
      body = JSON.parse(text);
    } catch (error) {
      throw new FinalResponseUnreadableError(
        `persisted final response at ${path} is not JSON: ${messageOf(error)}`,
      );
    }
    try {
      return parseWireResponse(body);
    } catch (error) {
      if (error instanceof WireError) {
        throw new FinalResponseUnreadableError(
          `persisted final response at ${path} is not a wire response: ${error.message}`,
        );
      }
      throw error;
    }
  } finally {
    closeSizedFile(file);
  }
}
