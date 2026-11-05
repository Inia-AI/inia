using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;

namespace IniaAi.AiConnection.Tests.Common.ChatConnector;

public class ResponseCommonContentTests
{
    [Fact]
    public void Constructor_WithType_SetsTypeProperty()
    {
        // Act
        var content = new ResponseCommonContent(CommonContentType.Image);

        // Assert
        Assert.Equal(CommonContentType.Image, content.Type);
        Assert.Null(content.TextContent);
    }

    [Fact]
    public void Constructor_WithString_SetsTextContentAndType()
    {
        // Act
        var content = new ResponseCommonContent("Hello");

        // Assert
        Assert.Equal("Hello", content.TextContent);
        Assert.Equal(CommonContentType.Text, content.Type);
    }
}
