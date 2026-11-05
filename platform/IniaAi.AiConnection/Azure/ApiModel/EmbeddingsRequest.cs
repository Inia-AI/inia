using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Azure.ApiModel;

/// <summary>
/// Request body for the Azure AI Model Inference embeddings API.
/// See: https://learn.microsoft.com/en-us/azure/ai-studio/reference/reference-model-inference-embeddings
/// </summary>
internal class EmbeddingsRequest : JsonSerializable<EmbeddingsRequest>
{
    [JsonProperty("input")]
    public string[] Input { get; set; } = [];

    [JsonProperty("model")]
    public string Model { get; set; } = "";

    [JsonProperty("dimensions", NullValueHandling = NullValueHandling.Ignore)]
    public int? Dimensions { get; set; }
}
