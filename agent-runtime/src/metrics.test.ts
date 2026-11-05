import { describe, expect, it } from "bun:test";
import {
  createEmfMetrics,
  emitRunSettledMetrics,
  emitRuntimeGauges,
  METRICS_NAMESPACE,
  type RunSettledMetricsSample,
  type RuntimeGaugeSample,
} from "./metrics.js";

function captureMetrics(now: () => number = () => 1_700_000_000_000): {
  lines: string[];
  metrics: ReturnType<typeof createEmfMetrics>;
} {
  const lines: string[] = [];
  const metrics = createEmfMetrics({ sink: (line) => lines.push(line), now });
  return { lines, metrics };
}

function parsedEmf(line: string): Record<string, unknown> {
  const parsed: unknown = JSON.parse(line);
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    throw new Error("metric line is not a JSON object");
  }
  return parsed as Record<string, unknown>;
}

function onlyLine(lines: string[]): Record<string, unknown> {
  expect(lines).toHaveLength(1);
  const line = lines[0];
  if (line === undefined) {
    throw new Error("no metric line captured");
  }
  return parsedEmf(line);
}

describe("createEmfMetrics", () => {
  it("emits a count of one as a valid EMF document", () => {
    const { lines, metrics } = captureMetrics();
    metrics.count("RunsStarted");
    const record = onlyLine(lines);
    expect(record["RunsStarted"]).toBe(1);
    expect(record["_aws"]).toEqual({
      Timestamp: 1_700_000_000_000,
      CloudWatchMetrics: [
        {
          Namespace: METRICS_NAMESPACE,
          Dimensions: [[]],
          Metrics: [{ Name: "RunsStarted", Unit: "Count" }],
        },
      ],
    });
  });

  it("places dimensions both in the directive and as root members", () => {
    const { lines, metrics } = captureMetrics();
    metrics.count("RunsSettled", { dimensions: { subtype: "succeeded" } });
    const record = onlyLine(lines);
    expect(record["subtype"]).toBe("succeeded");
    const aws = record["_aws"];
    expect(aws).toMatchObject({
      CloudWatchMetrics: [{ Dimensions: [["subtype"]] }],
    });
  });

  it("emits gauges with the requested unit and value", () => {
    const { lines, metrics } = captureMetrics();
    metrics.gauge("RetainedInputBytes", 1024, { unit: "Bytes" });
    const record = onlyLine(lines);
    expect(record["RetainedInputBytes"]).toBe(1024);
    expect(record["_aws"]).toMatchObject({
      CloudWatchMetrics: [{ Metrics: [{ Name: "RetainedInputBytes", Unit: "Bytes" }] }],
    });
  });

  it("carries extra properties as root members outside the metric directive", () => {
    const { lines, metrics } = captureMetrics();
    metrics.count("CapacityRefused", {
      dimensions: { reason: "upload_budget" },
      properties: { retryAfterSeconds: 5 },
    });
    const record = onlyLine(lines);
    expect(record["retryAfterSeconds"]).toBe(5);
    expect(record["_aws"]).toMatchObject({
      CloudWatchMetrics: [{ Metrics: [{ Name: "CapacityRefused", Unit: "Count" }] }],
    });
  });

  it("adds the undimensioned rollup beside the named set when a count asks to aggregate", () => {
    const { lines, metrics } = captureMetrics();
    metrics.count("RequestFailed", {
      dimensions: { route: "run_monitor", cause: "unhandled" },
      aggregate: true,
    });
    const record = onlyLine(lines);
    expect(record["RequestFailed"]).toBe(1);
    expect(record["route"]).toBe("run_monitor");
    expect(record["cause"]).toBe("unhandled");
    expect(record["_aws"]).toMatchObject({
      CloudWatchMetrics: [{ Dimensions: [[], ["route", "cause"]] }],
    });
  });

  it("never repeats the empty dimension set when an aggregating count carries no dimensions", () => {
    const { lines, metrics } = captureMetrics();
    metrics.count("RequestFailed", { aggregate: true });
    expect(onlyLine(lines)["_aws"]).toMatchObject({
      CloudWatchMetrics: [{ Dimensions: [[]] }],
    });
  });

  it("leaves a count that does not ask to aggregate on its named dimension set alone", () => {
    const { lines, metrics } = captureMetrics();
    metrics.count("CapacityRefused", { dimensions: { reason: "upload_budget" } });
    expect(onlyLine(lines)["_aws"]).toMatchObject({
      CloudWatchMetrics: [{ Dimensions: [["reason"]] }],
    });
  });
});

