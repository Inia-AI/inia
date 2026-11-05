import { spawn } from "node:child_process";
import type { Readable } from "node:stream";
import { type GateCommandResult, runQualityGates } from "./quality-gates.js";

function runCommand(command: readonly string[], captureOutput: boolean): Promise<GateCommandResult> {
  return new Promise((resolve, reject) => {
    const [executable, ...args] = command;
    if (executable === undefined) {
      reject(new Error("empty gate command"));
      return;
    }
    const pipedStd = captureOutput ? "pipe" : "inherit";
    const child = spawn(executable, args, { stdio: ["ignore", pipedStd, pipedStd] });
    const captured: Buffer[] = [];
    const tee = (source: Readable | null, sink: NodeJS.WriteStream): void => {
      source?.on("data", (chunk: Buffer) => {
        captured.push(chunk);
        sink.write(chunk);
      });
    };
    tee(child.stdout, process.stdout);
    tee(child.stderr, process.stderr);
    child.on("error", reject);
    child.on("close", (code) => {
      resolve({ exitCode: code ?? 1, output: Buffer.concat(captured).toString("utf8") });
    });
  });
}

const exitCode = await runQualityGates({
  runCommand,
  writeLine: (line) => console.log(line),
  annotate: process.env["GITHUB_ACTIONS"] === "true",
});
process.exit(exitCode);
