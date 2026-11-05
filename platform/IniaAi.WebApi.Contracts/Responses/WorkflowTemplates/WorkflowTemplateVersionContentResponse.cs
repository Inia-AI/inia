using System.Text.Json;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// One stored version's workflow template JSON, in the shape of the head content read. The
/// <paramref name="VersionId"/> here identifies the historical version; it is not the
/// <c>baseVersionId</c> for a save unless this version is also the current head.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type identifier.</param>
/// <param name="VersionId">The version whose content this is.</param>
/// <param name="ContentHash">The canonical content hash of this version.</param>
/// <param name="ParentVersionId">The preceding version in the type's chain (chain order, not edit provenance), or null for the first version.</param>
/// <param name="CreatedAtUtc">When the version was saved.</param>
/// <param name="CreatedByUserId">Who saved the version.</param>
/// <param name="Narrative">The stored plain-language summary of what this save changed, when one exists.</param>
/// <param name="Workflow">The verbatim workflow template JSON (PascalCase domain representation, passed through unmodified).</param>
public record WorkflowTemplateVersionContentResponse(
    string WorkflowTypeId,
    string VersionId,
    string ContentHash,
    string? ParentVersionId,
    DateTime CreatedAtUtc,
    string CreatedByUserId,
    string? Narrative,
    JsonElement Workflow);
