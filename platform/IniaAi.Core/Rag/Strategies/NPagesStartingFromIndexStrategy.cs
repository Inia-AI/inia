using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class NPagesStartingFromIndexStrategy : IRagStrategy
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

        int startIndex = executionState.StartIndex - 1 + executionState.PageAdjustment;

        var filteredIndices = context.PageVectors.Keys
            .Where(k => k >= startIndex)
            .OrderBy(k => k)
            .Take(executionState.NumberOfPagesToUse)
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
