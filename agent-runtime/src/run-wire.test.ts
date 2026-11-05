import { describe, expect, it } from "bun:test";
import { sealCheckpoint } from "./checkpoint-store.js";
import { MAX_OUTPUT_FORMAT_CHARS, MAX_SYSTEM_PROMPT_CHARS } from "./config.js";
import { sha256Hex } from "./hash.js";
import {
  createLineSplitter,
  fromWireCheckpoint,
  parseWireAnswerFrame,
  parseWireChildFrame,
  parseWireRequest,
  parseWireResponse,
  toWireCheckpoint,
  toWireCheckpointDescriptor,
  WireError,
} from "./run-wire.js";

const CHECKPOINT_BUNDLE = sealCheckpoint(new TextEncoder().encode('{"sessionId":"s-1"}'));
const WIRE_CHECKPOINT = toWireCheckpoint(CHECKPOINT_BUNDLE);
const WIRE_CHECKPOINT_DESCRIPTOR = toWireCheckpointDescriptor(CHECKPOINT_BUNDLE);
const QUESTION = {
  id: "q-1",
  toolName: "mcp__platform__request_analyst_input",
  prompt: "Which fund?",
  input: { question: "Which fund?", draft: { candidates: 2 } },
};

describe("parseWireRequest", () => {
  it("accepts a well-formed start request", () => {
    const request = {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
    };
    expect(parseWireRequest(request)).toEqual({
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
    });
  });

  it("carries a per-run effort level on both request kinds and omits it when unset", () => {
    const start = {
      kind: "start" as const,
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
    };
    expect(parseWireRequest({ ...start, effort: "medium" })).toEqual({ ...start, effort: "medium" });
    expect("effort" in parseWireRequest(start)).toBe(false);
  });

  it("carries the run's retained-output cap on both request kinds and refuses a request without one", () => {
    const start = {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 512,
    };
    expect(parseWireRequest(start)).toEqual({
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 512,
    });
    for (const bad of [
      { ...start, maxRetainedOutputBytes: undefined },
      { ...start, maxRetainedOutputBytes: 0 },
      { ...start, maxRetainedOutputBytes: "512" },
    ]) {
      expect(() => parseWireRequest(bad)).toThrow(WireError);
    }
  });

  it("rejects requests missing the attempt or the document budget", () => {
    const start = {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
    };
    for (const bad of [
      { ...start, attempt: undefined },
      { ...start, attempt: 0 },
      { ...start, documentBudgetBytes: undefined },
      { ...start, documentBudgetBytes: 0 },
    ]) {
      expect(() => parseWireRequest(bad)).toThrow(WireError);
    }
  });

  it("accepts a well-formed resume request carrying the checkpoint, answer, and prior spend", () => {
    const request = {
      kind: "resume",
      executionId: "e-1",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0.25,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "the second fund",
      allowFurtherPause: false,
    };
    expect(parseWireRequest(request)).toEqual({
      kind: "resume",
      executionId: "e-1",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0.25,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "the second fund",
      allowFurtherPause: false,
    });
  });

  it("rejects malformed payloads instead of trusting the sender", () => {
    const start = {
      kind: "start",
      executionId: "e",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
    };
    const resume = {
      kind: "resume",
      executionId: "e",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "a",
      allowFurtherPause: true,
    };
    for (const bad of [
      null,
      "text",
      { ...start, kind: undefined },
      { ...start, kind: "other" },
      { ...start, executionId: undefined },
      { ...start, executionId: "" },
      { ...start, executionId: "a b/c" },
      { ...start, maxBudgetUsd: "5" },
      { ...start, deadlineMs: 0 },
      { ...start, maxBudgetUsd: 0 },
      { ...start, maxBudgetUsd: -1 },
      { ...start, maxTurns: undefined },
      { ...start, maxTurns: 0 },
      { ...start, maxTurns: -1 },
      { ...start, maxTurns: 1.5 },
      { ...start, maxTurns: "300" },
      { ...start, effort: "extreme" },
      { ...start, effort: 3 },
      { ...start, effort: "" },
      { ...resume, checkpoint: undefined },
      { ...resume, priorCostUsd: undefined },
      { ...resume, priorCostUsd: -0.1 },
      { ...resume, checkpoint: { sha256: "", sizeBytes: 2 } },
      { ...resume, checkpoint: { sha256: WIRE_CHECKPOINT_DESCRIPTOR.sha256, sizeBytes: 0 } },
      { ...resume, checkpoint: { sha256: WIRE_CHECKPOINT_DESCRIPTOR.sha256, sizeBytes: 1.5 } },
      { ...resume, checkpoint: { sha256: WIRE_CHECKPOINT_DESCRIPTOR.sha256, sizeBytes: 65 * 1024 * 1024 } },
      { ...resume, answer: 7 },
      { ...resume, answer: undefined },
      { ...resume, allowFurtherPause: undefined },
      { ...resume, allowFurtherPause: "yes" },
    ]) {
      expect(() => parseWireRequest(bad)).toThrow(WireError);
    }
  });
});

