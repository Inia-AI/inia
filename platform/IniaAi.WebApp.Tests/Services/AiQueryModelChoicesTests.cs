using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.WebApp.Services;
using AnthropicModel = IniaAi.Configuration.Enums.Anthropic.AiModel;

namespace IniaAi.WebApp.Tests.Services;

/// <summary>
/// The AI Query block's model dropdown offers each supported model once, under its plain
/// Anthropic name, and the running stack resolves that name to its own id. A block that stores
/// an older stack-specific id shows as the same model.
/// </summary>
public class AiQueryModelChoicesTests
{
    private static List<string> SelectableIds()
    {
        return [.. AiQueryModelChoices.Selectable.Select(static model => model.GetEnumMember())];
    }

    [Fact]
    public void Selectable_OffersEachSupportedModelOnce_ByItsPlainName()
    {
        Assert.Equal(
            ["claude-haiku-4-5-20251001", "claude-haiku-5-5", "claude-sonnet-4-6", "claude-sonnet-5", "claude-sonnet-5-5", "claude-opus-4-8", "claude-opus-5", "claude-opus-5-5"],
            SelectableIds());
    }

    [Theory]
    [InlineData("claude-opus-5-5", "Claude Opus 5.5")]
    [InlineData("us.anthropic.claude-opus-5-5", "Claude Opus 5.5")]
    [InlineData("us.anthropic.claude-sonnet-5-5", "Claude Sonnet 5.5")]
    [InlineData("us.anthropic.claude-haiku-5-5", "Claude Haiku 5.5")]
    [InlineData("us.anthropic.claude-haiku-4-5-20251001-v1:0", "Claude Haiku 4.5")]
    [InlineData("eu.anthropic.claude-sonnet-4-6", "Claude Sonnet 4.6")]
    [InlineData("us.anthropic.claude-opus-4-6-v1", "Claude Opus 4.6")]
    [InlineData("claude-opus-9-9", "claude-opus-9-9")]
    public void Describe_ShowsAnyIdOfAModelAsThatModel(string modelId, string expected)
    {
        Assert.Equal(expected, AiQueryModelChoices.Describe(modelId));
    }

    [Theory]
    [InlineData(AnthropicModel.UsAwsOpus5, ModelEffort.High, "High (model default)")]
    [InlineData(AnthropicModel.UsAwsOpus5, ModelEffort.Medium, "Medium")]
    [InlineData(AnthropicModel.PublicOpus5_5, ModelEffort.Medium, "Medium (model default)")]
    [InlineData(AnthropicModel.PublicOpus5_5, ModelEffort.High, "High")]
    [InlineData(AnthropicModel.PublicHaiku5_5, ModelEffort.Medium, "Medium (model default)")]
    public void EffortOptions_MarkTheModelsOwnDefault(AnthropicModel model, ModelEffort effort, string expected)
    {
        Assert.Contains((effort, expected), AiQueryModelChoices.EffortOptions(model));
    }

    [Theory]
    [InlineData(AnthropicModel.UsAwsOpus5, "Model default (High)")]
    [InlineData(AnthropicModel.UsAwsOpus5_5, "Model default (Medium)")]
    [InlineData(AnthropicModel.UsAwsHaiku5_5, "Model default (Medium)")]
    public void DefaultEffortPlaceholder_NamesTheModelsOwnDefault(AnthropicModel model, string expected)
    {
        Assert.Equal(expected, AiQueryModelChoices.DefaultEffortPlaceholder(model));
    }

    [Theory]
    [InlineData(AnthropicModel.UsAwsOpus5, ModelEffort.Max)]
    [InlineData(AnthropicModel.PublicOpus5, ModelEffort.XHigh)]
    [InlineData(AnthropicModel.UsAwsSonnet5_5, ModelEffort.Max)]
    [InlineData(AnthropicModel.UsAwsHaiku5_5, ModelEffort.XHigh)]
    public void EffortCapNotice_ExplainsTheCap_WhenThinkingIsOffAboveTheCap(AnthropicModel model, ModelEffort effort)
    {
        Assert.Equal(
            "Without thinking, this model runs at High effort at most.",
            AiQueryModelChoices.EffortCapNotice(model, shouldUseThinkingMode: false, effort));
    }

    [Theory]
    [InlineData(AnthropicModel.UsAwsOpus5, true, ModelEffort.Max)]
    [InlineData(AnthropicModel.UsAwsOpus5, false, ModelEffort.High)]
    [InlineData(AnthropicModel.UsAwsOpus5, false, null)]
    [InlineData(AnthropicModel.UsAwsOpus4_8, false, ModelEffort.Max)]
    [InlineData(AnthropicModel.UsAwsOpus5_5, false, ModelEffort.Max)]
    public void EffortCapNotice_IsNull_WhenNothingIsCapped(AnthropicModel model, bool shouldUseThinkingMode, ModelEffort? effort)
    {
        Assert.Null(AiQueryModelChoices.EffortCapNotice(model, shouldUseThinkingMode, effort));
    }
}
