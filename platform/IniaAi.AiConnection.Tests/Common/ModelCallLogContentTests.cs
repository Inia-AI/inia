using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Testing;
using IniaAi.Testing.Mocks;

namespace IniaAi.AiConnection.Tests.Common;

/// <summary>
/// A model call writes the size of what it sends and receives to the logs, never the prompt or the
/// reply: both carry a customer's documents and what the model wrote about them.
/// </summary>
public sealed class ModelCallLogContentTests
{
    private const string PROMPT_MARKER = "QUOKKA-PROMPT-4D1A";
    private const string SYSTEM_MARKER = "QUOKKA-SYSTEM-8E2B";
    private const string REPLY_MARKER = "QUOKKA-REPLY-6F3C";
    private const string REPLY =
        $$$"""{"id":"msg_logged","type":"message","role":"assistant","model":"claude-sonnet-5-5","content":[{"type":"text","text":"{{{REPLY_MARKER}}} holds net assets of 1,234,567."}],"stop_reason":"end_turn","usage":{"input_tokens":500,"output_tokens":12}}""";

    [Fact]
    public async Task TheAnthropicConnector_LogsNeitherThePromptNorTheReply()
    {
        using var api = new AnthropicApiStandIn(REPLY);
        var logger = new CapturingLogger<AnthropicChatConnector>();
        var connector = new AnthropicChatConnector(
            new PlatformSettings { Anthropic = new AnthropicSettings { ApiUrl = api.ApiUrl, ApiKey = "test-key", AdvancedModel = AiModel.PublicSonnet5_5 } },
            logger,
            new NoOpLimiter());

        ResponseContentSequence response = await connector.GetResponseAsync(
            $"{PROMPT_MARKER}: summarise the net assets.",
            systemPrompt: $"{SYSTEM_MARKER}: you are a credit analyst.",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(REPLY_MARKER, response.GetPlainText(), StringComparison.Ordinal);
        AssertNoMarkerIn(logger.Entries);
    }

    [Fact]
    public void TheBedrockAdapter_LogsNoRequestBody()
    {
        var logger = new CapturingLogger<AnthropicFormatAdapter>();
        var adapter = new AnthropicFormatAdapter(new PlatformSettings(), logger);

        string plain = adapter.CreateRequestFromText($"{PROMPT_MARKER}: summarise.", $"{SYSTEM_MARKER}: analyst.", maxTokens: 1024);
        string thinking = adapter.CreateThinkingRequestFromText($"{PROMPT_MARKER}: summarise.", $"{SYSTEM_MARKER}: analyst.", maxTokens: 4096, thinkingBudget: 2048);

        Assert.Contains(PROMPT_MARKER, plain, StringComparison.Ordinal);
        Assert.Contains(PROMPT_MARKER, thinking, StringComparison.Ordinal);
        AssertNoMarkerIn(logger.Entries);
    }

    private static void AssertNoMarkerIn(IReadOnlyList<CapturedLogEntry> entries)
    {
        Assert.NotEmpty(entries);
        string[] leaks = entries.Holding(PROMPT_MARKER, SYSTEM_MARKER, REPLY_MARKER);
        Assert.True(leaks.Length == 0, $"{leaks.Length} log lines carry the prompt or the reply:{Environment.NewLine}{string.Join(Environment.NewLine, leaks)}");
    }
}
