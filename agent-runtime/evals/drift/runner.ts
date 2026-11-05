import { messageOf } from "../../src/errors.js";
import { RunFailedError, type RunFailure, type RunReportResult } from "../../src/run-report.js";
import type { DriftPrompt } from "./prompts.js";
import { type DriftRunRecord, harnessFailureOf, projectDriftRecord, skippedDriftRecord } from "./record.js";

export const DRIFT_REPEATS = 2;
export const DRIFT_MAX_TURNS = 10;
export const DRIFT_RUN_BUDGET_USD = 0.05;
export const DRIFT_DEADLINE_MS = 180_000;
export const DRIFT_SET_SPEND_CAP_USD = 0.15;

export type DriftRunOutcome = {
  result: RunReportResult | null;
  failure: RunFailure | null;
  tracePackText: string | null;
};

export type ExecuteDriftPromptFn = (input: {
  prompt: DriftPrompt;
  executionId: string;
}) => Promise<DriftRunOutcome>;

export type RunDriftSetInput = {
  prompts: readonly DriftPrompt[];
  repeats: number;
  batchSlug: string;
  spendCapUsd: number;
  maxTurns: number;
  execute: ExecuteDriftPromptFn;
  now?: () => number;
  onRecord?: (record: DriftRunRecord) => void;
};

export function driftExecutionId(batchSlug: string, promptId: string, repeat: number): string {
  return `drift-${batchSlug}-${promptId}-${repeat}`;
}

async function executeGuarded(
  execute: ExecuteDriftPromptFn,
  prompt: DriftPrompt,
  executionId: string,
): Promise<DriftRunOutcome> {
  try {
    return await execute({ prompt, executionId });
  } catch (error) {
    if (error instanceof RunFailedError) {
      return { result: null, failure: error.failure, tracePackText: null };
    }
    return { result: null, failure: harnessFailureOf(messageOf(error)), tracePackText: null };
  }
}

export async function runDriftSet(input: RunDriftSetInput): Promise<DriftRunRecord[]> {
  const now = input.now ?? Date.now;
  const records: DriftRunRecord[] = [];
  let spentUsd = 0;
  for (let repeat = 1; repeat <= input.repeats; repeat += 1) {
    for (const prompt of input.prompts) {
      let record: DriftRunRecord;
      if (spentUsd > input.spendCapUsd) {
        record = skippedDriftRecord(
          prompt.id,
          repeat,
          `run-set spend cap $${input.spendCapUsd.toFixed(2)} reached at $${spentUsd.toFixed(4)} before this run`,
        );
      } else {
        const executionId = driftExecutionId(input.batchSlug, prompt.id, repeat);
        const startedAtMs = now();
        const outcome = await executeGuarded(input.execute, prompt, executionId);
        const wallMs = now() - startedAtMs;
        record = projectDriftRecord({
          promptId: prompt.id,
          repeat,
          executionId,
          startedAt: new Date(startedAtMs).toISOString(),
          wallMs,
          expectedAnswer: prompt.expectedAnswer,
          maxTurns: input.maxTurns,
          result: outcome.result,
          failure: outcome.failure,
          tracePackText: outcome.tracePackText,
        });
        spentUsd += record.estimatedCostUsd;
      }
      records.push(record);
      input.onRecord?.(record);
    }
  }
  return records;
}