describe("parseWireRequest documents", () => {
  const WIRE_DOCUMENT = {
    name: "report.pdf",
    sha256: sha256Hex(Buffer.from("doc body")),
    sizeBytes: 8,
    mediaType: "application/pdf",
    documentClass: "AnnualReport",
  };

  it("carries documents on a start request", () => {
    const request = {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 200 * 1024 * 1024,
      maxRetainedOutputBytes: 1024,
      documents: [WIRE_DOCUMENT],
    };
    const parsed = parseWireRequest(request);
    if (parsed.kind !== "start") {
      throw new Error("expected a start request");
    }
    expect(parsed.documents).toEqual([WIRE_DOCUMENT]);
  });

  it("carries documents on a resume request", () => {
    const request = {
      kind: "resume",
      executionId: "e-1",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 200 * 1024 * 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "a",
      allowFurtherPause: true,
      documents: [WIRE_DOCUMENT],
    };
    const parsed = parseWireRequest(request);
    if (parsed.kind !== "resume") {
      throw new Error("expected a resume request");
    }
    expect(parsed.documents).toEqual([WIRE_DOCUMENT]);
  });

  it("requires every wire document to declare its sha256", () => {
    const { sha256: _dropped, ...withoutSha } = WIRE_DOCUMENT;
    const request = {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 200 * 1024 * 1024,
      maxRetainedOutputBytes: 1024,
      documents: [withoutSha],
    };
    expect(() => parseWireRequest(request)).toThrow(WireError);
  });

  it("rejects malformed wire documents, inline content included", () => {
    for (const documents of [
      "not-an-array",
      [{ sha256: "0".repeat(64), sizeBytes: 2 }],
      [{ name: "../escape", sha256: "0".repeat(64), sizeBytes: 2 }],
      [{ ...WIRE_DOCUMENT, contentBase64: "aGk=" }],
      [{ name: "report.pdf", sha256: "0".repeat(64) }],
      [WIRE_DOCUMENT, WIRE_DOCUMENT],
    ]) {
      const request = {
        kind: "start",
        executionId: "e-1",
        attempt: 1,
        prompt: "p",
        maxBudgetUsd: 5,
        maxTurns: 300,
        deadlineMs: 1000,
        documentBudgetBytes: 200 * 1024 * 1024,
        maxRetainedOutputBytes: 1024,
        documents,
      };
      expect(() => parseWireRequest(request)).toThrow(WireError);
    }
  });

  it("keeps a large frame intact when fed to the splitter in thousands of chunks", () => {
    const prompt = Array.from({ length: 64 * 1024 }, (_value, index) => String(index % 10)).join("");
    const frame = JSON.stringify({
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 200 * 1024 * 1024,
      maxRetainedOutputBytes: 1024,
      documents: [WIRE_DOCUMENT],
    });
    const lines: string[] = [];
    const feed = createLineSplitter((line) => lines.push(line));
    const chunkSize = Math.ceil(frame.length / 5000);
    for (let offset = 0; offset < frame.length; offset += chunkSize) {
      feed(frame.slice(offset, offset + chunkSize));
    }
    feed("\n");
    expect(lines).toHaveLength(1);
    const parsed = parseWireRequest(JSON.parse(lines[0] ?? "{}") as unknown);
    if (parsed.kind !== "start" || parsed.documents === undefined) {
      throw new Error("expected a start request with documents");
    }
    expect(parsed.prompt).toBe(prompt);
    expect(parsed.documents).toEqual([WIRE_DOCUMENT]);
  });
});

