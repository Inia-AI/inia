import { describe, expect, it } from "bun:test";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import {
  EvalParseError,
  type ExpectedFixture,
  type ExtractionDoc,
  gradeExtraction,
  parseExpectedFixture,
  parseExtractionDoc,
  parseExtractionText,
  varianceAcrossRuns,
} from "./grader.js";

const GREENVOLT_EXPECTED_PATH = fileURLToPath(new URL("./fixtures/greenvolt/expected.json", import.meta.url));

function greenvoltExpected(): ExpectedFixture {
  return parseExpectedFixture(JSON.parse(readFileSync(GREENVOLT_EXPECTED_PATH, "utf8")));
}

function minimalExpected(): ExpectedFixture {
  return {
    fixture: "mini",
    document: "mini.pdf",
    currency: "EUR",
    sourcePages: [1],
    notes: [],
    anchors: {
      totalAssets: { FY0: 100, FY_1: 90 },
      totalLiabilities: { FY0: 60, FY_1: 55 },
      totalEquity: { FY0: 40, FY_1: 35 },
      shareholdersEquity: { FY0: 30, FY_1: 25 },
      nonControllingInterests: { FY0: 10, FY_1: 10 },
    },
    performance: null,
    judgment: {
      shortTermDebt: {
        acceptedBases: [
          {
            id: "run1",
            basis: "current_borrowings_excl_leases",
            composition: ["bank_loans", "bonds"],
            values: { FY0: 20, FY_1: 18 },
          },
        ],
      },
    },
  };
}

function extractionOf(fields: ExtractionDoc["fields"]): ExtractionDoc {
  return { fields, citations: {} };
}

function perfectExtraction(): ExtractionDoc {
  return extractionOf({
    totalAssets: { values: { FY0: 100, FY_1: 90 } },
    totalLiabilities: { values: { FY0: 60, FY_1: 55 } },
    totalEquity: { values: { FY0: 40, FY_1: 35 } },
    shareholdersEquity: { values: { FY0: 30, FY_1: 25 } },
    nonControllingInterests: { values: { FY0: 10, FY_1: 10 } },
    shortTermDebt: { values: { FY0: 20, FY_1: 18 }, composition: ["bonds", "bank_loans"] },
  });
}

function gradeOf(
  report: ReturnType<typeof gradeExtraction>,
  field: string,
  period: "FY0" | "FY_1",
): { state: string; matchedBasisId: string | null; compositionMismatch: boolean } {
  const grade = report.fields.find((entry) => entry.field === field && entry.period === period);
  if (grade === undefined) {
    throw new Error(`no grade for ${field} ${period}`);
  }
  return grade;
}

