using IniaAi.AiConnection.Common;
using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.AiConnection.Tests.Common;

/// <summary>
/// A block names a model by any of its ids, and each stack calls the model under the id of its
/// own route: the route of the stack's configured default model.
/// </summary>
public class ModelRouteResolverTests
{
    [Theory]
    [InlineData("claude-opus-5-5", AiModel.UsAwsSonnet4_6, AiModel.UsAwsOpus5_5)]
    [InlineData("claude-opus-4-8", AiModel.UsAwsSonnet4_6, AiModel.UsAwsOpus4_8)]
    [InlineData("claude-haiku-4-5-20251001", AiModel.UsAwsSonnet4_6, AiModel.UsAwsHaiku4_5)]
    [InlineData("claude-sonnet-5", AiModel.EuAwsSonnet4_6, AiModel.EuAwsSonnet5)]
    [InlineData("claude-opus-5", AiModel.GlobalAwsSonnet4_6, AiModel.GlobalAwsOpus5)]
    [InlineData("claude-sonnet-5-5", AiModel.UsAwsSonnet4_6, AiModel.UsAwsSonnet5_5)]
    [InlineData("claude-sonnet-5-5", AiModel.EuAwsSonnet4_6, AiModel.EuAwsSonnet5_5)]
    [InlineData("claude-sonnet-5-5", AiModel.GlobalAwsSonnet4_6, AiModel.GlobalAwsSonnet5_5)]
    [InlineData("claude-haiku-5-5", AiModel.UsAwsSonnet4_6, AiModel.UsAwsHaiku5_5)]
    [InlineData("claude-haiku-5-5", AiModel.EuAwsSonnet4_6, AiModel.EuAwsHaiku5_5)]
    [InlineData("claude-haiku-5-5", AiModel.GlobalAwsSonnet4_6, AiModel.GlobalAwsHaiku5_5)]
    public void Resolve_PlainName_RunsAsTheBedrockStacksOwnProfile(string requested, AiModel stackModel, AiModel expected)
    {
        Assert.Equal(expected, ModelRouteResolver.Resolve(requested, stackModel));
    }

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-8", AiModel.PublicOpus4_8)]
    [InlineData("us.anthropic.claude-opus-5-5", AiModel.PublicOpus5_5)]
    [InlineData("us.anthropic.claude-sonnet-5-5", AiModel.PublicSonnet5_5)]
    [InlineData("us.anthropic.claude-haiku-5-5", AiModel.PublicHaiku5_5)]
    [InlineData("eu.anthropic.claude-sonnet-4-6", AiModel.PublicSonnet4_6)]
    [InlineData("us.anthropic.claude-haiku-4-5-20251001-v1:0", AiModel.Haiku4_5)]
    [InlineData("us.anthropic.claude-opus-4-6-v1", AiModel.PublicOpus4_6)]
    public void Resolve_BedrockId_RunsAsThePlainNameOnAFoundryOrDirectStack(string requested, AiModel expected)
    {
        Assert.Equal(expected, ModelRouteResolver.Resolve(requested, AiModel.PublicSonnet4_6));
    }

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-8", AiModel.UsAwsSonnet4_6, AiModel.UsAwsOpus4_8)]
    [InlineData("us.anthropic.claude-sonnet-5-5", AiModel.UsAwsSonnet5, AiModel.UsAwsSonnet5_5)]
    [InlineData("eu.anthropic.claude-sonnet-5-5", AiModel.EuAwsSonnet5, AiModel.EuAwsSonnet5_5)]
    [InlineData("global.anthropic.claude-sonnet-5", AiModel.GlobalAwsSonnet4_6, AiModel.GlobalAwsSonnet5)]
    [InlineData("claude-opus-4-8", AiModel.PublicSonnet4_6, AiModel.PublicOpus4_8)]
    public void BothResolutions_KeepAnIdThatAlreadyFitsTheStack(string requested, AiModel stackModel, AiModel expected)
    {
        Assert.Equal(expected, ModelRouteResolver.Resolve(requested, stackModel));
        Assert.Equal(expected, ModelRouteResolver.ResolveInStackRegion(requested, stackModel));
    }

    [Theory]
    [InlineData("eu.anthropic.claude-opus-4-8", AiModel.UsAwsSonnet4_6, AiModel.EuAwsOpus4_8)]
    [InlineData("global.anthropic.claude-sonnet-5", AiModel.UsAwsSonnet4_6, AiModel.GlobalAwsSonnet5)]
    [InlineData("us.anthropic.claude-sonnet-5-5", AiModel.EuAwsSonnet5, AiModel.UsAwsSonnet5_5)]
    public void Resolve_KeepsABedrockIdOnItsOwnRegion(string requested, AiModel stackModel, AiModel expected)
    {
        Assert.Equal(expected, ModelRouteResolver.Resolve(requested, stackModel));
    }

