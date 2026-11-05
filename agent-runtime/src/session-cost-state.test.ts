import { afterAll, describe, expect, it } from "bun:test";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  createRestoredSessionCostReader,
  createRestoredSessionModelUsageReader,
  restoredSessionCostUsdOf,
  restoredSessionModelUsageOf,
} from "./session-cost-state.js";

const TEST_ROOT = mkdtempSync(join(tmpdir(), "session-cost-state-tests-"));

afterAll(() => {
  rmSync(TEST_ROOT, { recursive: true, force: true });
});

function costStateLine(sessionId: string, totalCostUSD: unknown): string {
  return JSON.stringify({ type: "cost-state", sessionId, totalCostUSD, totalAPIDuration: 1 });
}

const USER_LINE = JSON.stringify({
  type: "user",
  sessionId: "s-1",
  message: { role: "user", content: "hi" },
});

describe("restoredSessionCostUsdOf", () => {
  it("reads the last cost-state record for the session, which is the one the CLI restores", () => {
    const transcript = [
      USER_LINE,
      costStateLine("s-1", 0.12),
      USER_LINE,
      costStateLine("s-1", 0.31),
      "",
    ].join("\n");
    expect(restoredSessionCostUsdOf(transcript, "s-1")).toBe(0.31);
  });

  it("reads zero from a transcript carrying no cost-state, as an older CLI wrote it", () => {
    expect(restoredSessionCostUsdOf(`${USER_LINE}\n`, "s-1")).toBe(0);
  });

  it("ignores another session's record, a malformed line, and a cost that is not a non-negative number", () => {
    const transcript = [
      costStateLine("s-1", 0.2),
      costStateLine("s-other", 9),
      "{not json",
      costStateLine("s-1", "0.5"),
      costStateLine("s-1", -1),
      costStateLine("s-1", Number.NaN),
    ].join("\n");
    expect(restoredSessionCostUsdOf(transcript, "s-1")).toBe(0.2);
  });
});

describe("createRestoredSessionCostReader", () => {
  const CWD = "/workspaces/run-a";
  const PROJECT_DIR = "-workspaces-run-a";

  function configDirWith(name: string, files: Record<string, string>): string {
    const configDir = join(TEST_ROOT, name);
    for (const [relative, content] of Object.entries(files)) {
      const path = join(configDir, relative);
      mkdirSync(join(path, ".."), { recursive: true });
      writeFileSync(path, content);
    }
    mkdirSync(configDir, { recursive: true });
    return configDir;
  }

  it("reads the transcript in the project dir the CLI derives from the run's cwd", () => {
    const configDir = configDirWith("derived", {
      [`projects/${PROJECT_DIR}/s-1.jsonl`]: `${costStateLine("s-1", 0.4)}\n`,
    });
    expect(createRestoredSessionCostReader({ configDir, cwd: CWD })("s-1")).toBe(0.4);
  });

  it("ignores a transcript for the same session under another project dir", () => {
    const configDir = configDirWith("other-project", {
      "projects/-workspaces-run-b/s-1.jsonl": `${costStateLine("s-1", 0.4)}\n`,
    });
    expect(createRestoredSessionCostReader({ configDir, cwd: CWD })("s-1")).toBe(0);
  });

  it("reads zero when the config dir holds no transcript for the session", () => {
    const configDir = configDirWith("absent", { [`projects/${PROJECT_DIR}/s-other.jsonl`]: "" });
    expect(createRestoredSessionCostReader({ configDir, cwd: CWD })("s-1")).toBe(0);
    expect(
      createRestoredSessionCostReader({ configDir: join(TEST_ROOT, "no-such-dir"), cwd: CWD })("s-1"),
    ).toBe(0);
  });

  it("reads zero rather than throwing when the projects path cannot be read as a dir", () => {
    const configDir = configDirWith("not-a-dir", { projects: "a file where the dir should be" });
    expect(createRestoredSessionCostReader({ configDir, cwd: CWD })("s-1")).toBe(0);
  });

  it("reads zero for a cwd long enough that the CLI would hash its project dir name", () => {
    const longCwd = `/workspaces/${"x".repeat(240)}`;
    const configDir = configDirWith("long-cwd", {
      [`projects/${longCwd.replace(/[^a-zA-Z0-9]/g, "-")}/s-1.jsonl`]: `${costStateLine("s-1", 0.4)}\n`,
    });
    expect(createRestoredSessionCostReader({ configDir, cwd: longCwd })("s-1")).toBe(0);
  });

  it("refuses a session id that is not a safe path segment rather than walking out of the project dir", () => {
    const configDir = configDirWith("unsafe", {
      "projects/escape.jsonl": `${costStateLine("../escape", 7)}\n`,
    });
    expect(createRestoredSessionCostReader({ configDir, cwd: CWD })("../escape")).toBe(0);
  });

  it("reads only the transcript's tail, where the CLI appends the record, and finds it there", () => {
    const padding = `${USER_LINE}\n`.repeat(200);
    const configDir = configDirWith("tail", {
      [`projects/${PROJECT_DIR}/s-1.jsonl`]: `${costStateLine("s-1", 0.1)}\n${padding}${costStateLine("s-1", 0.4)}\n`,
    });
    const reader = createRestoredSessionCostReader({ configDir, cwd: CWD, tailBytes: 1024 });
    expect(reader("s-1")).toBe(0.4);
  });

  it("never parses a partial first line of the tail as a record", () => {
    const record = costStateLine("s-1", 0.4);
    const configDir = configDirWith("partial", {
      [`projects/${PROJECT_DIR}/s-1.jsonl`]: `${record}\n${USER_LINE}\n`,
    });
    const tailBytes = USER_LINE.length + 1 + Math.floor(record.length / 2);
    expect(createRestoredSessionCostReader({ configDir, cwd: CWD, tailBytes })("s-1")).toBe(0);
  });
});

