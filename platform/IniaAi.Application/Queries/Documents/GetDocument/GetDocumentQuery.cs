using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.Application.Queries.Documents.GetDocument;

/// <summary>
/// Retrieves the full detail projection for a single document, scoped to the
/// caller's organization. Returns NotFound for an unknown id and for a document
/// of another tenant alike.
/// </summary>
public sealed record GetDocumentQuery(
    string OrganizationId,
    string UserId,
    string DocumentId) : IRequest<ErrorOr<DocumentDetailDto>>;
