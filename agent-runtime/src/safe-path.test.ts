import { describe, expect, it } from "bun:test";
import { areSafePathSegments, isSafePathSegment } from "./safe-path.js";

describe("isSafePathSegment", () => {
  it("accepts ordinary names", () => {
    for (const segment of ["report.md", "attempt-1", "a b", "..leading-dots", "...", "-"]) {
      expect(isSafePathSegment(segment)).toBe(true);
    }
  });

  it("rejects every segment that is not just a name", () => {
    for (const segment of ["", ".", "..", "a/b", "a\\b", "a\0b"]) {
      expect(isSafePathSegment(segment)).toBe(false);
    }
  });
});

describe("areSafePathSegments", () => {
  it("requires at least one segment and holds every one of them to the rule", () => {
    expect(areSafePathSegments([])).toBe(false);
    expect(areSafePathSegments(["out", "report.md"])).toBe(true);
    expect(areSafePathSegments(["out", ".", "report.md"])).toBe(false);
    expect(areSafePathSegments([".", "docs", "evil.pdf"])).toBe(false);
  });
});