function costStateWithUsage(sessionId: string, outputTokens: number): string {
  return JSON.stringify({
    type: "cost-state",
    sessionId,
    totalCostUSD: 0.1,
    modelUsage: {
      "us.anthropic.claude-sonnet-5": {
        inputTokens: 10,
        outputTokens,
        cacheReadInputTokens: 100,
        cacheCreationInputTokens: 50,
        costUSD: 0.1,
      },
    },
  });
}

describe("restoredSessionModelUsageOf", () => {
  it("reads the per-model totals of the last cost-state record for the session", () => {
    const transcript = [costStateWithUsage("s-1", 5), USER_LINE, costStateWithUsage("s-1", 9), ""].join("\n");
    expect(restoredSessionModelUsageOf(transcript, "s-1")).toEqual(
      new Map([
        [
          "us.anthropic.claude-sonnet-5",
          { inputTokens: 10, outputTokens: 9, cacheReadInputTokens: 100, cacheCreationInputTokens: 50 },
        ],
      ]),
    );
  });

  it("reads no totals from a transcript without cost-state, or another session's", () => {
    expect(restoredSessionModelUsageOf(`${USER_LINE}\n`, "s-1")).toEqual(new Map());
    expect(restoredSessionModelUsageOf(`${costStateWithUsage("s-other", 9)}\n`, "s-1")).toEqual(new Map());
  });

  it("reads them from the transcript the CLI derives from the run's cwd", () => {
    const configDir = join(TEST_ROOT, "model-usage");
    mkdirSync(join(configDir, "projects", "-workspaces-run-a"), { recursive: true });
    writeFileSync(
      join(configDir, "projects", "-workspaces-run-a", "s-1.jsonl"),
      `${costStateWithUsage("s-1", 7)}\n`,
    );
    const reader = createRestoredSessionModelUsageReader({ configDir, cwd: "/workspaces/run-a" });
    expect(reader("s-1").get("us.anthropic.claude-sonnet-5")?.outputTokens).toBe(7);
    expect(reader("../escape")).toEqual(new Map());
  });
});
