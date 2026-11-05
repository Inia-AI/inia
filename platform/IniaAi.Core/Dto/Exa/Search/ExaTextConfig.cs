using Newtonsoft.Json;

namespace IniaAi.Core.Dto.Exa.Search;

public record ExaTextConfig
{
    [JsonProperty("maxCharacters", NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxCharacters { get; init; }

    [JsonProperty("includeHtmlTags", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IncludeHtmlTags { get; init; }
}
