namespace IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

/// <summary>
/// A saved version with its full canonical workflow JSON, for loading a historical version back
/// into the editor or feeding a comparison.
/// </summary>
public record WorkflowVersionContentDto(
    string Id,
    string WorkflowTypeId,
    string ContentHash,
    string ShortCode,
    string WorkflowJson,
    DateTime CreatedAtUtc,
    string CreatedByUserId,
    string? Narrative)
{
    /// <summary>The preceding version in the type's chain (chain order, not edit provenance), or null for the first version.</summary>
    public string? ParentVersionId { get; init; }
}
