namespace IniaAi.WebApi.Contracts.Responses.AgentActivity;

/// <summary>
/// The receipt facts a settled activity feed collapses into. Every value derives from the
/// run's own data; counts are zero and lists empty until the trace carries the underlying
/// records.
/// </summary>
public sealed record AgentActivityReceiptDto
{
    /// <summary>
    /// Wall-clock seconds from run start to settle; absent while the run is live.
    /// </summary>
    public int? DurationSeconds { get; init; }

    /// <summary>
    /// The document names the run worked with, as the run itself named them.
    /// </summary>
    public required List<string> DocumentNames { get; init; }

    /// <summary>
    /// The names of the skills the run loaded.
    /// </summary>
    public required List<string> SkillNames { get; init; }

    /// <summary>
    /// How many document pages the agent viewed as images.
    /// </summary>
    public required int PagesViewed { get; init; }

    /// <summary>
    /// How many times the agent wrote and ran its own code.
    /// </summary>
    public required int CodeRuns { get; init; }

    /// <summary>
    /// How many checks the agent itself reported.
    /// </summary>
    public required int ChecksReported { get; init; }
}
