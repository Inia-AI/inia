import { type Stats, statSync } from "node:fs";
import { ConfigError } from "./config-error.js";
import { hasErrorCode } from "./errors.js";

const CREDENTIALS_FILE_LABEL = "AGENT_RUNTIME_CREDENTIALS_FILE";
const OTHERS_READ_BIT = 0o004;

// Each run's uid reads the file through credential_process; the config already placed it where
// executed code cannot.
export function assertCredentialsFileReadable(path: string): void {
  let fileStat: Stats;
  try {
    fileStat = statSync(path);
  } catch (error) {
    if (hasErrorCode(error, "ENOENT", "EACCES")) {
      throw new ConfigError(`${CREDENTIALS_FILE_LABEL} names "${path}", which this process cannot reach`);
    }
    throw error;
  }
  if (!fileStat.isFile() || (fileStat.mode & OTHERS_READ_BIT) === 0) {
    throw new ConfigError(
      `${CREDENTIALS_FILE_LABEL} "${path}" must be a regular file that every run's uid can read`,
    );
  }
}
