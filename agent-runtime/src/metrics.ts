export const METRICS_NAMESPACE = "AgentRuntime";

export type MetricUnit = "Count" | "Bytes" | "Milliseconds" | "None";

export type MetricDimensions = Record<string, string>;

export type MetricOptions = {
  dimensions?: MetricDimensions;
  properties?: Record<string, unknown>;
};

// `aggregate` publishes the empty dimension set beside the named one, because CloudWatch resolves
// an alarm against a single dimension set. Counts only: an undimensioned rollup of a gauge would
// average unrelated series into one meaningless statistic.
export type CountOptions = MetricOptions & { value?: number; aggregate?: boolean };

export type GaugeOptions = MetricOptions & { unit?: MetricUnit };

type EmitOptions = MetricOptions & { aggregate?: boolean };

export type Metrics = {
  count(name: string, options?: CountOptions): void;
  gauge(name: string, value: number, options?: GaugeOptions): void;
};

export type MetricsDeps = {
  sink?: (line: string) => void;
  now?: () => number;
  namespace?: string;
};

const stdoutSink = (line: string): void => {
  process.stdout.write(`${line}\n`);
};

export function createEmfMetrics(deps: MetricsDeps = {}): Metrics {
  const sink = deps.sink ?? stdoutSink;
  const now = deps.now ?? Date.now;
  const namespace = deps.namespace ?? METRICS_NAMESPACE;

  function emit(name: string, value: number, unit: MetricUnit, options: EmitOptions): void {
    const dimensions = options.dimensions ?? {};
    const dimensionKeys = Object.keys(dimensions);
    const dimensionSets =
      options.aggregate === true && dimensionKeys.length > 0 ? [[], dimensionKeys] : [dimensionKeys];
    const document = {
      _aws: {
        Timestamp: now(),
        CloudWatchMetrics: [
          {
            Namespace: namespace,
            Dimensions: dimensionSets,
            Metrics: [{ Name: name, Unit: unit }],
          },
        ],
      },
      [name]: value,
      ...dimensions,
      ...(options.properties ?? {}),
    };
    sink(JSON.stringify(document));
  }

  return {
    count: (name, options = {}) => emit(name, options.value ?? 1, "Count", options),
    gauge: (name, value, options = {}) => emit(name, value, options.unit ?? "Count", options),
  };
}

// The metrics sink is stdout, which this repo has already seen die with a synchronous EPIPE
// mid-process (ADR 0129). An emit that throws from inside a catch block reaches no handler but
// the process-level one, so telemetry is reported and dropped rather than allowed to escape.
export function createGuardedMetrics(metrics: Metrics, onFailure: (error: Error) => void): Metrics {
  const guard = (emit: () => void): void => {
    try {
      emit();
    } catch (error) {
      onFailure(error instanceof Error ? error : new Error(String(error)));
    }
  };
  return {
    count: (name, options) => guard(() => metrics.count(name, options)),
    gauge: (name, value, options) => guard(() => metrics.gauge(name, value, options)),
  };
}

export const createNoopMetrics = (): Metrics => ({ count: () => {}, gauge: () => {} });

export type RuntimeGaugeSample = {
  uidPoolFree: number;
  uidPoolSize: number;
  uidPoolRetained: number;
  workflowSlotsFree: number;
  chatSlotsHeld: number;
  admissionQueueDepth: number;
  workflowQueueDepth: number;
  inflightUploadBytes: number;
  retainedInputBytes: number;
  retainedOutputBytes: number;
  runsActive: number;
  runsAwaitingInput: number;
};

export const GAUGE_SAMPLE_INTERVAL_MS = 60_000;

export function emitRuntimeGauges(metrics: Metrics, sample: RuntimeGaugeSample): void {
  metrics.gauge("UidPoolFree", sample.uidPoolFree);
  metrics.gauge("UidPoolSize", sample.uidPoolSize);
  metrics.gauge("UidPoolRetained", sample.uidPoolRetained);
  metrics.gauge("WorkflowSlotsFree", sample.workflowSlotsFree);
  metrics.gauge("ChatSlotsHeld", sample.chatSlotsHeld);
  metrics.gauge("AdmissionQueueDepth", sample.admissionQueueDepth);
  metrics.gauge("WorkflowQueueDepth", sample.workflowQueueDepth);
  metrics.gauge("InflightUploadBytes", sample.inflightUploadBytes, { unit: "Bytes" });
  metrics.gauge("RetainedInputBytes", sample.retainedInputBytes, { unit: "Bytes" });
  metrics.gauge("RetainedOutputBytes", sample.retainedOutputBytes, { unit: "Bytes" });
  metrics.gauge("RunsActive", sample.runsActive);
  metrics.gauge("RunsAwaitingInput", sample.runsAwaitingInput);
}

export type RunSettledMetricsSample = {
  status: string;
  durationMs: number;
  costUsd: number;
  inputTokens: number;
  outputTokens: number;
  documentBytes: number;
};

export function emitRunSettledMetrics(metrics: Metrics, sample: RunSettledMetricsSample): void {
  const dimensions = { status: sample.status };
  metrics.gauge("RunDurationMs", sample.durationMs, { unit: "Milliseconds", dimensions });
  metrics.gauge("RunCostUsd", sample.costUsd, { unit: "None", dimensions });
  metrics.gauge("RunInputTokens", sample.inputTokens, { dimensions });
  metrics.gauge("RunOutputTokens", sample.outputTokens, { dimensions });
  metrics.gauge("RunDocumentBytes", sample.documentBytes, { unit: "Bytes", dimensions });
}
