using IniaAi.Core.Documents;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Strategies;

public class RetrieveInformationFromDocumentStrategy : IBlockStrategy
{
    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BlockStrategyOutput output = new();

        RetrieveDataFromDocumentsProperties props = block.Properties as RetrieveDataFromDocumentsProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(RetrieveDataFromDocumentsProperties)}");

        Dictionary<string, Dictionary<int, Page>> filteredPages = await context.RagService
            .GetFilteredPagesAsync(
                block,
                context.WorkflowTypeId,
                context.RagResultsCache,
                context.AllPages,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        context.Logger.LogDebug(
            "{block} Filtered pages: {num}",
            block,
            filteredPages.Count);

        // Combine all indexed pages into a single dictionary, handling duplicate page numbers
        var combinedPages = filteredPages
            .SelectMany(kvp => kvp.Value)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);

        // Use the updated helper that expects Dictionary<int, Page>
        string response = props.ShouldUseOverviews
            ? PagesHelper.GetOverviewsInputContext(combinedPages)
            : PagesHelper.GetTextsInputContext(combinedPages);

        // Extract debug info from RAG execution state if available
        string? debugInfo = block.RagSettings
            .FirstOrDefault(rs => rs.ExecutionState?.DebugInfo is not null)
            ?.ExecutionState?.DebugInfo;

        _ = new BlockResponse(block)
        {
            RawResponse = response,
            DebugInfo = debugInfo,
        };

        context.Logger.LogTrace(
            "{block} response:\ntext from {number} pages",
            block,
            combinedPages.Count);

        if (context.IsCitationSource
            && !props.ShouldUseOverviews) // Overviews are not used as they are summaries and may not be suitable for citation, but the full text is.
        {
            BlockAnnotationProcessor.CreateFactsFromPages(
            combinedPages,
            context.BlockAnnotations,
            props.ShouldUseOverviews);
        }

        return output;
    }
}
