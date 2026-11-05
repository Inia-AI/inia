using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.Application.Queries.Documents.GetDocumentPage;

/// <summary>
/// One page of a document with its full text — the read a workspace uses to show what an
/// extraction actually saw. NotFound for an unknown document or page number, and for a
/// document of another tenant alike.
/// </summary>
public sealed record GetDocumentPageQuery(
    string OrganizationId,
    string UserId,
    string DocumentId,
    int PageNumber) : IRequest<ErrorOr<PageDetailDto>>;
