using Newtonsoft.Json;

namespace IniaAi.Core.Workflows.Strategies.Perplexity;

internal class PerplexitySearchResult
{
    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("snippet")]
    public string? Snippet { get; set; }

    [JsonProperty("date")]
    public string? Date { get; set; }
}
