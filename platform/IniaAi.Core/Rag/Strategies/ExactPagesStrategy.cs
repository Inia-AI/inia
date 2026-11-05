using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class ExactPagesStrategy : IRagStrategy
{
    public async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(
        RagExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Logger.LogInformation(
            "Executing data retrieval using '{name}'.",
            GetType().Name);

        List<int> filteredIndices = FilterIndices(
            context.RagSettings,
            context.Pages.Count);

        Dictionary<int, Page> indexedPages = [];

        foreach (int index in filteredIndices.Order())
        {
            if (index < 0 || index >= context.Pages.Count)
            {
                continue;
            }

            indexedPages[index] = context.Pages[index];
        }

        return await Task.FromResult(new Dictionary<string, Dictionary<int, Page>>
        {
            { string.Empty, indexedPages }
        });
    }

    private static List<int> FilterIndices(RagSettings ragSettings, int pageCount)
    {
        RagSettingsExecutionState executionState = ragSettings.ExecutionState
            ?? new RagSettingsExecutionState(ragSettings);

        List<int> adjustedIndices = [.. executionState
            .ExactIndexes
            .Select(index => ResolveIndex(index, pageCount) + executionState.PageAdjustment)];

        int numberOfFollowingPages = string.IsNullOrWhiteSpace(ragSettings.NumberOfPagesInput)
            ? 0
            : executionState.NumberOfPagesToUse;

        if (numberOfFollowingPages <= 0)
        {
            return adjustedIndices;
        }

        var expanded = new SortedSet<int>();
        foreach (int index in adjustedIndices)
        {
            if (index < 0 || index >= pageCount)
            {
                continue;
            }

            for (int offset = 0; offset <= numberOfFollowingPages; offset++)
            {
                _ = expanded.Add(index + offset);
            }
        }

        return [.. expanded];
    }

    private static int ResolveIndex(int index, int pageCount)
    {
        return index > 0 ? index - 1 : index < 0 ? pageCount + index : -1;
    }
}
