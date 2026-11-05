import { describe, expect, it } from "bun:test";
import { hasErrorCode, messageOf, toError } from "./errors.js";

describe("toError", () => {
  it("passes an Error through untouched", () => {
    const error = new Error("boom");
    expect(toError(error)).toBe(error);
  });

  it("wraps a non-Error throw into an Error carrying its string form", () => {
    expect(toError("boom")).toBeInstanceOf(Error);
    expect(toError(42).message).toBe("42");
  });
});

describe("messageOf", () => {
  it("reads the message from an Error and stringifies anything else", () => {
    expect(messageOf(new Error("boom"))).toBe("boom");
    expect(messageOf("plain")).toBe("plain");
    expect(messageOf(7)).toBe("7");
  });
});

describe("hasErrorCode", () => {
  it("matches a coded system error against any of the given codes", () => {
    const enoent = Object.assign(new Error("missing"), { code: "ENOENT" });
    expect(hasErrorCode(enoent, "ENOENT")).toBe(true);
    expect(hasErrorCode(enoent, "ECONNRESET", "ENOENT")).toBe(true);
    expect(hasErrorCode(enoent, "EEXIST")).toBe(false);
  });

  it("rejects non-errors and errors without a string code", () => {
    expect(hasErrorCode("ENOENT", "ENOENT")).toBe(false);
    expect(hasErrorCode(new Error("plain"), "ENOENT")).toBe(false);
    expect(hasErrorCode(Object.assign(new Error("n"), { code: 2 }), "ENOENT")).toBe(false);
  });
});
