namespace IniaAi.Common.Interfaces.Services;

/// <summary>
/// Interface for services that accumulate token usage from LLM calls.
/// Services implementing this interface track tokens internally and expose
/// accumulated counts that can be recorded by calling code.
/// </summary>
public interface ITokenAccumulator
{
    /// <summary>
    /// Gets the total number of input tokens accumulated since the last reset.
    /// </summary>
    int AccumulatedInputTokens { get; }

    /// <summary>
    /// Gets the total number of output tokens accumulated since the last reset.
    /// </summary>
    int AccumulatedOutputTokens { get; }

    /// <summary>
    /// Gets the total number of cache creation tokens accumulated since the last reset.
    /// </summary>
    int AccumulatedCacheCreationTokens { get; }

    /// <summary>
    /// Gets the total number of cache read tokens accumulated since the last reset.
    /// </summary>
    int AccumulatedCacheReadTokens { get; }

    /// <summary>
    /// Gets the last LLM model ID used by this service.
    /// Used for token usage tracking to identify which model was used.
    /// </summary>
    string? LastUsedModelId => null;

    /// <summary>
    /// Gets the last embedding model ID used by this service.
    /// Used for embedding token usage tracking.
    /// </summary>
    string? LastUsedEmbeddingModelId => null;

    /// <summary>
    /// Resets all accumulated token counts to zero.
    /// Call this before starting a new operation to get clean counts.
    /// </summary>
    void ResetTokenAccumulator();
}
