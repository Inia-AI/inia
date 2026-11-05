using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class ResponseBodyRefusalTests
{
    private const string REFUSAL_WITHOUT_CONTENT_JSON =
    """
    {
      "content": [],
      "id": "msg_01Q8Faay6S7QPTvEUUQARt7h",
      "model": "us.anthropic.claude-fable-5",
      "role": "assistant",
      "stop_reason": "refusal",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 42,
        "output_tokens": 0
      }
    }
    """;

    private const string REFUSAL_WITH_PARTIAL_CONTENT_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Partial answer before the refusal"
        }
      ],
      "id": "msg_01Q8Faay6S7QPTvEUUQARt7h",
      "model": "us.anthropic.claude-fable-5",
      "role": "assistant",
      "stop_reason": "refusal",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 42,
        "output_tokens": 12
      }
    }
    """;

    private const string REFUSAL_WITH_STOP_DETAILS_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "<reasoning>First I"
        }
      ],
      "id": "msg_bdrk_refused",
      "model": "us.anthropic.claude-sonnet-5-5",
      "role": "assistant",
      "stop_reason": "refusal",
      "stop_details": {
        "type": "refusal",
        "category": "reasoning_extraction",
        "explanation": "The request asks for internal reasoning."
      },
      "type": "message",
      "usage": {
        "input_tokens": 1200,
        "output_tokens": 4
      }
    }
    """;

    private const string REFUSAL_AFTER_THINKING_JSON =
    """
    {
      "content": [
        {
          "type": "thinking",
          "thinking": "Let me think about this request.",
          "signature": "sig"
        }
      ],
      "id": "msg_thinking_refused",
      "model": "us.anthropic.claude-opus-5-5",
      "role": "assistant",
      "stop_reason": "refusal",
      "stop_details": { "type": "refusal", "category": "bio", "explanation": null },
      "type": "message",
      "usage": {
        "input_tokens": 30,
        "output_tokens": 9
      }
    }
    """;

    private const string OPUS_4_8_RESPONSE_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Hello!"
        }
      ],
      "id": "msg_01EcyWo6m4hyW8KHs2y2pei5",
      "model": "claude-opus-4-8",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 10,
        "output_tokens": 3
      }
    }
    """;

    private const string SONNET_5_RESPONSE_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Hello!"
        }
      ],
      "id": "msg_01EcyWo6m4hyW8KHs2y2pei5",
      "model": "us.anthropic.claude-sonnet-5",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 10,
        "output_tokens": 3
      }
    }
    """;

    [Fact]
    public void FromJson_RefusalStopReason_Parses()
    {
        var responseBody = ResponseBody.FromJson(REFUSAL_WITHOUT_CONTENT_JSON);

        Assert.NotNull(responseBody);
        Assert.Equal(StopReasonType.Refusal, responseBody.StopReason);
        Assert.Equal(AiModel.UsAwsFable5, responseBody.Model);
    }

    [Fact]
    public void FormatResponseContent_RefusalWithoutContent_SurfacesError()
    {
        ResponseBody responseBody = ResponseBody.FromJson(REFUSAL_WITHOUT_CONTENT_JSON)!;

        ResponseContentSequence responses = responseBody.FormatResponseContent();

        Assert.NotEmpty(responses);
        string plainText = responses.GetPlainText() ?? string.Empty;
        Assert.StartsWith(IChatConnector.ERROR_PREFIX, plainText);
        Assert.Contains("refus", plainText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(StopReasonType.Refusal), responses.Metadata[ResponseBody.STOP_REASON_KEY]);
    }

    [Fact]
    public void FormatResponseContent_RefusalWithPartialContent_DropsThePartialAnswer()
    {
        ResponseBody responseBody = ResponseBody.FromJson(REFUSAL_WITH_PARTIAL_CONTENT_JSON)!;

        ResponseContentSequence responses = responseBody.FormatResponseContent();

        string plainText = responses.GetPlainText() ?? string.Empty;
        Assert.DoesNotContain("Partial answer", plainText);
        Assert.True(ModelRefusal.IsAnswerText(plainText));
        Assert.NotNull(responses.Refusal);
        Assert.Equal(12, responses.OutputTokens);
    }

    [Fact]
    public void FormatResponseContent_RefusalWithStopDetails_NamesTheCategory()
    {
        ResponseBody responseBody = ResponseBody.FromJson(REFUSAL_WITH_STOP_DETAILS_JSON)!;

        ResponseContentSequence responses = responseBody.FormatResponseContent();

        ModelRefusal refusal = Assert.IsType<ModelRefusal>(responses.Refusal);
        Assert.Equal("reasoning_extraction", refusal.Category);
        Assert.Equal("The request asks for internal reasoning.", refusal.Explanation);
        Assert.Equal("msg_bdrk_refused", refusal.RequestId);
        Assert.Equal("us.anthropic.claude-sonnet-5-5", refusal.ModelId);
        Assert.Contains("(refusal category: reasoning_extraction)", responses.GetPlainText());
        Assert.Equal(1200, responses.InputTokens);
    }

    [Fact]
    public void FormatResponseContent_RefusalWithoutStopDetails_IsUnspecified()
    {
        ResponseBody responseBody = ResponseBody.FromJson(REFUSAL_WITHOUT_CONTENT_JSON)!;

        ResponseContentSequence responses = responseBody.FormatResponseContent();

        Assert.Equal(ModelRefusal.UNSPECIFIED_CATEGORY, responses.Refusal?.Category);
    }

    [Fact]
    public void FormatResponseContent_RefusalAfterThinkingOnly_DoesNotReturnTheThinking()
    {
        ResponseBody responseBody = ResponseBody.FromJson(REFUSAL_AFTER_THINKING_JSON)!;

        ResponseContentSequence responses = responseBody.FormatResponseContent();

        Assert.False(responses.IsThinkingOnly);
        Assert.DoesNotContain("Let me think", responses.GetPlainText());
        Assert.Equal("bio", responses.Refusal?.Category);
    }

    [Fact]
    public void FormatResponseContent_EndTurn_HasNoRefusal()
    {
        ResponseBody responseBody = ResponseBody.FromJson(OPUS_4_8_RESPONSE_JSON)!;

        ResponseContentSequence responses = responseBody.FormatResponseContent();

        Assert.Null(responses.Refusal);
        Assert.Equal("Hello!", responses.GetPlainText());
    }

    [Theory]
    [InlineData(OPUS_4_8_RESPONSE_JSON, AiModel.PublicOpus4_8)]
    [InlineData(SONNET_5_RESPONSE_JSON, AiModel.UsAwsSonnet5)]
    public void FromJson_NewGenerationModelIds_Parse(string json, AiModel expectedModel)
    {
        var responseBody = ResponseBody.FromJson(json);

        Assert.NotNull(responseBody);
        Assert.Equal(expectedModel, responseBody.Model);
        Assert.Equal(StopReasonType.EndTurn, responseBody.StopReason);
    }
}
