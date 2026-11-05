namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// One entry in a workflow template's append-only version history. Ordering is newest first.
/// </summary>
/// <param name="VersionId">The version identifier.</param>
/// <param name="ContentHash">The canonical content hash of this version's workflow JSON.</param>
/// <param name="ParentVersionId">
/// The preceding version in the type's chain, or null for the first version. This is chain order:
/// versions saved before the per-organization chains were merged carry a chronological pointer, so
/// never read it as proof of which version the author edited from.
/// </param>
/// <param name="CreatedAtUtc">When the version was saved.</param>
/// <param name="CreatedByUserId">Who saved the version.</param>
/// <param name="CreatedByName">The author's display name, falling back to the raw user id when no user matches.</param>
/// <param name="PayloadStored">
/// False once retention has cleared this version's workflow JSON. Such a version still lists here,
/// but its content and comparisons, and the changes of any version that needs its content, answer
/// <c>WorkflowVersion.PayloadTrimmed</c>.
/// </param>
/// <param name="PayloadTrimmedAtUtc">When retention cleared the workflow JSON, or null while it is stored.</param>
/// <param name="Narrative">
/// The plain-language summary of what this save changed, generated and stored at save time. It
/// survives payload trimming. Null when the save produced none.
/// </param>
public record WorkflowTemplateVersionDto(
    string VersionId,
    string ContentHash,
    string? ParentVersionId,
    DateTime CreatedAtUtc,
    string CreatedByUserId,
    string CreatedByName,
    bool PayloadStored,
    DateTime? PayloadTrimmedAtUtc,
    string? Narrative);
