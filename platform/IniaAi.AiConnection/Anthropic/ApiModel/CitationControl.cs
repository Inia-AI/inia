using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

internal class CitationControl : JsonSerializable<CitationControl>
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

}
