using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.Application.Commands.Documents.ProcessDocument;

/// <summary>
/// Queues one stored document for standalone processing into pages — the front door for
/// validation baselines that arrive by upload rather than through an execution.
/// </summary>
public sealed record ProcessDocumentCommand(
    string OrganizationId,
    string UserId,
    string DocumentId) : IRequest<ErrorOr<DocumentSummaryDto>>;
