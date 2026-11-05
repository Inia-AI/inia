using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Bedrock;
using IniaAi.Common.Enums;
using IniaAi.Configuration.Enums.Anthropic;
using Newtonsoft.Json.Linq;

namespace IniaAi.AiConnection.Tests.Anthropic;

public class RequestShapeForModelTests
{
    private const string USER_INPUT = "Hello, Claude";

    private static RequestBody CreateRequestBody(AiModel model)
    {
        var message = new Message(new ContentRequest(new(USER_INPUT)));

        return new RequestBody(message, maxTokens: 16000)
        {
            Model = model,
            Temperature = 0.2
        };
    }

    private static GatewayRequestBody CreateGatewayRequestBody(AiModel model)
    {
        var message = new Message(new ContentRequest(new(USER_INPUT)));
        var bedrockRequestBody = new BedrockRequestBody(message, maxTokens: 16000)
        {
            Temperature = 0.2
        };

        return new GatewayRequestBody(bedrockRequestBody) { ModelId = model };
    }

    [Theory]
    [InlineData(AiModel.PublicOpus4_8)]
    [InlineData(AiModel.PublicOpus5_5)]
    [InlineData(AiModel.PublicSonnet5_5)]
    [InlineData(AiModel.PublicHaiku5_5)]
    public void ApplyThinkingMode_AdaptiveModel_UsesAdaptiveTypeAndClearsTemperature(AiModel model)
    {
        RequestBody requestBody = CreateRequestBody(model);

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: 8000);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
        Assert.Equal("summarized", json["thinking"]?["display"]?.ToString());
        Assert.Null(json["thinking"]?["budget_tokens"]);
        Assert.Null(json["temperature"]);
    }

    [Fact]
    public void ApplyThinkingMode_AdaptiveModel_AllowsUnsetBudget()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet5);

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: -1);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyThinkingMode_LegacyModel_UsesEnabledTypeWithBudgetAndTemperatureOne()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet4_6);

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: 8000);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("enabled", json["thinking"]?["type"]?.ToString());
        Assert.Equal(8000, json["thinking"]?["budget_tokens"]?.Value<int>());
        Assert.Equal(1.0, json["temperature"]?.Value<double>());
    }

    [Fact]
    public void ApplyThinkingMode_ModelOverrideWithAdaptiveModel_WinsOverEnumModel()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet4_6);
        requestBody.ModelOverride = "claude-opus-4-8";

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: 8000);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
        Assert.Null(json["temperature"]);
    }

    [Fact]
    public void ApplyThinkingMode_GatewayAdaptiveModel_UsesAdaptiveTypeAndClearsTemperature()
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsSonnet5);

        AnthropicChatConnector.ApplyThinkingMode(gatewayRequestBody, thinkingBudget: 8000);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
        Assert.Null(json["thinking"]?["budget_tokens"]);
        Assert.Null(json["temperature"]);
    }

    [Fact]
    public void ApplyThinkingMode_GatewayModelOverrideWithAdaptiveModel_WinsOverEnumModel()
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsSonnet4_6);
        gatewayRequestBody.ModelOverride = "anthropic.claude-sonnet-5";

        AnthropicChatConnector.ApplyThinkingMode(gatewayRequestBody, thinkingBudget: 8000);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
        Assert.Null(json["temperature"]);
    }

    [Fact]
    public void ApplyThinkingMode_GatewayLegacyModel_KeepsEnabledType()
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsSonnet4_6);

        AnthropicChatConnector.ApplyThinkingMode(gatewayRequestBody, thinkingBudget: 8000);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Equal("enabled", json["thinking"]?["type"]?.ToString());
        Assert.Equal(8000, json["thinking"]?["budget_tokens"]?.Value<int>());
    }

    [Fact]
    public void ApplyAdaptiveGenerationDefaults_AdaptiveModel_ClearsTemperatureAndDisablesThinking()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Null(json["temperature"]);
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
        Assert.Null(json["thinking"]?["budget_tokens"]);
    }

    [Theory]
    [InlineData(AiModel.PublicFable5, null)]
    [InlineData(AiModel.PublicOpus5_5, null)]
    [InlineData(AiModel.PublicOpus5_5, ModelEffort.Low)]
    [InlineData(AiModel.PublicOpus5_5, ModelEffort.High)]
    [InlineData(AiModel.PublicOpus5_5, ModelEffort.Max)]
    public void ApplyAdaptiveGenerationDefaults_ThinkingAlwaysOnModel_ClearsTemperatureAndOmitsThinking(AiModel model, ModelEffort? effort)
    {
        RequestBody requestBody = CreateRequestBody(model);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody, effort);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Null(json["temperature"]);
        Assert.Null(json["thinking"]);
    }

    [Fact]
    public void ApplyAdaptiveGenerationDefaults_LegacyModel_LeavesRequestUntouched()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet4_6);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal(0.2, json["temperature"]?.Value<double>());
        Assert.Null(json["thinking"]);
    }

    [Fact]
    public void ApplyAdaptiveGenerationDefaults_GatewayAdaptiveModel_ClearsTemperatureAndDisablesThinking()
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsOpus4_8);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(gatewayRequestBody);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Null(json["temperature"]);
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyAdaptiveGenerationDefaults_AdaptiveModelWithEffort_SetsOutputConfigEffortAndDisablesThinking()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody, ModelEffort.Low);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("low", json["output_config"]?["effort"]?.ToString());
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyAdaptiveGenerationDefaults_AdaptiveModelWithoutEffort_OmitsOutputConfig()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Null(json["output_config"]);
    }

    [Fact]
    public void ApplyAdaptiveGenerationDefaults_LegacyModelWithEffort_OmitsOutputConfig()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet4_6);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody, ModelEffort.Max);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Null(json["output_config"]);
    }

    [Theory]
    [InlineData(AiModel.PublicOpus4_8, ModelEffort.Low, "low")]
    [InlineData(AiModel.PublicOpus4_8, ModelEffort.Medium, "medium")]
    [InlineData(AiModel.PublicOpus4_8, ModelEffort.High, "high")]
    [InlineData(AiModel.PublicOpus4_8, ModelEffort.XHigh, "xhigh")]
    [InlineData(AiModel.PublicOpus4_8, ModelEffort.Max, "max")]
    [InlineData(AiModel.PublicOpus5, ModelEffort.Max, "max")]
    public void ApplyThinkingMode_AdaptiveModelWithEffort_SerializesEffortWireValue(AiModel model, ModelEffort effort, string expectedWireValue)
    {
        RequestBody requestBody = CreateRequestBody(model);

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: -1, effort);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal(expectedWireValue, json["output_config"]?["effort"]?.ToString());
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyThinkingMode_LegacyModelWithEffort_OmitsOutputConfig()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet4_6);

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: 8000, ModelEffort.High);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Null(json["output_config"]);
        Assert.Equal("enabled", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyAdaptiveGenerationDefaults_GatewayAdaptiveModelWithEffort_SetsOutputConfigEffort()
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsSonnet5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(gatewayRequestBody, ModelEffort.Medium);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Equal("medium", json["output_config"]?["effort"]?.ToString());
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyThinkingMode_ThinkingAlwaysOnModelWithEffort_SetsOutputConfigAndAdaptiveThinking()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicFable5);

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: -1, ModelEffort.XHigh);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("xhigh", json["output_config"]?["effort"]?.ToString());
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyAdaptiveGenerationDefaults_GatewayOpus5_5_NeverSendsThinking()
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsOpus5_5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(gatewayRequestBody, ModelEffort.Max);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Null(json["thinking"]);
        Assert.Equal("max", json["output_config"]?["effort"]?.ToString());
    }

    [Theory]
    [InlineData(ModelEffort.XHigh)]
    [InlineData(ModelEffort.Max)]
    public void ApplyAdaptiveGenerationDefaults_Opus5AboveHighEffort_CapsEffortAtHighAndKeepsThinkingDisabled(ModelEffort effort)
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicOpus5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody, effort);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("high", json["output_config"]?["effort"]?.ToString());
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
    }

    [Theory]
    [InlineData(ModelEffort.Low, "low")]
    [InlineData(ModelEffort.High, "high")]
    public void ApplyAdaptiveGenerationDefaults_Opus5AtOrBelowHighEffort_KeepsEffort(ModelEffort effort, string expectedWireValue)
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsOpus5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(gatewayRequestBody, effort);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Equal(expectedWireValue, json["output_config"]?["effort"]?.ToString());
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(ModelEffort.Low, "low")]
    [InlineData(ModelEffort.High, "high")]
    public void ApplyAdaptiveGenerationDefaults_Sonnet5_5_SwitchesThinkingOffBetweenTools(ModelEffort? effort, string? expectedEffort)
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet5_5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody, effort);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Null(json["temperature"]);
        Assert.Equal("between_tools", json["thinking"]?["type"]?.ToString());
        Assert.Single(json["thinking"]!.Children());
        Assert.Equal(expectedEffort, json["output_config"]?["effort"]?.ToString());
    }

    [Theory]
    [InlineData(ModelEffort.XHigh)]
    [InlineData(ModelEffort.Max)]
    public void ApplyAdaptiveGenerationDefaults_GatewaySonnet5_5AboveHighEffort_CapsEffortAtHighBetweenTools(ModelEffort effort)
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsSonnet5_5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(gatewayRequestBody, effort);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Equal("high", json["output_config"]?["effort"]?.ToString());
        Assert.Equal("between_tools", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyThinkingMode_Sonnet5_5WithMaxEffort_KeepsMaxEffortWithAdaptiveThinking()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicSonnet5_5);

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: -1, ModelEffort.Max);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("max", json["output_config"]?["effort"]?.ToString());
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(ModelEffort.Low, "low")]
    [InlineData(ModelEffort.High, "high")]
    public void ApplyAdaptiveGenerationDefaults_Haiku5_5_DisablesThinking(ModelEffort? effort, string? expectedEffort)
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicHaiku5_5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(requestBody, effort);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Null(json["temperature"]);
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
        Assert.Equal(expectedEffort, json["output_config"]?["effort"]?.ToString());
    }

    [Theory]
    [InlineData(ModelEffort.XHigh)]
    [InlineData(ModelEffort.Max)]
    public void ApplyAdaptiveGenerationDefaults_GatewayHaiku5_5AboveHighEffort_CapsEffortAtHighAndKeepsThinkingDisabled(ModelEffort effort)
    {
        GatewayRequestBody gatewayRequestBody = CreateGatewayRequestBody(AiModel.UsAwsHaiku5_5);

        AnthropicChatConnector.ApplyAdaptiveGenerationDefaults(gatewayRequestBody, effort);

        JObject json = JObject.Parse(gatewayRequestBody.RequestBody.ToJson(pretty: false));
        Assert.Equal("high", json["output_config"]?["effort"]?.ToString());
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
    }

    [Fact]
    public void ApplyThinkingMode_Haiku5_5WithMaxEffort_KeepsMaxEffortWithAdaptiveThinking()
    {
        RequestBody requestBody = CreateRequestBody(AiModel.PublicHaiku5_5);

        AnthropicChatConnector.ApplyThinkingMode(requestBody, thinkingBudget: -1, ModelEffort.Max);

        JObject json = JObject.Parse(requestBody.ToJson(pretty: false));
        Assert.Equal("max", json["output_config"]?["effort"]?.ToString());
        Assert.Equal("adaptive", json["thinking"]?["type"]?.ToString());
    }
}
