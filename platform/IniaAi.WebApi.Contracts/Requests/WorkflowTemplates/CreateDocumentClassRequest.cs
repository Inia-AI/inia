using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.WorkflowTemplates;

/// <summary>
/// Body for creating a document class on a workflow template. The class is addressed by
/// <see cref="Key"/>; creating a class whose key already exists is a conflict (use the update
/// endpoint to change an existing class, or the rename endpoint to change its key).
/// </summary>
/// <param name="Key">Stable identifier of the document class, referenced by blocks and sections.</param>
/// <param name="DisplayName">User-facing name of the document class.</param>
/// <param name="Requirement">Whether documents of this class are required at execution.</param>
/// <param name="ShouldBeSingular">When true, at most one document of this class per execution.</param>
/// <param name="IsOutputDocument">When true, this class is produced by the workflow rather than uploaded.</param>
/// <param name="Description">Optional description.</param>
/// <param name="DocumentCategoryId">Optional block-template category association; an empty string is treated as absent.</param>
/// <param name="BaseVersionId">Head version id for optimistic concurrency; required when a head version exists, omit (null) for a template with no version history yet.</param>
/// <param name="LlmDescription">Optional description written for a model: what qualifies a document as this class.</param>
public record CreateDocumentClassRequest(
    string Key,
    string DisplayName,
    DocumentClassRequirement Requirement = DocumentClassRequirement.Optional,
    bool ShouldBeSingular = false,
    bool IsOutputDocument = false,
    string? Description = null,
    string? DocumentCategoryId = null,
    string? BaseVersionId = null,
    string? LlmDescription = null);
