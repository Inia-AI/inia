using System.Text.Json;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// The full current workflow template content plus the head version information
/// the caller must echo back as <c>baseVersionId</c> when saving an edit.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type identifier.</param>
/// <param name="VersionId">The head version identifier, or null when no version snapshot exists yet.</param>
/// <param name="ContentHash">The canonical content hash of the head version, when available.</param>
/// <param name="CreatedAtUtc">When the head version was created, when available.</param>
/// <param name="CreatedByUserId">Who created the head version, when available.</param>
/// <param name="Workflow">The verbatim workflow template JSON (PascalCase domain representation, passed through unmodified).</param>
public record WorkflowTemplateContentResponse(
    string WorkflowTypeId,
    string? VersionId,
    string? ContentHash,
    DateTime? CreatedAtUtc,
    string? CreatedByUserId,
    JsonElement Workflow);
