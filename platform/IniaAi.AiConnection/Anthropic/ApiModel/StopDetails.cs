using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// Why the model stopped, filled in only for <c>stop_reason: "refusal"</c>. The fields are
/// informational: either can be null on a refusal.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
internal sealed class StopDetails
{
    [JsonProperty("type")]
    public string? Type { get; set; }

    /// <summary>The policy area, such as <c>cyber</c>, <c>bio</c> or <c>reasoning_extraction</c>. An open set.</summary>
    [JsonProperty("category")]
    public string? Category { get; set; }

    [JsonProperty("explanation")]
    public string? Explanation { get; set; }
}
