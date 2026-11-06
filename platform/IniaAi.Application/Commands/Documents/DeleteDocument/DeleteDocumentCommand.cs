using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.Documents.DeleteDocument;

/// <summary>
/// Command to delete a single document and its backing file bytes. The delete
/// is scoped to the caller's organization — a cross-tenant attempt returns
/// <see cref="ErrorType.NotFound"/>, as for an unknown id.
/// </summary>
/// <param name="OwnOnly">The caller may delete only the documents they uploaded; another user's answers as an unknown id.</param>
public sealed record DeleteDocumentCommand(
    string OrganizationId,
    string UserId,
    string DocumentId,
    bool OwnOnly = false) : IRequest<ErrorOr<Deleted>>;