describe("parseWireRequest skillFiles", () => {
  const WIRE_SKILL = {
    name: "grading.md",
    sha256: sha256Hex(Buffer.from("# skill")),
    sizeBytes: 7,
  };

  function startRequestWith(extra: Record<string, unknown>): Record<string, unknown> {
    return {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 200 * 1024 * 1024,
      maxRetainedOutputBytes: 1024,
      ...extra,
    };
  }

  it("carries skillFiles on a start request", () => {
    const parsed = parseWireRequest(startRequestWith({ skillFiles: [WIRE_SKILL] }));
    if (parsed.kind !== "start") {
      throw new Error("expected a start request");
    }
    expect(parsed.skillFiles).toEqual([WIRE_SKILL]);
  });

  it("carries skillFiles on a resume request", () => {
    const parsed = parseWireRequest({
      kind: "resume",
      executionId: "e-1",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 200 * 1024 * 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "a",
      allowFurtherPause: true,
      skillFiles: [WIRE_SKILL],
    });
    if (parsed.kind !== "resume") {
      throw new Error("expected a resume request");
    }
    expect(parsed.skillFiles).toEqual([WIRE_SKILL]);
  });

  it("requires every wire skill file to declare its sha256", () => {
    const { sha256: _dropped, ...withoutSha } = WIRE_SKILL;
    expect(() => parseWireRequest(startRequestWith({ skillFiles: [withoutSha] }))).toThrow(WireError);
  });

  it("rejects skill files carrying document metadata, inline content, or unsafe names", () => {
    for (const skillFiles of [
      "not-an-array",
      [{ ...WIRE_SKILL, mediaType: "text/markdown" }],
      [{ ...WIRE_SKILL, documentClass: "Skill" }],
      [{ ...WIRE_SKILL, contentBase64: "aGk=" }],
      [{ ...WIRE_SKILL, name: "../escape" }],
      [WIRE_SKILL, WIRE_SKILL],
    ]) {
      expect(() => parseWireRequest(startRequestWith({ skillFiles }))).toThrow(WireError);
    }
  });
});

describe("parseWireRequest model", () => {
  function startRequest(extra: Record<string, unknown>): Record<string, unknown> {
    return {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      ...extra,
    };
  }

  it("carries the chosen model on start and resume requests, absent when unset", () => {
    const started = parseWireRequest(startRequest({ model: "us.anthropic.claude-sonnet-4-6" }));
    expect(started.model).toBe("us.anthropic.claude-sonnet-4-6");
    expect(parseWireRequest(startRequest({})).model).toBeUndefined();
    const resumed = parseWireRequest({
      kind: "resume",
      executionId: "e-1",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "a",
      allowFurtherPause: true,
      model: "us.anthropic.claude-sonnet-4-6",
    });
    expect(resumed.model).toBe("us.anthropic.claude-sonnet-4-6");
  });

  it("rejects a model that is not a non-empty string", () => {
    for (const model of [7, "", null, {}]) {
      expect(() => parseWireRequest(startRequest({ model }))).toThrow(WireError);
    }
  });
});

describe("parseWireRequest outputFormat", () => {
  function startRequest(extra: Record<string, unknown>): Record<string, unknown> {
    return {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      ...extra,
    };
  }

  it("carries the output format on start and resume requests, absent when unset", () => {
    const outputFormat = { type: "json_schema", schema: { type: "object" } };
    const started = parseWireRequest(startRequest({ outputFormat }));
    expect(started.outputFormat).toEqual({ type: "json_schema", schema: { type: "object" } });
    expect(parseWireRequest(startRequest({})).outputFormat).toBeUndefined();
    const resumed = parseWireRequest({
      kind: "resume",
      executionId: "e-1",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "a",
      allowFurtherPause: true,
      outputFormat,
    });
    expect(resumed.outputFormat).toEqual({ type: "json_schema", schema: { type: "object" } });
  });

  it("rejects malformed output formats", () => {
    for (const outputFormat of [7, "json_schema", { type: "json_schema" }, { type: "text", schema: {} }]) {
      expect(() => parseWireRequest(startRequest({ outputFormat }))).toThrow(WireError);
    }
  });

  it("re-enforces the schema size cap at the process boundary", () => {
    const outputFormat = {
      type: "json_schema",
      schema: { description: "x".repeat(MAX_OUTPUT_FORMAT_CHARS) },
    };
    expect(() => parseWireRequest(startRequest({ outputFormat }))).toThrow(WireError);
  });
});