describe("parseExpectedFixture", () => {
  it("round-trips the committed greenvolt fixture", () => {
    const expected = greenvoltExpected();
    expect(expected.fixture).toBe("greenvolt");
    expect(expected.document).toBe("greenvolt-ar-2025.pdf");
    expect(expected.currency).toBe("EUR");
    expect(expected.sourcePages).toEqual([267]);
    expect(expected.anchors["totalAssets"]).toEqual({ FY0: 4027553656, FY_1: 3318824587 });
    expect(expected.judgment["cashAndShortTermInvestments"]?.acceptedBases[0]).toMatchObject({
      id: "run1",
      basis: "cash_and_equivalents_only",
      values: { FY0: 204454491, FY_1: 326818129 },
    });
  });

  it("the greenvolt anchors foot: equity components sum and the balance sheet balances", () => {
    const { anchors } = greenvoltExpected();
    for (const period of ["FY0", "FY_1"] as const) {
      const shareholders = anchors["shareholdersEquity"]?.[period] ?? Number.NaN;
      const minority = anchors["nonControllingInterests"]?.[period] ?? Number.NaN;
      const equity = anchors["totalEquity"]?.[period] ?? Number.NaN;
      const liabilities = anchors["totalLiabilities"]?.[period] ?? Number.NaN;
      const assets = anchors["totalAssets"]?.[period] ?? Number.NaN;
      expect(shareholders + minority).toBe(equity);
      expect(liabilities + equity).toBe(assets);
    }
  });

  it("rejects fixtures with malformed values", () => {
    expect(() => parseExpectedFixture({ fixture: "x" })).toThrow(EvalParseError);
    expect(() =>
      parseExpectedFixture({
        fixture: "x",
        document: "x.pdf",
        currency: "EUR",
        sourcePages: [1],
        notes: [],
        anchors: { totalAssets: { FY0: "lots", FY_1: 1 } },
        judgment: {},
      }),
    ).toThrow(EvalParseError);
  });

  it("parses an optional performance block and defaults it to null when absent", () => {
    const base = {
      fixture: "x",
      document: "x.pdf",
      currency: "EUR",
      sourcePages: [1],
      notes: [],
      anchors: { totalAssets: { FY0: 1, FY_1: 1 } },
      judgment: {},
    };
    expect(parseExpectedFixture(base).performance).toBeNull();
    expect(parseExpectedFixture({ ...base, performance: null }).performance).toBeNull();
    const baseline = {
      p75DurationSeconds: 400,
      p75OutputTokens: 40000,
      promptSha256: "ab".repeat(32),
      modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
      sdkVersion: "0.3.227",
      runtimeVersion: "0.2.8",
      measuredAt: "2026-08-11",
    };
    const parsed = parseExpectedFixture({ ...base, performance: { baseline } }).performance;
    expect(parsed?.baseline).toEqual(baseline);
    expect(parsed?.failAboveBaselineRatio).toBe(1.5);
    expect(parsed?.warnAboveBaselineRatio).toBe(1.2);
    expect(parsed?.spreadWarnRatio).toBe(1.6);
    expect(() =>
      parseExpectedFixture({ ...base, performance: { baseline: { p75DurationSeconds: 400 } } }),
    ).toThrow(EvalParseError);
    expect(() =>
      parseExpectedFixture({
        ...base,
        performance: { baseline: { ...baseline, p75OutputTokens: 0 } },
      }),
    ).toThrow(EvalParseError);
    expect(() =>
      parseExpectedFixture({ ...base, performance: { baseline, failAboveBaselineRatio: 1.1 } }),
    ).toThrow(EvalParseError);
  });

  it("rejects a baseline with a missing or malformed measurement identity", () => {
    const base = {
      fixture: "x",
      document: "x.pdf",
      currency: "EUR",
      sourcePages: [1],
      notes: [],
      anchors: { totalAssets: { FY0: 1, FY_1: 1 } },
      judgment: {},
    };
    const baseline = {
      p75DurationSeconds: 400,
      p75OutputTokens: 40000,
      promptSha256: "ab".repeat(32),
      modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
      sdkVersion: "0.3.227",
      runtimeVersion: "0.2.8",
      measuredAt: "2026-08-11",
    };
    const { promptSha256, ...withoutPrompt } = baseline;
    expect(promptSha256).toBeString();
    expect(() => parseExpectedFixture({ ...base, performance: { baseline: withoutPrompt } })).toThrow(
      EvalParseError,
    );
    expect(() =>
      parseExpectedFixture({ ...base, performance: { baseline: { ...baseline, promptSha256: "xyz" } } }),
    ).toThrow(EvalParseError);
    const { modelPin, ...withoutModel } = baseline;
    expect(modelPin).toBeString();
    expect(() => parseExpectedFixture({ ...base, performance: { baseline: withoutModel } })).toThrow(
      EvalParseError,
    );
  });
});

describe("parseExtractionDoc", () => {
  it("accepts a valid extraction with basis and composition", () => {
    const doc = parseExtractionDoc({
      fields: {
        totalAssets: { values: { FY0: 1, FY_1: null } },
        shortTermDebt: { values: { FY0: 2, FY_1: 2 }, basis: "b", composition: ["x"] },
      },
      citations: { totalAssets: "Total assets 1" },
    });
    expect(doc.fields["totalAssets"]?.values).toEqual({ FY0: 1, FY_1: null });
    expect(doc.fields["shortTermDebt"]?.composition).toEqual(["x"]);
    expect(doc.citations["totalAssets"]).toEqual({ asPrinted: "Total assets 1", page: null });
  });

  it("accepts a page-and-line citation and keeps its page", () => {
    const doc = parseExtractionDoc({
      fields: { totalAssets: { values: { FY0: 1, FY_1: null } } },
      citations: { totalAssets: { page: 267, as_printed: "Total assets 1" } },
    });
    expect(doc.citations["totalAssets"]).toEqual({ asPrinted: "Total assets 1", page: 267 });
  });

  it("rejects a page-and-line citation whose page is not a positive integer", () => {
    const withPage = (page: unknown) => ({
      fields: { totalAssets: { values: { FY0: 1, FY_1: null } } },
      citations: { totalAssets: { page, as_printed: "Total assets 1" } },
    });
    expect(() => parseExtractionDoc(withPage("267"))).toThrow(EvalParseError);
    expect(() => parseExtractionDoc(withPage(2.5))).toThrow(EvalParseError);
    expect(() => parseExtractionDoc(withPage(0))).toThrow(EvalParseError);
  });

  it("rejects a citation object without as_printed", () => {
    expect(() =>
      parseExtractionDoc({
        fields: { totalAssets: { values: { FY0: 1, FY_1: null } } },
        citations: { totalAssets: { page: 267 } },
      }),
    ).toThrow(EvalParseError);
  });

  it("rejects non-numeric non-null values and non-object shapes", () => {
    expect(() => parseExtractionDoc(null)).toThrow(EvalParseError);
    expect(() => parseExtractionDoc({ fields: { a: { values: { FY0: "1", FY_1: null } } } })).toThrow(
      EvalParseError,
    );
  });
});

