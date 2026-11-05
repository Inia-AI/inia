namespace IniaAi.WebApi.Contracts.Responses.AgentActivity;

/// <summary>
/// The live activity of one agent block inside a polled workflow execution. Carried only by
/// the GET workflow-report endpoint while the process holds a live feed entry for the run;
/// after the entry expires the block's activity is served by the persisted digest endpoint.
/// </summary>
public sealed record AgentBlockActivityDto
{
    /// <summary>
    /// Gets the identifier of the executed agent block, the key for the persisted digest
    /// endpoint.
    /// </summary>
    public required string BlockId { get; init; }

    /// <summary>
    /// Gets the block's display name.
    /// </summary>
    public required string BlockName { get; init; }

    /// <summary>
    /// Gets the run this entry reports on. A verification group buys a run per block per round,
    /// so the block id alone does not identify a leg — the pair of this and
    /// <see cref="Round"/> does, and it is what a desk keys a per-leg row on.
    /// </summary>
    public required string RunExecutionId { get; init; }

    /// <summary>
    /// Gets the verification round the run belongs to, counting from one. One for every run
    /// outside a verification loop.
    /// </summary>
    public required int Round { get; init; }

    /// <summary>
    /// Gets the leg's role as a named value: <c>worker</c> or <c>verifier</c>. A desk reads it
    /// to say whose leg it is showing, so a group mid-loop is legible rather than a burst of
    /// unlabelled runs. The platform owns the mapping from its internal run-id letters, and a
    /// role it has not named arrives verbatim — a consumer must not render an unrecognised
    /// value as a worker.
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run behind this entry has settled — the signal
    /// to fetch the persisted digest for the final record.
    /// </summary>
    public required bool Settled { get; init; }

    /// <summary>
    /// Gets the latest projected digest; absent until the run's first mid-run projection.
    /// </summary>
    public AgentActivityDigestDto? Activity { get; init; }
}
