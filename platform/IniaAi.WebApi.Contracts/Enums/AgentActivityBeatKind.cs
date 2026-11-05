namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// The rendering class of one activity beat. Mirror of
/// <c>IniaAi.Common.Interfaces.Services.AgentRuntime.Activity.AgentActivityBeatKind</c> —
/// kept in lockstep by <c>AgentActivityDtoMapperTests</c>.
/// </summary>
public enum AgentActivityBeatKind
{
    /// <summary>
    /// Reserved forward-compatibility default; never emitted by the current projection.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A calm feed line.
    /// </summary>
    Normal,

    /// <summary>
    /// A moment the capability lands: the agent ran code it wrote, or viewed a page image.
    /// </summary>
    Hero,

    /// <summary>
    /// A verification the agent itself reported.
    /// </summary>
    Check,
}
