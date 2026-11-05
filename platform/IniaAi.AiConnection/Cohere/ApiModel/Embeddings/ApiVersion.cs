using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Cohere.ApiModel.Embeddings;

internal class ApiVersion : JsonSerializable<ApiVersion>
{
    [JsonProperty("version")]
    public string Version { get; set; } = "";

    [JsonProperty("is_deprecated")]
    public bool IsDeprecated { get; set; }

    [JsonProperty("is_experimental")]
    public bool IsExperimental { get; set; }
}
