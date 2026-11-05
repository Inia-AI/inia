import { describe, expect, it } from "bun:test";
import { existsSync, statSync } from "node:fs";
import { join } from "node:path";
import { sha256Hex } from "../../src/hash.js";
import { buildTraceEnvelope } from "../../src/run-child.js";
import { ENVELOPE_ABSENT } from "../../src/trace.js";
import {
  answerOf,
  DRIFT_FIXTURES_DIR,
  DRIFT_PROMPT_IDS,
  DRIFT_PROMPTS,
  driftPromptById,
  driftPromptSha256,
  driftStagedInputsOf,
  stagedInputsDigestOf,
} from "./prompts.js";

describe("the drift prompt set", () => {
  it("is exactly the three designed prompts, each with a load-bearing tool", () => {
    expect(DRIFT_PROMPTS.map((prompt) => prompt.id)).toEqual([...DRIFT_PROMPT_IDS]);
    expect(DRIFT_PROMPT_IDS).toEqual(["bash-loop", "pdf-read", "subagent"]);
    for (const prompt of DRIFT_PROMPTS) {
      expect(prompt.loadBearingTools.length).toBeGreaterThan(0);
      expect(driftPromptById(prompt.id)).toBe(prompt);
    }
    expect(driftPromptById("nope")).toBeUndefined();
  });

  it("stages the committed one-page PDF for pdf-read and nothing for the others", () => {
    const pdfRead = driftPromptById("pdf-read");
    expect(pdfRead?.document?.name).toBe("drift-page.pdf");
    const fixturePath = join(DRIFT_FIXTURES_DIR, pdfRead?.document?.sourceFile ?? "");
    expect(existsSync(fixturePath)).toBe(true);
    expect(statSync(fixturePath).size).toBeLessThan(16_384);
    expect(driftPromptById("bash-loop")?.document).toBeUndefined();
    expect(driftPromptById("subagent")?.document).toBeUndefined();
  });

  it("declares one agent for subagent only, named as the prompt asks the model to call it", () => {
    const subagent = driftPromptById("subagent");
    expect(subagent?.agents?.map((agent) => agent.name)).toEqual(["helper"]);
    expect(subagent?.prompt).toContain('"helper"');
    expect(subagent?.loadBearingTools).toEqual(["Task", "Agent"]);
    expect(driftPromptById("bash-loop")?.agents).toBeUndefined();
  });

  it("hashes the caller prompt the way the trace envelope does", () => {
    for (const prompt of DRIFT_PROMPTS) {
      expect(driftPromptSha256(prompt)).toBe(sha256Hex(new TextEncoder().encode(prompt.prompt)));
      expect(buildTraceEnvelope({ prompt: prompt.prompt, env: {} }).promptSha256).toBe(
        driftPromptSha256(prompt),
      );
    }
  });

  it("digests staged inputs the way the trace envelope does", () => {
    const pdfRead = driftPromptById("pdf-read");
    if (pdfRead === undefined) {
      throw new Error("pdf-read prompt missing");
    }
    const stagedInputs = driftStagedInputsOf(pdfRead);
    expect(stagedInputs).toHaveLength(1);
    expect(stagedInputs[0]?.sha256).toMatch(/^[0-9a-f]{64}$/);
    const envelope = buildTraceEnvelope({ prompt: pdfRead.prompt, env: {}, stagedInputs });
    expect(stagedInputsDigestOf(stagedInputs)).toBe(envelope.stagedInputsSha256);
    expect(stagedInputsDigestOf([])).toBe(ENVELOPE_ABSENT);
    expect(buildTraceEnvelope({ prompt: "x", env: {} }).stagedInputsSha256).toBe(ENVELOPE_ABSENT);
  });
});

describe("answerOf", () => {
  it("reads the numeric answer off the structured output and nothing else", () => {
    expect(answerOf({ answer: 339 })).toBe(339);
    expect(answerOf({ answer: "339" })).toBeNull();
    expect(answerOf({ product: 339 })).toBeNull();
    expect(answerOf(undefined)).toBeNull();
    expect(answerOf(null)).toBeNull();
  });
});
