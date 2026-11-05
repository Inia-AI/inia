using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Azure.ApiModel;

/// <summary>
/// Response body from the Azure AI Model Inference embeddings API.
/// Uses the OpenAI-compatible format with data[].embedding.
/// </summary>
internal class EmbeddingsResponse : JsonSerializable<EmbeddingsResponse>
{
    [JsonProperty("data")]
    public EmbeddingItem[] Data { get; set; } = [];

    [JsonProperty("model")]
    public string Model { get; set; } = "";

    [JsonProperty("usage")]
    public EmbeddingsUsage Usage { get; set; } = new();
}

internal class EmbeddingItem
{
    [JsonProperty("index")]
    public int Index { get; set; }

    [JsonProperty("embedding")]
    public double[] Embedding { get; set; } = [];
}

internal class EmbeddingsUsage
{
    [JsonProperty("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonProperty("total_tokens")]
    public int TotalTokens { get; set; }
}
