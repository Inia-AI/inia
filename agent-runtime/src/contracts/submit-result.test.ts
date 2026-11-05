import { describe, expect, test } from "bun:test";
import { ContractValidationError, parseSubmitResult } from "./submit-result.js";

function buildValidSubmission(): { resultId: string; data: unknown } {
  return {
    resultId: "bs",
    data: {
      currency_and_units: "DKK '000",
      "Cash and Short-Term Investments": { FY0: 3129709, "FY-1": 2960111 },
      "Accounts Receivable": { FY0: 4841694, "FY-1": 5113202 },
      Inventories: { FY0: 164936, "FY-1": 149818 },
      "Deferred Tax Liabilities": { FY0: 0, "FY-1": 0 },
      "Long-Term Debt": { FY0: null, "FY-1": null },
      "Total Current Assets": { FY0: 12682854, "FY-1": 13146366 },
    },
  };
}

describe("parseSubmitResult", () => {
  test("accepts a valid submission and returns it typed", () => {
    const submission = buildValidSubmission();
    const parsed = parseSubmitResult(submission);
    expect(parsed.resultId).toBe("bs");
    expect(parsed.data).toEqual(submission.data);
  });

  test("passes workflow data through unvalidated for the bundle schema to check", () => {
    const parsed = parseSubmitResult({ resultId: "cf", data: [1, "anything", null] });
    expect(parsed.data).toEqual([1, "anything", null]);
  });

  test("rejects a non-object body", () => {
    expect(() => parseSubmitResult("not an object")).toThrow(ContractValidationError);
  });

  test("rejects an unknown top-level field", () => {
    expect(() => parseSubmitResult({ ...buildValidSubmission(), audit: [] })).toThrow(
      ContractValidationError,
    );
  });

  test("rejects an empty resultId", () => {
    expect(() => parseSubmitResult({ ...buildValidSubmission(), resultId: "" })).toThrow(
      ContractValidationError,
    );
  });

  test("formats the message with the dotted field path, exactly", () => {
    expect(validationMessageFor({ ...buildValidSubmission(), resultId: 7 })).toBe(
      "resultId: Invalid input: expected string, received number",
    );
  });

  test("root-level failures carry no path prefix", () => {
    expect(validationMessageFor("not an object")).toBe("Invalid input: expected object, received string");
  });

  test("reports every problem in one message, joined", () => {
    expect(validationMessageFor({ ...buildValidSubmission(), resultId: "", audit: [] })).toBe(
      'resultId: Too small: expected string to have >=1 characters; Unrecognized key: "audit"',
    );
  });
});

function validationMessageFor(body: unknown): string {
  try {
    parseSubmitResult(body);
  } catch (error) {
    if (error instanceof ContractValidationError) {
      return error.message;
    }
  }
  throw new Error("expected the body to fail contract validation");
}
