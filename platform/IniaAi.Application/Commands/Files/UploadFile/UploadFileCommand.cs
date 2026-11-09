using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace IniaAi.Application.Commands.Files.UploadFile;

/// <summary>
/// Command to upload a new file.
/// </summary>
/// <param name="File">The file being uploaded (multipart/form-data).</param>
/// <param name="OrganizationId">The organization ID of the current user.</param>
/// <param name="UserId">The ID of the user performing the upload.</param>
/// <param name="DocumentCategoryId">Optional document category ID, stamped on the document. During workflow runs, the document class's category takes precedence and is persisted onto the document.</param>
/// <param name="FilePassword">Optional password for an encrypted file. Used once to decrypt at ingest, never persisted, and excluded from <see cref="ToString"/> so it cannot leak into logs.</param>
/// <param name="Uploader">The signed-in user, when the upload came through the API, so the organization's upload limits can leave out the platform's staff.</param>
/// <param name="SourceEmailIdentifier">The identifier of the email the file was made from, which the document then carries in its sources, as the email intake marks what it makes.</param>
public sealed record UploadFileCommand(
    IFormFile File,
    string OrganizationId,
    string UserId,
    string? DocumentCategoryId = null,
    string? FilePassword = null,
    ClaimsPrincipal? Uploader = null,
    string? SourceEmailIdentifier = null) : IRequest<ErrorOr<FileDto>>
{
    public override string ToString()
    {
        return $"{nameof(UploadFileCommand)} {{ File = {File.FileName}, OrganizationId = {OrganizationId}, "
            + $"UserId = {UserId}, DocumentCategoryId = {DocumentCategoryId} }}";
    }
}