describe("gradeExtraction anchors", () => {
  it("grades exact matches correct and mismatches wrong", () => {
    const report = gradeExtraction(minimalExpected(), perfectExtraction());
    expect(gradeOf(report, "totalAssets", "FY0").state).toBe("correct");
    const off = perfectExtraction();
    const totalAssets = off.fields["totalAssets"];
    if (totalAssets === undefined) {
      throw new Error("fixture field missing");
    }
    totalAssets.values.FY0 = 101;
    expect(gradeOf(gradeExtraction(minimalExpected(), off), "totalAssets", "FY0").state).toBe("wrong");
  });

  it("splits null-mismatch into miss (extracted null) and hallucination (invented value)", () => {
    const expected = minimalExpected();
    expected.anchors["totalAssets"] = { FY0: 100, FY_1: null };
    const extraction = perfectExtraction();
    const totalAssets = extraction.fields["totalAssets"];
    if (totalAssets === undefined) {
      throw new Error("fixture field missing");
    }
    totalAssets.values = { FY0: null, FY_1: 90 };
    const report = gradeExtraction(expected, extraction);
    expect(gradeOf(report, "totalAssets", "FY0").state).toBe("miss");
    expect(gradeOf(report, "totalAssets", "FY_1").state).toBe("hallucination");
  });

  it("treats a field absent from the extraction as missed in every period", () => {
    const extraction = perfectExtraction();
    delete extraction.fields["totalAssets"];
    const report = gradeExtraction(minimalExpected(), extraction);
    expect(gradeOf(report, "totalAssets", "FY0").state).toBe("miss");
    expect(gradeOf(report, "totalAssets", "FY_1").state).toBe("miss");
  });

  it("flags fields the fixture never asked for as hallucinations", () => {
    const extraction = perfectExtraction();
    extraction.fields["surpriseField"] = { values: { FY0: 7, FY_1: null } };
    const report = gradeExtraction(minimalExpected(), extraction);
    expect(gradeOf(report, "surpriseField", "FY0").state).toBe("hallucination");
  });

  it("grades matching nulls as correct", () => {
    const expected = minimalExpected();
    expected.anchors["totalAssets"] = { FY0: 100, FY_1: null };
    const extraction = perfectExtraction();
    const totalAssets = extraction.fields["totalAssets"];
    if (totalAssets === undefined) {
      throw new Error("fixture field missing");
    }
    totalAssets.values = { FY0: 100, FY_1: null };
    expect(gradeOf(gradeExtraction(expected, extraction), "totalAssets", "FY_1").state).toBe("correct");
  });
});

