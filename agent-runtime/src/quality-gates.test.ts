import { describe, expect, test } from "bun:test";
import {
  type GateCommandResult,
  QUALITY_GATES,
  type RunGateCommand,
  runQualityGates,
  tscErrorAnnotations,
} from "./quality-gates.js";

const TSC_SINGLE_ERROR =
  "src/config.ts(287,3): error TS2322: Type 'number' is not assignable to type 'string'.";

describe("tscErrorAnnotations", () => {
  test("maps a plain tsc diagnostic to a file-addressed error annotation", () => {
    expect(tscErrorAnnotations(TSC_SINGLE_ERROR)).toEqual([
      "::error file=src/config.ts,line=287,col=3::TS2322: Type 'number' is not assignable to type 'string'.",
    ]);
  });

  test("joins indented continuation lines into the annotation message", () => {
    const output = [
      "src/server.ts(10,5): error TS2345: Argument of type 'A' is not assignable to parameter of type 'B'.",
      "  Types of property 'x' are incompatible.",
      "    Type 'string' is not assignable to type 'number'.",
    ].join("\n");
    expect(tscErrorAnnotations(output)).toEqual([
      "::error file=src/server.ts,line=10,col=5::TS2345: Argument of type 'A' is not assignable to parameter of type 'B'.%0A  Types of property 'x' are incompatible.%0A    Type 'string' is not assignable to type 'number'.",
    ]);
  });

  test("emits one annotation per diagnostic and ignores non-diagnostic lines", () => {
    const output = [
      TSC_SINGLE_ERROR,
      "src/main.ts(5,1): error TS6133: 'unused' is declared but its value is never read.",
      "",
      "Found 2 errors in 2 files.",
    ].join("\n");
    const annotations = tscErrorAnnotations(output);
    expect(annotations).toHaveLength(2);
    expect(annotations[1]).toBe(
      "::error file=src/main.ts,line=5,col=1::TS6133: 'unused' is declared but its value is never read.",
    );
  });

  test("escapes workflow-command control characters in the message", () => {
    const output = "src/a.ts(1,1): error TS0000: uses 100% of something";
    expect(tscErrorAnnotations(output)).toEqual([
      "::error file=src/a.ts,line=1,col=1::TS0000: uses 100%25 of something",
    ]);
  });

  test("emits a file-less annotation for a global diagnostic with no location", () => {
    const output = [
      "error TS2688: Cannot find type definition file for 'node'.",
      "  The file is in the program because:",
      "    Entry point of type library 'node' specified in compilerOptions",
      TSC_SINGLE_ERROR,
    ].join("\n");
    expect(tscErrorAnnotations(output)).toEqual([
      "::error::TS2688: Cannot find type definition file for 'node'.%0A  The file is in the program because:%0A    Entry point of type library 'node' specified in compilerOptions",
      "::error file=src/config.ts,line=287,col=3::TS2322: Type 'number' is not assignable to type 'string'.",
    ]);
  });

  test("returns nothing for clean output", () => {
    expect(tscErrorAnnotations("")).toEqual([]);
  });
});

function createFakeRunner(resultByGateScript: Record<string, GateCommandResult>): {
  commands: (readonly string[])[];
  captureFlags: boolean[];
  runCommand: RunGateCommand;
} {
  const commands: (readonly string[])[] = [];
  const captureFlags: boolean[] = [];
  return {
    commands,
    captureFlags,
    runCommand: (command, captureOutput) => {
      commands.push(command);
      captureFlags.push(captureOutput);
      const script = command[2] ?? "";
      return Promise.resolve(resultByGateScript[script] ?? { exitCode: 0, output: "" });
    },
  };
}

const ALL_GREEN: Record<string, GateCommandResult> = {
  typecheck: { exitCode: 0, output: "" },
  lint: { exitCode: 0, output: "" },
  test: { exitCode: 0, output: "" },
};

describe("runQualityGates", () => {
  test("runs every gate and exits zero when all pass", async () => {
    const runner = createFakeRunner(ALL_GREEN);
    const lines: string[] = [];
    const exitCode = await runQualityGates({
      runCommand: runner.runCommand,
      writeLine: (line) => lines.push(line),
      annotate: false,
    });
    expect(exitCode).toBe(0);
    expect(runner.commands.map((command) => command[2])).toEqual(["typecheck", "lint", "test"]);
    expect(lines.at(-1)).toContain("all gates passed");
  });

  test("keeps running later gates after an earlier one fails and reports every failure", async () => {
    const runner = createFakeRunner({
      ...ALL_GREEN,
      typecheck: { exitCode: 2, output: TSC_SINGLE_ERROR },
      test: { exitCode: 1, output: "1 fail" },
    });
    const lines: string[] = [];
    const exitCode = await runQualityGates({
      runCommand: runner.runCommand,
      writeLine: (line) => lines.push(line),
      annotate: false,
    });
    expect(exitCode).toBe(1);
    expect(runner.commands).toHaveLength(QUALITY_GATES.length);
    const summary = lines.at(-1) ?? "";
    expect(summary).toContain("typecheck");
    expect(summary).toContain("test");
    expect(summary).not.toContain("lint");
  });

  test("without annotate, gates run with their plain commands", async () => {
    const runner = createFakeRunner(ALL_GREEN);
    await runQualityGates({ runCommand: runner.runCommand, writeLine: () => {}, annotate: false });
    expect(runner.commands).toEqual([
      ["bun", "run", "typecheck"],
      ["bun", "run", "lint"],
      ["bun", "run", "test"],
    ]);
  });

  test("with annotate, each gate's extra reporter flags are appended", async () => {
    const runner = createFakeRunner(ALL_GREEN);
    await runQualityGates({ runCommand: runner.runCommand, writeLine: () => {}, annotate: true });
    expect(runner.commands).toEqual([
      ["bun", "run", "typecheck", "--pretty", "false"],
      ["bun", "run", "lint", "--reporter=github"],
      ["bun", "run", "test"],
    ]);
  });

  test("captures output only for gates whose annotations are parsed from it, only in annotate mode", async () => {
    const runner = createFakeRunner(ALL_GREEN);
    await runQualityGates({ runCommand: runner.runCommand, writeLine: () => {}, annotate: true });
    expect(runner.captureFlags).toEqual([true, false, false]);

    const plainRunner = createFakeRunner(ALL_GREEN);
    await runQualityGates({ runCommand: plainRunner.runCommand, writeLine: () => {}, annotate: false });
    expect(plainRunner.captureFlags).toEqual([false, false, false]);
  });

  test("with annotate, a typecheck failure is reprinted as error annotations", async () => {
    const runner = createFakeRunner({
      ...ALL_GREEN,
      typecheck: { exitCode: 2, output: TSC_SINGLE_ERROR },
    });
    const lines: string[] = [];
    const exitCode = await runQualityGates({
      runCommand: runner.runCommand,
      writeLine: (line) => lines.push(line),
      annotate: true,
    });
    expect(exitCode).toBe(1);
    expect(lines).toContain(
      "::error file=src/config.ts,line=287,col=3::TS2322: Type 'number' is not assignable to type 'string'.",
    );
  });

  test("without annotate, no workflow commands are written", async () => {
    const runner = createFakeRunner({
      ...ALL_GREEN,
      typecheck: { exitCode: 2, output: TSC_SINGLE_ERROR },
    });
    const lines: string[] = [];
    await runQualityGates({
      runCommand: runner.runCommand,
      writeLine: (line) => lines.push(line),
      annotate: false,
    });
    expect(lines.filter((line) => line.startsWith("::error"))).toEqual([]);
  });
});
