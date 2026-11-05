using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.Domain.Entities.Citations.Sources;

public abstract class BaseSource
{
    [JsonProperty(nameof(Type))]
    [JsonConverter(typeof(StringEnumConverter))]
    public abstract CitationSourceType Type { get; }

    [JsonProperty(nameof(CitedText))]
    public string CitedText { get; set; } = string.Empty;

    [JsonIgnore]
    public abstract string SourceKey { get; }

    public BaseSource ShallowCopy()
    {
        return (BaseSource)MemberwiseClone();
    }

    public BaseSource WithCitedText(string citedText)
    {
        BaseSource copy = ShallowCopy();
        copy.CitedText = citedText;
        return copy;
    }
}
