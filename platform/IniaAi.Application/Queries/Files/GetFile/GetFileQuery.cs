using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Files;

namespace IniaAi.Application.Queries.Files.GetFile;

/// <summary>
/// Query to retrieve file content (binary data) by file ID.
/// </summary>
/// <param name="OrganizationId">The organization ID for tenant isolation.</param>
/// <param name="UserId">The user ID for audit logging.</param>
/// <param name="FileId">The unique identifier of the file.</param>
/// <param name="Disposition">The content disposition type ("preview" or "download").</param>
public sealed record GetFileQuery(
    string OrganizationId,
    string UserId,
    string FileId,
    string Disposition = "download") : IRequest<ErrorOr<FileResult>>;
