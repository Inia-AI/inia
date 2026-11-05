using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace IniaAi.WebApp.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize(Roles = Roles.OPERATOR_ROLES)]
public class LogController : ControllerBase
{
    private readonly IConfiguration _configuration;

    private const long ZIP_THRESHOLD_BYTES = 5 * 1024 * 1024;

    public LogController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("download")]
    public IActionResult DownloadLog()
    {
        IEnumerable<IConfigurationSection> writeTos = _configuration.GetSection("Serilog:WriteTo").GetChildren();

        foreach (IConfigurationSection writeTo in writeTos)
        {
            if (writeTo["Name"] is not "File")
            {
                continue;
            }

            string path = writeTos.FirstOrDefault(w => w["Name"] == "File")?["Args:path"] ?? "logs/log-.log";
            string fileNamePattern = Path.GetFileName(path)?.Replace("log-", "*") ?? "*";
            string logDirectory = Path.GetDirectoryName(path) ?? "logs";

            string[] logFiles = Directory.GetFiles(logDirectory, fileNamePattern);

            if (logFiles.Length == 0)
            {
                return NotFound("No log files found.");
            }

            Array.Sort(logFiles, (a, b) => System.IO.File.GetLastWriteTime(b).CompareTo(System.IO.File.GetLastWriteTime(a)));

            string logFilePath = logFiles[0];
            string fileName = Path.GetFileName(logFilePath);
            var fileInfo = new FileInfo(logFilePath);

            if (fileInfo.Length <= ZIP_THRESHOLD_BYTES)
            {
                var stream = new FileStream(
                    logFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                return File(stream, "text/plain", fileName);
            }

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                ZipArchiveEntry entry = zip.CreateEntry(fileName, CompressionLevel.Optimal);
                using Stream entryStream = entry.Open();
                using var fileStream = new FileStream(
                    logFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                fileStream.CopyTo(entryStream);
            }

            ms.Position = 0;

            return File(ms.ToArray(), "application/zip", fileName + ".zip");
        }

        return NotFound("No log file configuration found.");
    }
}
