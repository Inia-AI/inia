namespace IniaAi.AiConnection.Abstractions;

/// <summary>
/// The vectors of one embeddings call, in the order of its texts, and the input tokens the
/// provider counted for that call alone.
/// </summary>
public sealed record EmbeddingsAnswer(List<double[]> Vectors, int InputTokens)
{
    /// <summary>
    /// The marker of the warning a caller logs for an uncounted answer, which records no usage.
    /// An alarm reads it.
    /// </summary>
    public const string UNCOUNTED_LOG_MARKER = "EmbeddingTokensUncounted";

    public static EmbeddingsAnswer Empty => new([], 0);

    /// <summary>The provider answered vectors and counted no tokens for them.</summary>
    public bool IsUncounted => Vectors.Count > 0 && InputTokens == 0;
}
