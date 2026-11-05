namespace IniaAi.WebApi.Contracts.Requests.Documents;

/// <summary>
/// Request body for partially updating a document's metadata. All fields are
/// optional — only fields with a non-whitespace value are applied. Clearing a
/// previously-set field is not supported through this endpoint, except the static
/// mark: <c>IsStatic</c> false clears <c>WorkflowTypeId</c> and <c>Class</c>, and
/// <c>IsStatic</c> true needs a <c>WorkflowTypeId</c>. A static document joins every
/// run of its workflow type in its organization.
/// </summary>
public sealed record UpdateDocumentRequest(
    string? Name,
    string? Description,
    string? DocumentCategoryId,
    string? WorkflowTypeId,
    string? Class,
    string? Topics,
    string? TableOfContents,
    bool? IsStatic = null);
