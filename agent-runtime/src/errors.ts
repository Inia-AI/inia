export function toError(value: unknown): Error {
  return value instanceof Error ? value : new Error(String(value));
}

export function messageOf(value: unknown): string {
  return value instanceof Error ? value.message : String(value);
}

export function hasErrorCode(error: unknown, ...codes: readonly string[]): boolean {
  return (
    error instanceof Error && "code" in error && typeof error.code === "string" && codes.includes(error.code)
  );
}
