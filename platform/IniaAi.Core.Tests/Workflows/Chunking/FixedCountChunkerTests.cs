using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Chunking;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Tests.Workflows.Chunking;

public class FixedCountChunkerTests
{
    private static Document CreateTestDocument()
    {
        return new Document(new FileMetadata("test-id", "test.pdf", [0]), MediaType.ApplicationPdf);
    }

    private static Page CreatePage(Document document, int index)
    {
        return new Page(document, index, index) { Text = $"Page {index} content" };
    }

    private static List<(int Index, Page Page)> CreatePages(int count)
    {
        Document document = CreateTestDocument();
        return [.. Enumerable.Range(0, count).Select(i => (i, CreatePage(document, i)))];
    }

    [Fact]
    public void ChunkPages_ExactMultiple_CreatesEqualChunks()
    {
        // Arrange: 10 pages, chunk size 5
        List<(int Index, Page Page)> pages = CreatePages(10);
        var chunker = new FixedCountChunker(chunkSize: 5);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: 2 chunks of 5 each
        Assert.Equal(2, result.Count);
        Assert.Equal(5, result["0"].Count);
        Assert.Equal(5, result["5"].Count);
    }

    [Fact]
    public void ChunkPages_NotExactMultiple_LastChunkSmaller()
    {
        // Arrange: 7 pages, chunk size 3
        List<(int Index, Page Page)> pages = CreatePages(7);
        var chunker = new FixedCountChunker(chunkSize: 3);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: 3 chunks (3, 3, 1)
        Assert.Equal(3, result.Count);
        Assert.Equal(3, result["0"].Count);
        Assert.Equal(3, result["3"].Count);
        _ = Assert.Single(result["6"]);
    }

    [Fact]
    public void ChunkPages_FewerThanChunkSize_SingleChunk()
    {
        // Arrange: 3 pages, chunk size 10
        List<(int Index, Page Page)> pages = CreatePages(3);
        var chunker = new FixedCountChunker(chunkSize: 10);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Single chunk with all pages
        _ = Assert.Single(result);
        Assert.Equal(3, result["0"].Count);
    }

    [Fact]
    public void ChunkPages_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var pages = new List<(int Index, Page Page)>();
        var chunker = new FixedCountChunker(chunkSize: 5);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ChunkPages_ChunkSizeOne_OnePagePerChunk()
    {
        // Arrange
        List<(int Index, Page Page)> pages = CreatePages(3);
        var chunker = new FixedCountChunker(chunkSize: 1);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: 3 chunks, one page each
        Assert.Equal(3, result.Count);
        Assert.All(result.Values, chunk => Assert.Single(chunk));
    }

    [Fact]
    public void ChunkPages_PreservesPageIndices()
    {
        // Arrange
        List<(int Index, Page Page)> pages = CreatePages(5);
        var chunker = new FixedCountChunker(chunkSize: 2);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Original indices are preserved
        Assert.True(result["0"].ContainsKey(0));
        Assert.True(result["0"].ContainsKey(1));
        Assert.True(result["2"].ContainsKey(2));
        Assert.True(result["2"].ContainsKey(3));
        Assert.True(result["4"].ContainsKey(4));
    }

    [Fact]
    public void ChunkPages_InvalidChunkSize_DefaultsToPositive()
    {
        // Arrange: Invalid chunk size should be handled
        List<(int Index, Page Page)> pages = CreatePages(5);
        var chunker = new FixedCountChunker(chunkSize: 0);

        // Act
        Dictionary<string, Dictionary<int, Page>> result = chunker.ChunkPages(pages);

        // Assert: Should still work (defaults to 20)
        _ = Assert.Single(result);
    }
}
