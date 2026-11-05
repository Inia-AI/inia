import { SHA256_HEX_PATTERN } from "../src/hash.js";
import { createNarrower, isRecord, type Narrower } from "../src/narrow.js";
import {
  DEFAULT_FAIL_ABOVE_BASELINE_RATIO,
  DEFAULT_SPREAD_WARN_RATIO,
  DEFAULT_WARN_ABOVE_BASELINE_RATIO,
  type PerformanceConfig,
} from "./performance.js";

export class EvalParseError extends Error {}

export type Period = "FY0" | "FY_1";

export const PERIODS: readonly Period[] = ["FY0", "FY_1"];

export type PeriodValues = { FY0: number | null; FY_1: number | null };

export type AcceptedBasis = {
  id: string;
  basis: string;
  composition?: string[];
  values: PeriodValues;
};

export type ExpectedFixture = {
  fixture: string;
  document: string;
  currency: string;
  sourcePages: number[];
  notes: string[];
  anchors: Record<string, PeriodValues>;
  judgment: Record<string, { acceptedBases: AcceptedBasis[] }>;
  performance: PerformanceConfig | null;
};

export type ExtractedField = {
  values: PeriodValues;
  basis?: string;
  composition?: string[];
};

export type Citation = { asPrinted: string; page: number | null };

export type ExtractionDoc = {
  fields: Record<string, ExtractedField>;
  citations: Record<string, Citation>;
};

export type FieldGradeState = "correct" | "wrong" | "miss" | "hallucination";

export type FieldKind = "anchor" | "judgment" | "unexpected";

export type FieldGrade = {
  field: string;
  period: Period;
  kind: FieldKind;
  state: FieldGradeState;
  accepted: (number | null)[];
  actual: number | null;
  matchedBasisId: string | null;
  compositionMismatch: boolean;
};

export type FootingCheck = {
  name: string;
  period: Period;
  computed: number | null;
  reported: number | null;
  ok: boolean | null;
};

export type GradeSummary = { correct: number; wrong: number; miss: number; hallucination: number };

export type GradeReport = {
  fixture: string;
  fields: FieldGrade[];
  footings: FootingCheck[];
  summary: GradeSummary;
};

export type VarianceFlag = { field: string; period: Period; valuesSeen: (number | null)[] };

const parse: Narrower = createNarrower((message) => new EvalParseError(message));

function numberOrNullOf(record: Record<string, unknown>, key: string, label: string): number | null {
  const value = record[key];
  if (value === null) {
    return null;
  }
  if (typeof value !== "number" || !Number.isFinite(value)) {
    parse.fail(`${label}.${key} must be a finite number or null`);
  }
  return value;
}

function parsePeriodValues(value: unknown, label: string): PeriodValues {
  const record = parse.asRecord(value, label);
  return { FY0: numberOrNullOf(record, "FY0", label), FY_1: numberOrNullOf(record, "FY_1", label) };
}

function parseStringArray(value: unknown, label: string): string[] {
  return parse.requireArray(value, label).map((entry, index) => {
    if (typeof entry !== "string" || entry === "") {
      parse.fail(`${label}[${index}] must be a non-empty string`);
    }
    return entry;
  });
}

function parseAcceptedBasis(value: unknown, label: string): AcceptedBasis {
  const record = parse.asRecord(value, label);
  const composition =
    record["composition"] === undefined
      ? undefined
      : parseStringArray(record["composition"], `${label}.composition`);
  return {
    id: parse.requireString(record, "id", label),
    basis: parse.requireString(record, "basis", label),
    ...(composition === undefined ? {} : { composition }),
    values: parsePeriodValues(record["values"], `${label}.values`),
  };
}

function parseBaselineFigure(record: Record<string, unknown>, key: string, label: string): number {
  const figure = parse.requireNumber(record, key, label);
  if (figure <= 0) {
    parse.fail(`${label}.${key} must be positive`);
  }
  return figure;
}

function parseRatio(record: Record<string, unknown>, key: string, fallback: number): number {
  if (record[key] === undefined) {
    return fallback;
  }
  const ratio = parse.requireNumber(record, key, "performance");
  if (ratio < 1) {
    parse.fail(`performance.${key} must be at least 1`);
  }
  return ratio;
}

