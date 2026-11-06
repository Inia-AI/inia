using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.Application.Commands.Documents.BulkDeleteDocuments;

/// <summary>
/// Command to delete many documents in one call. Each id is processed
/// independently so clients receive a partial-success result rather than an
/// all-or-nothing failure — useful for large batches where a single bad id
/// should not block the rest.
/// </summary>
/// <param name="OwnOnly">The caller may delete only the documents they uploaded; another user's fails as an unknown id.</param>
public sealed record BulkDeleteDocumentsCommand(
    string OrganizationId,
    string UserId,
    List<string> DocumentIds,
    bool OwnOnly = false) : IRequest<ErrorOr<BulkDeleteDocumentsResultDto>>;
