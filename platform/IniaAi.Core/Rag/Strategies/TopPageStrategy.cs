using IniaAi.Common.Embeddings;
using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class TopPageStrategy : IRagStrategy
{
    public async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(
        RagExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Logger.LogInformation(
            "Executing data retrieval using '{name}'.",
            GetType().Name);

        List<int> filteredIndices = [];

        foreach (double[] filterVector in context.FilterVectors)
        {
            filteredIndices.Add(
                EmbeddingsUtils.RankVectors(context.PageVectors, filterVector)[0].Index);
        }

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
