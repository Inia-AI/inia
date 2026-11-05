using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides endpoints to download application log files, optionally zipped based on size.
/// </summary>
[Route("api/v{version:apiVersion}/diagnostics")]
public class DiagnosticsController : ApiController
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="configuration">Application configuration instance, used to locate log file settings.</param>
    public DiagnosticsController(
        IRequestContext requestContext,
        IConfiguration configuration)
        : base(requestContext)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Downloads the latest application log file.
    /// </summary>
    /// <remarks>
    /// This endpoint reads the Serilog configuration to determine the log file
    /// path and retrieves the log files for the current day, compressing them
    /// into a ZIP archive and returning it as a file stream.
    /// </remarks>
    /// <returns>The latest log file as a plain text stream or ZIP archive.</returns>
    /// <response code="200">Returns the log file or compressed ZIP stream successfully.</response>
    /// <response code="400">The request is invalid or misconfigured.</response>
    /// <response code="401">Authentication is required to access this resource.</response>
    /// <response code="404">No log files were found based on the Serilog configuration.</response>
    /// <response code="500">An unexpected error occurred while processing the request.</response>
    [HttpGet("logs/download")]
    [Authorize(Roles = Roles.OPERATOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileStreamResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public IActionResult DownloadLog()
    {
        IEnumerable<IConfigurationSection> writeTos = _configuration.GetSection("Serilog:WriteTo").GetChildren();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (IConfigurationSection writeTo in writeTos)
        {
            if (writeTo["Name"] != "File")
            {
                continue;
            }

            string path = writeTos.FirstOrDefault(w => w["Name"] == "File")?["Args:path"] ?? "logs/log-.log";
            string fileNamePattern = Path.GetFileName(path)?.Replace("log-", "*") ?? "*";
            string logDirectory = Path.GetDirectoryName(path) ?? "logs";

            string todayString = today.ToString("yyyyMMdd");
            var logFiles = Directory.GetFiles(logDirectory, fileNamePattern)
                .Where(file => file.Contains(todayString))
                .ToList();

            if (logFiles.Count == 0)
            {
                return NotFound("No log files found.");
            }

            try
            {
                var zipStream = new MemoryStream();
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    foreach (string? logFile in logFiles)
                    {
                        ZipArchiveEntry entry = archive.CreateEntry(Path.GetFileName(logFile));
                        using Stream entryStream = entry.Open();
                        using var fileStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        fileStream.CopyTo(entryStream);
                    }
                }

                zipStream.Position = 0;
                string zipFileName = $"logs-{todayString}.zip";
                return File(zipStream, "application/zip", zipFileName);
            }
            catch (Exception ex)
            {
                // Log the error if logging is available
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error retrieving log files: {ex.Message}");
            }
        }

        return NotFound("No log file configuration found.");
    }
}
