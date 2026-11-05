using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Workflows.Chunking;

/// <summary>
/// Interface for page chunking strategies.
/// </summary>
public interface IPageChunker
{
    /// <summary>
    /// Chunks pages into groups based on the implementation strategy.
    /// </summary>
    /// <param name="pages">List of pages with their original indices.</param>
    /// <returns>Dictionary where key is chunk name and value is pages in that chunk.</returns>
    Dictionary<string, Dictionary<int, Page>> ChunkPages(List<(int Index, Page Page)> pages);
}
