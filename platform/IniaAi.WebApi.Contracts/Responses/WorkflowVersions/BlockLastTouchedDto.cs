namespace IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

/// <summary>
/// The saved version in which a given block last changed, for the per-block "last changed
/// {when} by {who}" strip in the editor. <see cref="CreatedByName"/> is the author's display
/// name (falling back to the id).
/// </summary>
public record BlockLastTouchedDto(
    string ReplacementTag,
    string VersionId,
    string ShortCode,
    DateTime CreatedAtUtc,
    string CreatedByUserId,
    string CreatedByName,
    string? Narrative);
