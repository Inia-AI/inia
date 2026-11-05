using IniaAi.Common.Content;
using IniaAi.Common.Extensions;
using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.Configuration.Tests;

/// <summary>
/// Anthropic's vision tiers (platform.claude.com/docs/en/build-with-claude/vision, "Resolution and
/// token cost"): Claude 4.7 and later read images up to 2,576 px on the long edge and 4,784 visual
/// tokens, every older model up to 1,568 px and 1,568 tokens, and each downscales a larger image
/// before reading it.
/// </summary>
public class ModelVisionTierTests
{
    private static readonly string[] _standardTierFamilies =
        ["claude-sonnet-4-5", "claude-haiku-4-5", "claude-opus-4-5", "claude-sonnet-4-6", "claude-opus-4-6"];

    public static TheoryData<AiModel> AllModels()
    {
        return [.. Enum.GetValues<AiModel>()];
    }

    [Theory]
    [MemberData(nameof(AllModels))]
    public void EveryModel_ReadsImagesAtItsAnthropicTier(AiModel model)
    {
        string modelId = model.GetEnumMember();
        VisionTier expected = _standardTierFamilies.Any(family => modelId.Contains(family, StringComparison.Ordinal))
            ? VisionTier.Standard
            : VisionTier.HighResolution;

        Assert.Equal(expected, model.GetVisionTier());
    }

    [Fact]
    public void AnUnrecognisedModel_ReadsAtTheLargestTier()
    {
        Assert.Equal(VisionTier.HighResolution, VisionTier.ForModel(null));
    }
}
