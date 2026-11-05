const PATH_SEPARATORS = /[/\\]/;

// `join` collapses ".", so a "." segment slips past a literal-first-segment check and
// can reach a reserved workspace area.
export function isSafePathSegment(segment: string): boolean {
  return (
    segment !== "" &&
    segment !== "." &&
    segment !== ".." &&
    !PATH_SEPARATORS.test(segment) &&
    !segment.includes("\0")
  );
}

export function areSafePathSegments(segments: readonly string[]): boolean {
  return segments.length > 0 && segments.every(isSafePathSegment);
}

// The lookahead bars "." and "..", which the charset alone admits and which walk out of
// every directory the executionId names.
export const EXECUTION_ID_PATTERN = /^(?!\.\.?$)[A-Za-z0-9._-]{1,128}$/;

// The one gate for every executionId-keyed filesystem area; a second, divergent check
// elsewhere would let a purge miss.
export function assertSafePathSegment(executionId: string): void {
  if (!EXECUTION_ID_PATTERN.test(executionId)) {
    throw new Error(`executionId "${executionId}" is not a safe path segment`);
  }
}
