namespace IniaAi.AiConnection.Abstractions;

public interface IEmbeddingsConnector
{
    /// <summary>
    /// Gets the display name of the model (e.g., "Cohere: EmbedV40").
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the actual model ID used for API calls and pricing lookup.
    /// This should return the normalized model ID that matches ModelPricing records
    /// (e.g., "embed-english-v3.0").
    /// </summary>
    string ModelId { get; }

    /// <summary>
    /// Gets the total number of tokens consumed by this connector instance.
    /// </summary>
    int TotalTokens { get; }

    /// <summary>
    /// The setting this connector needs and does not have, so it embeds nothing; null when it can
    /// embed.
    /// </summary>
    string? MissingSetting => null;

    Task<List<double[]>> GetResponseAsync(string document, CancellationToken cancellationToken = default);
    Task<List<double[]>> GetResponseAsync(List<string> documents, CancellationToken cancellationToken = default);
}
