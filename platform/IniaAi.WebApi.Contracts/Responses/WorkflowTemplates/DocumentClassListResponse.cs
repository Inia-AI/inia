using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// The document classes of a workflow template plus the head version id. Send the version id back as
/// <c>baseVersionId</c> when mutating a document class so a concurrent edit is detected.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type identifier.</param>
/// <param name="VersionId">The head version identifier, or null when no version snapshot exists yet.</param>
/// <param name="DocumentClasses">The document classes defined on the template.</param>
public record DocumentClassListResponse(
    string WorkflowTypeId,
    string? VersionId,
    IReadOnlyList<DocumentClassDto> DocumentClasses);
