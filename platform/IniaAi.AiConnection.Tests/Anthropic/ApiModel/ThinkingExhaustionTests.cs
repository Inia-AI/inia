using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Enums;
using IniaAi.Configuration.Enums.Anthropic;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class ThinkingExhaustionTests
{
    private const string ANSWER = "<answer>Harbour Media Group plc is a UK television producer.</answer>";

    private const string THINKING_ONLY_AT_MAX_TOKENS_JSON =
    """
    {
      "content": [
        {
          "type": "thinking",
          "thinking": "I'm checking the draft's revenue and total assets figures against the source facts. Tallying this first paragraph's word count comes to around 158 words, so I need to compress it.",
          "signature": "EsAJCkgICxABGAIqQJGfA0i"
        }
      ],
      "id": "msg_bdrk_01Exhausted",
      "model": "us.anthropic.claude-sonnet-5",
      "role": "assistant",
      "stop_reason": "max_tokens",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 9018,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 4000,
        "output_tokens": 32000
      }
    }
    """;

    private const string THINKING_ONLY_AT_END_TURN_JSON =
    """
    {
      "content": [
        {
          "type": "thinking",
          "thinking": "Page 25 holds the NAV table.\n<answer>\n25\n</answer>",
          "signature": "EsAJCkgICxABGAIqQJGfA0i"
        }
      ],
      "id": "msg_bdrk_01AnswerInThinking",
      "model": "us.anthropic.claude-sonnet-5",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 5800,
        "output_tokens": 410
      }
    }
    """;

    private const string TRUNCATED_ANSWER_JSON =
    """
    {
      "content": [
        {
          "type": "thinking",
          "thinking": "I'm drafting the first paragraph.",
          "signature": "EsAJCkgICxABGAIqQJGfA0i"
        },
        {
          "type": "text",
          "text": "<answer>Harbour Media Group plc is a UK television"
        }
      ],
      "id": "msg_bdrk_01TruncatedAnswer",
      "model": "us.anthropic.claude-sonnet-5",
      "role": "assistant",
      "stop_reason": "max_tokens",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 9046,
        "output_tokens": 8000
      }
    }
    """;

    private const string ANSWER_AT_LOW_EFFORT_JSON =
    """
    {
      "content": [
        {
          "type": "text",
          "text": "<answer>Harbour Media Group plc is a UK television producer.</answer>"
        }
      ],
      "id": "msg_bdrk_01Answered",
      "model": "us.anthropic.claude-sonnet-5",
      "role": "assistant",
      "stop_reason": "end_turn",
      "stop_sequence": null,
      "type": "message",
      "usage": {
        "input_tokens": 9018,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 4000,
        "output_tokens": 1200
      }
    }
    """;

    private static ResponseContentSequence Parse(string json)
    {
        return ResponseBody.FromJson(json)!.FormatResponseContent();
    }

    [Fact]
    public void FormatResponseContent_ThinkingOnly_IsMarkedThinkingOnly()
    {
        ResponseContentSequence response = Parse(THINKING_ONLY_AT_MAX_TOKENS_JSON);

        Assert.True(response.IsThinkingOnly);
        Assert.True(response.IsTruncated);
    }

    [Fact]
    public void FormatResponseContent_WithAnswerText_IsNotThinkingOnly()
    {
        ResponseContentSequence response = Parse(TRUNCATED_ANSWER_JSON);

        Assert.False(response.IsThinkingOnly);
        Assert.True(response.IsTruncated);
    }

    [Theory]
    [InlineData(ModelEffort.Low)]
    [InlineData(ModelEffort.XHigh)]
    public void OutputConfig_ReadsBackTheEffortItCarries(ModelEffort effort)
    {
        Assert.Equal(effort, OutputConfig.FromEffort(effort)!.ToEffort());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ModelEffort.Medium)]
    [InlineData(ModelEffort.High)]
    [InlineData(ModelEffort.Max)]
    public async Task SendAsync_AdaptiveModelSpentAllOutputOnThinking_SendsOnceMoreAtLowEffortAsync(ModelEffort? effort)
    {
        List<ModelEffort?> sentEfforts = [];

        ResponseContentSequence result = await ThinkingExhaustion.SendAsync(
            sentEffort =>
            {
                sentEfforts.Add(sentEffort);
                return Task.FromResult(Parse(sentEfforts.Count == 1 ? THINKING_ONLY_AT_MAX_TOKENS_JSON : ANSWER_AT_LOW_EFFORT_JSON));
            },
            AiModel.UsAwsSonnet5,
            effort,
            NullLogger.Instance);

        Assert.Equal([effort, ModelEffort.Low], sentEfforts);
        Assert.Equal(ANSWER, result.GetPlainText());
    }

    [Fact]
    public async Task SendAsync_Retried_ReportsTheUsageOfBothCallsAsync()
    {
        ResponseContentSequence result = await ThinkingExhaustion.SendAsync(
            sentEffort => Task.FromResult(Parse(sentEffort is ModelEffort.Low ? ANSWER_AT_LOW_EFFORT_JSON : THINKING_ONLY_AT_MAX_TOKENS_JSON)),
            AiModel.UsAwsSonnet5,
            effort: null,
            NullLogger.Instance);

        Assert.Equal(9018 * 2, result.InputTokens);
        Assert.Equal(32000 + 1200, result.OutputTokens);
        Assert.Equal(8000, result.Metadata[ResponseBody.CACHE_READ_INPUT_TOKENS_KEY]);
        Assert.Equal(2, result.RequestCount);
    }

    [Fact]
    public async Task SendAsync_EffortAlreadyLow_KeepsTheResponseAsync()
    {
        await AssertKeptAsync(THINKING_ONLY_AT_MAX_TOKENS_JSON, AiModel.UsAwsSonnet5, ModelEffort.Low);
    }

    [Fact]
    public async Task SendAsync_BudgetThinkingModel_KeepsTheResponseAsync()
    {
        await AssertKeptAsync(THINKING_ONLY_AT_MAX_TOKENS_JSON, AiModel.UsAwsSonnet4_6, effort: null);
    }

    [Fact]
    public async Task SendAsync_AnswerWrittenInsideThinking_KeepsTheResponseAsync()
    {
        await AssertKeptAsync(THINKING_ONLY_AT_END_TURN_JSON, AiModel.UsAwsSonnet5, effort: null);
    }

    [Fact]
    public async Task SendAsync_TruncatedAnswerText_KeepsTheResponseAsync()
    {
        await AssertKeptAsync(TRUNCATED_ANSWER_JSON, AiModel.UsAwsSonnet5, effort: null);
    }

    private static async Task AssertKeptAsync(string json, AiModel model, ModelEffort? effort)
    {
        ResponseContentSequence response = Parse(json);
        int sends = 0;

        ResponseContentSequence result = await ThinkingExhaustion.SendAsync(
            _ =>
            {
                sends++;
                return Task.FromResult(response);
            },
            model,
            effort,
            NullLogger.Instance);

        Assert.Equal(1, sends);
        Assert.Same(response, result);
    }
}
