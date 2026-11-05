namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The runtime's stage-event vocabulary as written on the trace wire (agent-runtime
/// <c>src/trace.ts</c> <c>StageEventName</c>). Pinned here so consumers stop matching
/// free-text event names; <c>gate.decision</c> is reserved by the runtime and not yet
/// emitted.
/// </summary>
public static class AgentTraceStageEvents
{
    public const string RUN_STARTED = "run.started";
    public const string RUN_COMPLETED = "run.completed";
    public const string RUN_FAILED = "run.failed";
    public const string GATE_DECISION = "gate.decision";
}
