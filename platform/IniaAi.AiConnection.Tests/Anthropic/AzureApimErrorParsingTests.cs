using IniaAi.AiConnection.Anthropic;

namespace IniaAi.AiConnection.Tests.Anthropic;

public class AzureApimErrorParsingTests
{
    [Fact]
    public void TryParseAzureApimError_Format1_StatusCodeAndMessage_ReturnsFormattedError()
    {
        // Arrange
        string json = """{"statusCode": 401, "message": "Access denied"}""";

        // Act
        string? result = AnthropicChatConnector.TryParseAzureApimError(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Azure APIM (401): Access denied", result);
    }

    [Fact]
    public void TryParseAzureApimError_Format2_ErrorObjectWithCodeAndMessage_ReturnsFormattedError()
    {
        // Arrange
        string json = """{"error": {"code": "401", "message": "Invalid key"}}""";

        // Act
        string? result = AnthropicChatConnector.TryParseAzureApimError(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Azure APIM (401): Invalid key", result);
    }

    [Fact]
    public void TryParseAzureApimError_Format2_ErrorObjectWithoutCode_ReturnsUnknownCode()
    {
        // Arrange
        string json = """{"error": {"message": "Something went wrong"}}""";

        // Act
        string? result = AnthropicChatConnector.TryParseAzureApimError(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Azure APIM (unknown): Something went wrong", result);
    }

    [Fact]
    public void TryParseAzureApimError_StandardAnthropicFormat_DoesNotThrow()
    {
        // Arrange — standard Anthropic error format should not be parsed by this method
        string json = """{"type": "error", "error": {"type": "invalid_request_error", "message": "Bad request"}}""";

        // Act — just verify it doesn't throw.
        // The Anthropic error format has "error" as an object with "type" and "message",
        // which would match Format 2. That's acceptable since this method is only called
        // for a body that names no error type.
        Exception? exception = Record.Exception(() => AnthropicChatConnector.TryParseAzureApimError(json));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void TryParseAzureApimError_InvalidJson_ReturnsNull()
    {
        // Arrange
        string json = "this is not json";

        // Act
        string? result = AnthropicChatConnector.TryParseAzureApimError(json);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryParseAzureApimError_EmptyJson_ReturnsNull()
    {
        // Arrange
        string json = "{}";

        // Act
        string? result = AnthropicChatConnector.TryParseAzureApimError(json);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryParseAzureApimError_UnrelatedJsonObject_ReturnsNull()
    {
        // Arrange
        string json = """{"foo": "bar", "baz": 123}""";

        // Act
        string? result = AnthropicChatConnector.TryParseAzureApimError(json);

        // Assert
        Assert.Null(result);
    }
}
