using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class WholeDocumentStrategy : IRagStrategy
{
    public async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(RagExecutionContext context, CancellationToken cancellationToken = default)
    {
        context.Logger.LogInformation(
            "Executing data retrieval using '{name}'.",
            GetType().Name);

        Dictionary<int, Page> indexedPages = [];

        for (int i = 0; i < context.Pages.Count; i++)
        {
            indexedPages[i] = context.Pages[i];
        }

        return await Task.FromResult(new Dictionary<string, Dictionary<int, Page>>
        {
            { string.Empty, indexedPages }
        });
    }
}
