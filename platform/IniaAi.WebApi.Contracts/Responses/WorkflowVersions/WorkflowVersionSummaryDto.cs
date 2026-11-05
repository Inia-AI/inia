namespace IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

/// <summary>
/// A single saved version in a workflow's history, shaped for the editor history list and the
/// "last saved" header chip. <see cref="ShortCode"/> is the user-facing version/ID code derived
/// from <see cref="ContentHash"/> (the full hash lets the editor compare its live content against
/// the head without another round-trip), and <see cref="CreatedByName"/> is the display name of
/// the author (falling back to the id). <see cref="PayloadTrimmedAt"/> is set once a version's
/// saved content has been cleared to reclaim storage; such a version still shows in history but
/// can no longer be opened or compared.
/// </summary>
public record WorkflowVersionSummaryDto(
    string Id,
    DateTime CreatedAtUtc,
    string CreatedByUserId,
    string CreatedByName,
    string ShortCode,
    string ContentHash,
    string? Narrative,
    DateTime? PayloadTrimmedAt)
{
    /// <summary>
    /// The preceding version in the type's chain, or null for the first version. Rows saved before
    /// the per-organization chains were merged carry a chronological pointer here, so it is chain
    /// order and never proof of which version an author edited from.
    /// </summary>
    public string? ParentVersionId { get; init; }

    /// <summary>
    /// A note saved with the version beside its generated summary, such as how many of the saved
    /// changes the editor agent proposed.
    /// </summary>
    public string? Note { get; init; }
}