describe("gradeExtraction judgment fields", () => {
  it("passes only on an accepted basis value and names the matched basis", () => {
    const report = gradeExtraction(minimalExpected(), perfectExtraction());
    const grade = gradeOf(report, "shortTermDebt", "FY0");
    expect(grade.state).toBe("correct");
    expect(grade.matchedBasisId).toBe("run1");
  });

  it("marks a value off every accepted basis as wrong", () => {
    const extraction = perfectExtraction();
    const shortTermDebt = extraction.fields["shortTermDebt"];
    if (shortTermDebt === undefined) {
      throw new Error("fixture field missing");
    }
    shortTermDebt.values.FY0 = 21;
    expect(gradeOf(gradeExtraction(minimalExpected(), extraction), "shortTermDebt", "FY0").state).toBe(
      "wrong",
    );
  });

  it("lets the value decide and flags a mismatched composition as corroboration failure, not wrong", () => {
    const extraction = perfectExtraction();
    const shortTermDebt = extraction.fields["shortTermDebt"];
    if (shortTermDebt === undefined) {
      throw new Error("fixture field missing");
    }
    shortTermDebt.composition = ["bank_loans", "trade_payables"];
    const report = gradeExtraction(minimalExpected(), extraction);
    const grade = gradeOf(report, "shortTermDebt", "FY0");
    expect(grade.state).toBe("correct");
    expect(grade.matchedBasisId).toBe("run1");
    expect(grade.compositionMismatch).toBe(true);
  });

  it("does not flag composition when the value is off every basis — wrong already says it", () => {
    const extraction = perfectExtraction();
    const shortTermDebt = extraction.fields["shortTermDebt"];
    if (shortTermDebt === undefined) {
      throw new Error("fixture field missing");
    }
    shortTermDebt.values.FY0 = 21;
    shortTermDebt.composition = ["bank_loans", "trade_payables"];
    const grade = gradeOf(gradeExtraction(minimalExpected(), extraction), "shortTermDebt", "FY0");
    expect(grade.state).toBe("wrong");
    expect(grade.compositionMismatch).toBe(false);
  });

  it("matches composition through spelling normalization, not literal tokens", () => {
    const extraction = perfectExtraction();
    const shortTermDebt = extraction.fields["shortTermDebt"];
    if (shortTermDebt === undefined) {
      throw new Error("fixture field missing");
    }
    shortTermDebt.composition = ["Bonds", "Bank loans"];
    const report = gradeExtraction(minimalExpected(), extraction);
    const grade = gradeOf(report, "shortTermDebt", "FY0");
    expect(grade.state).toBe("correct");
    expect(grade.matchedBasisId).toBe("run1");
    expect(grade.compositionMismatch).toBe(false);
  });

  it("treats an empty composition array as no composition claim", () => {
    const extraction = perfectExtraction();
    const shortTermDebt = extraction.fields["shortTermDebt"];
    if (shortTermDebt === undefined) {
      throw new Error("fixture field missing");
    }
    shortTermDebt.composition = [];
    expect(gradeOf(gradeExtraction(minimalExpected(), extraction), "shortTermDebt", "FY0").state).toBe(
      "correct",
    );
  });

  it("ignores composition when the extraction reports none", () => {
    const extraction = perfectExtraction();
    const shortTermDebt = extraction.fields["shortTermDebt"];
    if (shortTermDebt === undefined) {
      throw new Error("fixture field missing");
    }
    delete shortTermDebt.composition;
    expect(gradeOf(gradeExtraction(minimalExpected(), extraction), "shortTermDebt", "FY0").state).toBe(
      "correct",
    );
  });

  it("marks an extracted null as a miss when accepted bases carry values", () => {
    const extraction = perfectExtraction();
    const shortTermDebt = extraction.fields["shortTermDebt"];
    if (shortTermDebt === undefined) {
      throw new Error("fixture field missing");
    }
    shortTermDebt.values.FY_1 = null;
    expect(gradeOf(gradeExtraction(minimalExpected(), extraction), "shortTermDebt", "FY_1").state).toBe(
      "miss",
    );
  });
});

describe("gradeExtraction against the real greenvolt fixture", () => {
  it("passes a right number claimed on a different composition, flagging the mismatch", () => {
    const expected = greenvoltExpected();
    const extraction = extractionOf({
      cashAndShortTermInvestments: {
        values: { FY0: 204454491, FY_1: 326818129 },
        composition: ["restricted_cash"],
      },
    });
    const report = gradeExtraction(expected, extraction);
    for (const period of ["FY0", "FY_1"] as const) {
      const grade = gradeOf(report, "cashAndShortTermInvestments", period);
      expect(grade.state).toBe("correct");
      expect(grade.compositionMismatch).toBe(true);
    }
  });

  it("passes the same numbers on the verified composition, in any order", () => {
    const expected = greenvoltExpected();
    const verified = expected.judgment["goodwillAndIntangibleAssets"]?.acceptedBases[0]?.composition;
    if (verified === undefined) {
      throw new Error("the greenvolt goodwillAndIntangibleAssets basis must carry its verified composition");
    }
    const extraction = extractionOf({
      goodwillAndIntangibleAssets: {
        values: { FY0: 720589474, FY_1: 724099501 },
        composition: [...verified].reverse(),
      },
    });
    const report = gradeExtraction(expected, extraction);
    expect(gradeOf(report, "goodwillAndIntangibleAssets", "FY0")).toMatchObject({
      state: "correct",
      matchedBasisId: "run1",
    });
  });

  it("every greenvolt accepted basis names its composition so the guard is never inert", () => {
    const { judgment } = greenvoltExpected();
    for (const [field, entry] of Object.entries(judgment)) {
      for (const basis of entry.acceptedBases) {
        if (basis.composition === undefined || basis.composition.length === 0) {
          throw new Error(`greenvolt judgment field ${field} basis ${basis.id} has no composition`);
        }
      }
    }
  });
});

