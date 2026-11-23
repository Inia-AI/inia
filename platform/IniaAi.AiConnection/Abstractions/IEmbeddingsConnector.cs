namespace IniaAi.AiConnection.Abstractions;

public interface IEmbeddingsConnector
{
    /// <summary>
    /// Gets the display name of the model (e.g., "Cohere: EmbedV40").
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the model the connector calls, such as "eu.cohere.embed-v4:0" or "embed-v4.0". A usage
    /// record takes the price list's model name from it, also on Azure, where the request names the
    /// deployment instead.
    /// </summary>
    string ModelId { get; }

    /// <summary>
    /// The route the connector calls, as usage records name it: one of <c>ModelProviders</c>.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// The setting this connector needs and does not have, so it embeds nothing; null when it can
    /// embed.
    /// </summary>
    string? MissingSetting => null;

    /// <summary>
    /// Embeds <paramref name="documents"/> in one call. The answer carries this call's own token
    /// count, so callers running at the same time never count each other's tokens.
    /// </summary>
    Task<EmbeddingsAnswer> EmbedAsync(List<string> documents, CancellationToken cancellationToken = default);
}
