import { describe, expect, it } from "bun:test";
import { createNarrower, type Narrower } from "./narrow.js";

class NarrowTestError extends Error {}

const parse: Narrower = createNarrower((message) => new NarrowTestError(message));

describe("optionalWith", () => {
  const parseShout = (value: unknown, narrow: Narrower, label: string): string => {
    const record = narrow.asRecord(value, label);
    return narrow.requireString(record, "shout", label);
  };

  it("returns undefined for an absent key without consulting the parser", () => {
    expect(parse.optionalWith({}, "cfg", parseShout)).toBeUndefined();
  });

  it("hands a present value to the parser, labelled with the key", () => {
    expect(parse.optionalWith({ cfg: { shout: "hi" } }, "cfg", parseShout)).toBe("hi");
    expect(() => parse.optionalWith({ cfg: 7 }, "cfg", parseShout)).toThrow("cfg must be a JSON object");
  });

  it("labels through the parent path exactly like the other narrowers", () => {
    expect(() => parse.optionalWith({ cfg: 7 }, "cfg", parseShout, "run request")).toThrow(
      "run request.cfg must be a JSON object",
    );
  });

  it("treats only undefined as absent — a JSON null reaches the parser and is refused", () => {
    expect(() => parse.optionalWith({ cfg: null }, "cfg", parseShout)).toThrow(NarrowTestError);
  });
});

describe("requireNonNegativeNumber", () => {
  it("accepts zero and positive finite numbers", () => {
    expect(parse.requireNonNegativeNumber({ n: 0 }, "n")).toBe(0);
    expect(parse.requireNonNegativeNumber({ n: 1.5 }, "n")).toBe(1.5);
  });

  it("refuses negatives with the path-labelled message", () => {
    expect(() => parse.requireNonNegativeNumber({ n: -1 }, "n", "frame")).toThrow(
      "frame.n must not be negative",
    );
  });

  it("refuses non-numbers through requireNumber's rule", () => {
    expect(() => parse.requireNonNegativeNumber({ n: "0" }, "n")).toThrow("n must be a finite number");
  });
});
