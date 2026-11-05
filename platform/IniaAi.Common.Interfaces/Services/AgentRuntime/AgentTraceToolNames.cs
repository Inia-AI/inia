namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Tool-name vocabulary of the trace pack's <c>tool_call</c> records (the sidecar SDK's tool
/// names, verbatim), mirrored so consumers branch on constants instead of scattering wire
/// literals. The set is open — the runtime's agent gains tools without notice — so readers
/// treat unrecognised names generically.
/// </summary>
public static class AgentTraceToolNames
{
    public const string BASH = "Bash";
    public const string READ = "Read";
}
