namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// One page of a workflow template's version history, newest first.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type identifier.</param>
/// <param name="Versions">The versions on this page, newest first.</param>
/// <param name="TotalCount">The number of versions in the whole history, across all pages.</param>
/// <param name="HasMore">True when older versions exist beyond this page.</param>
/// <param name="NextCursor">
/// Opaque token for the next (older) page; pass it back as <c>cursor</c>. Null when
/// <paramref name="HasMore"/> is false.
/// </param>
public record WorkflowTemplateVersionsResponse(
    string WorkflowTypeId,
    IReadOnlyList<WorkflowTemplateVersionDto> Versions,
    int TotalCount,
    bool HasMore,
    string? NextCursor);
