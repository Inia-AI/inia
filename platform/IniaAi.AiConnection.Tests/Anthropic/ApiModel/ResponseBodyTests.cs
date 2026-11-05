using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.AiConnection.Enums;
using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class ResponseBodyTests
{
    public const string JSON1 =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Hi! My name is Claude."
        }
      ],
      "id": "msg_013Zva2CMHLNnXjNJJKqJ2EF",
      "model": "us.anthropic.claude-sonnet-4-6",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 10,
        "output_tokens": 25,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 0
      }
    }
    """;

    public const string JSON2 =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Hello!"
        }
      ],
      "id": "msg_01XFDUDYJgAACzvnptvVoYEL",
      "model": "us.anthropic.claude-sonnet-4-6",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 12,
        "output_tokens": 6,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 0
      }
    }
    """;

    private const string JSON3 =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Sure, I'd be happy to provide..."
        }
      ],
      "id": "msg_018gCsTGsXkYJVqYPxTgDHBU",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 30,
        "output_tokens": 309,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 0
      }
    }
    """;

    private const string JSON4 =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "C"
        }
      ],
      "id": "msg_01Q8Faay6S7QPTvEUUQARt7h",
      "model": "us.anthropic.claude-sonnet-4-6",
      "role": "assistant",
      "stop_reason": "max_tokens",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 42,
        "output_tokens": 1,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 0
      }
    }
    """;

    private const string JSON5 =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "This image shows an ant, specifically a close-up view of an ant. The ant is shown in detail, with its distinct head, antennae, and legs clearly visible. The image is focused on capturing the intricate details and features of the ant, likely taken with a macro lens to get an extreme close-up perspective."
        }
      ],
      "id": "msg_01EcyWo6m4hyW8KHs2y2pei5",
      "model": "us.anthropic.claude-sonnet-4-6",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 1551,
        "output_tokens": 71,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 0
      }
    }
    """;

    [Fact]
    public void FromJson_ValidJson_ReturnsResponseBody1()
    {
        // Arrange & Act
        var responseBody = ResponseBody.FromJson(JSON1);

        // Assert
        Assert.NotNull(responseBody);
        Assert.Equal(AiModel.UsAwsSonnet4_6, responseBody.Model);
        Assert.Equal(MessageSenderRole.Assistant, responseBody.Role);
        Assert.Equal("msg_013Zva2CMHLNnXjNJJKqJ2EF", responseBody.Id);
        Assert.Equal(StopReasonType.EndTurn, responseBody.StopReason);
        Assert.Null(responseBody.StopSequence);
        Assert.Equal(ResponseType.Message, responseBody.Type);
        _ = Assert.Single(responseBody.Content);
        Assert.Equal(ContentType.Text, responseBody.Content[0].Type);
        Assert.Equal("Hi! My name is Claude.", responseBody.Content[0].Text);
        Assert.NotNull(responseBody.Usage);
        Assert.Equal(10, responseBody.Usage.InputTokens);
        Assert.Equal(25, responseBody.Usage.OutputTokens);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsResponseBody2()
    {
        // Arrange & Act
        var responseBody = ResponseBody.FromJson(JSON2);

        // Assert
        Assert.NotNull(responseBody);
        Assert.Equal("msg_01XFDUDYJgAACzvnptvVoYEL", responseBody.Id);
        Assert.Equal(ResponseType.Message, responseBody.Type);
        Assert.Equal(MessageSenderRole.Assistant, responseBody.Role);
        Assert.Equal(AiModel.UsAwsSonnet4_6, responseBody.Model);
        Assert.Equal(StopReasonType.EndTurn, responseBody.StopReason);
        Assert.Null(responseBody.StopSequence);
        Assert.NotNull(responseBody.Usage);
        Assert.Equal(12, responseBody.Usage!.InputTokens);
        Assert.Equal(6, responseBody.Usage!.OutputTokens);
        _ = Assert.Single(responseBody.Content);
        Assert.Equal(ContentType.Text, responseBody.Content[0].Type);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsResponseBody3()
    {
        // Arrange & Act
        var responseBody = ResponseBody.FromJson(JSON3);

        // Assert
        Assert.NotNull(responseBody);
        Assert.Equal("msg_018gCsTGsXkYJVqYPxTgDHBU", responseBody.Id);
        Assert.Equal(ResponseType.Message, responseBody.Type);
        Assert.Equal(MessageSenderRole.Assistant, responseBody.Role);
        Assert.Equal("Sure, I'd be happy to provide...", responseBody.Content[0].Text);
        Assert.Equal(StopReasonType.EndTurn, responseBody.StopReason);
        Assert.Null(responseBody.StopSequence);
        Assert.NotNull(responseBody.Usage);
        Assert.Equal(30, responseBody.Usage!.InputTokens);
        Assert.Equal(309, responseBody.Usage!.OutputTokens);
        _ = Assert.Single(responseBody.Content);
        Assert.Equal(ContentType.Text, responseBody.Content[0].Type);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsResponseBody4()
    {
        // Arrange & Act
        var responseBody = ResponseBody.FromJson(JSON4);

        // Assert
        Assert.NotNull(responseBody);
        Assert.Equal("msg_01Q8Faay6S7QPTvEUUQARt7h", responseBody.Id);
        Assert.Equal(ResponseType.Message, responseBody.Type);
        Assert.Equal(MessageSenderRole.Assistant, responseBody.Role);
        Assert.Equal("C", responseBody.Content[0].Text);
        Assert.Equal(AiModel.UsAwsSonnet4_6, responseBody.Model);
        Assert.Equal(StopReasonType.MaxTokens, responseBody.StopReason);
        Assert.Null(responseBody.StopSequence);
        Assert.NotNull(responseBody.Usage);
        Assert.Equal(42, responseBody.Usage!.InputTokens);
        Assert.Equal(1, responseBody.Usage!.OutputTokens);
        _ = Assert.Single(responseBody.Content);
        Assert.Equal(ContentType.Text, responseBody.Content[0].Type);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsResponseBody5()
    {
        // Arrange & Act
        var responseBody = ResponseBody.FromJson(JSON5);

        // Assert
        Assert.NotNull(responseBody);
        Assert.Equal("msg_01EcyWo6m4hyW8KHs2y2pei5", responseBody.Id);
        Assert.Equal(ResponseType.Message, responseBody.Type);
        Assert.Equal(MessageSenderRole.Assistant, responseBody.Role);
        Assert.Equal("This image shows an ant, specifically a close-up view of an ant. The ant is shown in detail, with its distinct head, antennae, and legs clearly visible. The image is focused on capturing the intricate details and features of the ant, likely taken with a macro lens to get an extreme close-up perspective.", responseBody.Content[0].Text);
        Assert.Equal(AiModel.UsAwsSonnet4_6, responseBody.Model);
        Assert.Equal(StopReasonType.EndTurn, responseBody.StopReason);
        Assert.Null(responseBody.StopSequence);
        Assert.NotNull(responseBody.Usage);
        Assert.Equal(1551, responseBody.Usage!.InputTokens);
        Assert.Equal(71, responseBody.Usage!.OutputTokens);
        _ = Assert.Single(responseBody.Content);
        Assert.Equal(ContentType.Text, responseBody.Content[0].Type);
    }

    [Theory]
    [InlineData(JSON1)]
    [InlineData(JSON2)]
    [InlineData(JSON3)]
    [InlineData(JSON4)]
    [InlineData(JSON5)]
    public void ToJson_ValidResponseBody_ReturnsJson(string json)
    {
        // Arrange
        var responseBody = ResponseBody.FromJson(json);

        // Act
        string result = responseBody!.ToJson(pretty: true);

        // Assert
        Assert.Equal(json.Replace("\r\n", "\n"), result);
    }

    [Fact]
    public void FormatResponseContent_SingleTextContent_ReturnsJustText()
    {
        // Arrange
        var responseBody = ResponseBody.FromJson(JSON1);

        // Act
        ResponseContentSequence result = responseBody!.FormatResponseContent();

        // Assert
        string expected = "Hi! My name is Claude.";
        Assert.NotEmpty(result);
        Assert.Equal(expected, result[0].TextContent);
    }

    [Fact]
    public void FormatResponseContent_MultipleContentTypes_ReturnsFormattedResult()
    {
        // Arrange
        var responseBody = new ResponseBody
        {
            Content =
            [
                new() { Type = ContentType.Text, Text = "Here is the answer. " },
                new() { Type = ContentType.Thinking, Thinking = "Processing the request" },
                new() { Type = ContentType.Image, Text = "Image description" }
            ]
        };

        // Act
        ResponseContentSequence result = responseBody.FormatResponseContent();

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(2, result.Count());
        Assert.Equal("Here is the answer. ", result[0].TextContent);
        Assert.Equal(CommonContentType.Image, result[1].Type);
        Assert.NotNull(result[1].BinaryContent);
    }

    [Theory]
    [InlineData(JSON_SPECIAL)]
    [InlineData(JSON_SPECIAL2)]
    public void FormatResponseContent_ThinkingOnlyContentTypes_ReturnsFormattedResult(string json)
    {
        // Arrange
        var responseBody = ResponseBody.FromJson(json);
        // Act
        ResponseContentSequence result = responseBody!.FormatResponseContent();

        // Assert
        Assert.NotEmpty(result);
        _ = Assert.Single(result);
        Assert.Contains("<summary>", result[0].TextContent);
        Assert.Contains("<answer>", result[0].TextContent);
    }

    private const string JSON_SPECIAL =
    """
    {
        "model": "us.anthropic.claude-sonnet-4-6",
        "id": "msg_bdrk_01SyntheticFixture0000001",
        "type": "message",
        "role": "assistant",
        "content": [
            {
                "type": "thinking",
                "thinking": "Let me analyze the two pages provided for Example Macro Fund A.\n\nPage 12 (attachment_returns_since_inception.xlsx):\n- Contains data for \"Example Macro Fund A\"\n- Shows columns: NAV, MonthlyGross, YTDGross, CUMGross\n- Has dates ranging from 1/31/20 to 2/29/24\n- Contains NAV values and performance data starting from 9/30/21\n- Shows monthly returns, YTD returns, and cumulative returns\n- Data is for the fund we're looking for\n\nPage 13 (attachment_returns_since_inception.xlsx):\n- Continues data for \"Example Macro Fund A\" \n- Shows same columns: NAV, MonthlyGross, YTDGross, CUMGross\n- Has dates from 3/29/24 to 9/30/25\n- Contains NAV values and performance data\n- This is a continuation of page 12 (Adjacent Page Rule applies)\n\nBoth pages contain relevant NAV and performance data for Example Macro Fund A. The data extends through September 30, 2025, which is within the time frame. Both pages should be included.\n</summary>\n\n<answer>\n12, 13\n</answer>\n\n<summary>\nBoth pages contain NAV values, monthly gross returns, YTD returns, and cumulative returns for Example Macro Fund A from September 2021 through September 2025.\n</summary>",
                "signature": "5pQjXw/gJQuDkwewL6KWWkH898BOESEJ7fszSboiQlrE4VyvD46JLZfeJIphemQW2viAeCrahj59TYhqifJf1JTYcumqWreercCbnhBOkhrhDEtHCWLZyxI4rG3JoLAWyj/T2gOmr3+02tIhKB3yPM7wkTKJhZehMpDTmVerepOdNRWWIPf0yj9KNpRwQnXviWwhX6+7RLD5vnRtOclY76+U9Ji7nV1Tq4YyD3udBwi/dkZwBX4oGlb1JBiqfSj2RlWqTdjoPv8UOnfsZ5pmO0CN7wdsOIvm46uA4nMrb6T65podZqsonXwvyLubtysow8bT/LKPpWP1fTPPaR9ynKn28+75m11V1MvIEnmQ+f8NTbY0cNc4WemAlBNW84Wok6bvcgFV/NwgdLpncvnBRgSboHo0ScrCHzbG6Ac56QClQKyfHh0xPW3PYqKtnMAg/tV9WVtPgXZbn4IVUVy04yAZbgxY0d8qorOXMD1RCnxOzOPrGAjlun8ehUOfObh21Q1W2samCgz2eNOs0hO6F0gpYco98jEYHW2mP25k667FKN6528AVdo4UaxpcvGz7FbltzkelFXbdMn8jAwZ/ZEdrXhS82Gz2jJYgRLs6059LU0CKXn1wYXVMSjF7baXRKh5lKIhS9Qwq3dhTsdnyx3NdZ0jU08xjVXy2V+wMvBHSLLcO1zyu+XOiZR8rnGoJuJYCKSolIT/Zifn/eW47Uaike4jZhI0R5x4hXw3SvsolbSUuOGyAO8wxFBupJSe4GSgj9i6k8j77f8cfJn3c20aCE5KjI3HLLbmIwLvdW4ArAx9DU6ReJlsyS0kPxnOq6krcDJPBlkolSOzOxlzTe09IuTwfTQDGMRzoYsp8JnzEYJCV62kSGZeewPRcX2i7gN9mqy/ZlW9zbglzsdZGlfNsWuWjshHhMa604xHuV0NrRczGZdsc/RLTsd9UonUsVeYB6kHO/OITmsebSXO8WEW5+bGM9NWA2FggAHmrsIjkQsl4Vj9B/195sqS5O0T2y9xPrG6UGDYUOdfzDEH7Ro/Es6qUQaYFpKcAG1OzXKxFXeG8Q3x3+BS8McLL6zOfhI23X0nprstoDbX7MLSIu8elgszvglhObQBt7wgEBqxdeiXv3U9WLgfJN5D5nyLH01id4qVcIr4npGfr+gOMXUQXxf4bMUPjK8YCmuz82PAAjGSH2wW5D0wBvf5fGJXD8VsTXJUMcO9Cp2s6T8ON5yyje0kM5sjNdrYAdaOPRRdPSjl8x8y/vN6LvYyw7f5LZUMTcq1wOk6W5gmWlAjeCEu8U+OzFdOJ6ukXrr6ozkv7a83RvtZswg=="
            }
        ],
        "stop_reason": "end_turn",
        "stop_sequence": null,
        "usage": {
            "input_tokens": 5800,
            "cache_creation_input_tokens": 0,
            "cache_read_input_tokens": 0,
            "cache_creation": {
                "ephemeral_5m_input_tokens": 0,
                "ephemeral_1h_input_tokens": 0
            },
            "output_tokens": 410
        }
    }
    """;

    private const string JSON_SPECIAL2 =
    """
    {
        "model": "us.anthropic.claude-sonnet-4-6",
        "id": "msg_bdrk_01SyntheticFixture0000002",
        "type": "message",
        "role": "assistant",
        "content":
            [
            {
                "type": "thinking",
                "thinking": "Let me analyze the page provided:n\nPage 7 contains:\n- Fund name: \"Example Multi-Strategy Fund B\"\n- Performance Summary for the period January 01, 2025 to June 30, 2025\n- Monthly data showing:\n  - Beginning Net Asset Value\n  - Additions (capital contributions)\n  - Withdrawals (capital withdrawals/distributions)\n  - Net Performance\n  - Ending Net Asset Value\n- Data for months: Jan-25, Feb-25, Mar-25, Apr-25, May-25, Jun-25\n\nThis page clearly contains:\n1. ✓ Fund name matches exactly: \"Example Multi-Strategy Fund B\"\n2. ✓ NAV data: Beginning and Ending Net Asset Values for each month\n3. ✓ Performance data: Net Performance figures for each month\n4. ✓ Date is relevant: January-June 2025\n5. ✓ Contains financial data, not just descriptions\n\nThis page should definitely be included.\n</summary>\n\n<answer>\n7\n</answer>\n\n<summary>\nPage 7 contains monthly NAV and net performance data for Example Multi-Strategy Fund B for the reporting period January 2025 through June 2025.\n</summary>",
                "signature": "ZT/g7QHU3tAlUPHHNp7/1IgBVLgc2DVtMkvTd5xZ7ej3EikLW5n+Ks9okYLV9nO4bW0EU7Uus0AiUNuT5W1f5TsRTG73eyEBsk3IGdQdI8JhkcDTy661tBE1oD/fBFFI24QnB1s9s8mKJOjKGG3wWMcoI5tc9X2kJ61cBRJgF46ShG2SbKHhyMkc8+uxOiOTfH7PdCwCAw5PmQT6fvsS8tVXPO32BLmBglmPX8VL3wzdOdg7OFaYmLmcujp7ivzRLTNHpmrUQsLJL3+D75SYPqc6e/gNdDUQDY5R7j2AbqpSZvGrDcFWtEwZ5Xp+gu7VDcaws0MrUirhkuIs4LZEtXfyJ+aeF8qdx110pi5yqyH6AfVcF7aUxNLnhuQvBBII/R31kh+1tFLJcduYKddcC7sGlWmafA6ojByz17hgtoEliMIvw1dWpoOeJRVx+u2NGqVeIQOZC8LwH0s53JqHJ0cft4LUFsIUfjQmR/Ln4TqA8NNYcDabvhDTAX3rfivq36ISqQJmzEW3UhYGDlwO1uw6/vb2a0Mwm6dWDSaIBF005VvmP/zNP16y6GOTMTzwSy1iUPzAvK5x6F7u1uPq2kHJocvhRZU9hJhOmIwFVSpEdRrerlazqS707jRJSy84rv5KesxR+NlC5qEsfOA1Gav1BNY/KL9s05s2N6qksvEYjrjglcflXDAWVb2tej83TQK857BLJ78QE5TuaMb0MZjFlsTWInlidovyCJmxH01v/E5FQnqyKcT1lxzQ+mTj30RtsH6l1rLQmcGjHY4IaSFt4ju06mttGd0MBo+qHiwlm4PsvyTlHNiaytAJy9IsicZclNp2Cca8XZAERkq+THaF6AbKHl5Vp5PrNURxS1HBZ+WlEdRGg2KYIL/ULpvqDn7IRn4hBrSpPXovhrQeHADLCbMkIP/0F6Cj7A==",
            },
            ],
        "stop_reason": "end_turn",
        "stop_sequence": null,
        "usage":
            {
            "input_tokens": 994,
            "cache_creation_input_tokens": 0,
            "cache_read_input_tokens": 0,
            "cache_creation":
                { "ephemeral_5m_input_tokens": 0, "ephemeral_1h_input_tokens": 0 },
            "output_tokens": 311,
            },
        }
    """;
}
