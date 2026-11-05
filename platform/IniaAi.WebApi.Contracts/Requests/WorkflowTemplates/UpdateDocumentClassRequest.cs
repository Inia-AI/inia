using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.WorkflowTemplates;

/// <summary>
/// Body for updating fields of an existing document class, addressed by the path key. Every field
/// is optional: a field left null (or omitted) is left unchanged, so an update never resets a field
/// the caller did not send. To clear <see cref="Description"/> send an empty string; to clear the
/// <see cref="DocumentCategoryId"/> association send an empty string. Updating never changes the key
/// (use the rename endpoint for that).
/// </summary>
/// <param name="DisplayName">New display name, or null to leave unchanged.</param>
/// <param name="Requirement">New requirement level, or null to leave unchanged.</param>
/// <param name="ShouldBeSingular">New singular flag, or null to leave unchanged.</param>
/// <param name="IsOutputDocument">New output-document flag, or null to leave unchanged.</param>
/// <param name="Description">New description; empty string clears it; null leaves it unchanged.</param>
/// <param name="DocumentCategoryId">New category association; empty string clears it; null leaves it unchanged.</param>
/// <param name="BaseVersionId">Head version id for optimistic concurrency; a stale or missing value is rejected with 409 Conflict.</param>
/// <param name="LlmDescription">New model-facing description; empty string clears it; null leaves it unchanged.</param>
public record UpdateDocumentClassRequest(
    string? DisplayName = null,
    DocumentClassRequirement? Requirement = null,
    bool? ShouldBeSingular = null,
    bool? IsOutputDocument = null,
    string? Description = null,
    string? DocumentCategoryId = null,
    string? BaseVersionId = null,
    string? LlmDescription = null);
