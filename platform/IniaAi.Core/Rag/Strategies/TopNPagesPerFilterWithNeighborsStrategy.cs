using IniaAi.Common.Embeddings;
using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class TopNPagesPerFilterWithNeighborsStrategy : IRagStrategy
{
    public async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(
        RagExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Logger.LogInformation(
            "Executing data retrieval using '{name}'.",
            GetType().Name);

        RagSettingsExecutionState executionState = context.RagSettings.ExecutionState
            ?? new RagSettingsExecutionState(context.RagSettings);

        List<int> filteredIndices = EmbeddingsUtils.GetBestMatchesWithNeighbors(
            context.PageVectors,
            context.FilterVectors,
            executionState.NumberOfPagesToUse);

        Dictionary<int, Page> indexedPages = [];

        foreach (int index in filteredIndices.Order())
        {
            indexedPages[index] = context.Pages[index];
        }

        return await Task.FromResult(new Dictionary<string, Dictionary<int, Page>>
        {
            { string.Empty, indexedPages }
        });
    }
}
