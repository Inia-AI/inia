namespace IniaAi.WebApi.Contracts.Responses.AgentRuns;

/// <summary>
/// The bytes of one collected agent run file, ready to serve as a download.
/// </summary>
public sealed record AgentRunFileContent
{
    /// <summary>The file's bytes, exactly as collected.</summary>
    public byte[] Content { get; init; } = [];

    /// <summary>The MIME type the file is served with.</summary>
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>The suggested download name.</summary>
    public string FileName { get; init; } = string.Empty;
}
