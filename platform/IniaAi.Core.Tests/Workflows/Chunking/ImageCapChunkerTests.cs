using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Chunking;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Tests.Workflows.Chunking;

public class ImageCapChunkerTests
{
    private static Document CreateTestDocument()
    {
        return new Document(new FileMetadata("test-id", "test.pdf", [0]), MediaType.ApplicationPdf);
    }

    private static Page CreateImagePage(Document document, int index)
    {
        return new Page(document, index, index)
        {
            Image = new BinaryContent([1, 2, 3], MediaType.ImagePng)
        };
    }

    private static Page CreateTextPage(Document document, int index)
    {
        return new Page(document, index, index)
        {
            Text = "text"
        };
    }

    private static int TotalPages(Dictionary<string, Dictionary<int, Page>> groups)
    {
        return groups.Values.Sum(g => g.Count);
    }

    private static int MaxImagesInAnyGroup(Dictionary<string, Dictionary<int, Page>> groups)
    {
        return groups.Values.Max(g => g.Values.Count(p => p.Image is not null));
    }

    [Fact]
    public void CapByImageCount_GroupUnderLimit_ReturnsSingleGroupUnchanged()
    {
        // Arrange
        Document document = CreateTestDocument();
        var groups = new Dictionary<string, Dictionary<int, Page>>
        {
            ["chunk_0"] = new()
            {
                [0] = CreateImagePage(document, 0),
                [1] = CreateImagePage(document, 1)
            }
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> result =
            ImageCapChunker.CapByImageCount(groups, maxImagesPerRequest: 3);

        // Assert
        _ = Assert.Single(result);
        Assert.Equal(2, TotalPages(result));
    }

    [Fact]
    public void CapByImageCount_GroupOverLimit_SplitsIntoMultipleGroups()
    {
        // Arrange: 5 image pages, cap 2 → 3 groups (2, 2, 1)
        Document document = CreateTestDocument();
        var inner = new Dictionary<int, Page>();
        for (int i = 0; i < 5; i++)
        {
            inner[i] = CreateImagePage(document, i);
        }
        var groups = new Dictionary<string, Dictionary<int, Page>> { ["chunk_0"] = inner };

        // Act
        Dictionary<string, Dictionary<int, Page>> result =
            ImageCapChunker.CapByImageCount(groups, maxImagesPerRequest: 2);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(5, TotalPages(result));
        Assert.True(MaxImagesInAnyGroup(result) <= 2);
    }

    [Fact]
    public void CapByImageCount_NonImagePagesDoNotCountTowardCap()
    {
        // Arrange: 6 text-only pages, cap 2 → still a single group (no images)
        Document document = CreateTestDocument();
        var inner = new Dictionary<int, Page>();
        for (int i = 0; i < 6; i++)
        {
            inner[i] = CreateTextPage(document, i);
        }
        var groups = new Dictionary<string, Dictionary<int, Page>> { ["chunk_0"] = inner };

        // Act
        Dictionary<string, Dictionary<int, Page>> result =
            ImageCapChunker.CapByImageCount(groups, maxImagesPerRequest: 2);

        // Assert
        _ = Assert.Single(result);
        Assert.Equal(6, TotalPages(result));
    }

    [Fact]
    public void CapByImageCount_MixedPages_OnlyImagesCountTowardCap()
    {
        // Arrange: img, txt, img, txt, img (3 images), cap 2 → splits, all 5 pages preserved
        Document document = CreateTestDocument();
        var inner = new Dictionary<int, Page>
        {
            [0] = CreateImagePage(document, 0),
            [1] = CreateTextPage(document, 1),
            [2] = CreateImagePage(document, 2),
            [3] = CreateTextPage(document, 3),
            [4] = CreateImagePage(document, 4)
        };
        var groups = new Dictionary<string, Dictionary<int, Page>> { ["chunk_0"] = inner };

        // Act
        Dictionary<string, Dictionary<int, Page>> result =
            ImageCapChunker.CapByImageCount(groups, maxImagesPerRequest: 2);

        // Assert
        Assert.True(result.Count >= 2);
        Assert.Equal(5, TotalPages(result));
        Assert.True(MaxImagesInAnyGroup(result) <= 2);
    }

    [Fact]
    public void CapByImageCount_MultipleInputGroups_EachCappedAndRekeyedSequentially()
    {
        // Arrange: two groups of 3 image pages, cap 2 → each → (2, 1) = 4 groups
        Document document = CreateTestDocument();
        var groups = new Dictionary<string, Dictionary<int, Page>>
        {
            ["chunk_0"] = new()
            {
                [0] = CreateImagePage(document, 0),
                [1] = CreateImagePage(document, 1),
                [2] = CreateImagePage(document, 2)
            },
            ["chunk_1"] = new()
            {
                [3] = CreateImagePage(document, 3),
                [4] = CreateImagePage(document, 4),
                [5] = CreateImagePage(document, 5)
            }
        };

        // Act
        Dictionary<string, Dictionary<int, Page>> result =
            ImageCapChunker.CapByImageCount(groups, maxImagesPerRequest: 2);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Equal(6, TotalPages(result));
        Assert.True(MaxImagesInAnyGroup(result) <= 2);
        Assert.Equal(["chunk_0", "chunk_1", "chunk_2", "chunk_3"], result.Keys);
    }

    [Fact]
    public void CapByImageCount_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var groups = new Dictionary<string, Dictionary<int, Page>>();

        // Act
        Dictionary<string, Dictionary<int, Page>> result =
            ImageCapChunker.CapByImageCount(groups, maxImagesPerRequest: 2);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void CapByImageCount_ScrambledInsertionOrder_SplitsByPageIndexOrder()
    {
        // Arrange
        Document document = CreateTestDocument();
        var scrambled = new Dictionary<int, Page>
        {
            [3] = CreateImagePage(document, 3),
            [0] = CreateImagePage(document, 0),
            [2] = CreateImagePage(document, 2),
            [1] = CreateImagePage(document, 1)
        };
        var groups = new Dictionary<string, Dictionary<int, Page>> { ["chunk_0"] = scrambled };

        // Act
        Dictionary<string, Dictionary<int, Page>> result =
            ImageCapChunker.CapByImageCount(groups, maxImagesPerRequest: 2);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal([0, 1], result["chunk_0"].Keys.Order());
        Assert.Equal([2, 3], result["chunk_1"].Keys.Order());
    }
}
