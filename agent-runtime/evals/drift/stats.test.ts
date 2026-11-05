import { describe, expect, it } from "bun:test";
import { medianOf, percentileOf, summarizeSamples } from "./stats.js";

describe("percentileOf", () => {
  it("interpolates linearly between sorted neighbours", () => {
    expect(percentileOf([10, 20, 30, 40], 0.5)).toBe(25);
    expect(percentileOf([10, 20, 30, 40], 0.25)).toBe(17.5);
    expect(percentileOf([10, 20, 30, 40], 0.75)).toBe(32.5);
  });

  it("answers the single sample for every quantile", () => {
    expect(percentileOf([7], 0)).toBe(7);
    expect(percentileOf([7], 0.5)).toBe(7);
    expect(percentileOf([7], 1)).toBe(7);
  });
});

describe("medianOf", () => {
  it("is the middle sample or the mean of the two middles", () => {
    expect(medianOf([3, 1, 2])).toBe(2);
    expect(medianOf([4, 1, 3, 2])).toBe(2.5);
  });

  it("is null over no samples", () => {
    expect(medianOf([])).toBeNull();
  });
});

describe("summarizeSamples", () => {
  it("records the five-point summary and the sample count, unsorted input welcome", () => {
    expect(summarizeSamples([40, 10, 30, 20])).toEqual({
      min: 10,
      p25: 17.5,
      median: 25,
      p75: 32.5,
      max: 40,
      samples: 4,
    });
  });

  it("is null over no samples rather than a summary of NaNs", () => {
    expect(summarizeSamples([])).toBeNull();
  });
});
