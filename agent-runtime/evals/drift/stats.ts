export type SampleSummary = {
  min: number;
  p25: number;
  median: number;
  p75: number;
  max: number;
  samples: number;
};

const LOWER_QUARTILE = 0.25;
const MEDIAN_QUANTILE = 0.5;
const UPPER_QUARTILE = 0.75;

function sorted(values: readonly number[]): number[] {
  return [...values].sort((left, right) => left - right);
}

export function percentileOf(sortedValues: readonly number[], quantile: number): number {
  const last = sortedValues.length - 1;
  const position = last * quantile;
  const lowerIndex = Math.floor(position);
  const upperIndex = Math.ceil(position);
  const lower = sortedValues[lowerIndex] ?? 0;
  const upper = sortedValues[upperIndex] ?? lower;
  return lower + (upper - lower) * (position - lowerIndex);
}

export function medianOf(values: readonly number[]): number | null {
  if (values.length === 0) {
    return null;
  }
  return percentileOf(sorted(values), MEDIAN_QUANTILE);
}

export function summarizeSamples(values: readonly number[]): SampleSummary | null {
  if (values.length === 0) {
    return null;
  }
  const ordered = sorted(values);
  return {
    min: ordered[0] ?? 0,
    p25: percentileOf(ordered, LOWER_QUARTILE),
    median: percentileOf(ordered, MEDIAN_QUANTILE),
    p75: percentileOf(ordered, UPPER_QUARTILE),
    max: ordered[ordered.length - 1] ?? 0,
    samples: ordered.length,
  };
}
