using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// Result of a document-class create/update/rename/delete on a workflow template. Send the new head
/// version id back as <c>baseVersionId</c> on the next mutation.
/// </summary>
/// <param name="VersionId">The head version after the save — a new snapshot, or the prior head when deduped.</param>
/// <param name="ContentHash">The canonical content hash of the saved template.</param>
/// <param name="WasDeduped">True when the save matched the prior head and appended no new version.</param>
/// <param name="Key">The key of the created, updated, renamed, or removed document class.</param>
/// <param name="DocumentClass">The resulting document class after create/update/rename; null for a delete.</param>
public record DocumentClassMutationResponse(
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    string Key,
    DocumentClassDto? DocumentClass = null);
