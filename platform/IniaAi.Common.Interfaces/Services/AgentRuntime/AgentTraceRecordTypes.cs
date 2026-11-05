namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The trace pack envelope's record-type vocabulary (the <c>type</c> field of each
/// <c>trace/trace.ndjson</c> line, written by the sidecar's trace writers), mirrored so
/// consumers branch on constants instead of scattering wire literals. The stream is open —
/// the sidecar may add record types — so readers skip unrecognised types instead of throwing.
/// </summary>
public static class AgentTraceRecordTypes
{
    public const string ENVELOPE = "envelope";
    public const string MESSAGE = "message";
    public const string MODEL_INFO = "model_info";

    /// <summary>
    /// A settled segment's own per-model token totals as the model SDK counted them, restored
    /// earlier segments taken out.
    /// </summary>
    public const string MODEL_USAGE = "model_usage";
    public const string STAGE = "stage";
    public const string SUBAGENTS = "subagents";
    public const string TOOL_CALL = "tool_call";
    public const string USAGE = "usage";
}