describe("parseWireRequest agents", () => {
  const WIRE_AGENTS = [
    { name: "verifier", description: "checks figures", prompt: "verify", skills: ["skills:grading"] },
  ];

  function agentsRequest(extra: Record<string, unknown>): Record<string, unknown> {
    return {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 200 * 1024 * 1024,
      maxRetainedOutputBytes: 1024,
      ...extra,
    };
  }

  it("carries resolved agents on a start request and leaves them absent when undeclared", () => {
    const parsed = parseWireRequest(agentsRequest({ agents: WIRE_AGENTS }));
    expect(parsed.agents).toEqual(WIRE_AGENTS);
    expect(parseWireRequest(agentsRequest({})).agents).toBeUndefined();
  });

  it("refuses malformed declarations and unqualified skill names at the process boundary", () => {
    expect(() => parseWireRequest(agentsRequest({ agents: [{ name: "Bad Name" }] }))).toThrow(WireError);
    expect(() => parseWireRequest(agentsRequest({ agents: "verifier" }))).toThrow(WireError);
    expect(() =>
      parseWireRequest(
        agentsRequest({
          agents: [{ name: "verifier", description: "d", prompt: "p", skills: ["grading.md"] }],
        }),
      ),
    ).toThrow(/plugin-qualified/);
  });
});

describe("parseWireRequest systemPrompt", () => {
  function startRequest(extra: Record<string, unknown>): Record<string, unknown> {
    return {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      ...extra,
    };
  }

  it("carries the system prompt on start and resume requests, absent when unset", () => {
    const systemPrompt = ["static role and rules", "per-pass context"];
    expect(parseWireRequest(startRequest({ systemPrompt: "you are an analyst" })).systemPrompt).toBe(
      "you are an analyst",
    );
    expect(parseWireRequest(startRequest({})).systemPrompt).toBeUndefined();
    const resumed = parseWireRequest({
      kind: "resume",
      executionId: "e-1",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "a",
      allowFurtherPause: true,
      systemPrompt,
    });
    expect(resumed.systemPrompt).toEqual(systemPrompt);
  });

  it("rejects malformed system prompts, the preset variant included", () => {
    for (const systemPrompt of [7, "", [], ["ok", 7], { type: "preset", preset: "claude_code" }]) {
      expect(() => parseWireRequest(startRequest({ systemPrompt }))).toThrow(WireError);
    }
  });

  it("re-enforces the system prompt size cap at the process boundary", () => {
    const systemPrompt = "x".repeat(MAX_SYSTEM_PROMPT_CHARS);
    expect(() => parseWireRequest(startRequest({ systemPrompt }))).toThrow(WireError);
  });
});

describe("parseWireRequest mcpServerUrl and runToken", () => {
  function startRequest(extra: Record<string, unknown>): Record<string, unknown> {
    return {
      kind: "start",
      executionId: "e-1",
      attempt: 1,
      prompt: "p",
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      ...extra,
    };
  }

  it("carries the MCP URL and run token on start and resume requests, absent when unset", () => {
    const started = parseWireRequest(
      startRequest({ mcpServerUrl: "https://platform.internal/mcp", runToken: "tok-1" }),
    );
    expect(started.mcpServerUrl).toBe("https://platform.internal/mcp");
    expect(started.runToken).toBe("tok-1");
    const bare = parseWireRequest(startRequest({}));
    expect(bare.mcpServerUrl).toBeUndefined();
    expect(bare.runToken).toBeUndefined();
    const resumed = parseWireRequest({
      kind: "resume",
      executionId: "e-1",
      attempt: 1,
      maxBudgetUsd: 5,
      maxTurns: 300,
      deadlineMs: 1000,
      documentBudgetBytes: 1024,
      maxRetainedOutputBytes: 1024,
      priorCostUsd: 0,
      checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
      answer: "a",
      allowFurtherPause: true,
      mcpServerUrl: "https://platform.internal/mcp",
      runToken: "tok-1",
    });
    expect(resumed.mcpServerUrl).toBe("https://platform.internal/mcp");
    expect(resumed.runToken).toBe("tok-1");
  });

  it("re-verifies the MCP URL at the process boundary and rejects malformed tokens", () => {
    for (const mcpServerUrl of [7, "", "not-a-url", "ftp://host/mcp"]) {
      expect(() => parseWireRequest(startRequest({ mcpServerUrl }))).toThrow(WireError);
    }
    for (const runToken of [7, "", {}, "tok\n", "tok\r\nx: y", "tok en", "töken", "a".repeat(4097)]) {
      expect(() => parseWireRequest(startRequest({ runToken }))).toThrow(WireError);
    }
  });

  it("treats only undefined as absent — the wire does not pre-normalize nulls", () => {
    for (const field of ["runToken", "mcpServerUrl", "outputFormat", "systemPrompt", "effort"]) {
      expect(() => parseWireRequest(startRequest({ [field]: null }))).toThrow(WireError);
    }
  });
});

