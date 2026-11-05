using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class ErrorResponseTests
{
    private const string JSON =
    """
    {
      "type": "error",
      "error": {
        "type": "not_found_error",
        "message": "The requested resource could not be found."
      }
    }
    """;

    [Fact]
    public void FromJson_ValidJson_ReturnsErrorResponse()
    {
        // Arrange

        // Act
        var errorResponse = ErrorResponse.FromJson(JSON);

        // Assert
        Assert.NotNull(errorResponse);
        Assert.Equal("error", errorResponse.Type);
        Assert.NotNull(errorResponse.Error);
        Assert.Equal(ErrorType.NotFound, errorResponse.Error!.ToErrorType());
        Assert.Equal("The requested resource could not be found.", errorResponse.Error!.Message);
    }

    [Fact]
    public void ToJson_ValidErrorResponse_ReturnsJson()
    {
        // Arrange
        var errorResponse = ErrorResponse.FromJson(JSON);

        // Act
        string result = errorResponse!.ToJson(pretty: true);

        // Assert
        Assert.Equal(JSON.Replace("\r\n", "\n"), result);
    }
}
