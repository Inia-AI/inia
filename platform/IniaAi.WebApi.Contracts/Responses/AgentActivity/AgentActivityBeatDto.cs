using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.AgentActivity;

/// <summary>
/// One paced feed line of an agent block's activity digest.
/// </summary>
public sealed record AgentActivityBeatDto
{
    /// <summary>
    /// Seconds since the run started — an offset, never an absolute timestamp, so replays
    /// pace themselves without knowing wall time.
    /// </summary>
    public required int AtSeconds { get; init; }

    /// <summary>
    /// Index into the digest's phase list for the phase current at this beat.
    /// </summary>
    public required int PhaseIndex { get; init; }

    /// <summary>
    /// The rendering class: a calm line, a hero moment, or a reported check.
    /// </summary>
    public required AgentActivityBeatKind Kind { get; init; }

    /// <summary>
    /// The feed line itself. Agent-authored text where the run narrated; a neutral
    /// machinery label otherwise. Never a raw command.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Optional second line: document names, search terms, page numbers, working-file
    /// names, or the remainder of a long narration.
    /// </summary>
    public string? Detail { get; init; }
}
