using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class ContentRequestTests
{
    private const string JSON1 =
    """
    {
      "type": "image",
      "source": {
        "type": "base64",
        "media_type": "image/jpeg",
        "data": "base64_encoded_data_here..."
      }
    }
    """;

    private const string JSON2 =
    """
    {
      "type": "text",
      "text": "What is in the above image?"
    }
    """;

    private const string JSON3 =
    """
    {
      "type": "text",
      "text": "<the entire contents of Pride and Prejudice>",
      "cache_control": {
        "type": "ephemeral"
      }
    }
    """;

    [Fact]
    public void FromJson_ValidJson_ReturnsContent()
    {
        // Arrange

        // Act
        var content1 = ContentRequest.FromJson(JSON1);
        var content2 = ContentRequest.FromJson(JSON2);

        // Assert
        Assert.NotNull(content1);
        Assert.NotNull(content2);
        Assert.Equal(ContentType.Image, content1.Type);
        Assert.Equal(ContentType.Text, content2.Type);
        Assert.NotNull(content1.Source);
        Assert.Null(content2.Source);
        Assert.Equal(SourceType.Base64, content1.Source!.Type);
        Assert.Equal(MediaType.ImageJpeg, content1.Source!.MediaType);
        Assert.Equal("base64_encoded_data_here...", content1.Source!.Data);
        Assert.Equal("What is in the above image?", content2.Text);
    }

    [Fact]
    public void FromJson_ValidJsonWithCaching_ReturnsContent()
    {
        // Arrange

        // Act
        var content = ContentRequest.FromJson(JSON3);

        // Assert
        Assert.NotNull(content);
        Assert.Equal(ContentType.Text, content.Type);
        Assert.Null(content.Source);
        Assert.Equal("<the entire contents of Pride and Prejudice>", content.Text);
        Assert.NotNull(content.CacheControl);
        Assert.Equal(CachingType.Ephemeral, content.CacheControl!.Type);
    }

    [Theory]
    [InlineData(JSON1)]
    [InlineData(JSON2)]
    [InlineData(JSON3)]
    public void ToJson_ValidContent_ReturnsJson(string json)
    {
        // Arrange
        var content = ContentRequest.FromJson(json);

        // Act
        string result = content!.ToJson(pretty: true);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Equal(json.Replace("\r\n", "\n"), result);
    }

    [Fact]
    public void CreateCachedContent_ContentSequence_ReturnsCachedContentList()
    {
        // Arrange
        var sequence = new RequestContentSequence()
        {
            "Text content",
            new BinaryContent(
                [0, 1, 2, 3, 4],
                MediaType.ImageJpeg),
        };

        // Act
        List<ContentRequest> contentList = ContentRequest.CreateCachedContent(sequence);

        // Assert
        Assert.NotNull(contentList);
        Assert.Equal(2, contentList.Count);

        Assert.Equal(ContentType.Text, contentList[0].Type);
        Assert.Equal("Text content", contentList[0].Text);
        Assert.Null(contentList[0].CacheControl);

        Assert.Equal(ContentType.Image, contentList[1].Type);
        Assert.NotNull(contentList[1].Source);
        Assert.Equal(MediaType.ImageJpeg, contentList[1].Source?.MediaType);
        Assert.NotNull(contentList[1].CacheControl);
    }

    [Fact]
    public void Constructor_TextContent_SetsTextType()
    {
        // Arrange
        var commonContent = new RequestCommonContent("Hello world");

        // Act
        var contentRequest = new ContentRequest(commonContent);

        // Assert
        Assert.Equal(ContentType.Text, contentRequest.Type);
        Assert.Equal("Hello world", contentRequest.Text);
        Assert.Null(contentRequest.Source);
        Assert.Null(contentRequest.CacheControl);
        Assert.Null(contentRequest.CitationsControl);
    }

    [Fact]
    public void Constructor_ImageContent_SetsImageType()
    {
        // Arrange
        byte[] imageData = [0x89, 0x50, 0x4E, 0x47];
        var binaryContent = new BinaryContent(imageData, MediaType.ImagePng);
        var commonContent = new RequestCommonContent(binaryContent, CommonContentType.Image);

        // Act
        var contentRequest = new ContentRequest(commonContent);

        // Assert
        Assert.Equal(ContentType.Image, contentRequest.Type);
        Assert.NotNull(contentRequest.Source);
        Assert.Equal(SourceType.Base64, contentRequest.Source!.Type);
        Assert.Equal(MediaType.ImagePng, contentRequest.Source.MediaType);
        Assert.Null(contentRequest.CacheControl);
        Assert.Null(contentRequest.CitationsControl);
    }

    [Fact]
    public void Constructor_TextInputContext_SetsDocumentTypeWithCitations()
    {
        // Arrange
        var commonContent = new RequestCommonContent("Document text content")
        {
            IsInputContext = true,
            Title = "Page 1",
            Context = "A page from document.pdf"
        };

        // Act
        var contentRequest = new ContentRequest(commonContent);

        // Assert
        Assert.Equal(ContentType.Document, contentRequest.Type);
        Assert.NotNull(contentRequest.Source);
        Assert.Equal(SourceType.Text, contentRequest.Source!.Type);
        Assert.Equal(MediaType.TextPlain, contentRequest.Source.MediaType);
        Assert.Equal("Document text content", contentRequest.Source.Data);
        Assert.Equal("Page 1", contentRequest.Title);
        Assert.Equal("A page from document.pdf", contentRequest.Context);
        Assert.NotNull(contentRequest.CitationsControl);
        Assert.True(contentRequest.CitationsControl!.Enabled);
    }

    [Fact]
    public void Constructor_ImageInputContext_SetsDocumentTypeWithCitations()
    {
        // Arrange
        byte[] imageData = [0x89, 0x50, 0x4E, 0x47];
        var binaryContent = new BinaryContent(imageData, MediaType.ImagePng);
        var commonContent = new RequestCommonContent(binaryContent, CommonContentType.Image)
        {
            IsInputContext = true,
            Title = "Chart Image",
            Context = "A chart from the report"
        };

        // Act
        var contentRequest = new ContentRequest(commonContent);

        // Assert
        Assert.Equal(ContentType.Document, contentRequest.Type);
        Assert.NotNull(contentRequest.Source);
        Assert.Equal(MediaType.ImagePng, contentRequest.Source!.MediaType);
        Assert.Equal("Chart Image", contentRequest.Title);
        Assert.Equal("A chart from the report", contentRequest.Context);
        Assert.NotNull(contentRequest.CitationsControl);
        Assert.True(contentRequest.CitationsControl!.Enabled);
    }

    [Fact]
    public void Constructor_WithCacheControl_SetsCacheControl()
    {
        // Arrange
        var commonContent = new RequestCommonContent("Cached content");

        // Act
        var contentRequest = new ContentRequest(commonContent, shouldAddCacheControl: true);

        // Assert
        Assert.Equal(ContentType.Text, contentRequest.Type);
        Assert.NotNull(contentRequest.CacheControl);
        Assert.Equal(CachingType.Ephemeral, contentRequest.CacheControl!.Type);
    }

    [Fact]
    public void Constructor_InputContextWithCacheControl_SetsBoth()
    {
        // Arrange
        var commonContent = new RequestCommonContent("Document with caching")
        {
            IsInputContext = true,
            Title = "Cached Doc"
        };

        // Act
        var contentRequest = new ContentRequest(commonContent, shouldAddCacheControl: true);

        // Assert
        Assert.Equal(ContentType.Document, contentRequest.Type);
        Assert.NotNull(contentRequest.CitationsControl);
        Assert.NotNull(contentRequest.CacheControl);
    }
}
