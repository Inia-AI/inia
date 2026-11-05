namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The path convention behind <see cref="AgentRunFileKind"/>: a run's published deliverables
/// live under <c>out/</c>, its reasoning record under <c>trace/</c>. Collected
/// <c>AgentRunTraceFile</c> rows persist no kind column — the prefix is the kind — so every
/// consumer that routes by kind (the activity projector, the verifier's check-file staging,
/// which must never stage a trace) reads this one constant.
/// </summary>
public static class AgentRunFilePaths
{
    public const string ARTIFACT_PREFIX = "out/";
}
