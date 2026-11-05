using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.Application.Commands.Documents.UpdateDocument;

/// <summary>
/// Command to patch a document's editable metadata. Any field left null (or
/// whitespace-only) is treated as "unchanged" to match the convention used by
/// the existing Update handlers in this project. <paramref name="IsStatic"/> false
/// clears the workflow type and class that make a document static.
/// </summary>
public sealed record UpdateDocumentCommand(
    string OrganizationId,
    string UserId,
    string DocumentId,
    string? Name,
    string? Description,
    string? DocumentCategoryId,
    string? WorkflowTypeId,
    string? Class,
    string? Topics,
    string? TableOfContents,
    bool? IsStatic = null) : IRequest<ErrorOr<DocumentDetailDto>>;