describe("parseWireAnswerFrame", () => {
  it("accepts an answer frame, the empty answer included", () => {
    expect(
      parseWireAnswerFrame({ kind: "answer", answer: "the second fund", allowFurtherPause: true }),
    ).toEqual({
      kind: "answer",
      answer: "the second fund",
      allowFurtherPause: true,
    });
    expect(parseWireAnswerFrame({ kind: "answer", answer: "", allowFurtherPause: false })).toEqual({
      kind: "answer",
      answer: "",
      allowFurtherPause: false,
    });
  });

  it("rejects frames that are not an answer", () => {
    for (const bad of [
      null,
      { answer: "a", allowFurtherPause: true },
      { kind: "paused", answer: "a", allowFurtherPause: true },
      { kind: "answer", allowFurtherPause: true },
      { kind: "answer", answer: 7, allowFurtherPause: true },
      { kind: "answer", answer: "a" },
      { kind: "answer", answer: "a", allowFurtherPause: "yes" },
    ]) {
      expect(() => parseWireAnswerFrame(bad)).toThrow(WireError);
    }
  });
});

const ROUND_TRIP_USAGE = {
  inputTokens: 0,
  outputTokens: 0,
  cacheCreationInputTokens: 0,
  cacheReadInputTokens: 0,
};

describe("parseWireChildFrame", () => {
  it("accepts a paused frame carrying the question, session, cost, and checkpoint", () => {
    const frame = {
      kind: "paused",
      question: QUESTION,
      sessionId: "s-1",
      estimatedCostUsd: 0.2,
      checkpoint: WIRE_CHECKPOINT,
    };
    expect(parseWireChildFrame(frame)).toEqual({
      kind: "paused",
      question: QUESTION,
      sessionId: "s-1",
      estimatedCostUsd: 0.2,
      checkpoint: WIRE_CHECKPOINT,
    });
  });

  it("accepts final frames wrapping both response shapes", () => {
    const success = {
      kind: "final",
      response: {
        ok: true,
        result: {
          executionId: "e",
          output: "o",
          estimatedCostUsd: 0.5,
          sessionId: "s",
          usage: ROUND_TRIP_USAGE,
        },
      },
    } as const;
    expect(parseWireChildFrame(success)).toEqual(success);
    const failure = {
      kind: "final",
      response: { ok: false, error: { message: "m", subtype: "x", estimatedCostUsd: 0, sessionId: null } },
    } as const;
    expect(parseWireChildFrame(failure)).toEqual(failure);
  });

  it("accepts a cost frame carrying the running estimate", () => {
    expect(parseWireChildFrame({ kind: "cost", estimatedCostUsd: 1.25, usage: ROUND_TRIP_USAGE })).toEqual({
      kind: "cost",
      estimatedCostUsd: 1.25,
      usage: ROUND_TRIP_USAGE,
    });
    expect(parseWireChildFrame({ kind: "cost", estimatedCostUsd: 0, usage: ROUND_TRIP_USAGE })).toEqual({
      kind: "cost",
      estimatedCostUsd: 0,
      usage: ROUND_TRIP_USAGE,
    });
  });

  it("rejects cost frames whose estimate is missing, negative, or not a finite number", () => {
    for (const bad of [
      { kind: "cost", usage: ROUND_TRIP_USAGE },
      { kind: "cost", estimatedCostUsd: -0.1, usage: ROUND_TRIP_USAGE },
      { kind: "cost", estimatedCostUsd: "1.25", usage: ROUND_TRIP_USAGE },
      { kind: "cost", estimatedCostUsd: Number.NaN, usage: ROUND_TRIP_USAGE },
      { kind: "cost", estimatedCostUsd: Number.POSITIVE_INFINITY },
    ]) {
      expect(() => parseWireChildFrame(bad)).toThrow(WireError);
    }
  });

  it("rejects frames that are neither paused nor final", () => {
    const paused = {
      question: QUESTION,
      sessionId: "s-1",
      estimatedCostUsd: 0,
      checkpoint: WIRE_CHECKPOINT,
    };
    for (const bad of [
      null,
      "text",
      { kind: "unknown" },
      { kind: "paused", ...paused, sessionId: "" },
      { kind: "paused", ...paused, question: "none" },
      { kind: "paused", ...paused, question: { ...QUESTION, id: "" } },
      { kind: "paused", ...paused, question: { ...QUESTION, toolName: undefined } },
      { kind: "paused", ...paused, question: { ...QUESTION, input: "not-a-record" } },
      { kind: "paused", ...paused, estimatedCostUsd: -0.2 },
      { kind: "paused", question: QUESTION, sessionId: "s-1", estimatedCostUsd: 0 },
      { kind: "final" },
      { kind: "final", response: { ok: true } },
    ]) {
      expect(() => parseWireChildFrame(bad)).toThrow(WireError);
    }
  });
});

