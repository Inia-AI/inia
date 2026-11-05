using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.Documents.GetDocumentText;

/// <summary>
/// The text of every page of a document, in page order, each page wrapped in a numbered tag.
/// NotFound for an unknown document and for a document of another tenant alike.
/// </summary>
public sealed record GetDocumentTextQuery(
    string OrganizationId,
    string UserId,
    string DocumentId) : IRequest<ErrorOr<DocumentText>>;

/// <summary>
/// The text of a document and the file name it downloads under.
/// </summary>
public sealed record DocumentText(string FileName, string Text);
