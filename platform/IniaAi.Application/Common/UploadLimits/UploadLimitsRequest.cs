using System.Security.Claims;

namespace IniaAi.Application.Common.UploadLimits;

/// <param name="User">The signed-in user when the upload came through the API, so a limit can leave out the platform's staff.</param>
/// <param name="SizeBytes">The size the file would be stored at.</param>
public sealed record UploadLimitsRequest(string OrganizationId, ClaimsPrincipal? User, long SizeBytes);
