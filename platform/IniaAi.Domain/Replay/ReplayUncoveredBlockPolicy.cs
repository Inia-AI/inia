namespace IniaAi.Domain.Replay;

/// <summary>
/// What a replay run does with a freeze-eligible block that has no captured output.
/// </summary>
public enum ReplayUncoveredBlockPolicy
{
    /// <summary>
    /// The dormant regression path: the block is skipped without a live call and the run is
    /// recorded as inconclusive.
    /// </summary>
    SkipAsInconclusive = 0,

    /// <summary>
    /// The freeze submission option: the block runs live and the execution discloses replay
    /// coverage (<c>FreezeReplayedCount</c> of <c>FreezeEligibleCount</c>).
    /// </summary>
    RunLive = 1,
}
