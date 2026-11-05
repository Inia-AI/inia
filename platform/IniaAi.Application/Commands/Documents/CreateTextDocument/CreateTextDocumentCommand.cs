using System.Security.Claims;
using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.Application.Commands.Documents.CreateTextDocument;

/// <summary>
/// Pasted text or markdown becomes a document of the organization: stored as a <c>.txt</c> or
/// <c>.md</c> file and queued for processing, where the whole text becomes page 1 with no
/// format extraction in between. The stored text counts against the organization's upload limits
/// like an uploaded file of its size.
/// </summary>
/// <param name="Uploader">The signed-in user, so the organization's upload limits can leave out the platform's staff.</param>
public sealed record CreateTextDocumentCommand(
    string OrganizationId,
    string UserId,
    string Name,
    string Text,
    TextDocumentFormat Format,
    ClaimsPrincipal? Uploader = null) : IRequest<ErrorOr<DocumentSummaryDto>>;
