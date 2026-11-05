using IniaAi.Common.Embeddings;
using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class KeywordBasedStrategy : IRagStrategy
{
    public async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(
        RagExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.Logger.LogInformation(
            "Executing data retrieval using '{name}'.",
            GetType().Name);

        RagSettingsExecutionState executionState = context.RagSettings.ExecutionState
            ?? new RagSettingsExecutionState(context.RagSettings);

        // Get vector-based matches
        HashSet<int> filteredIndices = [.. EmbeddingsUtils.GetBestMatchesWithNeighbors(
            context.PageVectors,
            context.FilterVectors,
            executionState.NumberOfPagesToUse)];

        // Get keyword-based matches (with neighbors if applicable)
        List<int> keywordIndices = GetKeywordIndices(
            executionState.Keywords,
            context.Pages,
            context.RagSettings.ShouldFilterKeyWordsExclusively,
            context.RagSettings.ShouldSearchOverviews,
            context.RagSettings.ShouldIncludeNeighboringPages);

        filteredIndices.UnionWith(keywordIndices);

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

    private static List<int> GetKeywordIndices(
        List<string> keywords,
        List<Page> pages,
        bool shouldFilterKeyWordsExclusively,
        bool shouldSearchOverviews,
        bool shouldIncludeNeighboringPages)
    {
        if (keywords is null || keywords.Count == 0)
        {
            return [];
        }

        List<List<int>> allKeywordIndices = [];

        foreach (string keyword in keywords)
        {
            List<int> keywordIndices = [];

            for (int i = 0; i < pages.Count; i++)
            {
                Page page = pages[i];
                string? searchTarget = shouldSearchOverviews
                    ? page.Overview
                    : page.ReadableText;

                if (searchTarget is null)
                {
                    continue;
                }

                if (searchTarget.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    keywordIndices.Add(i);
                }
            }

            allKeywordIndices.Add(keywordIndices);
        }

        if (allKeywordIndices.Count == 0)
        {
            return [];
        }

        // For exclusive (AND) filtering: if ANY keyword has no matches, result is empty
        // For inclusive (OR) filtering: if ALL keywords have no matches, result is empty
        if (shouldFilterKeyWordsExclusively)
        {
            if (allKeywordIndices.Any(list => list.Count == 0))
            {
                return [];
            }
        }
        else
        {
            if (allKeywordIndices.All(list => list.Count == 0))
            {
                return [];
            }
        }

        List<int> aggregatedIndices = shouldFilterKeyWordsExclusively
            ? allKeywordIndices.Aggregate((current, next) => [.. current.Intersect(next)])
            : allKeywordIndices.Aggregate((current, next) => [.. current.Union(next)]);

        if (!shouldIncludeNeighboringPages)
        {
            return [.. aggregatedIndices.Distinct().Order()];
        }

        HashSet<int> result = [];

        foreach (int index in aggregatedIndices)
        {
            _ = result.Add(index);

            if (index > 0)
            {
                _ = result.Add(index - 1);
            }

            if (index < pages.Count - 1)
            {
                _ = result.Add(index + 1);
            }
        }

        return [.. result.Order()];
    }
}
