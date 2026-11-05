using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Workflows.Chunking;

/// <summary>
/// Chunks pages optimally by character limit using bin-packing algorithms.
/// Minimizes the number of chunks while respecting the character limit.
/// </summary>
public class OptimalChunker : IPageChunker
{
    // ~1568 tokens per attached page image (the Anthropic estimate used in
    // AnthropicFormatAdapter) × ~4 characters per token.
    private const int IMAGE_NOMINAL_CHAR_WEIGHT = 6272;

    private readonly int _maxCharacters;
    private readonly bool _preserveOrder;

    public OptimalChunker(int maxCharacters, bool preserveOrder = true)
    {
        _maxCharacters = maxCharacters > 0 ? maxCharacters : 50000;
        _preserveOrder = preserveOrder;
    }

    public Dictionary<string, Dictionary<int, Page>> ChunkPages(List<(int Index, Page Page)> pages)
    {
        if (pages.Count == 0)
        {
            return [];
        }

        var pagesWithSizes = pages
            .Select(p => (p.Index, p.Page, CharCount: GetPageCharacterCount(p.Page)))
            .ToList();

        return _preserveOrder
            ? FirstFitSequential(pagesWithSizes)
            : FirstFitDecreasing(pagesWithSizes);
    }

    private static int GetPageCharacterCount(Page page)
    {
        int textCharacters = page.Text?.Length ?? 0;

        return page.IsOcrImageOnly
            ? textCharacters + IMAGE_NOMINAL_CHAR_WEIGHT
            : textCharacters;
    }

    /// <summary>
    /// First-Fit Sequential: Process pages in order, adding to current chunk
    /// until the limit is reached, then start a new chunk.
    /// Use when page order must be preserved.
    /// </summary>
    private Dictionary<string, Dictionary<int, Page>> FirstFitSequential(
        List<(int Index, Page Page, int CharCount)> pagesWithSizes)
    {
        Dictionary<string, Dictionary<int, Page>> chunks = [];
        var currentChunk = new Dictionary<int, Page>();
        int currentChunkSize = 0;
        int chunkIndex = 0;

        foreach ((int index, Page? page, int charCount) in pagesWithSizes)
        {
            if (charCount >= _maxCharacters)
            {
                FlushCurrentChunk(chunks, ref currentChunk, ref currentChunkSize, ref chunkIndex);
                chunks[$"chunk_{chunkIndex}"] = new Dictionary<int, Page> { [index] = page };
                chunkIndex++;
                continue;
            }

            if (currentChunkSize + charCount <= _maxCharacters)
            {
                currentChunk[index] = page;
                currentChunkSize += charCount;
            }
            else
            {
                FlushCurrentChunk(chunks, ref currentChunk, ref currentChunkSize, ref chunkIndex);
                currentChunk = new Dictionary<int, Page> { [index] = page };
                currentChunkSize = charCount;
            }
        }

        FlushCurrentChunk(chunks, ref currentChunk, ref currentChunkSize, ref chunkIndex);
        return chunks;
    }

    private static void FlushCurrentChunk(
        Dictionary<string, Dictionary<int, Page>> chunks,
        ref Dictionary<int, Page> currentChunk,
        ref int currentChunkSize,
        ref int chunkIndex)
    {
        if (currentChunk.Count > 0)
        {
            chunks[$"chunk_{chunkIndex}"] = currentChunk;
            chunkIndex++;
            currentChunk = [];
            currentChunkSize = 0;
        }
    }

    /// <summary>
    /// First-Fit Decreasing: Sort pages by size (largest first),
    /// then fit each page into the first chunk that has room.
    /// Produces fewer chunks but doesn't preserve page order.
    /// </summary>
    private Dictionary<string, Dictionary<int, Page>> FirstFitDecreasing(
        List<(int Index, Page Page, int CharCount)> pagesWithSizes)
    {
        var sortedPages = pagesWithSizes
            .OrderByDescending(p => p.CharCount)
            .ToList();

        var chunkList = new List<ChunkBucket>();

        foreach ((int index, Page? page, int charCount) in sortedPages)
        {
            if (charCount >= _maxCharacters)
            {
                chunkList.Add(new ChunkBucket(_maxCharacters, index, page, charCount));
                continue;
            }

            ChunkBucket? targetBucket = chunkList.FirstOrDefault(b => b.CanFit(charCount));

            if (targetBucket is not null)
            {
                targetBucket.Add(index, page, charCount);
            }
            else
            {
                chunkList.Add(new ChunkBucket(_maxCharacters, index, page, charCount));
            }
        }

        return chunkList
            .Select((bucket, i) => (Key: $"chunk_{i}", Value: bucket.Pages))
            .ToDictionary(x => x.Key, x => x.Value);
    }

    /// <summary>
    /// Helper class to track a chunk's pages and remaining capacity.
    /// </summary>
    private sealed class ChunkBucket
    {
        private readonly int _maxCharacters;
        private int _currentSize;

        public Dictionary<int, Page> Pages { get; } = [];

        public ChunkBucket(int maxCharacters, int initialIndex, Page initialPage, int initialCharCount)
        {
            _maxCharacters = maxCharacters;
            _currentSize = initialCharCount;
            Pages[initialIndex] = initialPage;
        }

        public bool CanFit(int charCount)
        {
            return _currentSize + charCount <= _maxCharacters;
        }

        public void Add(int index, Page page, int charCount)
        {
            Pages[index] = page;
            _currentSize += charCount;
        }
    }
}
