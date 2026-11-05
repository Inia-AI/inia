using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Citations;

/// <summary>
/// A citation of one figure inside a data output's JSON: where it sits and what it says.
/// One value can carry several citations, and one citation can back values on several paths.
/// </summary>
public class DataOutputCitation : Citation
{
    /// <summary>The JSON Pointer (RFC 6901) of the value that holds the cited figure.</summary>
    [JsonProperty("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>The cited figure as it is written in that value.</summary>
    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;
}
