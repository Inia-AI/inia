export interface QualityGate {
  readonly name: string;
  readonly annotateExtraArgs?: readonly string[];
  readonly annotationsFrom?: (output: string) => string[];
}

export const QUALITY_GATES: readonly QualityGate[] = [
  { name: "typecheck", annotateExtraArgs: ["--pretty", "false"], annotationsFrom: tscErrorAnnotations },
  { name: "lint", annotateExtraArgs: ["--reporter=github"] },
  { name: "test" },
];

export interface GateCommandResult {
  readonly exitCode: number;
  readonly output: string;
}

export type RunGateCommand = (
  command: readonly string[],
  captureOutput: boolean,
) => Promise<GateCommandResult>;

export interface RunQualityGatesDeps {
  readonly runCommand: RunGateCommand;
  readonly writeLine: (line: string) => void;
  readonly annotate: boolean;
}

const TSC_DIAGNOSTIC_BLOCK_PATTERN = /^(.+)\((\d+),(\d+)\): error (TS\d+): ([\s\S]+)$/;
const TSC_GLOBAL_DIAGNOSTIC_PATTERN = /^error (TS\d+): ([\s\S]+)$/;
const TSC_BLOCK_BOUNDARY = /\r?\n(?![ \t])/;

function escapeAnnotationMessage(message: string): string {
  return message.replaceAll("%", "%25").replaceAll("\r", "%0D").replaceAll("\n", "%0A");
}

function escapeAnnotationProperty(value: string): string {
  return escapeAnnotationMessage(value).replaceAll(":", "%3A").replaceAll(",", "%2C");
}

export function tscErrorAnnotations(output: string): string[] {
  const annotations: string[] = [];
  for (const block of output.split(TSC_BLOCK_BOUNDARY)) {
    const located = TSC_DIAGNOSTIC_BLOCK_PATTERN.exec(block);
    if (located !== null) {
      const [, file = "", line = "", col = "", code = "", message = ""] = located;
      const location = `file=${escapeAnnotationProperty(file)},line=${line},col=${col}`;
      annotations.push(`::error ${location}::${escapeAnnotationMessage(`${code}: ${message}`)}`);
      continue;
    }
    const global = TSC_GLOBAL_DIAGNOSTIC_PATTERN.exec(block);
    if (global !== null) {
      const [, code = "", message = ""] = global;
      annotations.push(`::error::${escapeAnnotationMessage(`${code}: ${message}`)}`);
    }
  }
  return annotations;
}

export async function runQualityGates(deps: RunQualityGatesDeps): Promise<number> {
  const failedGates: string[] = [];
  for (const gate of QUALITY_GATES) {
    deps.writeLine(`[check] ${gate.name}`);
    const extraArgs = deps.annotate ? (gate.annotateExtraArgs ?? []) : [];
    const captureOutput = deps.annotate && gate.annotationsFrom !== undefined;
    const result = await deps.runCommand(["bun", "run", gate.name, ...extraArgs], captureOutput);
    if (result.exitCode !== 0) {
      failedGates.push(gate.name);
      if (deps.annotate && gate.annotationsFrom !== undefined) {
        for (const annotation of gate.annotationsFrom(result.output)) {
          deps.writeLine(annotation);
        }
      }
      deps.writeLine(`[check] ${gate.name} failed (exit ${result.exitCode})`);
    }
  }
  if (failedGates.length === 0) {
    deps.writeLine("[check] all gates passed");
    return 0;
  }
  deps.writeLine(`[check] failing gates: ${failedGates.join(", ")}`);
  return 1;
}
