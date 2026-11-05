using System.Diagnostics;
using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.WebApi.Contracts.Responses.Meta;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides application metadata such as version, and uptime.
/// </summary>
[Route("api/v{version:apiVersion}/meta")]
public sealed class MetaController : ApiController
{
    private static readonly DateTime _processStartTimeUtc = ReadProcessStartTimeUtc();

    /// <summary>
    /// Initializes a new instance of the <see cref="MetaController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    public MetaController(IRequestContext requestContext)
        : base(requestContext)
    {
    }

    /// <summary>
    /// Retrieves the current application version and informational version.
    /// </summary>
    /// <remarks>
    /// Useful for debugging, monitoring, or verifying deployment versions.
    /// </remarks>
    /// <returns>The current API version information.</returns>
    [AllowAnonymous]
    [HttpGet("version")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetMetaVersionResponse))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public ActionResult<GetMetaVersionResponse> GetVersion()
    {
        var versionInfo = new GetMetaVersionResponse(ApplicationVersion.Version, ApplicationVersion.InformationalVersion);
        return Ok(versionInfo);
    }

    /// <summary>
    /// Returns build metadata such as commit hash, build date, and branch.
    /// </summary>
    /// <remarks>
    /// Use this endpoint to confirm which commit was deployed or when the build occurred.
    /// </remarks>
    /// <returns>Build metadata.</returns>
    [AllowAnonymous]
    [HttpGet("build")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetMetaBuildInfoResponse))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public ActionResult<GetMetaBuildInfoResponse> GetBuildInfo()
    {
        // These would be set at build time via CI/CD or MSBuild properties
        var response = new GetMetaBuildInfoResponse
        {
            CommitSha = ApplicationVersion.CommitSha,
            BuildDateUtc = ApplicationVersion.BuildDateUtc,
            Branch = ApplicationVersion.Branch
        };

        return Ok(response);
    }

    /// <summary>
    /// Returns the time the application has been running since the last restart.
    /// </summary>
    /// <remarks>
    /// This can be used to monitor uptime or verify when the last deployment occurred.
    /// </remarks>
    /// <returns>Uptime duration and start time.</returns>
    [AllowAnonymous]
    [HttpGet("uptime")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetMetaUptimeResponse))]
    public ActionResult<GetMetaUptimeResponse> GetUptime()
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan uptime = now - _processStartTimeUtc;

        var response = new GetMetaUptimeResponse
        {
            StartedAtUtc = _processStartTimeUtc,
            Uptime = uptime
        };

        return Ok(response);
    }

    private static DateTime ReadProcessStartTimeUtc()
    {
        using Process process = Process.GetCurrentProcess();

        return process.StartTime.ToUniversalTime();
    }
}
