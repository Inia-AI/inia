using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class ResponseBodyToleranceTests
{
    private const string CONTEXT_WINDOW_EXCEEDED_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Partial answer"
        }
      ],
      "id": "msg_01Q8Faay6S7QPTvEUUQARt7h",
      "model": "us.anthropic.claude-sonnet-5",
      "role": "assistant",
      "stop_reason": "model_context_window_exceeded",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 999999,
        "output_tokens": 3
      }
    }
    """;

    private const string UNKNOWN_STOP_REASON_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Answer"
        }
      ],
      "id": "msg_01Q8Faay6S7QPTvEUUQARt7h",
      "model": "claude-opus-4-8",
      "role": "assistant",
      "stop_reason": "some_future_stop_reason",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 10,
        "output_tokens": 2
      }
    }
    """;

    private const string UNKNOWN_MODEL_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Answer"
        }
      ],
      "id": "msg_01Q8Faay6S7QPTvEUUQARt7h",
      "model": "claude-hexameter-6",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 10,
        "output_tokens": 2
      }
    }
    """;

    private const string NON_STRING_STOP_REASON_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "Answer"
        }
      ],
      "id": "msg_01Q8Faay6S7QPTvEUUQARt7h",
      "model": "claude-opus-4-8",
      "role": "assistant",
      "stop_reason": {
        "unexpected": "object"
      },
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 10,
        "output_tokens": 2
      }
    }
    """;

    [Fact]
    public void FromJson_ContextWindowExceededStopReason_Parses()
    {
        var responseBody = ResponseBody.FromJson(CONTEXT_WINDOW_EXCEEDED_JSON);

        Assert.NotNull(responseBody);
        Assert.Equal(StopReasonType.ModelContextWindowExceeded, responseBody.StopReason);
        Assert.Equal("Partial answer", responseBody.FormatResponseContent().GetPlainText());
    }

    [Fact]
    public void FromJson_UnknownStopReason_ParsesAndKeepsContent()
    {
        var responseBody = ResponseBody.FromJson(UNKNOWN_STOP_REASON_JSON);

        Assert.NotNull(responseBody);
        Assert.Null(responseBody.StopReason);

        ResponseContentSequence responses = responseBody.FormatResponseContent();
        Assert.Equal("Answer", responses.GetPlainText());
        Assert.False(responses.Metadata.ContainsKey(ResponseBody.STOP_REASON_KEY));
    }

    [Fact]
    public void FromJson_UnknownModel_ParsesAndKeepsContent()
    {
        var responseBody = ResponseBody.FromJson(UNKNOWN_MODEL_JSON);

        Assert.NotNull(responseBody);
        Assert.Null(responseBody.Model);
        Assert.Equal("Answer", responseBody.FormatResponseContent().GetPlainText());
    }

    [Fact]
    public void FromJson_NonStringStopReasonToken_FailsParseInsteadOfTolerating()
    {
        var responseBody = ResponseBody.FromJson(NON_STRING_STOP_REASON_JSON);

        Assert.Null(responseBody);
    }
}
