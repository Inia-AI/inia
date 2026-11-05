namespace IniaAi.WebApi.Contracts.Responses.AgentRuns;

/// <summary>
/// One collected file of an agent run inside a workflow execution: a trace pack file
/// (<c>trace/</c>) or a published artifact (<c>out/</c>). The bytes are served separately.
/// </summary>
public sealed record AgentRunFileDto
{
    /// <summary>The executed block that bought the run.</summary>
    public string BlockId { get; init; } = string.Empty;

    /// <summary>The block's name in the executed workflow.</summary>
    public string? BlockName { get; init; }

    /// <summary>The block's replacement tag in the executed workflow (a looped or parameterized copy carries its own).</summary>
    public string? ReplacementTag { get; init; }

    /// <summary>The runtime's id for the run.</summary>
    public string RunExecutionId { get; init; } = string.Empty;

    /// <summary>The collection attempt the file came from.</summary>
    public int Attempt { get; init; }

    /// <summary>The file's path inside the run pack, for example <c>trace/trace.ndjson</c>.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>The file's size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>The SHA-256 of the file's bytes, in hex.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>When the platform collected the file.</summary>
    public DateTime CollectedAt { get; init; }
}
