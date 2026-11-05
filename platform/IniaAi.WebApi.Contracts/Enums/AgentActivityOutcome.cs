namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How an agent run stands as of its projected activity digest. Mirror of
/// <c>IniaAi.Common.Interfaces.Services.AgentRuntime.Activity.AgentActivityOutcome</c> —
/// kept in lockstep by <c>AgentActivityDtoMapperTests</c>.
/// </summary>
public enum AgentActivityOutcome
{
    /// <summary>
    /// The trace carried no stage records — projection degraded, never errored.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The run has started and no terminal stage record has arrived yet.
    /// </summary>
    Running,

    /// <summary>
    /// The run settled well.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The run settled badly; the digest keeps the partial activity record.
    /// </summary>
    Failed,
}
