import { closeSync, constants, fstatSync, openSync, readlinkSync, readSync } from "node:fs";
import { resolve } from "node:path";
import { hasErrorCode } from "./errors.js";

export class NotARegularFileError extends Error {}

export class OpenedPathMismatchError extends Error {}

// O_NONBLOCK stops the open from blocking forever on a writerless fifo (regular-file reads
// ignore the flag).
const READ_ONLY_NO_FOLLOW = constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK;

// One descriptor for both the size and the read, so a file grown or swapped after open is
// never over-read and its type check (below) can't be defeated by a later swap either.
export type SizedFile = { fd: number; sizeBytes: number };

// Any failure to read the identity back — /proc missing included — is a mismatch: this
// check must never fail open.
export function openedPathMatches(fd: number, expectedPath: string): boolean {
  let openedPath: string;
  try {
    openedPath = readlinkSync(`/proc/self/fd/${fd}`);
  } catch {
    return false;
  }
  return openedPath === resolve(expectedPath);
}

export function openSizedFile(path: string): SizedFile {
  const fd = openSync(path, READ_ONLY_NO_FOLLOW);
  try {
    const stats = fstatSync(fd);
    if (!stats.isFile()) {
      throw new NotARegularFileError(`${path} is not a regular file`);
    }
    if (!openedPathMatches(fd, path)) {
      throw new OpenedPathMismatchError(`${path} is not the path the descriptor actually opened`);
    }
    return { fd, sizeBytes: stats.size };
  } catch (error) {
    closeSync(fd);
    throw error;
  }
}

// A refused open — missing, swapped mid-walk, or not a servable regular file — is undefined;
// any other failure (EACCES, EMFILE, ...) still throws.
export function openSizedFileOrUndefined(path: string): SizedFile | undefined {
  try {
    return openSizedFile(path);
  } catch (error) {
    if (
      error instanceof NotARegularFileError ||
      error instanceof OpenedPathMismatchError ||
      hasErrorCode(error, "ENOENT", "ENOTDIR", "ELOOP")
    ) {
      return undefined;
    }
    throw error;
  }
}

export function readSizedFile(file: SizedFile): Buffer {
  return readSizedRange(file.fd, 0, file.sizeBytes);
}

// A file that shrank mid-read yields the surviving bytes, never a zero-filled tail
// that would hash as content nobody wrote.
export function readSizedRange(fd: number, offset: number, length: number): Buffer {
  const bytes = Buffer.alloc(length);
  let read = 0;
  while (read < length) {
    const chunk = readSync(fd, bytes, read, length - read, offset + read);
    if (chunk === 0) {
      break;
    }
    read += chunk;
  }
  return read === length ? bytes : bytes.subarray(0, read);
}

export function closeSizedFile(file: SizedFile): void {
  closeSync(file.fd);
}
