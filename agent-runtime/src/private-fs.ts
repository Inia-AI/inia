import { randomUUID } from "node:crypto";
import { chmodSync, mkdirSync, readdirSync, rmSync, statSync } from "node:fs";
import { rename, rm } from "node:fs/promises";
import { dirname, join } from "node:path";

export const PRIVATE_DIR_MODE = 0o700;
export const PRIVATE_FILE_MODE = 0o600;

// Chmods only the levels this call created — chmodding a pre-existing directory owned
// by another uid is an EPERM that would fail an otherwise-successful run.
export function claimCreatedDirs(path: string, mode: number): void {
  const firstCreated = mkdirSync(path, { recursive: true, mode });
  if (firstCreated === undefined) {
    return;
  }
  let current = path;
  while (current.length >= firstCreated.length) {
    chmodSync(current, mode);
    if (current === firstCreated) {
      return;
    }
    current = dirname(current);
  }
}

export const STAGING_PREFIX = ".staging-";

// Claimed before any run child exists: on the shared 1777 tmpfs a run uid could
// otherwise pre-create the root and own everything in it.
export function claimPrivateRoot(root: string, label: string): void {
  mkdirSync(root, { recursive: true, mode: PRIVATE_DIR_MODE });
  const rootStat = statSync(root);
  const ownUid = process.getuid?.();
  if ((rootStat.mode & 0o777) !== PRIVATE_DIR_MODE || (ownUid !== undefined && rootStat.uid !== ownUid)) {
    throw new Error(`${label} root ${root} must be a private dir owned by this process`);
  }
  for (const entry of readdirSync(root)) {
    if (entry.startsWith(STAGING_PREFIX)) {
      rmSync(join(root, entry), { recursive: true, force: true });
    }
  }
}

// `replace` removes an existing entry at the final path first — rename cannot
// overwrite a directory.
export async function publishAtomically(
  root: string,
  finalPath: string,
  write: (stagingPath: string) => Promise<void>,
  options: { replace?: boolean } = {},
): Promise<void> {
  const stagingPath = join(root, `${STAGING_PREFIX}${randomUUID()}`);
  try {
    await write(stagingPath);
    if (options.replace === true) {
      await rm(finalPath, { recursive: true, force: true });
    }
    await rename(stagingPath, finalPath);
  } finally {
    await rm(stagingPath, { recursive: true, force: true });
  }
}