function parsePerformanceConfig(value: unknown): PerformanceConfig | null {
  if (value === undefined || value === null) {
    return null;
  }
  const record = parse.asRecord(value, "performance");
  const baselineRecord = parse.asRecord(record["baseline"], "performance.baseline");
  const promptSha256 = parse.requireString(baselineRecord, "promptSha256", "performance.baseline");
  if (!SHA256_HEX_PATTERN.test(promptSha256)) {
    parse.fail("performance.baseline.promptSha256 must be 64 lowercase hex characters");
  }
  const baseline = {
    p75DurationSeconds: parseBaselineFigure(baselineRecord, "p75DurationSeconds", "performance.baseline"),
    p75OutputTokens: parseBaselineFigure(baselineRecord, "p75OutputTokens", "performance.baseline"),
    promptSha256,
    modelPin: parse.requireString(baselineRecord, "modelPin", "performance.baseline"),
    sdkVersion: parse.requireString(baselineRecord, "sdkVersion", "performance.baseline"),
    runtimeVersion: parse.requireString(baselineRecord, "runtimeVersion", "performance.baseline"),
    measuredAt: parse.requireString(baselineRecord, "measuredAt", "performance.baseline"),
  };
  const failAboveBaselineRatio = parseRatio(
    record,
    "failAboveBaselineRatio",
    DEFAULT_FAIL_ABOVE_BASELINE_RATIO,
  );
  const warnAboveBaselineRatio = parseRatio(
    record,
    "warnAboveBaselineRatio",
    DEFAULT_WARN_ABOVE_BASELINE_RATIO,
  );
  if (failAboveBaselineRatio <= warnAboveBaselineRatio) {
    parse.fail("performance.failAboveBaselineRatio must exceed warnAboveBaselineRatio");
  }
  return {
    baseline,
    failAboveBaselineRatio,
    warnAboveBaselineRatio,
    spreadWarnRatio: parseRatio(record, "spreadWarnRatio", DEFAULT_SPREAD_WARN_RATIO),
  };
}

export function parseExpectedFixture(value: unknown): ExpectedFixture {
  const record = parse.asRecord(value, "expected fixture");
  const anchors: Record<string, PeriodValues> = {};
  for (const [field, values] of Object.entries(parse.asRecord(record["anchors"], "anchors"))) {
    anchors[field] = parsePeriodValues(values, `anchors.${field}`);
  }
  const judgment: Record<string, { acceptedBases: AcceptedBasis[] }> = {};
  for (const [field, entry] of Object.entries(parse.asRecord(record["judgment"], "judgment"))) {
    const bases = parse
      .requireArray(
        parse.asRecord(entry, `judgment.${field}`)["acceptedBases"],
        `judgment.${field}.acceptedBases`,
      )
      .map((basis, index) => parseAcceptedBasis(basis, `judgment.${field}.acceptedBases[${index}]`));
    if (bases.length === 0) {
      parse.fail(`judgment.${field}.acceptedBases must not be empty`);
    }
    judgment[field] = { acceptedBases: bases };
  }
  return {
    fixture: parse.requireString(record, "fixture", "expected fixture"),
    document: parse.requireString(record, "document", "expected fixture"),
    currency: parse.requireString(record, "currency", "expected fixture"),
    sourcePages: parse.requireArray(record["sourcePages"], "sourcePages").map((page, index) => {
      if (typeof page !== "number" || !Number.isInteger(page)) {
        parse.fail(`sourcePages[${index}] must be an integer`);
      }
      return page;
    }),
    notes: parseStringArray(record["notes"] ?? [], "notes"),
    anchors,
    judgment,
    performance: parsePerformanceConfig(record["performance"]),
  };
}

function parseCitation(value: unknown, label: string): Citation {
  if (typeof value === "string") {
    return { asPrinted: value, page: null };
  }
  const record = parse.asRecord(value, label);
  const asPrinted = parse.requireString(record, "as_printed", label);
  const page = record["page"];
  if (page === undefined || page === null) {
    return { asPrinted, page: null };
  }
  if (typeof page !== "number" || !Number.isInteger(page) || page < 1) {
    parse.fail(`${label}.page must be a positive integer or null`);
  }
  return { asPrinted, page };
}

