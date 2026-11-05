using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class AgentTemplateTagsTransformTests
{
    private static string Suffix(string text)
    {
        return text.Replace("{facts", "{facts_N", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Read {{facts}}.", "Read {{facts_N}}.")]
    [InlineData("Read {{facts.answer}}.", "Read {{facts_N.answer}}.")]
    [InlineData("Keep {facts} and {\"facts\": 1}.", "Keep {facts} and {\"facts\": 1}.")]
    [InlineData("Keep ${{facts}} and {{ facts }}.", "Keep ${{facts}} and {{ facts }}.")]
    [InlineData("", "")]
    public void TransformEngineTags_RewritesOnlyDoubleBraceTags(string text, string expected)
    {
        Assert.Equal(expected, AgentTemplateTags.TransformEngineTags(text, Suffix));
    }

    [Fact]
    public void TransformEngineTags_AParameterValue_IsSplicedAsText()
    {
        string result = AgentTemplateTags.TransformEngineTags(
            "Region: {{_REGION}}.",
            text => text.Replace("{_REGION}", "North", StringComparison.Ordinal));

        Assert.Equal("Region: North.", result);
    }

    [Fact]
    public void TransformEngineTags_TagsInsideAParameterValue_StayResolvable()
    {
        string result = AgentTemplateTags.TransformEngineTags(
            "Period: {{_PERIOD}}.",
            text => text.Replace("{_PERIOD}", "{InterimPeriod} month", StringComparison.Ordinal));

        Assert.Equal("Period: {{InterimPeriod}} month.", result);
    }

    [Fact]
    public void TransformEngineTags_DigitBracesInsideAParameterValue_StayText()
    {
        string result = AgentTemplateTags.TransformEngineTags(
            "Match: {{_PATTERN}}.",
            text => text.Replace("{_PATTERN}", @"\d{3} and {0}", StringComparison.Ordinal));

        Assert.Equal(@"Match: \d{3} and {0}.", result);
    }

    [Fact]
    public void TransformEngineTags_AFanIn_KeepsEveryIterationAsATag()
    {
        string result = AgentTemplateTags.TransformEngineTags(
            "All: {{echo}}",
            text => text.Replace("{echo}", "{items_echo_0}\n{items_echo_1}", StringComparison.Ordinal));

        Assert.Equal("All: {{items_echo_0}}\n{{items_echo_1}}", result);
    }

    [Fact]
    public void TransformEngineTags_StrategyNamespaces_AreNeverRewritten()
    {
        string result = AgentTemplateTags.TransformEngineTags(
            "{{Verifier.findings}} {{Validator.errors}}",
            _ => "{changed}");

        Assert.Equal("{{Verifier.findings}} {{Validator.errors}}", result);
    }
}
