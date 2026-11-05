using IniaAi.Common.Embeddings;
using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class TopNPagesStrategy : IRagStrategy
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

        if (executionState.NumberOfPagesToUse <= 0)
        {
            context.Logger.LogError(
                "Invalid number of pages: {numberOfPages}",
                executionState.NumberOfPagesToUse);

            throw new ArgumentException("Number of pages must be greater than zero.");
        }

        List<(double Score, int Index)> scores = [];

        foreach (double[] filterVector in context.FilterVectors)
        {
            scores.AddRange(EmbeddingsUtils.RankVectors(context.PageVectors, filterVector));
        }

        scores.Sort((a, b) => b.Score.CompareTo(a.Score));

        EmbeddingsUtils.RemoveDoubledEntiresWithLowerScores(scores);

        var filteredIndices = scores
            .Take(executionState.NumberOfPagesToUse)
            .Select(s => s.Index)
            .ToList();

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
