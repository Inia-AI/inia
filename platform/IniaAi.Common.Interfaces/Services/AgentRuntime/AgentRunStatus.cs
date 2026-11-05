using System.Text.Json.Serialization;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Lifecycle status of an agent runtime run. Wire values are snake_case strings;
/// any value this client does not recognise maps to <see cref="Unknown"/> instead of throwing,
/// so the sidecar can add statuses without breaking older platform builds.
/// </summary>
[JsonConverter(typeof(SnakeCaseEnumJsonConverter<AgentRunStatus>))]
public enum AgentRunStatus
{
    Unknown = 0,
    Running,
    AwaitingInput,
    Cancelling,
    Succeeded,
    Failed,
    Cancelled,
    Expired,
}

public static class AgentRunStatusExtensions
{
    public static bool IsTerminal(this AgentRunStatus status)
    {
        return status is AgentRunStatus.Succeeded
            or AgentRunStatus.Failed
            or AgentRunStatus.Cancelled
            or AgentRunStatus.Expired;
    }
}
