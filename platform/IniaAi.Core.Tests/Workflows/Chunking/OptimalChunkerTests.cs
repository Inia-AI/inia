using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Chunking;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;

namespace IniaAi.Core.Tests.Workflows.Chunking;

public class OptimalChunkerTests
{
    private static Document CreateTestDocument()
    {
        return new Document(new FileMetadata("test-id", "test.pdf", [0]), MediaType.ApplicationPdf);
    }

    private static Page CreatePage(Document document, int index, int charCount)
    {
        return new Page(document, index, index)
        {
            Text = new string('x', charCount)
        };
    }

    private static List<(int Index, Page Page)> CreatePages(params int[] charCounts)
    {
        Document document = CreateTestDocument();
        return [.. charCounts.Select((count, i) => (i, CreatePage(document, i, count)))];
    }

    private static Page CreateImageOnlyPage(Document document, int index)
    {
        return new Page(document, index, index)
        {
            Status = PageStatus.OCRNeedsReview
        };
    }

    #region FirstFitSequential (PreserveOrder = true)

    [Fact]
    public void ChunkPages_PreserveOrder_GroupsPagesSequentially()
    {
        // Arrange: 5 pages of 100 chars each, limit 250
        List<(int Index, Page Page)> pages = CreatePages(100, 100, 100, 100, 100);
        var chunker = new OptimalChunker(maxCharacters: 250, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Should create 3 chunks (100+100, 100+100, 100)
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result["chunk_0"].Count);
        Assert.Equal(2, result["chunk_1"].Count);
        _ = Assert.Single(result["chunk_2"]);
    }

    [Fact]
    public void ChunkPages_PreserveOrder_OversizedPageGetsOwnChunk()
    {
        // Arrange: Page 1 is oversized (500), others are normal (100)
        List<(int Index, Page Page)> pages = CreatePages(100, 500, 100, 100);
        var chunker = new OptimalChunker(maxCharacters: 250, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: 3 chunks - (100), (500 oversized), (100+100)
        Assert.Equal(3, result.Count);
        _ = Assert.Single(result["chunk_0"]);
        Assert.True(result["chunk_0"].ContainsKey(0));
        _ = Assert.Single(result["chunk_1"]);
        Assert.True(result["chunk_1"].ContainsKey(1)); // Oversized
        Assert.Equal(2, result["chunk_2"].Count);
    }

    [Fact]
    public void ChunkPages_PreserveOrder_AllPagesOversized()
    {
        // Arrange: All pages exceed limit
        List<(int Index, Page Page)> pages = CreatePages(500, 600, 700);
        var chunker = new OptimalChunker(maxCharacters: 250, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Each page in its own chunk
        Assert.Equal(3, result.Count);
        _ = Assert.Single(result["chunk_0"]);
        _ = Assert.Single(result["chunk_1"]);
        _ = Assert.Single(result["chunk_2"]);
    }

    [Fact]
    public void ChunkPages_PreserveOrder_MaintainsPageOrder()
    {
        // Arrange
        List<(int Index, Page Page)> pages = CreatePages(50, 60, 70, 80, 90);
        var chunker = new OptimalChunker(maxCharacters: 150, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Pages should be in ascending order within each chunk
        foreach (Dictionary<int, Page> chunk in result.Values)
        {
            var keys = chunk.Keys.ToList();
            for (int i = 1; i < keys.Count; i++)
            {
                Assert.True(keys[i] > keys[i - 1], "Keys should be in ascending order");
            }
        }
    }

    #endregion

    #region FirstFitDecreasing (PreserveOrder = false)

    [Fact]
    public void ChunkPages_NoPreserveOrder_OptimizesPacking()
    {
        // Arrange: Pages of varying sizes that can be packed more efficiently
        List<(int Index, Page Page)> pages = CreatePages(80, 70, 60, 50, 40);
        var chunker = new OptimalChunker(maxCharacters: 120, preserveOrder: false);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: FFD should produce fewer chunks (3 instead of 5 with naive approach)
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ChunkPages_NoPreserveOrder_OversizedPagesIsolated()
    {
        // Arrange
        List<(int Index, Page Page)> pages = CreatePages(200, 50, 40, 30);
        var chunker = new OptimalChunker(maxCharacters: 100, preserveOrder: false);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Oversized page (200) should be in its own chunk
        Dictionary<int, Page> oversizedChunk = result.Values.First(c => c.ContainsKey(0));
        _ = Assert.Single(oversizedChunk);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ChunkPages_EmptyInput_ReturnsEmptyDictionary()
    {
        // Arrange
        var pages = new List<(int Index, Page Page)>();
        var chunker = new OptimalChunker(maxCharacters: 100, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ChunkPages_SinglePage_ReturnsSingleChunk()
    {
        // Arrange
        List<(int Index, Page Page)> pages = CreatePages(50);
        var chunker = new OptimalChunker(maxCharacters: 100, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert
        _ = Assert.Single(result);
        _ = Assert.Single(result["chunk_0"]);
    }

    [Fact]
    public void ChunkPages_NullPageText_TreatedAsZeroChars()
    {
        // Arrange
        Document document = CreateTestDocument();
        var pages = new List<(int Index, Page Page)>
        {
            (0, new Page(document, 0, 0) { Text = null }),
            (1, new Page(document, 1, 1) { Text = "some text" })
        };
        var chunker = new OptimalChunker(maxCharacters: 100, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Both should fit in one chunk since null = 0 chars
        _ = Assert.Single(result);
        Assert.Equal(2, result["chunk_0"].Count);
    }

    [Fact]
    public void ChunkPages_ExactlyAtLimit_FitsInCurrentChunk()
    {
        // Arrange: Two pages that exactly hit the limit
        List<(int Index, Page Page)> pages = CreatePages(50, 50);
        var chunker = new OptimalChunker(maxCharacters: 100, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Both should fit in one chunk
        _ = Assert.Single(result);
        Assert.Equal(2, result["chunk_0"].Count);
    }

    [Fact]
    public void ChunkPages_OneCharOverLimit_StartsNewChunk()
    {
        // Arrange: Second page pushes over the limit
        List<(int Index, Page Page)> pages = CreatePages(50, 51);
        var chunker = new OptimalChunker(maxCharacters: 100, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Should create 2 chunks
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region Image-only pages (OCRNeedsReview)

    [Fact]
    public void ChunkPages_PreserveOrder_ImageOnlyPagesCarryNominalWeight()
    {
        // Arrange: two image-only pages (null text) at a limit that fits one
        // nominal image weight but not two.
        Document document = CreateTestDocument();
        var pages = new List<(int Index, Page Page)>
        {
            (0, CreateImageOnlyPage(document, 0)),
            (1, CreateImageOnlyPage(document, 1))
        };
        var chunker = new OptimalChunker(maxCharacters: 7000, preserveOrder: true);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: nominal image weight splits the two pages (zero-weight would pack into one)
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ChunkPages_NoPreserveOrder_ImageOnlyPagesCarryNominalWeight()
    {
        // Arrange
        Document document = CreateTestDocument();
        var pages = new List<(int Index, Page Page)>
        {
            (0, CreateImageOnlyPage(document, 0)),
            (1, CreateImageOnlyPage(document, 1))
        };
        var chunker = new OptimalChunker(maxCharacters: 7000, preserveOrder: false);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion
}
