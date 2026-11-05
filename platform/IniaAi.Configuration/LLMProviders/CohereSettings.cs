using IniaAi.Configuration.Enums.Cohere;

namespace IniaAi.Configuration.LLMProviders;

public class CohereSettings
{
    public string? ApiKey { get; set; }

    public string EmbeddingsApiUrl { get; set; } = "https://api.cohere.com/v2/embed";

    public int MaxRateLimitRetryAttempts { get; set; } = 3;

    public int MaxServiceUnavailableRetryAttempts { get; set; } = 5;

    public AiModel EmbeddingsModel { get; set; } = AiModel.AwsEmbedV40;

    /// <summary>
    /// Output dimension for embeddings. Only used by models supporting Matryoshka embeddings (v4+).
    /// Valid values: 256, 512, 1024, 1536. Null uses model default (1536 for v4).
    /// Default is 1024 to maintain compatibility with v3 embeddings.
    /// </summary>
    public int? EmbeddingsOutputDimension { get; set; } = 1024;
}
