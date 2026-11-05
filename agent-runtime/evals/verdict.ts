import type { FieldGrade, GradeReport, VarianceFlag } from "./grader.js";
import { type PerformanceConfig, performanceVerdict, type RunPerformanceMetrics } from "./performance.js";

export type GradedRunForVerdict = {
  file: string;
  report: GradeReport | null;
  citationMisses: string[];
};

export type EvalVerdictInput = {
  runs: GradedRunForVerdict[];
  variance: VarianceFlag[];
  anchorFields: string[];
  performance?: { config: PerformanceConfig; runs: RunPerformanceMetrics[] };
};

export type EvalVerdict = {
  tripped: boolean;
  reasons: string[];
  warnings: string[];
};

function offExpectedLine(file: string, offExpected: FieldGrade[]): string {
  const count = (state: FieldGrade["state"]) => offExpected.filter((grade) => grade.state === state).length;
  const named = offExpected.map((grade) => `${grade.field} ${grade.period}`).join(", ");
  return (
    `${file}: ${offExpected.length} field-period(s) off expected ` +
    `(wrong=${count("wrong")} miss=${count("miss")} hallucination=${count("hallucination")}): ${named}`
  );
}

export function evalVerdict(input: EvalVerdictInput): EvalVerdict {
  const reasons: string[] = [];
  const warnings: string[] = [];
  for (const run of input.runs) {
    if (run.report === null) {
      reasons.push(`${run.file}: ungradeable — no parseable extraction`);
      continue;
    }
    const off = run.report.fields.filter((grade) => grade.state !== "correct");
    if (off.length > 0) {
      reasons.push(offExpectedLine(run.file, off));
    }
    const compositionMismatches = run.report.fields.filter((grade) => grade.compositionMismatch);
    if (compositionMismatches.length > 0) {
      const named = compositionMismatches.map((grade) => `${grade.field} ${grade.period}`).join(", ");
      warnings.push(
        `${run.file}: value matched an accepted basis but the stated composition differs ` +
          `(observational, does not gate): ${named}`,
      );
    }
    const brokenFootings = run.report.footings.filter((footing) => footing.ok === false);
    if (brokenFootings.length > 0) {
      reasons.push(`${run.file}: ${brokenFootings.length} recomputed footing(s) broken`);
    }
    if (run.citationMisses.length > 0) {
      warnings.push(
        `${run.file}: citation check misses — quote not found as printed, or reported figure ` +
          `not in the quote (observational, does not gate): ` +
          run.citationMisses.join(", "),
      );
    }
  }
  if (input.performance !== undefined) {
    const findings = performanceVerdict(input.performance);
    reasons.push(...findings.reasons);
    warnings.push(...findings.warnings);
  }
  const anchorFieldSet = new Set(input.anchorFields);
  for (const flag of input.variance) {
    const line = `${flag.field} ${flag.period} varies across runs: ${flag.valuesSeen.join(", ")}`;
    if (anchorFieldSet.has(flag.field)) {
      reasons.push(`anchor ${line}`);
    } else {
      warnings.push(`judgment ${line}`);
    }
  }
  return { tripped: reasons.length > 0, reasons, warnings };
}
