using IniaAi.Core.Dto.Exa.Search.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.Core.Dto.Exa.Search;

public record ExaSearchRequest
{
    [JsonProperty("query")]
    public required string Query { get; init; }

    [JsonProperty("numResults")]
    public int NumResults { get; init; } = 10;

    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public ExaSearchType Type { get; init; } = ExaSearchType.Auto;

    [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(StringEnumConverter))]
    public ExaSearchCategory? Category { get; init; }

    [JsonProperty("startPublishedDate", NullValueHandling = NullValueHandling.Ignore)]
    public string? StartPublishedDate { get; init; }

    [JsonProperty("endPublishedDate", NullValueHandling = NullValueHandling.Ignore)]
    public string? EndPublishedDate { get; init; }

    [JsonProperty("includeDomains", NullValueHandling = NullValueHandling.Ignore)]
    public string[]? IncludeDomains { get; init; }

    [JsonProperty("excludeDomains", NullValueHandling = NullValueHandling.Ignore)]
    public string[]? ExcludeDomains { get; init; }

    [JsonProperty("contents")]
    public ExaContentsConfig? Contents { get; init; } = new ExaContentsConfig();

    [JsonProperty("liveCrawl", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(StringEnumConverter))]
    public ExaLiveCrawlStrategy? LiveCrawl { get; init; }

    [JsonProperty("liveCrawlTimeout", NullValueHandling = NullValueHandling.Ignore)]
    public int? LiveCrawlTimeout { get; init; }
}
