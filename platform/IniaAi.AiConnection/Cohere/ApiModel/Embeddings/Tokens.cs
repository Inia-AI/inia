using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Cohere.ApiModel.Embeddings;

internal class Tokens : JsonSerializable<Tokens>
{
    [JsonProperty("input_tokens")]
    public int InputTokens { get; set; }

    [JsonProperty("output_tokens")]
    public int OutputTokens { get; set; }
}