describe("wire checkpoints", () => {
  it("round-trips a bundle through base64 with the hash intact", () => {
    const decoded = fromWireCheckpoint(WIRE_CHECKPOINT);
    expect(new TextDecoder().decode(decoded.bytes)).toBe('{"sessionId":"s-1"}');
    expect(decoded.sha256).toBe(CHECKPOINT_BUNDLE.sha256);
  });

  it("rejects a wire checkpoint whose bytes do not match the declared hash", () => {
    const tampered = {
      sha256: CHECKPOINT_BUNDLE.sha256,
      bytesBase64: Buffer.from("evil").toString("base64"),
    };
    expect(() => fromWireCheckpoint(tampered)).toThrow(WireError);
  });
});

describe("parseWireResponse", () => {
  it("accepts a success envelope", () => {
    const result = {
      executionId: "e-1",
      output: "o",
      estimatedCostUsd: 0.5,
      sessionId: "s",
      usage: ROUND_TRIP_USAGE,
    };
    expect(parseWireResponse({ ok: true, result })).toEqual({ ok: true, result });
  });

  it("accepts the empty sessionId a run with no session legitimately produces", () => {
    const result = {
      executionId: "e-1",
      output: "",
      estimatedCostUsd: 0,
      sessionId: "",
      usage: ROUND_TRIP_USAGE,
    };
    expect(parseWireResponse({ ok: true, result })).toEqual({ ok: true, result });
  });

  it("accepts a failure envelope carrying cost and session", () => {
    const error = {
      message: "boom",
      subtype: "error_max_turns",
      estimatedCostUsd: 0.25,
      sessionId: "s-2",
    };
    expect(parseWireResponse({ ok: false, error })).toEqual({ ok: false, error });
  });

  it("accepts a failure envelope with a null session", () => {
    const error = { message: "boom", subtype: "x", estimatedCostUsd: 0, sessionId: null };
    expect(parseWireResponse({ ok: false, error })).toEqual({ ok: false, error });
  });

  it("carries the trace pack path through when the result names one", () => {
    const result = {
      executionId: "e-1",
      output: "o",
      estimatedCostUsd: 0.5,
      sessionId: "s",
      usage: ROUND_TRIP_USAGE,
      tracePath: "/workspaces/traces/e-1/attempt-1",
    };
    expect(parseWireResponse({ ok: true, result })).toEqual({ ok: true, result });
  });

  it("omits tracePath entirely when the result has none", () => {
    const result = {
      executionId: "e-1",
      output: "o",
      estimatedCostUsd: 0.5,
      sessionId: "s",
      usage: ROUND_TRIP_USAGE,
    };
    const parsed = parseWireResponse({ ok: true, result });
    if (!parsed.ok) {
      throw new Error("unreachable");
    }
    expect("tracePath" in parsed.result).toBe(false);
  });

  it("carries the structured output through on success, absent when the result has none", () => {
    const result = {
      executionId: "e-1",
      output: "o",
      estimatedCostUsd: 0.5,
      sessionId: "s",
      usage: ROUND_TRIP_USAGE,
      structuredOutput: { verdict: "pass", criteria: [1, 2] },
    };
    expect(parseWireResponse({ ok: true, result })).toEqual({ ok: true, result });
    const bare = {
      executionId: "e-1",
      output: "o",
      estimatedCostUsd: 0.5,
      sessionId: "s",
      usage: ROUND_TRIP_USAGE,
    };
    const parsed = parseWireResponse({ ok: true, result: bare });
    if (!parsed.ok) {
      throw new Error("unreachable");
    }
    expect("structuredOutput" in parsed.result).toBe(false);
  });

  it("rejects a tracePath that is not a non-empty string", () => {
    for (const badTracePath of [42, "", {}]) {
      const result = {
        executionId: "e-1",
        output: "o",
        estimatedCostUsd: 0.5,
        sessionId: "s",
        tracePath: badTracePath,
      };
      expect(() => parseWireResponse({ ok: true, result })).toThrow(WireError);
    }
  });

  it("rejects envelopes that are neither shape", () => {
    for (const bad of [
      null,
      { ok: true },
      { ok: false },
      { ok: "true", result: {} },
      { ok: true, result: { executionId: "e", output: "o", estimatedCostUsd: "free", sessionId: "s" } },
      { ok: false, error: { message: "m", subtype: "s", estimatedCostUsd: 0 } },
    ]) {
      expect(() => parseWireResponse(bad)).toThrow(WireError);
    }
  });
});

