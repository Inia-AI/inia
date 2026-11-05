using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

internal class CacheControl : JsonSerializable<CacheControl>
{
    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public CachingType Type { get; set; } = CachingType.Ephemeral;
}
