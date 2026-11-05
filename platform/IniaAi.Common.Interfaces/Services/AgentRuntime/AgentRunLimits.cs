namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// One fuse's deployed limit as the runtime's <c>GET /v1/limits</c> advertises it: the value a
/// run gets when it declares nothing, the largest value a run may declare, and the unit both
/// numbers are expressed in (<c>milliseconds</c>, <c>usd</c>, <c>turns</c>, or <c>bytes</c>).
/// </summary>
public sealed record AgentRunFuseLimit(decimal Default, decimal Ceiling, string Unit);

/// <summary>
/// The runtime's deployed per-run fuse limits, keyed exactly as the start request declares them.
/// Discovery only: the runtime's own 400 on run start stays the enforcement, and the start
/// response's effective-fuse echo stays the authoritative per-run record. Ceilings move with
/// runtime deployments, so these values are fetched and cached
/// (<see cref="IAgentRunLimitsProvider"/>), never hardcoded.
/// </summary>
public sealed record AgentRunLimits(
    AgentRunFuseLimit DeadlineMs,
    AgentRunFuseLimit MaxBudgetUsd,
    AgentRunFuseLimit MaxTurns,
    AgentRunFuseLimit MaxRetainedOutputBytes);
