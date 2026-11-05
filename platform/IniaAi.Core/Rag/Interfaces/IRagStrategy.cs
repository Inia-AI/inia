using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Rag.Interfaces;

internal interface IRagStrategy
{
    Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(RagExecutionContext context, CancellationToken cancellationToken = default);
}