describe("emitRuntimeGauges", () => {
  it("emits every pool, queue, budget, and run-state gauge from one sample", () => {
    const { lines, metrics } = captureMetrics();
    const sample: RuntimeGaugeSample = {
      uidPoolFree: 30,
      uidPoolSize: 32,
      uidPoolRetained: 1,
      workflowSlotsFree: 26,
      chatSlotsHeld: 2,
      admissionQueueDepth: 2,
      workflowQueueDepth: 1,
      inflightUploadBytes: 100,
      retainedInputBytes: 200,
      retainedOutputBytes: 300,
      runsActive: 3,
      runsAwaitingInput: 1,
    };
    emitRuntimeGauges(metrics, sample);
    const byName = new Map<string, Record<string, unknown>>();
    for (const line of lines) {
      const record = parsedEmf(line);
      for (const key of Object.keys(record)) {
        if (key !== "_aws") {
          byName.set(key, record);
        }
      }
    }
    expect(byName.get("UidPoolFree")?.["UidPoolFree"]).toBe(30);
    expect(byName.get("UidPoolSize")?.["UidPoolSize"]).toBe(32);
    expect(byName.get("UidPoolRetained")?.["UidPoolRetained"]).toBe(1);
    expect(byName.get("WorkflowSlotsFree")?.["WorkflowSlotsFree"]).toBe(26);
    expect(byName.get("ChatSlotsHeld")?.["ChatSlotsHeld"]).toBe(2);
    expect(byName.get("AdmissionQueueDepth")?.["AdmissionQueueDepth"]).toBe(2);
    expect(byName.get("WorkflowQueueDepth")?.["WorkflowQueueDepth"]).toBe(1);
    expect(byName.get("InflightUploadBytes")?.["InflightUploadBytes"]).toBe(100);
    expect(byName.get("RetainedInputBytes")?.["RetainedInputBytes"]).toBe(200);
    expect(byName.get("RetainedOutputBytes")?.["RetainedOutputBytes"]).toBe(300);
    expect(byName.get("RunsActive")?.["RunsActive"]).toBe(3);
    expect(byName.get("RunsAwaitingInput")?.["RunsAwaitingInput"]).toBe(1);
  });
});

describe("emitRunSettledMetrics", () => {
  const sample: RunSettledMetricsSample = {
    status: "succeeded",
    durationMs: 44_000,
    costUsd: 0.0908,
    inputTokens: 40_600,
    outputTokens: 3_276,
    documentBytes: 2_400_000,
  };

  type EmittedMetric = { record: Record<string, unknown>; unit: string };

  function firstMetricDirective(record: Record<string, unknown>): { name: string; unit: string } {
    const aws = record["_aws"];
    if (typeof aws !== "object" || aws === null || Array.isArray(aws)) {
      throw new Error("metric line carried no _aws directive");
    }
    const cloudwatch = (aws as Record<string, unknown>)["CloudWatchMetrics"];
    if (!Array.isArray(cloudwatch)) {
      throw new Error("metric line carried no CloudWatchMetrics");
    }
    const first: unknown = cloudwatch[0];
    if (typeof first !== "object" || first === null) {
      throw new Error("metric line carried an empty CloudWatchMetrics");
    }
    const metricsList = (first as Record<string, unknown>)["Metrics"];
    if (!Array.isArray(metricsList)) {
      throw new Error("metric line carried no Metrics");
    }
    const metric: unknown = metricsList[0];
    if (typeof metric !== "object" || metric === null) {
      throw new Error("metric line carried an empty Metrics list");
    }
    const name = (metric as Record<string, unknown>)["Name"];
    const unit = (metric as Record<string, unknown>)["Unit"];
    if (typeof name !== "string" || typeof unit !== "string") {
      throw new Error("metric directive missing Name or Unit");
    }
    return { name, unit };
  }

  function metricsByName(lines: string[]): Map<string, EmittedMetric> {
    const byName = new Map<string, EmittedMetric>();
    for (const line of lines) {
      const record = parsedEmf(line);
      const directive = firstMetricDirective(record);
      byName.set(directive.name, { record, unit: directive.unit });
    }
    return byName;
  }

  function emitted(byName: Map<string, EmittedMetric>, name: string): EmittedMetric {
    const entry = byName.get(name);
    if (entry === undefined) {
      throw new Error(`metric ${name} was not emitted`);
    }
    return entry;
  }

  it("emits every per-run series dimensioned by terminal status", () => {
    const { lines, metrics } = captureMetrics();
    emitRunSettledMetrics(metrics, sample);
    const byName = metricsByName(lines);
    expect([...byName.keys()].sort()).toEqual([
      "RunCostUsd",
      "RunDocumentBytes",
      "RunDurationMs",
      "RunInputTokens",
      "RunOutputTokens",
    ]);
    for (const [name, entry] of byName) {
      expect(entry.record["status"]).toBe("succeeded");
      expect(typeof entry.record[name]).toBe("number");
    }
  });

  it("labels duration in milliseconds, bytes in bytes, tokens as counts, and cost unitless", () => {
    const { lines, metrics } = captureMetrics();
    emitRunSettledMetrics(metrics, sample);
    const byName = metricsByName(lines);
    expect(emitted(byName, "RunDurationMs").unit).toBe("Milliseconds");
    expect(emitted(byName, "RunDocumentBytes").unit).toBe("Bytes");
    expect(emitted(byName, "RunInputTokens").unit).toBe("Count");
    expect(emitted(byName, "RunOutputTokens").unit).toBe("Count");
    expect(emitted(byName, "RunCostUsd").unit).toBe("None");
  });

  it("carries the measured values on the document root", () => {
    const { lines, metrics } = captureMetrics();
    emitRunSettledMetrics(metrics, sample);
    const byName = metricsByName(lines);
    expect(emitted(byName, "RunDurationMs").record["RunDurationMs"]).toBe(44_000);
    expect(emitted(byName, "RunCostUsd").record["RunCostUsd"]).toBe(0.0908);
    expect(emitted(byName, "RunInputTokens").record["RunInputTokens"]).toBe(40_600);
    expect(emitted(byName, "RunOutputTokens").record["RunOutputTokens"]).toBe(3_276);
    expect(emitted(byName, "RunDocumentBytes").record["RunDocumentBytes"]).toBe(2_400_000);
  });
});
