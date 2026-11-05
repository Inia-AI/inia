using IniaAi.AiConnection.AwsBedrock;
using IniaAi.Common.Enums;
using IniaAi.Configuration;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IniaAi.AiConnection.Tests.AwsBedrock;

public class AnthropicFormatAdapterRequestShapeTests
{
    private const string OPUS_4_8_MODEL_ID = "us.anthropic.claude-opus-4-8";
    private const string SONNET_5_MODEL_ID = "us.anthropic.claude-sonnet-5";
    private const string FABLE_5_MODEL_ID = "us.anthropic.claude-fable-5";
    private const string OPUS_5_MODEL_ID = "us.anthropic.claude-opus-5";
    private const string OPUS_5_5_MODEL_ID = "us.anthropic.claude-opus-5-5";
    private const string SONNET_5_5_MODEL_ID = "us.anthropic.claude-sonnet-5-5";
    private const string HAIKU_5_5_MODEL_ID = "us.anthropic.claude-haiku-5-5";
    private const string LEGACY_MODEL_ID = "us.anthropic.claude-sonnet-4-6";
    private const string USER_INPUT = "Hello, Claude";

    private readonly AnthropicFormatAdapter _adapter;

    public AnthropicFormatAdapterRequestShapeTests(ITestOutputHelper output)
    {
        ILogger<AnthropicFormatAdapter> logger = TestsLogger.CreateLogger<AnthropicFormatAdapter>(output);
        _adapter = new AnthropicFormatAdapter(new PlatformSettings(), logger);
    }

    [Theory]
    [InlineData(OPUS_4_8_MODEL_ID)]
    [InlineData(SONNET_5_MODEL_ID)]
    [InlineData(FABLE_5_MODEL_ID)]
    [InlineData(OPUS_5_MODEL_ID)]
    [InlineData(OPUS_5_5_MODEL_ID)]
    [InlineData(SONNET_5_5_MODEL_ID)]
    [InlineData(HAIKU_5_5_MODEL_ID)]
    public void CreateRequest_AdaptiveGenerationModel_OmitsTemperature(string modelId)
    {
        string json = _adapter.CreateRequestFromText(USER_INPUT, maxTokens: 1024, modelId: modelId);

        JObject request = JObject.Parse(json);
        Assert.Null(request["temperature"]);
    }

    [Theory]
    [InlineData(OPUS_4_8_MODEL_ID)]
    [InlineData(SONNET_5_MODEL_ID)]
    [InlineData(OPUS_5_MODEL_ID)]
    [InlineData(HAIKU_5_5_MODEL_ID)]
    public void CreateRequest_AdaptiveGenerationModel_DisablesThinking(string modelId)
    {
        string json = _adapter.CreateRequestFromText(USER_INPUT, maxTokens: 1024, modelId: modelId);

        JObject request = JObject.Parse(json);
        Assert.Equal("disabled", request["thinking"]?["type"]?.ToString());
        Assert.Null(request["thinking"]?["budget_tokens"]);
    }

    [Fact]
    public void CreateRequest_Sonnet5_5_SwitchesThinkingOffBetweenTools()
    {
        string json = _adapter.CreateRequestFromText(USER_INPUT, maxTokens: 1024, modelId: SONNET_5_5_MODEL_ID);

        JObject request = JObject.Parse(json);
        Assert.Equal("between_tools", request["thinking"]?["type"]?.ToString());
        Assert.Single(request["thinking"]!.Children());
    }

    [Theory]
    [InlineData(FABLE_5_MODEL_ID)]
    [InlineData(OPUS_5_5_MODEL_ID)]
    public void CreateRequest_ThinkingAlwaysOnModel_OmitsThinkingField(string modelId)
    {
        string json = _adapter.CreateRequestFromText(USER_INPUT, maxTokens: 1024, modelId: modelId);

        JObject request = JObject.Parse(json);
        Assert.Null(request["thinking"]);
    }

    [Fact]
    public void CreateRequest_LegacyModel_KeepsTemperatureAndOmitsThinking()
    {
        string json = _adapter.CreateRequestFromText(USER_INPUT, maxTokens: 1024, temperature: 0.2, modelId: LEGACY_MODEL_ID);

        JObject request = JObject.Parse(json);
        Assert.Equal(0.2, request["temperature"]?.Value<double>());
        Assert.Null(request["thinking"]);
    }

