using System.Text.Json.Serialization;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// One entry of a run's file manifest: a published deliverable under <c>out/</c> or a trace
/// file. Paths are relative, forward-slash separated, and are the exact strings accepted by
/// <see cref="IAgentRuntimeClient.GetRunFileAsync"/>.
/// </summary>
public sealed record AgentRunFileEntry
{
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }

    [JsonConverter(typeof(SnakeCaseEnumJsonConverter<AgentRunFileKind>))]
    public AgentRunFileKind Kind { get; init; }
}

/// <summary>
/// Manifest entry kind. Snake_case on the wire; a value this client does not recognise maps to
/// <see cref="Unknown"/> instead of throwing, so a runtime that grows new kinds keeps working.
/// </summary>
[JsonConverter(typeof(SnakeCaseEnumJsonConverter<AgentRunFileKind>))]
public enum AgentRunFileKind
{
    Unknown = 0,
    Artifact,
    Trace,
}
