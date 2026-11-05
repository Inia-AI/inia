namespace IniaAi.WebApi.Contracts.Responses.AgentActivity;

/// <summary>
/// One entry of the digest's phase vocabulary.
/// </summary>
public sealed record AgentActivityPhaseDto
{
    /// <summary>
    /// The phase name shown on the phase indicator.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// The honest reading state a client shows between beats while this phase is current.
    /// </summary>
    public required string GapLabel { get; init; }
}
