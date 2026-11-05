using IniaAi.AiConnection.Anthropic;

namespace IniaAi.AiConnection.Tests.Anthropic;

public class ErrorTextTests
{
    [Fact]
    public void ReadErrorText_AnthropicError_ReturnsTypeAndMessage()
    {
        // Arrange
        string json = """{"type": "error", "error": {"type": "invalid_request_error", "message": "Bad request"}}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: false);

        // Assert
        Assert.Equal("InvalidRequest: Bad request", result);
    }

    [Fact]
    public void ReadErrorText_AnthropicErrorOfUnlistedType_ReturnsRawTypeAndMessage()
    {
        // Arrange
        string json = """{"type": "error", "error": {"type": "request_too_large", "message": "Request exceeds the maximum allowed number of bytes."}}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: false);

        // Assert
        Assert.Equal("request_too_large: Request exceeds the maximum allowed number of bytes.", result);
    }

    [Fact]
    public void ReadErrorText_CapitalisedErrorType_ReturnsTypeAndMessage()
    {
        // Arrange
        string json = """{"Error": {"Type": "invalid_request_error", "Message": "Bad request"}}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: false);

        // Assert
        Assert.Equal("InvalidRequest: Bad request", result);
    }

    [Fact]
    public void ReadErrorText_BlankErrorType_ReturnsMessageOnly()
    {
        // Arrange
        string json = """{"error": {"type": "", "message": "Something went wrong"}}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: false);

        // Assert
        Assert.Equal("Something went wrong", result);
    }

    [Fact]
    public void ReadErrorText_AnthropicErrorOnAzure_ReturnsTypeAndMessage()
    {
        // Arrange
        string json = """{"type": "error", "error": {"type": "overloaded_error", "message": "Overloaded"}}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: true);

        // Assert
        Assert.Equal("Overloaded: Overloaded", result);
    }

    [Fact]
    public void ReadErrorText_ValidationErrorDetail_ReturnsDetailJson()
    {
        // Arrange: the shape a FastAPI service gives a request it cannot validate
        string json = """{"detail": [{"loc": ["body", "params"], "msg": "field required", "type": "value_error.missing"}]}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: false);

        // Assert
        Assert.Equal("""[{"loc":["body","params"],"msg":"field required","type":"value_error.missing"}]""", result);
    }

    [Fact]
    public void ReadErrorText_DetailText_ReturnsDetail()
    {
        // Arrange: a FastAPI service's refusal of a key
        string json = """{"detail":"Invalid API Key"}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: false);

        // Assert
        Assert.Equal("Invalid API Key", result);
    }

    [Fact]
    public void ReadErrorText_ErrorWithoutType_ReturnsMessageOnly()
    {
        // Arrange
        string json = """{"error": {"message": "Something went wrong"}}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: false);

        // Assert
        Assert.Equal("Something went wrong", result);
    }

    [Fact]
    public void ReadErrorText_AzureApimErrorObject_ReturnsApimText()
    {
        // Arrange
        string json = """{"error": {"code": "401", "message": "Invalid key"}}""";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: true);

        // Assert
        Assert.Equal("Azure APIM (401): Invalid key", result);
    }

    [Fact]
    public void ReadErrorText_UnreadableBody_ReturnsGenericError()
    {
        // Arrange
        string json = "this is not json";

        // Act
        string result = AnthropicChatConnector.ReadErrorText(json, isAzureFoundry: false);

        // Assert
        Assert.Equal("Error occurred", result);
    }
}