export function parseExtractionDoc(value: unknown): ExtractionDoc {
  const record = parse.asRecord(value, "extraction");
  const fields: Record<string, ExtractedField> = {};
  for (const [field, entry] of Object.entries(parse.asRecord(record["fields"], "extraction.fields"))) {
    const fieldRecord = parse.asRecord(entry, `extraction.fields.${field}`);
    const basis =
      fieldRecord["basis"] === undefined
        ? undefined
        : parse.requireString(fieldRecord, "basis", `extraction.fields.${field}`);
    const composition =
      fieldRecord["composition"] === undefined
        ? undefined
        : parseStringArray(fieldRecord["composition"], `extraction.fields.${field}.composition`);
    fields[field] = {
      values: parsePeriodValues(fieldRecord["values"], `extraction.fields.${field}.values`),
      ...(basis === undefined ? {} : { basis }),
      ...(composition === undefined ? {} : { composition }),
    };
  }
  const citations: Record<string, Citation> = {};
  for (const [field, citation] of Object.entries(
    parse.asRecord(record["citations"] ?? {}, "extraction.citations"),
  )) {
    citations[field] = parseCitation(citation, `extraction.citations.${field}`);
  }
  return { fields, citations };
}

function balancedObjectEnd(output: string, start: number): number {
  let depth = 0;
  let inString = false;
  let escaped = false;
  for (let index = start; index < output.length; index += 1) {
    const char = output[index];
    if (inString) {
      if (escaped) {
        escaped = false;
      } else if (char === "\\") {
        escaped = true;
      } else if (char === '"') {
        inString = false;
      }
      continue;
    }
    if (char === '"') {
      inString = true;
    } else if (char === "{") {
      depth += 1;
    } else if (char === "}") {
      depth -= 1;
      if (depth === 0) {
        return index;
      }
    }
  }
  return -1;
}

// Models narrate around the JSON despite being told not to — recover the first balanced,
// extraction-shaped JSON object rather than trusting the naive outermost-brace slice.
function embeddedExtractionValue(output: string): unknown {
  for (let start = output.indexOf("{"); start !== -1; start = output.indexOf("{", start + 1)) {
    const end = balancedObjectEnd(output, start);
    if (end === -1) {
      continue;
    }
    let candidate: unknown;
    try {
      candidate = JSON.parse(output.slice(start, end + 1));
    } catch {
      continue;
    }
    if (isRecord(candidate) && "fields" in candidate) {
      return candidate;
    }
  }
  return undefined;
}

export function parseExtractionText(output: string): ExtractionDoc {
  const firstBrace = output.indexOf("{");
  const lastBrace = output.lastIndexOf("}");
  if (firstBrace === -1 || lastBrace <= firstBrace) {
    parse.fail("run output contains no JSON object");
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(output.slice(firstBrace, lastBrace + 1));
  } catch {
    parsed = embeddedExtractionValue(output);
    if (parsed === undefined) {
      parse.fail("run output is not valid JSON between its outermost braces");
    }
  }
  return parseExtractionDoc(parsed);
}

function gradeAnchor(
  field: string,
  period: Period,
  expected: number | null,
  actual: number | null,
): FieldGrade {
  const base = {
    field,
    period,
    kind: "anchor" as const,
    accepted: [expected],
    actual,
    matchedBasisId: null,
    compositionMismatch: false,
  };
  if (expected === null && actual === null) {
    return { ...base, state: "correct" };
  }
  if (expected === null || actual === null) {
    return { ...base, state: actual === null ? "miss" : "hallucination" };
  }
  return { ...base, state: expected === actual ? "correct" : "wrong" };
}

const NON_ALPHANUMERIC_RUN = /[^0-9a-z]+/g;

function normalizeComponent(component: string): string {
  return component.toLowerCase().replace(NON_ALPHANUMERIC_RUN, "_").replace(/^_|_$/g, "");
}

function sameComponentSet(left: string[], right: string[]): boolean {
  const leftSet = new Set(left.map(normalizeComponent));
  const rightSet = new Set(right.map(normalizeComponent));
  return leftSet.size === rightSet.size && [...leftSet].every((entry) => rightSet.has(entry));
}

function compositionClaimOf(extracted: ExtractedField | undefined): string[] | undefined {
  return extracted?.composition === undefined || extracted.composition.length === 0
    ? undefined
    : extracted.composition;
}

