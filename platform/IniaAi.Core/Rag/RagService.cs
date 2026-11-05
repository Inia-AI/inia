using System.Collections.Concurrent;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Constants;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Core.Documents;
using IniaAi.Core.Rag.Interfaces;
using IniaAi.Core.Rag.Strategies;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;

namespace IniaAi.Core.Rag;

/// <summary>
/// Provides methods for different RAG (Retrieval-Augmented Generation) types.
/// </summary>
public class RagService : ITokenAccumulator
{
    #region Private fields

    private readonly ILogger _logger;
    private readonly IEmbeddingsConnector _embeddingsConnector;
    private readonly IChatConnector _chatConnector;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Dictionary<RagTypes, IRagStrategy> _strategies = [];

    // Token accumulation for tracking (thread-safe for parallel calls)
    private int _accumulatedInputTokens;
    private int _accumulatedOutputTokens;
    private int _accumulatedEmbeddingTokens;

    // Per-call tracking from the last RAG operation

    #endregion

    #region ITokenAccumulator Implementation

    public int AccumulatedInputTokens => _accumulatedInputTokens;
    public int AccumulatedOutputTokens => _accumulatedOutputTokens;
    public int AccumulatedCacheCreationTokens => 0;
    public int AccumulatedCacheReadTokens => 0;
    public int AccumulatedEmbeddingTokens => _accumulatedEmbeddingTokens;
    public string? LastUsedModelId { get; private set; }
    public string? LastUsedEmbeddingModelId { get; private set; }

    /// <summary>
    /// Gets the individual LLM calls from the last RAG operation for granular tracking.
    /// </summary>
    public IReadOnlyCollection<RagLlmCall>? LastLlmCalls { get; private set; }

    public void ResetTokenAccumulator()
    {
        _ = Interlocked.Exchange(ref _accumulatedInputTokens, 0);
        _ = Interlocked.Exchange(ref _accumulatedOutputTokens, 0);
        _ = Interlocked.Exchange(ref _accumulatedEmbeddingTokens, 0);
        LastUsedModelId = null;
        LastUsedEmbeddingModelId = null;
        LastLlmCalls = null;
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="RagService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public RagService(
        ILogger<RagService> logger,
        IEmbeddingsConnector embeddingsConnector,
        IChatConnector chatConnector,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _embeddingsConnector = embeddingsConnector;
        _chatConnector = chatConnector;
        _serviceScopeFactory = serviceScopeFactory;

        _strategies.Add(RagTypes.WholeDocument, new WholeDocumentStrategy());
        _strategies.Add(RagTypes.TopNPages, new TopNPagesStrategy());
        _strategies.Add(RagTypes.TopNPagesPerFilterWithTwoNeighbors, new TopNPagesPerFilterWithNeighborsStrategy());
        _strategies.Add(RagTypes.TopPage, new TopPageStrategy());
        _strategies.Add(RagTypes.NPagesStartingFromIndex, new NPagesStartingFromIndexStrategy());
        _strategies.Add(RagTypes.TopPageWithNFollowingPages, new TopPageWithNFollowingPagesStrategy());
        _strategies.Add(RagTypes.KeywordBased, new KeywordBasedStrategy());
        _strategies.Add(RagTypes.ExactPages, new ExactPagesStrategy());
        _strategies.Add(RagTypes.RagAgent, new RagAgentRagStrategy());
        _strategies.Add(RagTypes.UseTableOfContents, new UseTableOfContentsStrategy());
        _strategies.Add(RagTypes.UseAutoDetectedTableOfContents, new UseAutoDetectedTableOfContentsStrategy());
    }

    #endregion

    #region Public methods

