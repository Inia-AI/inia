using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.Common.Content;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Testing;
using IniaAi.Testing.Mocks;
using Microsoft.Extensions.Logging;

namespace IniaAi.AiConnection.Tests.Common;

/// <summary>
/// A caller that renders an image for a model asks the connector which vision tier the model it
/// would call reads at, so the render follows the deployment's configured model.
/// </summary>
public class ChatConnectorVisionTierTests(ITestOutputHelper output)
{
    private const string UNRECOGNISED_GATEWAY_MODEL_ID = "gateway-sonnet";

    [Theory]
    [InlineData(AiModel.UsAwsSonnet4_6, true, false)]
    [InlineData(AiModel.UsAwsSonnet5, true, true)]
    [InlineData(AiModel.UsAwsOpus4_8, true, true)]
    [InlineData(AiModel.UsAwsSonnet5, false, false)]
    public void Bedrock_ReportsTheVisionTierOfTheModelItWouldCall(AiModel advancedModel, bool shouldUseAdvancedModel, bool expectHighResolution)
    {
        AwsBedrockChatProvider provider = CreateBedrockProvider(advancedModel, AiModel.UsAwsHaiku4_5);

        Assert.Equal(Tier(expectHighResolution), provider.GetVisionTier(shouldUseAdvancedModel));
    }

    [Theory]
    [InlineData("claude-sonnet-4-6", false)]
    [InlineData("us.anthropic.claude-sonnet-4-6", false)]
    [InlineData("claude-opus-5", true)]
    public void Bedrock_ReportsTheVisionTierOfTheModelACallNames(string model, bool expectHighResolution)
    {
        AwsBedrockChatProvider provider = CreateBedrockProvider(AiModel.UsAwsSonnet5, AiModel.UsAwsHaiku4_5);

        Assert.Equal(Tier(expectHighResolution), provider.GetVisionTier(shouldUseAdvancedModel: true, model));
    }

    [Fact]
    public void Bedrock_AModelTheDeploymentCannotCall_ThrowsLikeACallWould()
    {
        AwsBedrockChatProvider provider = CreateBedrockProvider(AiModel.UsAwsSonnet5, AiModel.UsAwsHaiku4_5);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => provider.GetVisionTier(shouldUseAdvancedModel: true, UNRECOGNISED_GATEWAY_MODEL_ID));

        Assert.Contains(UNRECOGNISED_GATEWAY_MODEL_ID, error.Message);
    }

    [Theory]
    [InlineData(AiModel.PublicSonnet4_6, null, null, false)]
    [InlineData(AiModel.PublicSonnet5, null, null, true)]
    [InlineData(AiModel.PublicOpus5, "claude-sonnet-4-6", null, false)]
    [InlineData(AiModel.PublicSonnet4_6, UNRECOGNISED_GATEWAY_MODEL_ID, null, true)]
    [InlineData(AiModel.PublicSonnet5, null, "claude-sonnet-4-6", false)]
    [InlineData(AiModel.PublicSonnet4_6, null, "claude-opus-5", true)]
    [InlineData(AiModel.PublicSonnet4_6, UNRECOGNISED_GATEWAY_MODEL_ID, "claude-sonnet-4-6", true)]
    public void Anthropic_ReportsTheVisionTierOfTheModelOnTheWire(
        AiModel advancedModel,
        string? settingsModelIdOverride,
        string? model,
        bool expectHighResolution)
    {
        var settings = new PlatformSettings
        {
            Anthropic = new AnthropicSettings
            {
                ApiUrl = "http://127.0.0.1:1/v1/messages",
                ApiKey = "test-key",
                AdvancedModel = advancedModel,
                AdvancedModelIdOverride = settingsModelIdOverride,
            }
        };
        var connector = new AnthropicChatConnector(
            settings,
            TestsLogger.CreateLogger<AnthropicChatConnector>(output),
            new NoOpLimiter());

        Assert.Equal(Tier(expectHighResolution), connector.GetVisionTier(shouldUseAdvancedModel: true, model));
    }

    private static VisionTier Tier(bool highResolution)
    {
        return highResolution ? VisionTier.HighResolution : VisionTier.Standard;
    }

    private AwsBedrockChatProvider CreateBedrockProvider(AiModel advancedModel, AiModel basicModel)
    {
        var settings = new PlatformSettings
        {
            Anthropic = new AnthropicSettings { AdvancedModel = advancedModel, BasicModel = basicModel }
        };
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestsLoggerProvider(output)));

        return new AwsBedrockChatProvider(
            settings,
            loggerFactory.CreateLogger<AwsBedrockChatProvider>(),
            loggerFactory,
            new NoOpLimiter());
    }
}