// Value decides the grade; a differing composition sets compositionMismatch but stays correct.
function gradeJudgment(
  field: string,
  period: Period,
  acceptedBases: AcceptedBasis[],
  extracted: ExtractedField | undefined,
): FieldGrade {
  const actual = extracted?.values[period] ?? null;
  const claimed = compositionClaimOf(extracted);
  const accepted = acceptedBases.map((basis) => basis.values[period]);
  const base = {
    field,
    period,
    kind: "judgment" as const,
    accepted,
    actual,
    compositionMismatch: false,
  };
  if (actual === null) {
    const anyValue = acceptedBases.some((basis) => basis.values[period] !== null);
    return { ...base, matchedBasisId: null, state: anyValue ? "miss" : "correct" };
  }
  const matched = acceptedBases.find((basis) => basis.values[period] === actual);
  if (matched !== undefined) {
    const compositionMismatch =
      claimed !== undefined &&
      matched.composition !== undefined &&
      !sameComponentSet(matched.composition, claimed);
    return { ...base, matchedBasisId: matched.id, state: "correct", compositionMismatch };
  }
  return { ...base, matchedBasisId: null, state: "wrong" };
}

type FootingRule = { name: string; total: string; parts: [string, string] };

const FOOTING_RULES: readonly FootingRule[] = [
  {
    name: "totalEquity=shareholdersEquity+nonControllingInterests",
    total: "totalEquity",
    parts: ["shareholdersEquity", "nonControllingInterests"],
  },
  {
    name: "totalAssets=totalLiabilities+totalEquity",
    total: "totalAssets",
    parts: ["totalLiabilities", "totalEquity"],
  },
];

function footingsOf(extraction: ExtractionDoc): FootingCheck[] {
  const checks: FootingCheck[] = [];
  for (const rule of FOOTING_RULES) {
    for (const period of PERIODS) {
      const [firstPart, secondPart] = rule.parts;
      const first = extraction.fields[firstPart]?.values[period] ?? null;
      const second = extraction.fields[secondPart]?.values[period] ?? null;
      const reported = extraction.fields[rule.total]?.values[period] ?? null;
      const computed = first === null || second === null ? null : first + second;
      checks.push({
        name: rule.name,
        period,
        computed,
        reported,
        ok: computed === null || reported === null ? null : computed === reported,
      });
    }
  }
  return checks;
}

export function gradeExtraction(expected: ExpectedFixture, extraction: ExtractionDoc): GradeReport {
  const fields: FieldGrade[] = [];
  for (const [field, values] of Object.entries(expected.anchors)) {
    for (const period of PERIODS) {
      fields.push(
        gradeAnchor(field, period, values[period], extraction.fields[field]?.values[period] ?? null),
      );
    }
  }
  for (const [field, { acceptedBases }] of Object.entries(expected.judgment)) {
    for (const period of PERIODS) {
      fields.push(gradeJudgment(field, period, acceptedBases, extraction.fields[field]));
    }
  }
  const knownFields = new Set([...Object.keys(expected.anchors), ...Object.keys(expected.judgment)]);
  for (const [field, extracted] of Object.entries(extraction.fields)) {
    if (knownFields.has(field)) {
      continue;
    }
    for (const period of PERIODS) {
      const actual = extracted.values[period];
      if (actual !== null) {
        fields.push({
          field,
          period,
          kind: "unexpected",
          state: "hallucination",
          accepted: [null],
          actual,
          matchedBasisId: null,
          compositionMismatch: false,
        });
      }
    }
  }
  const summary: GradeSummary = { correct: 0, wrong: 0, miss: 0, hallucination: 0 };
  for (const grade of fields) {
    summary[grade.state] += 1;
  }
  return { fixture: expected.fixture, fields, footings: footingsOf(extraction), summary };
}

export function varianceAcrossRuns(extractions: ExtractionDoc[]): VarianceFlag[] {
  const fieldNames = new Set<string>();
  for (const extraction of extractions) {
    for (const field of Object.keys(extraction.fields)) {
      fieldNames.add(field);
    }
  }
  const flags: VarianceFlag[] = [];
  for (const field of [...fieldNames].sort()) {
    for (const period of PERIODS) {
      const valuesSeen = extractions.map((extraction) => extraction.fields[field]?.values[period] ?? null);
      if (new Set(valuesSeen).size > 1) {
        flags.push({ field, period, valuesSeen });
      }
    }
  }
  return flags;
}
