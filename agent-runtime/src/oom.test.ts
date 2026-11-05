import { describe, expect, it } from "bun:test";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { RUN_CHILD_OOM_SCORE_ADJ } from "./config.js";
import { biasOomScoreAdj } from "./oom.js";

describe("biasOomScoreAdj", () => {
  it("writes the run-child bias so the kernel prefers it over the supervisor", () => {
    const path = join(mkdtempSync(join(tmpdir(), "oom-")), "oom_score_adj");
    biasOomScoreAdj(path);
    expect(readFileSync(path, "utf8")).toBe(String(RUN_CHILD_OOM_SCORE_ADJ));
  });

  it("is best-effort: an unwritable path never throws", () => {
    expect(() => biasOomScoreAdj("/definitely/not/writable/oom_score_adj")).not.toThrow();
  });
});
