using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

internal class ErrorResponse : JsonSerializable<ErrorResponse>
{
    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("error")]
    public ErrorMessage? Error { get; set; }

    [JsonProperty("detail", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? Detail { get; set; }
}
