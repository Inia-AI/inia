using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Workflows.Chunking;

/// <summary>
/// Chunks pages by fixed page count (stride-based chunking).
/// </summary>
public class FixedCountChunker : IPageChunker
{
    private readonly int _chunkSize;

    public FixedCountChunker(int chunkSize)
    {
        _chunkSize = chunkSize > 0 ? chunkSize : 20;
    }

    public Dictionary<string, Dictionary<int, Page>> ChunkPages(List<(int Index, Page Page)> pages)
    {
        Dictionary<string, Dictionary<int, Page>> chunks = [];

        for (int i = 0; i < pages.Count; i += _chunkSize)
        {
            var chunkDict = new Dictionary<int, Page>();

            foreach ((int index, Page page) in pages.Skip(i).Take(_chunkSize))
            {
                chunkDict[index] = page;
            }

            chunks[$"{i}"] = chunkDict;
        }

        return chunks;
    }
}
