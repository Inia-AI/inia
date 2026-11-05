namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// One entry in a shared component's append-only version history. Lineage is expressed through
/// content hashes and parent pointers; ordering is newest-first.
/// </summary>
/// <param name="VersionId">The version identifier.</param>
/// <param name="ContentHash">The content hash of this version's definition.</param>
/// <param name="ParentVersionId">The preceding version in the chain, or null for the first version.</param>
/// <param name="CreatedAtUtc">When the version was created.</param>
/// <param name="CreatedByUserId">Who created the version.</param>
/// <param name="CreatedByName">The author's display name, falling back to the raw user id when no user matches.</param>
/// <param name="PayloadAbsent">True when this version is a hash-only lineage row without stored content.</param>
/// <param name="Narrative">
/// The plain-language summary of what this version changed relative to its parent. Populated only
/// when narratives were requested and both this version's and its parent's payloads are stored;
/// the first version reads "First saved version".
/// </param>
public record SharedComponentVersionDto(
    string VersionId,
    string ContentHash,
    string? ParentVersionId,
    DateTime CreatedAtUtc,
    string CreatedByUserId,
    string CreatedByName,
    bool PayloadAbsent,
    string? Narrative = null);