    /// <summary>
    /// Gets the filtered pages for the specified block.
    /// </summary>
    /// <param name="block">The block containing RagSettings and Documents.</param>
    /// <param name="workflowTypeId">The workflow type identifier.</param>
    /// <param name="resultsCache">Cache for reusing results across blocks with identical RagSettings.</param>
    /// <param name="pages">Optional pre-filtered pages.</param>
    /// <param name="topics">Optional topics for UseTopics strategy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Filtered pages organized by batch name.</returns>
    public async Task<Dictionary<string, Dictionary<int, Page>>> GetFilteredPagesAsync(
        Block block,
        string workflowTypeId,
        ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>> resultsCache,
        List<Page>? pages = null,
        string? topics = null,
        CancellationToken cancellationToken = default)
    {
        if (!block.ShouldUseDocuments())
        {
            _logger.LogWarning(
                "{block} Is not an AI query or simple RAG, skipping filtering",
                block);

            return [];
        }

        if (block.RagSettings.Count == 0)
        {
            if (block.HasPlaceholder(SharedInputKeys.INPUT_CONTEXT))
            {
                _logger.LogWarning(
                "{block} No RAG settings were available for block",
                block);
            }

            return [];
        }

        Dictionary<string, Dictionary<int, Page>> pagesToReturn = [];

        foreach (RagSettings ragSettings in block.RagSettings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check cache before executing expensive filtering
            string cacheKey = ComputeCacheKey(ragSettings, block.Documents);

            // Treat an empty cached result as a miss and recompute.
            if (resultsCache.TryGetValue(cacheKey, out Dictionary<string, Dictionary<int, Page>>? result)
                && result.Count > 0)
            {
                _logger.LogDebug(
                    "RAG cache hit for RagSettings type {Type}, reusing cached result with {PageCount} batches",
                    ragSettings.Type,
                    result.Count);
            }
            else
            {
                result = await GetFilteredPagesAsync(
                    ragSettings,
                    block.Documents,
                    workflowTypeId,
                    cancellationToken).ConfigureAwait(false);

                resultsCache[cacheKey] = result;

                _logger.LogDebug(
                    "RAG cache miss for RagSettings type {Type}, executed filtering",
                    ragSettings.Type);
            }

            MergeBatches(pagesToReturn, result);
        }

        return pagesToReturn;
    }

    /// <summary>
    /// Gets the filtered pages based on the specified filter expressions and
    /// RagSettings
    /// </summary>
    /// <param name="filterExpressions"></param>
    /// <param name="ragSettings"></param>
    /// <param name="documents"></param>
    /// <returns></returns>
    public async Task<Dictionary<string, Dictionary<int, Page>>> GetFilteredPagesAsync(
        RagSettings ragSettings,
        List<Document> documents,
        string workflowTypeId,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, Dictionary<int, Page>> pagesToReturn = [];

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug(
        "Filtering pages for documents {documents}",
        documents.Select(d => d.Name));

        List<Page> orderedPages = PagesHelper.GetOrderedPages(documents);
        string? topics = documents.FirstOrDefault(d => d.TableOfContents is not null)
            ?.TableOfContents;

        Dictionary<string, Dictionary<int, Page>> result = await GetFilteredPagesAsync(
            ragSettings,
            orderedPages,
            topics,
            workflowTypeId,
            cancellationToken).ConfigureAwait(false);

        MergeBatches(pagesToReturn, result);

        _logger.LogDebug(
            "Filtered pages for documents {document}: {num}",
            documents.Select(d => d.Name),
            pagesToReturn.Count);

        return pagesToReturn;
    }

