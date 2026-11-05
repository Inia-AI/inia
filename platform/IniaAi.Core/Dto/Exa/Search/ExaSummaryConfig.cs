using Newtonsoft.Json;

namespace IniaAi.Core.Dto.Exa.Search;

public record ExaSummaryConfig
{
    [JsonProperty("query", NullValueHandling = NullValueHandling.Ignore)]
    public string? Query { get; init; }

    [JsonProperty("schema", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, object>? Schema { get; init; }
}
