using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Core.Documents;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Strategies;

public class BuildTopicsForAllDocumentsStrategy : IBlockStrategy
{
    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        BlockStrategyOutput output = new();

        context.Logger.LogInformation("Building topics for all documents...");

        BuildTopicsProperties props = block.Properties as BuildTopicsProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(BuildTopicsProperties)}");

        props.ExecutionState ??= new (props);

        string? topicsModel = props.ExecutionState.TopicsModel;

        bool isForSpecificClass = block.SupportedDocumentClasses.Count > 0
            && block.SupportedDocumentClasses.All(x => x != "Any");

        Dictionary<int, Page> pages = [];
        if (block.RagSettings.Count != 0)
        {
            // Reset RAG token accumulator before filtering
            context.RagService.ResetTokenAccumulator();

            Dictionary<string, Dictionary<int, Page>> filteredPages = await context.RagService
               .GetFilteredPagesAsync(
                   block,
                   context.WorkflowTypeId,
                   context.RagResultsCache,
                   context.AllPages,
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);

            // Record RAG token usage
            RecordRagTokenUsage(block, context);

            // Combine all indexed pages into a single dictionary, handling duplicate page numbers
            pages = filteredPages
                .SelectMany(kvp => kvp.Value)
                .GroupBy(kvp => kvp.Key)
                .ToDictionary(g => g.Key, g => g.First().Value);
        }
        else
        {
            // Fallback to old functionality
            pages = GetPages(block, context, isForSpecificClass);
        }

        // Reset token accumulator before the operation
        context.DocumentAnalysisService.ResetTokenAccumulator();

        string? topics = await context.DocumentAnalysisService
            .GenerateTopicsFromOverviewsAsync(
                pages,
                context.OrganizationId,
                topicsModel,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Record accumulated token usage
        RecordTokenUsage(block, context, context.DocumentAnalysisService);

        _ = new BlockResponse(block)
        {
            RawResponse = topics,
        };

        if (isForSpecificClass)
        {
            context.Logger.LogInformation(
                "Built topics for {DocumentClass} documents: {Length} characters",
                block.SupportedDocumentClasses,
                topics?.Length ?? 0);
        }
        else
        {
            context.Logger.LogInformation(
                "Built topics for all documents: {Length} characters",
                topics?.Length ?? 0);
        }

        return output;
    }

    private static Dictionary<int, Page> GetPages(
        Block block,
        BlockExecutionContext context,
        bool isForSpecificClass)
    {
        List<Page> pages = isForSpecificClass
            ? PagesHelper.GetOrderedPages(block.Documents)
            : context.AllPages;

        return pages
            .Select((page, index) => new { Index = index, Page = page })
            .ToDictionary(p => p.Index, p => p.Page);
    }

    private static void RecordTokenUsage(
        Block block,
        BlockExecutionContext context,
        ITokenAccumulator accumulator)
    {
        if (string.IsNullOrEmpty(context.OrganizationId)
            || (accumulator.AccumulatedInputTokens == 0 && accumulator.AccumulatedOutputTokens == 0))
        {
            return;
        }

        string modelId = accumulator.LastUsedModelId ?? "standard";
        var record = new LlmUsageRecord
        {
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            WorkflowTypeId = context.WorkflowTypeId,
            WorkflowExecutionId = context.WorkflowExecutionId,
            SectionId = context.SectionId,
            BlockId = block.Id,
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = context.ExecutionContext,
            BlockType = block.Type,
            Provider = ModelIdNormalizer.GetProvider(modelId),
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = ModelIdNormalizer.Normalize(modelId),
            Components = UsageComponentHelper.FromTokens(
                accumulator.AccumulatedInputTokens,
                accumulator.AccumulatedOutputTokens,
                accumulator.AccumulatedCacheCreationTokens,
                accumulator.AccumulatedCacheReadTokens),
            Description = $"Build Topics block '{block.Name}'"
        };

        _ = context.TokenUsageService.RecordLlmUsageAsync(record, CancellationToken.None);
    }

    private static void RecordRagTokenUsage(
        Block block,
        BlockExecutionContext context)
    {
        if (string.IsNullOrEmpty(context.OrganizationId))
        {
            return;
        }

        // Record LLM token usage
        int inputTokens = context.RagService.AccumulatedInputTokens;
        int outputTokens = context.RagService.AccumulatedOutputTokens;

        if (inputTokens > 0 || outputTokens > 0)
        {
            string llmModelId = context.RagService.LastUsedModelId ?? "rag-filtering";
            var llmRecord = new LlmUsageRecord
            {
                OrganizationId = context.OrganizationId,
                UserId = context.UserId,
                WorkflowTypeId = context.WorkflowTypeId,
                WorkflowExecutionId = context.WorkflowExecutionId,
                SectionId = context.SectionId,
                BlockId = block.Id,
                UsageType = UsageType.LlmRagFiltering,
                ExecutionContext = context.ExecutionContext,
                BlockType = block.Type,
                Provider = ModelIdNormalizer.GetProvider(llmModelId),
                Family = ModelIdNormalizer.GetFamily(llmModelId),
                ModelId = ModelIdNormalizer.Normalize(llmModelId),
                Components = UsageComponentHelper.FromTokens(inputTokens, outputTokens),
                Description = $"RAG page filtering for block '{block.Name}'"
            };

            _ = context.TokenUsageService.RecordLlmUsageAsync(llmRecord, CancellationToken.None);
        }

        // Record embedding token usage for RAG query embeddings
        int embeddingTokens = context.RagService.AccumulatedEmbeddingTokens;

        if (embeddingTokens > 0)
        {
            string embeddingModelId = context.RagService.LastUsedEmbeddingModelId ?? "embed-english-v3.0";
            var embeddingRecord = new EmbeddingUsageRecord
            {
                OrganizationId = context.OrganizationId,
                UserId = context.UserId,
                WorkflowTypeId = context.WorkflowTypeId,
                WorkflowExecutionId = context.WorkflowExecutionId,
                SectionId = context.SectionId,
                BlockId = block.Id,
                UsageType = UsageType.EmbeddingQuery,
                ExecutionContext = context.ExecutionContext,
                Provider = ModelIdNormalizer.GetProvider(embeddingModelId),
                Family = ModelIdNormalizer.GetFamily(embeddingModelId),
                ModelId = embeddingModelId,
                Components = UsageComponentHelper.ForEmbedding(embeddingTokens),
                Description = $"RAG embedding query for block '{block.Name}'"
            };

            _ = context.TokenUsageService.RecordEmbeddingUsageAsync(embeddingRecord, CancellationToken.None);
        }
    }
}
