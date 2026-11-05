using IniaAi.Common.Content;

namespace IniaAi.Services.Export.Models;

public class WorkflowOutputExportResult
{
    public ExportStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public BinaryContent? Content { get; set; }

    /// <summary>The file name extension of the content, with its dot.</summary>
    public string FileExtension { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public static WorkflowOutputExportResult NotFound(string message)
    {
        return new() { Status = ExportStatus.NotFound, ErrorMessage = message };
    }

    public static WorkflowOutputExportResult Error(string message)
    {
        return new() { Status = ExportStatus.Error, ErrorMessage = message };
    }

    public static WorkflowOutputExportResult FromContent(BinaryContent content, string fileExtension, string contentType)
    {
        return new() { Status = ExportStatus.Success, Content = content, FileExtension = fileExtension, ContentType = contentType };
    }
}