describe("createLineSplitter", () => {
  it("reassembles lines split across chunks and ignores blank lines", () => {
    const lines: string[] = [];
    const feed = createLineSplitter((line) => lines.push(line));
    feed('{"kind":"pau');
    feed('sed"}\n\n{"kind":');
    feed('"final"}\n');
    expect(lines).toEqual(['{"kind":"paused"}', '{"kind":"final"}']);
  });

  it("rejects a line that exceeds its cap instead of buffering it forever", () => {
    const lines: string[] = [];
    const feed = createLineSplitter((line) => lines.push(line), 10);
    feed("short\n");
    expect(lines).toEqual(["short"]);
    expect(() => feed("x".repeat(20))).toThrow(WireError);
  });

  it("rejects an over-cap line even when its newline did arrive", () => {
    const lines: string[] = [];
    const feed = createLineSplitter((line) => lines.push(line), 10);
    expect(() => feed(`${"y".repeat(20)}\nshort\n`)).toThrow(WireError);
    expect(lines).toEqual([]);
  });

  it("holds an unterminated trailing line until its newline arrives", () => {
    const lines: string[] = [];
    const feed = createLineSplitter((line) => lines.push(line));
    feed("partial");
    expect(lines).toEqual([]);
    feed(" line\n");
    expect(lines).toEqual(["partial line"]);
  });

  it("reassembles a frame fed in thousands of chunks intact, then keeps splitting normally", () => {
    const lines: string[] = [];
    const feed = createLineSplitter((line) => lines.push(line));
    const chunk = "abcdefghij";
    const chunkCount = 5_000;
    for (let i = 0; i < chunkCount; i += 1) {
      feed(chunk);
    }
    feed("\nsecond line\nthird");
    feed(" line\n");
    expect(lines).toEqual([chunk.repeat(chunkCount), "second line", "third line"]);
  });
});

describe("token usage on the wire", () => {
  const USAGE = {
    inputTokens: 120,
    outputTokens: 900,
    cacheCreationInputTokens: 40,
    cacheReadInputTokens: 3_000,
  };

  it("accepts a cost frame carrying the running token usage", () => {
    expect(parseWireChildFrame({ kind: "cost", estimatedCostUsd: 1.25, usage: USAGE })).toEqual({
      kind: "cost",
      estimatedCostUsd: 1.25,
      usage: USAGE,
    });
  });

  it("rejects a cost frame whose usage is missing or malformed", () => {
    expect(() => parseWireChildFrame({ kind: "cost", estimatedCostUsd: 1 })).toThrow(WireError);
    expect(() =>
      parseWireChildFrame({ kind: "cost", estimatedCostUsd: 1, usage: { inputTokens: -1 } }),
    ).toThrow(WireError);
  });

  it("round-trips a success result's usage through the final response", () => {
    const response = {
      ok: true,
      result: {
        executionId: "e-1",
        output: "done",
        estimatedCostUsd: 0.5,
        sessionId: "s-1",
        usage: USAGE,
      },
    } as const;
    expect(parseWireResponse(response)).toEqual(response);
  });

  it("rejects a success result that lost its usage", () => {
    expect(() =>
      parseWireResponse({
        ok: true,
        result: { executionId: "e-1", output: "done", estimatedCostUsd: 0.5, sessionId: "s-1" },
      }),
    ).toThrow(WireError);
  });

  it("round-trips a failure's usage when the child knew it, and tolerates its absence", () => {
    const withUsage = {
      ok: false,
      error: {
        message: "boom",
        subtype: "deadline_exceeded",
        estimatedCostUsd: 0.3,
        sessionId: null,
        usage: USAGE,
      },
    } as const;
    expect(parseWireResponse(withUsage)).toEqual(withUsage);
    const withoutUsage = {
      ok: false,
      error: { message: "boom", subtype: "run_child_error", estimatedCostUsd: 0, sessionId: null },
    } as const;
    expect(parseWireResponse(withoutUsage)).toEqual(withoutUsage);
  });
});

