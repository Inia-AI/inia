using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Tools.RagStrategies;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace IniaAi.Core.Rag;

/// <summary>
/// Represents a single LLM call within a RAG operation for granular token tracking.
/// </summary>
public record RagLlmCall(
    string CallType,
    int InputTokens,
    int OutputTokens,
    string? ModelId,
    int? BatchIndex = null,
    int? BatchSize = null);

internal class RagExecutionContext
{
    #region Token Accumulation

    // LLM token accumulation for tracking (thread-safe for parallel calls)
    private int _accumulatedInputTokens;
    private int _accumulatedOutputTokens;

    // Embedding token accumulation
    private int _accumulatedEmbeddingTokens;

    // Model tracking

    // Per-call tracking for granular reporting
    private readonly ConcurrentBag<RagLlmCall> _llmCalls = [];

    public int AccumulatedInputTokens => _accumulatedInputTokens;
    public int AccumulatedOutputTokens => _accumulatedOutputTokens;
    public int AccumulatedEmbeddingTokens => _accumulatedEmbeddingTokens;
    public string? LastUsedModelId { get; private set; }
    public string? LastUsedEmbeddingModelId { get; private set; }

    /// <summary>
    /// Gets all individual LLM calls tracked during this RAG operation.
    /// </summary>
    public IReadOnlyCollection<RagLlmCall> LlmCalls => _llmCalls;

    public void AccumulateTokens(ResponseContentSequence response)
    {
        _ = Interlocked.Add(ref _accumulatedInputTokens, response.InputTokens);
        _ = Interlocked.Add(ref _accumulatedOutputTokens, response.OutputTokens);

        // Capture model ID from response metadata
        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? modelValue)
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString))
        {
            LastUsedModelId = modelString;
        }
    }

    /// <summary>
    /// Accumulates tokens and tracks the call with additional context for granular reporting.
    /// </summary>
    /// <param name="response">The LLM response</param>
    /// <param name="callType">Description of the call type (e.g., "TopicSelection", "PageAnalysis", "PageExtraction")</param>
    /// <param name="batchIndex">Optional batch index for batched operations</param>
    /// <param name="batchSize">Optional batch size for batched operations</param>
    public void AccumulateTokens(ResponseContentSequence response, string callType, int? batchIndex = null, int? batchSize = null)
    {
        AccumulateTokens(response);

        // Extract model ID
        string? modelId = null;
        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? modelValue)
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString))
        {
            modelId = modelString;
        }

        // Track this call
        _llmCalls.Add(new RagLlmCall(
            callType,
            response.InputTokens,
            response.OutputTokens,
            modelId,
            batchIndex,
            batchSize));
    }

    public void AccumulateEmbeddingTokens(int tokens)
    {
        _ = Interlocked.Add(ref _accumulatedEmbeddingTokens, tokens);
    }

    public void AccumulateEmbeddingTokens(int tokens, string? modelId)
    {
        AccumulateEmbeddingTokens(tokens);

        if (!string.IsNullOrWhiteSpace(modelId))
        {
            LastUsedEmbeddingModelId = modelId;
        }
    }

    #endregion

    #region Public properties

    public ILogger Logger { get; }

    public IChatConnector ChatConnector { get; }

    public IEmbeddingsConnector EmbeddingsConnector { get; }

    public RagSettings RagSettings { get; }

    public List<Page> Pages { get; } = [];

    public List<string> FilterExpressions { get; } = [];

    public Dictionary<int, double[]> PageVectors { get; private set; } = [];

    public List<double[]> FilterVectors { get; private set; } = [];

    public string? Topics { get; set; }

    public string WorkflowTypeId { get; set; }

    public UseAutoDetectedTableOfContentsPrompts? UseAutoDetectedTableOfContentsPrompts { get; set; }

    #endregion

    #region Constructors

    public RagExecutionContext(
        ILogger logger,
        IChatConnector chatConnector,
        IEmbeddingsConnector embeddingsConnector,
        RagSettings ragSettings,
        List<Page> pages,
        List<string> filterExpressions,
        string workflowTypeId)
    {
        Logger = logger;
        ChatConnector = chatConnector;
        EmbeddingsConnector = embeddingsConnector;
        RagSettings = ragSettings;
        Pages = pages;
        FilterExpressions = filterExpressions;
        WorkflowTypeId = workflowTypeId;
    }

    #endregion

    #region Public methods

    public async Task PrepareVectorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool ranksByEmbeddings = RagSettings.RanksPagesByEmbeddings();
        Dictionary<int, double[]> allPageVectors = Pages
            .Select(static (page, index) => (Vector: page.EmbeddingVector?.Vector ?? [], index))
            .Where(entry => !ranksByEmbeddings || entry.Vector.Length > 0)
            .ToDictionary(static entry => entry.index, static entry => entry.Vector);

        if (ranksByEmbeddings && EmbeddingsConnector.MissingSetting is { Length: > 0 } missingSetting)
        {
            Logger.LogWarning("A filter rule ranks pages by embeddings, and the embeddings connector lacks {Setting}", missingSetting);
            throw new EmbeddingsUnavailableException(keyMissing: true);
        }

        if (ranksByEmbeddings && Pages.Count > 0 && allPageVectors.Count == 0)
        {
            throw new EmbeddingsUnavailableException(keyMissing: false);
        }

        if (ranksByEmbeddings && FilterExpressions.Count > 0)
        {
            // Track embedding tokens before the call
            int tokensBefore = EmbeddingsConnector.TotalTokens;

            FilterVectors = await EmbeddingsConnector
                .GetResponseAsync(FilterExpressions, cancellationToken)
                .ConfigureAwait(false);

            // Calculate and accumulate embedding tokens consumed
            int tokensAfter = EmbeddingsConnector.TotalTokens;
            int tokensConsumed = tokensAfter - tokensBefore;
            if (tokensConsumed > 0)
            {
                AccumulateEmbeddingTokens(tokensConsumed, EmbeddingsConnector.ModelId);
            }
        }
        else
        {
            FilterVectors = [];
        }

        if (allPageVectors.Count < Pages.Count)
        {
            Logger.LogWarning(
                "Not all pages have embedding vectors." +
                "'{vectors}' vectors for '{pages}' pages",
                allPageVectors.Count,
                Pages.Count);
        }

        PageVectors = GetVectorsToFilter(
            allPageVectors,
            RagSettings.ExecutionState ?? new(RagSettings));
    }

    #endregion

    #region Private methods

    private Dictionary<int, double[]> GetVectorsToFilter(
        Dictionary<int, double[]> vectors,
        RagSettingsExecutionState ragSettings)
    {
        if (ragSettings.StartIndex < 0)
        {
            Logger.LogError("Invalid start index: {startIndex}", ragSettings.StartIndex);

            return vectors;
        }

        if (ragSettings.EndIndex < 0)
        {
            Logger.LogError("Invalid end index: {endIndex}", ragSettings.EndIndex);

            return vectors;
        }

        if (ragSettings.StartIndex >= ragSettings.EndIndex)
        {
            Logger.LogError(
                "Invalid start and end index: {startIndex}, {endIndex}",
                ragSettings.StartIndex,
                ragSettings.EndIndex);

            return vectors;
        }

        return vectors
            .Where(kvp => kvp.Key >= ragSettings.StartIndex - 1 && kvp.Key <= ragSettings.EndIndex - 1)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    #endregion
}
