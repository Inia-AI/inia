using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;

namespace IniaAi.AiConnection.Tests.Common.ChatConnector;

public class RequestContentSequenceTests
{
    [Fact]
    public void Add_StringContent_AddsStringContentToSequence()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        string input = "Hello, world!";

        // Act
        sequence.Add(input);

        // Assert
        _ = Assert.Single(sequence);
        Assert.Equal(input, sequence[0].TextContent);
    }

    [Fact]
    public void Add_BinaryContent_AddsBinaryContentToSequence()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var content = new BinaryContent(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);

        // Act
        sequence.Add(content);

        // Assert
        _ = Assert.Single(sequence);
        Assert.True(sequence[0].Type is CommonContentType.Image);
        Assert.Equal(MediaType.ImagePng, sequence[0].BinaryContent?.MediaType);
    }

    [Fact]
    public void Add_StringContentAndBinaryContent_AddsContentToSequence()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        string input = "Hello, world!";
        var content = new BinaryContent(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);

        // Act
        sequence.Add(input);
        sequence.Add(content);

        // Assert
        Assert.Equal(2, sequence.Count());
        Assert.Equal(input, sequence[0].TextContent);
        Assert.True(sequence[1].Type is CommonContentType.Image);
        Assert.Equal(MediaType.ImagePng, sequence[1].BinaryContent?.MediaType);
    }

    [Fact]
    public void Add_BinaryContentAndStringContent_AddsContentToSequence()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        string input = "Hello, world!";
        var content = new BinaryContent(new BinaryData([0x01, 0x02, 0x03]), MediaType.ImagePng);

        // Act
        sequence.Add(content);
        sequence.Add(input);

        // Assert
        Assert.Equal(2, sequence.Count());
        Assert.True(sequence[0].Type is CommonContentType.Image);
        Assert.Equal(MediaType.ImagePng, sequence[0].BinaryContent?.MediaType);
        Assert.Equal(input, sequence[1].TextContent);
    }

    [Fact]
    public void Replace_StringContent_ReplacesStringContentInSequence()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add("Hello, {Name}!");

        // Act
        sequence.Replace("{Name}", "John");

        // Assert
        Assert.Equal("Hello, John!", sequence[0].TextContent);
    }

    [Fact]
    public void GetSourceContents_EmptySequence_ReturnsEmptyList()
    {
        // Arrange
        var sequence = new RequestContentSequence();

        // Act
        List<RequestCommonContent> result = sequence.GetSourceContents();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetSourceContents_NoInputContextOrBlockReference_ReturnsEmptyList()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add("Plain text content");
        sequence.Add(new BinaryContent(new BinaryData([0x01, 0x02]), MediaType.ImagePng));

        // Act
        List<RequestCommonContent> result = sequence.GetSourceContents();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetSourceContents_OnlyInputContextItems_ReturnsInputContextItems()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var inputContextContent = new RequestCommonContent("Input context text")
        {
            IsInputContext = true,
            Title = "Document1",
            ReferenceId = "page-1"
        };
        sequence.Add(inputContextContent);
        sequence.Add("Plain text");

        // Act
        List<RequestCommonContent> result = sequence.GetSourceContents();

        // Assert
        _ = Assert.Single(result);
        Assert.Same(inputContextContent, result[0]);
    }

    [Fact]
    public void GetSourceContents_OnlyBlockReferenceItems_ReturnsBlockReferenceItems()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var blockRefContent = new RequestCommonContent("Block reference text")
        {
            IsBlockReference = true,
            ReferenceId = "block-123"
        };
        sequence.Add("Plain text");
        sequence.Add(blockRefContent);

        // Act
        List<RequestCommonContent> result = sequence.GetSourceContents();

        // Assert
        _ = Assert.Single(result);
        Assert.Same(blockRefContent, result[0]);
    }

    [Fact]
    public void GetSourceContents_MixedContent_ReturnsOnlySourceContents()
    {
        // Arrange
        var sequence = new RequestContentSequence();

        var inputContext1 = new RequestCommonContent("Input context 1")
        {
            IsInputContext = true,
            ReferenceId = "page-1"
        };
        var blockRef1 = new RequestCommonContent("Block ref 1")
        {
            IsBlockReference = true,
            ReferenceId = "block-1"
        };
        var inputContext2 = new RequestCommonContent("Input context 2")
        {
            IsInputContext = true,
            ReferenceId = "page-2"
        };

        sequence.Add("Plain text 1");
        sequence.Add(inputContext1);
        sequence.Add(new BinaryContent(new BinaryData([0x01]), MediaType.ImagePng));
        sequence.Add(blockRef1);
        sequence.Add("Plain text 2");
        sequence.Add(inputContext2);

        // Act
        List<RequestCommonContent> result = sequence.GetSourceContents();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Same(inputContext1, result[0]);
        Assert.Same(blockRef1, result[1]);
        Assert.Same(inputContext2, result[2]);
    }

    [Fact]
    public void GetSourceContents_ContentWithBothFlags_ReturnsContent()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var bothFlagsContent = new RequestCommonContent("Both flags content")
        {
            IsInputContext = true,
            IsBlockReference = true,
            ReferenceId = "page-1",
        };
        sequence.Add(bothFlagsContent);

        // Act
        List<RequestCommonContent> result = sequence.GetSourceContents();

        // Assert
        _ = Assert.Single(result);
        Assert.Same(bothFlagsContent, result[0]);
    }

    [Fact]
    public void GetSourceContents_PreservesOrderOfSourceContents()
    {
        // Arrange
        var sequence = new RequestContentSequence();

        var content1 = new RequestCommonContent("First") { IsInputContext = true };
        var content2 = new RequestCommonContent("Second") { IsBlockReference = true };
        var content3 = new RequestCommonContent("Third") { IsInputContext = true };

        sequence.Add(content1);
        sequence.Add("Not a source");
        sequence.Add(content2);
        sequence.Add("Also not a source");
        sequence.Add(content3);

        // Act
        List<RequestCommonContent> result = sequence.GetSourceContents();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("First", result[0].TextContent);
        Assert.Equal("Second", result[1].TextContent);
        Assert.Equal("Third", result[2].TextContent);
    }

    #region Condense Tests

    [Fact]
    public void Condense_EmptySequence_RemainsEmpty()
    {
        // Arrange
        var sequence = new RequestContentSequence();

        // Act
        sequence.Condense();

        // Assert
        Assert.Empty(sequence);
    }

    [Fact]
    public void Condense_SingleTextItem_RemainsUnchanged()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add("Hello, world!");

        // Act
        sequence.Condense();

        // Assert
        _ = Assert.Single(sequence);
        Assert.Equal("Hello, world!", sequence[0].TextContent);
    }

    [Fact]
    public void Condense_MultipleAdjacentTextItems_MergesIntoOne()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add("Hello, ");
        sequence.Add("world");
        sequence.Add("!");

        // Act
        sequence.Condense();

        // Assert
        _ = Assert.Single(sequence);
        Assert.Equal("Hello, world!", sequence[0].TextContent);
    }

    [Fact]
    public void Condense_SingleNonTextItem_RemainsUnchanged()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var imageContent = new BinaryContent(new BinaryData([0x01, 0x02]), MediaType.ImagePng);
        sequence.Add(imageContent);

        // Act
        sequence.Condense();

        // Assert
        _ = Assert.Single(sequence);
        Assert.Equal(CommonContentType.Image, sequence[0].Type);
        Assert.Same(imageContent, sequence[0].BinaryContent);
    }

    [Fact]
    public void Condense_MultipleNonTextItems_PreservesAll()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var image1 = new BinaryContent(new BinaryData([0x01]), MediaType.ImagePng);
        var image2 = new BinaryContent(new BinaryData([0x02]), MediaType.ImageJpeg);
        sequence.Add(image1);
        sequence.Add(image2);

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(2, sequence.Count());
        Assert.Same(image1, sequence[0].BinaryContent);
        Assert.Same(image2, sequence[1].BinaryContent);
    }

    [Fact]
    public void Condense_TextNonTextText_DoesNotMergeAcrossNonText()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var imageContent = new BinaryContent(new BinaryData([0x01]), MediaType.ImagePng);
        sequence.Add("Before image");
        sequence.Add(imageContent);
        sequence.Add("After image");

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(3, sequence.Count());
        Assert.Equal("Before image", sequence[0].TextContent);
        Assert.Equal(CommonContentType.Image, sequence[1].Type);
        Assert.Equal("After image", sequence[2].TextContent);
    }

    [Fact]
    public void Condense_MixedContent_MergesOnlyAdjacentText()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var imageContent = new BinaryContent(new BinaryData([0x01]), MediaType.ImagePng);
        sequence.Add("Hello, ");
        sequence.Add("world!");
        sequence.Add(imageContent);
        sequence.Add("Goodbye, ");
        sequence.Add("everyone!");

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(3, sequence.Count());
        Assert.Equal("Hello, world!", sequence[0].TextContent);
        Assert.Equal(CommonContentType.Image, sequence[1].Type);
        Assert.Equal("Goodbye, everyone!", sequence[2].TextContent);
    }

    [Fact]
    public void Condense_MultipleNonTextBetweenText_PreservesAll()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var image1 = new BinaryContent(new BinaryData([0x01]), MediaType.ImagePng);
        var image2 = new BinaryContent(new BinaryData([0x02]), MediaType.ImageJpeg);
        sequence.Add("Text 1");
        sequence.Add(image1);
        sequence.Add(image2);
        sequence.Add("Text 2");

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(4, sequence.Count());
        Assert.Equal("Text 1", sequence[0].TextContent);
        Assert.Same(image1, sequence[1].BinaryContent);
        Assert.Same(image2, sequence[2].BinaryContent);
        Assert.Equal("Text 2", sequence[3].TextContent);
    }

    [Fact]
    public void Condense_TextWithNullContent_HandlesGracefully()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add(new RequestCommonContent(null!));
        sequence.Add("Hello");

        // Act
        sequence.Condense();

        // Assert
        _ = Assert.Single(sequence);
        Assert.Equal("Hello", sequence[0].TextContent);
    }

    [Fact]
    public void Condense_AlternatingTextAndNonText_NoMerging()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        var image1 = new BinaryContent(new BinaryData([0x01]), MediaType.ImagePng);
        var image2 = new BinaryContent(new BinaryData([0x02]), MediaType.ImageJpeg);
        sequence.Add("Text 1");
        sequence.Add(image1);
        sequence.Add("Text 2");
        sequence.Add(image2);
        sequence.Add("Text 3");

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(5, sequence.Count());
        Assert.Equal("Text 1", sequence[0].TextContent);
        Assert.Same(image1, sequence[1].BinaryContent);
        Assert.Equal("Text 2", sequence[2].TextContent);
        Assert.Same(image2, sequence[3].BinaryContent);
        Assert.Equal("Text 3", sequence[4].TextContent);
    }

    [Fact]
    public void Condense_BlockReferenceText_NotMergedWithAdjacentText()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add("Plain text before ");
        sequence.Add(new RequestCommonContent("Block reference content")
        {
            IsBlockReference = true,
            ReferenceId = "block-1"
        });
        sequence.Add(" plain text after");

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(3, sequence.Count());
        Assert.Equal("Plain text before ", sequence[0].TextContent);
        Assert.False(sequence[0].IsBlockReference);

        Assert.Equal("Block reference content", sequence[1].TextContent);
        Assert.True(sequence[1].IsBlockReference);

        Assert.Equal(" plain text after", sequence[2].TextContent);
        Assert.False(sequence[2].IsBlockReference);
    }

    [Fact]
    public void Condense_InputContextText_NotMergedWithAdjacentText()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add("Plain text before ");
        sequence.Add(new RequestCommonContent("Input context content")
        {
            IsInputContext = true,
            ReferenceId = "page-1"
        });
        sequence.Add(" plain text after");

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(3, sequence.Count());
        Assert.Equal("Plain text before ", sequence[0].TextContent);
        Assert.False(sequence[0].IsInputContext);

        Assert.Equal("Input context content", sequence[1].TextContent);
        Assert.True(sequence[1].IsInputContext);

        Assert.Equal(" plain text after", sequence[2].TextContent);
        Assert.False(sequence[2].IsInputContext);
    }

    [Fact]
    public void Condense_MultipleBlockReferences_RemainSeparate()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add(new RequestCommonContent("Block 1") { IsBlockReference = true, ReferenceId = "b1" });
        sequence.Add(new RequestCommonContent("Block 2") { IsBlockReference = true, ReferenceId = "b2" });
        sequence.Add(new RequestCommonContent("Block 3") { IsBlockReference = true, ReferenceId = "b3" });

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(3, sequence.Count());
        Assert.Equal("Block 1", sequence[0].TextContent);
        Assert.Equal("Block 2", sequence[1].TextContent);
        Assert.Equal("Block 3", sequence[2].TextContent);
        Assert.All(sequence, item => Assert.True(item.IsBlockReference));
    }

    [Fact]
    public void Condense_MultipleInputContexts_RemainSeparate()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add(new RequestCommonContent("Page 1") { IsInputContext = true, ReferenceId = "p1" });
        sequence.Add(new RequestCommonContent("Page 2") { IsInputContext = true, ReferenceId = "p2" });
        sequence.Add(new RequestCommonContent("Page 3") { IsInputContext = true, ReferenceId = "p3" });

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(3, sequence.Count());
        Assert.Equal("Page 1", sequence[0].TextContent);
        Assert.Equal("Page 2", sequence[1].TextContent);
        Assert.Equal("Page 3", sequence[2].TextContent);
        Assert.All(sequence, item => Assert.True(item.IsInputContext));
    }

    [Fact]
    public void Condense_PlainTextAroundSourceContent_MergesOnlyPlainText()
    {
        // Arrange
        var sequence = new RequestContentSequence();
        sequence.Add("Hello ");
        sequence.Add("world! ");
        sequence.Add(new RequestCommonContent("Block output") { IsBlockReference = true });
        sequence.Add(" Goodbye ");
        sequence.Add("everyone!");

        // Act
        sequence.Condense();

        // Assert
        Assert.Equal(3, sequence.Count());
        Assert.Equal("Hello world! ", sequence[0].TextContent);
        Assert.Equal("Block output", sequence[1].TextContent);
        Assert.Equal(" Goodbye everyone!", sequence[2].TextContent);
    }

    #endregion
}
