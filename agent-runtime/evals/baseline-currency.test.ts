import { describe, expect, it } from "bun:test";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { type ExpectedFixture, parseExpectedFixture } from "./grader.js";
import { CITATION_MODES, evalPromptSha256 } from "./runner.js";

const FIXTURES_DIR = fileURLToPath(new URL("fixtures", import.meta.url));

function fixtureNames(): string[] {
  return readdirSync(FIXTURES_DIR).filter((name) => existsSync(join(FIXTURES_DIR, name, "expected.json")));
}

function currentPromptSha256s(expected: ExpectedFixture): string[] {
  return CITATION_MODES.map((mode) => evalPromptSha256(expected, mode));
}

function miniFixture(): Record<string, unknown> {
  return {
    fixture: "mini",
    document: "mini.pdf",
    currency: "EUR",
    sourcePages: [1],
    notes: [],
    anchors: { totalAssets: { FY0: 100, FY_1: 90 } },
    judgment: {},
  };
}

function baselineWith(promptSha256: string): Record<string, unknown> {
  return {
    p75DurationSeconds: 30,
    p75OutputTokens: 4000,
    promptSha256,
    modelPin: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
    sdkVersion: "0.3.227",
    runtimeVersion: "0.3.1001",
    measuredAt: "2026-08-18",
  };
}

describe("stamped baselines stay current with the prompt the eval actually runs", () => {
  it("every stamped fixture baseline matches a prompt this repo still builds", () => {
    for (const name of fixtureNames()) {
      const expected = parseExpectedFixture(
        JSON.parse(readFileSync(join(FIXTURES_DIR, name, "expected.json"), "utf8")),
      );
      if (expected.performance === null) {
        continue;
      }
      expect(currentPromptSha256s(expected)).toContain(expected.performance.baseline.promptSha256);
    }
  });

  it("covers the fixtures that exist", () => {
    expect(fixtureNames()).toContain("greenvolt");
  });

  it("accepts a baseline stamped from the prompt as currently built", () => {
    const unstamped = parseExpectedFixture(miniFixture());
    const stamped = parseExpectedFixture({
      ...miniFixture(),
      performance: { baseline: baselineWith(evalPromptSha256(unstamped)) },
    });
    expect(currentPromptSha256s(stamped)).toContain(stamped.performance?.baseline.promptSha256 ?? "");
  });

  it("catches a baseline stamped against a prompt that no longer exists", () => {
    const stamped = parseExpectedFixture({
      ...miniFixture(),
      performance: { baseline: baselineWith("0".repeat(64)) },
    });
    expect(currentPromptSha256s(stamped)).not.toContain(stamped.performance?.baseline.promptSha256 ?? "");
  });
});
