using Newtonsoft.Json;

namespace IniaAi.Core.Dto.Exa.Search;

public record ExaContentsConfig
{
    [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
    public object? Text { get; init; } = true; // Can be bool or ExaTextConfig

    [JsonProperty("highlights", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Highlights { get; init; }

    [JsonProperty("summary", NullValueHandling = NullValueHandling.Ignore)]
    public object? Summary { get; init; } // Can be bool or ExaSummaryConfig

    [JsonProperty("subpages", NullValueHandling = NullValueHandling.Ignore)]
    public int? Subpages { get; init; }

    [JsonProperty("subpageTarget", NullValueHandling = NullValueHandling.Ignore)]
    public string[]? SubpageTarget { get; init; }
}