describe("terminal checkpoint frames", () => {
  const TERMINAL_CHECKPOINT = sealCheckpoint(new TextEncoder().encode('{"sessionId":"s-9"}'));

  it("round-trips a terminal_checkpoint child frame", () => {
    const frame = {
      kind: "terminal_checkpoint",
      sessionId: "s-9",
      checkpoint: toWireCheckpoint(TERMINAL_CHECKPOINT),
    };
    expect(parseWireChildFrame(frame)).toEqual({
      kind: "terminal_checkpoint",
      sessionId: "s-9",
      checkpoint: {
        sha256: TERMINAL_CHECKPOINT.sha256,
        bytesBase64: Buffer.from(TERMINAL_CHECKPOINT.bytes).toString("base64"),
      },
    });
  });

  it("rejects a terminal_checkpoint frame missing its session id or checkpoint", () => {
    const checkpoint = toWireCheckpoint(TERMINAL_CHECKPOINT);
    expect(() => parseWireChildFrame({ kind: "terminal_checkpoint", checkpoint })).toThrow(WireError);
    expect(() => parseWireChildFrame({ kind: "terminal_checkpoint", sessionId: "s-9" })).toThrow(WireError);
    expect(() =>
      parseWireChildFrame({ kind: "terminal_checkpoint", sessionId: "s-9", checkpoint: { sha256: "x" } }),
    ).toThrow(WireError);
  });
});

describe("parseWireResponse budget-exhausted flag", () => {
  const BASE_RESULT = {
    executionId: "e-1",
    output: "",
    estimatedCostUsd: 2.58,
    sessionId: "s",
    usage: ROUND_TRIP_USAGE,
    structuredOutput: { status: "ok" },
  };

  it("carries the flag through on a salvaged success", () => {
    const result = { ...BASE_RESULT, budgetExhausted: true as const };
    expect(parseWireResponse({ ok: true, result })).toEqual({ ok: true, result });
  });

  it("leaves the flag absent when the result has none, and drops an explicit false", () => {
    const bare = parseWireResponse({ ok: true, result: BASE_RESULT });
    if (!bare.ok) {
      throw new Error("unreachable");
    }
    expect("budgetExhausted" in bare.result).toBe(false);
    const explicitFalse = parseWireResponse({ ok: true, result: { ...BASE_RESULT, budgetExhausted: false } });
    if (!explicitFalse.ok) {
      throw new Error("unreachable");
    }
    expect("budgetExhausted" in explicitFalse.result).toBe(false);
  });

  it("rejects a flag that is not a boolean", () => {
    for (const bad of ["true", 1, {}, []]) {
      const result = { ...BASE_RESULT, budgetExhausted: bad };
      expect(() => parseWireResponse({ ok: true, result })).toThrow(WireError);
    }
  });
});

describe("parseWireRequest continuation", () => {
  const resumeBase = {
    kind: "resume",
    executionId: "e-1",
    attempt: 1,
    maxBudgetUsd: 5,
    maxTurns: 300,
    deadlineMs: 1000,
    documentBudgetBytes: 1024,
    maxRetainedOutputBytes: 1024,
    priorCostUsd: 0,
    checkpoint: WIRE_CHECKPOINT_DESCRIPTOR,
    answer: "revise with these findings",
    allowFurtherPause: true,
  };

  const outFile = { name: "checks/model.py", sha256: "a".repeat(64), sizeBytes: 12 };

  it("carries the continuation's re-carried out/ set through, nested paths intact", () => {
    const parsed = parseWireRequest({ ...resumeBase, continuation: { outFiles: [outFile] } });
    if (parsed.kind !== "resume") {
      throw new Error("unreachable");
    }
    expect(parsed.continuation).toEqual({ outFiles: [outFile] });
  });

  it("accepts an explicitly empty re-carried set — a member that published nothing still continues", () => {
    const parsed = parseWireRequest({ ...resumeBase, continuation: { outFiles: [] } });
    if (parsed.kind !== "resume") {
      throw new Error("unreachable");
    }
    expect(parsed.continuation).toEqual({ outFiles: [] });
  });

  it("leaves an ordinary resume without the continuation marker", () => {
    const parsed = parseWireRequest(resumeBase);
    if (parsed.kind !== "resume") {
      throw new Error("unreachable");
    }
    expect(parsed.continuation).toBeUndefined();
  });

  it("rejects a name that walks, escapes, or loses its digest", () => {
    for (const bad of [
      { outFiles: [{ ...outFile, name: "../escape.py" }] },
      { outFiles: [{ ...outFile, name: "checks/../escape.py" }] },
      { outFiles: [{ ...outFile, name: "checks//model.py" }] },
      { outFiles: [{ ...outFile, name: "" }] },
      { outFiles: [{ name: outFile.name, sizeBytes: 12 }] },
      { outFiles: [{ ...outFile, sha256: "zz" }] },
      { outFiles: [outFile, outFile] },
      { outFiles: [outFile, { ...outFile, name: "checks" }] },
      {},
    ]) {
      expect(() => parseWireRequest({ ...resumeBase, continuation: bad })).toThrow(WireError);
    }
  });
});
