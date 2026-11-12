using IniaAi.Configuration;
using IniaAi.FileFormats.Inspection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.Attributes;

/// <summary>
/// Bounds the request body of a file upload by the deployment's file size limit
/// (<c>PlatformSettings:IniaAi:MaxFileSizeMb</c>) before the form is read, so a body over the limit is refused while it
/// streams in, not after the server has buffered it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FileUploadSizeLimitAttribute : Attribute, IAuthorizationFilter
{
    // Room for the multipart boundaries, the part headers and the small fields sent beside the file.
    private const long FORM_OVERHEAD_BYTES = 64 * 1024;

    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        HttpContext http = context.HttpContext;
        PlatformSettings settings = http.RequestServices.GetRequiredService<IOptions<PlatformSettings>>().Value;
        long limit = FileInspectionLimits.FromSettings(settings.IniaAi).MaxFileBytes + FORM_OVERHEAD_BYTES;

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = limit;
        }

        if (http.Features.Get<IFormFeature>()?.Form is null)
        {
            http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions { MultipartBodyLengthLimit = limit }));
        }
    }
}
