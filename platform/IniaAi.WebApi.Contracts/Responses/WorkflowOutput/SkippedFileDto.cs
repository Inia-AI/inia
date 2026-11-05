namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// A file the submission's intake could not turn into a document, with the reason.
/// </summary>
/// <param name="FileName">The file name, or another identifying label such as a URL.</param>
/// <param name="Reason">Why the file was skipped.</param>
public sealed record SkippedFileDto(string FileName, string Reason);