    [Theory]
    [InlineData(OPUS_4_8_MODEL_ID)]
    [InlineData(SONNET_5_MODEL_ID)]
    [InlineData(FABLE_5_MODEL_ID)]
    [InlineData(OPUS_5_MODEL_ID)]
    [InlineData(OPUS_5_5_MODEL_ID)]
    [InlineData(SONNET_5_5_MODEL_ID)]
    [InlineData(HAIKU_5_5_MODEL_ID)]
    public void CreateThinkingRequest_AdaptiveGenerationModel_UsesAdaptiveTypeWithoutBudget(string modelId)
    {
        string json = _adapter.CreateThinkingRequestFromText(
            USER_INPUT,
            maxTokens: 16000,
            thinkingBudget: 8000,
            modelId: modelId);

        JObject request = JObject.Parse(json);
        Assert.Equal("adaptive", request["thinking"]?["type"]?.ToString());
        Assert.Equal("summarized", request["thinking"]?["display"]?.ToString());
        Assert.Null(request["thinking"]?["budget_tokens"]);
        Assert.Null(request["temperature"]);
    }

    [Fact]
    public void CreateThinkingRequest_AdaptiveGenerationModel_AllowsUnsetBudget()
    {
        string json = _adapter.CreateThinkingRequestFromText(
            USER_INPUT,
            maxTokens: 16000,
            thinkingBudget: -1,
            modelId: OPUS_4_8_MODEL_ID);

        JObject request = JObject.Parse(json);
        Assert.Equal("adaptive", request["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void CreateThinkingRequest_LegacyModel_KeepsEnabledTypeWithBudget()
    {
        string json = _adapter.CreateThinkingRequestFromText(
            USER_INPUT,
            maxTokens: 16000,
            thinkingBudget: 8000,
            temperature: 1.0,
            modelId: LEGACY_MODEL_ID);

        JObject request = JObject.Parse(json);
        Assert.Equal("enabled", request["thinking"]?["type"]?.ToString());
        Assert.Equal(8000, request["thinking"]?["budget_tokens"]?.Value<int>());
        Assert.Equal(1.0, request["temperature"]?.Value<double>());
    }

    [Fact]
    public void CreateThinkingRequest_LegacyModel_RejectsUnsetBudget()
    {
        _ = Assert.Throws<ArgumentException>(() => _adapter.CreateThinkingRequestFromText(
            USER_INPUT,
            maxTokens: 16000,
            thinkingBudget: -1,
            modelId: LEGACY_MODEL_ID));
    }

    [Theory]
    [InlineData(OPUS_4_8_MODEL_ID)]
    [InlineData(SONNET_5_MODEL_ID)]
    [InlineData(FABLE_5_MODEL_ID)]
    public void CreateRequest_AdaptiveGenerationModelWithEffort_SetsOutputConfigEffort(string modelId)
    {
        string json = _adapter.CreateRequestFromText(
            USER_INPUT,
            maxTokens: 1024,
            modelId: modelId,
            effort: ModelEffort.Low);

        JObject request = JObject.Parse(json);
        Assert.Equal("low", request["output_config"]?["effort"]?.ToString());
    }

    [Fact]
    public void CreateRequest_AdaptiveGenerationModelWithoutEffort_OmitsOutputConfig()
    {
        string json = _adapter.CreateRequestFromText(USER_INPUT, maxTokens: 1024, modelId: SONNET_5_MODEL_ID);

        JObject request = JObject.Parse(json);
        Assert.Null(request["output_config"]);
    }

    [Fact]
    public void CreateRequest_LegacyModelWithEffort_OmitsOutputConfig()
    {
        string json = _adapter.CreateRequestFromText(
            USER_INPUT,
            maxTokens: 1024,
            temperature: 0.2,
            modelId: LEGACY_MODEL_ID,
            effort: ModelEffort.Medium);

        JObject request = JObject.Parse(json);
        Assert.Null(request["output_config"]);
    }

    [Fact]
    public void CreateThinkingRequest_AdaptiveGenerationModelWithEffort_SetsOutputConfigEffort()
    {
        string json = _adapter.CreateThinkingRequestFromText(
            USER_INPUT,
            maxTokens: 16000,
            thinkingBudget: -1,
            modelId: OPUS_4_8_MODEL_ID,
            effort: ModelEffort.XHigh);

        JObject request = JObject.Parse(json);
        Assert.Equal("xhigh", request["output_config"]?["effort"]?.ToString());
        Assert.Equal("adaptive", request["thinking"]?["type"]?.ToString());
    }
}