describe("gradeExtraction footings", () => {
  it("recomputes equity and balance footings from the extraction's own numbers", () => {
    const report = gradeExtraction(minimalExpected(), perfectExtraction());
    for (const footing of report.footings) {
      expect(footing.ok).toBe(true);
    }
    expect(report.footings.map((footing) => footing.name)).toContain(
      "totalEquity=shareholdersEquity+nonControllingInterests",
    );
    expect(report.footings.map((footing) => footing.name)).toContain(
      "totalAssets=totalLiabilities+totalEquity",
    );
  });

  it("fails the footing when the extraction's numbers do not foot", () => {
    const extraction = perfectExtraction();
    const equity = extraction.fields["totalEquity"];
    if (equity === undefined) {
      throw new Error("fixture field missing");
    }
    equity.values.FY0 = 41;
    const report = gradeExtraction(minimalExpected(), extraction);
    const broken = report.footings.filter((footing) => footing.ok === false);
    expect(broken.length).toBeGreaterThan(0);
  });

  it("leaves a footing undetermined when an input is null", () => {
    const extraction = perfectExtraction();
    const minority = extraction.fields["nonControllingInterests"];
    if (minority === undefined) {
      throw new Error("fixture field missing");
    }
    minority.values.FY0 = null;
    const report = gradeExtraction(minimalExpected(), extraction);
    const equityFooting = report.footings.find(
      (footing) =>
        footing.name === "totalEquity=shareholdersEquity+nonControllingInterests" && footing.period === "FY0",
    );
    expect(equityFooting?.ok).toBeNull();
  });
});

describe("gradeExtraction summary", () => {
  it("counts states across every graded field-period", () => {
    const report = gradeExtraction(minimalExpected(), perfectExtraction());
    expect(report.summary).toEqual({ correct: 12, wrong: 0, miss: 0, hallucination: 0 });
  });
});

describe("parseExtractionText", () => {
  const VALID = '{"fields":{"totalAssets":{"values":{"FY0":1,"FY_1":null}}},"citations":{}}';

  it("parses a bare JSON reply", () => {
    expect(parseExtractionText(VALID).fields["totalAssets"]?.values.FY0).toBe(1);
  });

  it("parses a reply wrapped in a fenced code block with prose around it", () => {
    const wrapped = `Here is the extraction:\n\`\`\`json\n${VALID}\n\`\`\`\nDone.`;
    expect(parseExtractionText(wrapped).fields["totalAssets"]?.values.FY0).toBe(1);
  });

  it("rejects output with no JSON object in it", () => {
    expect(() => parseExtractionText("I could not find the balance sheet.")).toThrow(EvalParseError);
  });

  it("recovers a fenced extraction when surrounding prose carries braces of its own", () => {
    const wrapped = `Note the shape {"example": 1} first.\n\`\`\`json\n${VALID}\n\`\`\`\nDone.`;
    expect(parseExtractionText(wrapped).fields["totalAssets"]?.values.FY0).toBe(1);
  });

  it("rejects output whose only JSON objects are not extractions", () => {
    expect(() => parseExtractionText('Only metadata here: {"example": 1}. Nothing else.')).toThrow(
      EvalParseError,
    );
  });

  it("recovers an extraction whose strings contain braces", () => {
    const tricky =
      'Prose {"example": 1} prose.\n' +
      '{"fields":{"totalAssets":{"values":{"FY0":1,"FY_1":null}}},"citations":{"totalAssets":"brace } in { quote"}}\n' +
      "Trailing prose.";
    expect(parseExtractionText(tricky).citations["totalAssets"]?.asPrinted).toBe("brace } in { quote");
  });
});

describe("varianceAcrossRuns", () => {
  it("returns no flags when every run agrees", () => {
    expect(varianceAcrossRuns([perfectExtraction(), perfectExtraction()])).toEqual([]);
  });

  it("flags differing field-periods with every value seen", () => {
    const second = perfectExtraction();
    const totalAssets = second.fields["totalAssets"];
    if (totalAssets === undefined) {
      throw new Error("fixture field missing");
    }
    totalAssets.values.FY0 = 99;
    const third = perfectExtraction();
    delete third.fields["totalAssets"];
    const flags = varianceAcrossRuns([perfectExtraction(), second, third]);
    expect(flags).toContainEqual({ field: "totalAssets", period: "FY0", valuesSeen: [100, 99, null] });
    expect(flags.some((flag) => flag.field === "totalLiabilities")).toBe(false);
  });
});