    public async Task<Dictionary<string, Dictionary<int, Page>>> GetFilteredPagesAsync(
        RagSettings ragSettings,
        List<Page> pages,
        string? topics,
        string workflowTypeId,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        ICoreUnitOfWork coreUnitOfWork = scope.ServiceProvider
            .GetRequiredService<ICoreUnitOfWork>();

        RagExecutionContext context = new(
            _logger,
            _chatConnector,
            _embeddingsConnector,
            ragSettings,
            pages,
            ragSettings.SmartFilterExpressions,
            workflowTypeId)
        {
            Topics = topics,
            UseAutoDetectedTableOfContentsPrompts = await coreUnitOfWork.Tools.GetUseAutoDetectedTableOfContentsPromptsAsNoTrackingAsync(),
        };

        await context.PrepareVectorsAsync(cancellationToken);

        if (!_strategies.TryGetValue(ragSettings.Type, out IRagStrategy? strategy))
        {
            _logger.LogError(
                "No strategy found for RAG type: {type}",
                ragSettings.Type);

            return [];
        }

        Dictionary<string, Dictionary<int, Page>> result = await strategy.ExecuteFilteringAsync(context, cancellationToken);

        // Accumulate tokens from the context to the service-level accumulator
        _ = Interlocked.Add(ref _accumulatedInputTokens, context.AccumulatedInputTokens);
        _ = Interlocked.Add(ref _accumulatedOutputTokens, context.AccumulatedOutputTokens);
        _ = Interlocked.Add(ref _accumulatedEmbeddingTokens, context.AccumulatedEmbeddingTokens);

        // Capture model ID from context
        if (!string.IsNullOrWhiteSpace(context.LastUsedModelId))
        {
            LastUsedModelId = context.LastUsedModelId;
        }

        // Capture embedding model ID from context
        if (!string.IsNullOrWhiteSpace(context.LastUsedEmbeddingModelId))
        {
            LastUsedEmbeddingModelId = context.LastUsedEmbeddingModelId;
        }

        // Capture per-call LLM tracking for granular reporting
        if (context.LlmCalls.Count > 0)
        {
            LastLlmCalls = context.LlmCalls;
        }

        return result;
    }

    #endregion

    #region Private methods

    private static void MergeBatches(
        Dictionary<string, Dictionary<int, Page>> batches,
        Dictionary<string, Dictionary<int, Page>> batchesToAdd)
    {
        foreach ((string batchName, Dictionary<int, Page> pagesToAdd) in batchesToAdd)
        {
            if (!batches.TryGetValue(batchName, out Dictionary<int, Page>? existingBatch))
            {
                existingBatch = [];
                batches.Add(batchName, existingBatch);
            }

            foreach ((int pageIndex, Page page) in pagesToAdd)
            {
                _ = existingBatch.TryAdd(pageIndex, page);
            }
        }
    }

    /// <summary>
    /// Computes a cache key from resolved RagSettings values and document IDs.
    /// </summary>
    private static string ComputeCacheKey(RagSettings ragSettings, List<Document> documents)
    {
        RagSettingsExecutionState? es = ragSettings.ExecutionState;
        if (es is null)
        {
            return ragSettings.Id;
        }

        var sb = new StringBuilder();

        // RagSettings type
        _ = sb.Append((int)ragSettings.Type).Append('|');

        // ExecutionState resolved values
        _ = sb.Append(es.NumberOfPagesInput ?? "").Append('|');
        _ = sb.Append(es.StartIndexInput ?? "").Append('|');
        _ = sb.Append(es.EndIndexInput ?? "").Append('|');
        _ = sb.Append(es.ExactIndexesInput ?? "").Append('|');
        _ = sb.Append(es.PageAdjustmentInput ?? "").Append('|');
        _ = sb.Append(es.TopicsQuery ?? "").Append('|');
        _ = sb.Append(es.TableOfContentsInput ?? "").Append('|');
        _ = sb.Append(es.QueryTemplate ?? "").Append('|');
        _ = sb.Append(es.IterationLimit ?? "").Append('|');
        _ = sb.Append(es.ContextLimit ?? "").Append('|');

        // Lists (sorted for determinism)
        _ = sb.Append(string.Join(",", es.Keywords.Order())).Append('|');
        _ = sb.Append(string.Join(",", es.SmartFilterExpressions.Order())).Append('|');

        // Boolean flags
        _ = sb.Append(ragSettings.ShouldFilterKeyWordsExclusively).Append('|');
        _ = sb.Append(ragSettings.ShouldSearchOverviews).Append('|');
        _ = sb.Append(ragSettings.ShouldIncludeNeighboringPages).Append('|');

        // Document IDs (sorted for determinism)
        _ = sb.Append(string.Join(",", documents.Select(d => d.Id).Order()));

        return sb.ToString();
    }

    #endregion
}