    [Theory]
    [InlineData("claude-sonnet-5-5", AiModel.EuAwsSonnet5, AiModel.EuAwsSonnet5_5)]
    [InlineData("us.anthropic.claude-opus-5", AiModel.PublicSonnet4_6, AiModel.PublicOpus5)]
    public void ResolveInStackRegion_MapsPlainNamesAndForeignRoutesAsResolveDoes(string requested, AiModel stackModel, AiModel expected)
    {
        Assert.Equal(ModelRouteResolver.Resolve(requested, stackModel), ModelRouteResolver.ResolveInStackRegion(requested, stackModel));
        Assert.Equal(expected, ModelRouteResolver.ResolveInStackRegion(requested, stackModel));
    }

    [Theory]
    [InlineData("us.anthropic.claude-sonnet-5-5", AiModel.EuAwsSonnet5, AiModel.EuAwsSonnet5_5)]
    [InlineData("us.anthropic.claude-opus-5-5", AiModel.EuAwsHaiku5_5, AiModel.EuAwsOpus5_5)]
    [InlineData("us.anthropic.claude-haiku-4-5-20251001-v1:0", AiModel.EuAwsSonnet5, AiModel.EuAwsHaiku4_5)]
    [InlineData("us.anthropic.claude-opus-4-6-v1", AiModel.EuAwsSonnet5, AiModel.EuAwsOpus4_6)]
    [InlineData("global.anthropic.claude-sonnet-4-6", AiModel.EuAwsSonnet5, AiModel.EuAwsSonnet4_6)]
    [InlineData("global.anthropic.claude-opus-5", AiModel.EuAwsSonnet5, AiModel.EuAwsOpus5)]
    [InlineData("eu.anthropic.claude-opus-4-8", AiModel.UsAwsSonnet4_6, AiModel.UsAwsOpus4_8)]
    [InlineData("global.anthropic.claude-sonnet-5", AiModel.UsAwsSonnet4_6, AiModel.UsAwsSonnet5)]
    [InlineData("us.anthropic.claude-sonnet-5-5", AiModel.GlobalAwsSonnet4_6, AiModel.GlobalAwsSonnet5_5)]
    [InlineData("eu.anthropic.claude-opus-5-5", AiModel.GlobalAwsSonnet4_6, AiModel.GlobalAwsOpus5_5)]
    public void ResolveInStackRegion_BedrockIdOfAnotherRegionFamily_RunsUnderTheStacksOwnPrefix(
        string requested, AiModel stackModel, AiModel expected)
    {
        Assert.Equal(expected, ModelRouteResolver.ResolveInStackRegion(requested, stackModel));
    }

    [Theory]
    [InlineData("us.anthropic.claude-haiku-4-5-20251001-v1:0", AiModel.GlobalAwsSonnet4_6)]
    [InlineData("eu.anthropic.claude-opus-4-6-v1", AiModel.GlobalAwsSonnet4_6)]
    public void ResolveInStackRegion_IsNull_WhenTheStacksRegionFamilyHasNoFormOfTheModel(string requested, AiModel stackModel)
    {
        Assert.Null(ModelRouteResolver.ResolveInStackRegion(requested, stackModel));
    }

    [Fact]
    public void ResolveInStackRegionOrThrow_SaysTheModelIsNotOfferedInTheRegion_InsteadOfFallingBackToAnotherRegion()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ModelRouteResolver.ResolveInStackRegionOrThrow("us.anthropic.claude-haiku-4-5-20251001-v1:0", AiModel.GlobalAwsSonnet4_6));

        Assert.Equal(
            "Model 'us.anthropic.claude-haiku-4-5-20251001-v1:0' (Claude Haiku 4.5) is not offered in this deployment's region. Choose another model from the block's model list.",
            exception.Message);
    }

    [Theory]
    [InlineData("claude-opus-9-9", AiModel.UsAwsSonnet4_6)]
    [InlineData("error_model_id", AiModel.PublicSonnet4_6)]
    [InlineData("claude-opus-4-6", AiModel.GlobalAwsSonnet4_6)]
    public void Resolve_IsNull_WhenTheStackHasNoFormOfTheModel(string requested, AiModel stackModel)
    {
        Assert.Null(ModelRouteResolver.Resolve(requested, stackModel));
    }

    [Fact]
    public void ResolveOrThrow_NamesTheRequestedModel_WhenTheStackCannotCallIt()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ModelRouteResolver.ResolveOrThrow("claude-opus-9-9", AiModel.UsAwsSonnet4_6));

        Assert.Contains("claude-opus-9-9", exception.Message);
    }

    [Fact]
    public void ResolveInStackRegionOrThrow_NamesAnUnknownModelAsResolveOrThrowDoes()
    {
        InvalidOperationException regional = Assert.Throws<InvalidOperationException>(
            () => ModelRouteResolver.ResolveInStackRegionOrThrow("claude-opus-9-9", AiModel.EuAwsSonnet5));
        InvalidOperationException plain = Assert.Throws<InvalidOperationException>(
            () => ModelRouteResolver.ResolveOrThrow("claude-opus-9-9", AiModel.EuAwsSonnet5));

        Assert.Equal(plain.Message, regional.Message);
    }
}
