using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Citations.Sources;

public class UrlSource : BaseSource
{
    public override CitationSourceType Type => CitationSourceType.Url;

    public override string SourceKey => $"url:{Url}";

    [JsonProperty(nameof(Url))]
    public string Url { get; set; } = string.Empty;
}
