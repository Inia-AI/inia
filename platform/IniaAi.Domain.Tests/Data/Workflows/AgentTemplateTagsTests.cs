using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class AgentTemplateTagsTests
{
    [Fact]
    public void ExtractEngineTags_DoubleBraceTag_IsExtracted()
    {
        List<string> tags = [.. AgentTemplateTags.ExtractEngineTags("Use {{PortfolioSummary}} as context.")];

        Assert.Equal(["PortfolioSummary"], tags);
    }

    [Fact]
    public void ExtractEngineTags_DottedAndIndexedTags_KeepFullForm()
    {
        const string content = "From {{Upstream.answer}} and {{PeerCompanies[0].Name}}.";

        List<string> tags = [.. AgentTemplateTags.ExtractEngineTags(content)];

        Assert.Equal(["Upstream.answer", "PeerCompanies[0].Name"], tags);
    }

    [Fact]
    public void ExtractEngineTags_SingleBraceContent_StaysInert()
    {
        const string content = """
            Extract line items for {ClientName}.
            Emit JSON exactly as: {"client":"...","total":0}
            """;

        Assert.Empty(AgentTemplateTags.ExtractEngineTags(content));
    }

    [Theory]
    [InlineData("Validator.decision")]
    [InlineData("Validator")]
    [InlineData("Validator[0].decision")]
    [InlineData("Verifier.verdict")]
    [InlineData("Verifier")]
    [InlineData("Verifier[1]")]
    public void ExtractEngineTags_StrategyNamespaces_AreExcluded(string strategyTag)
    {
        string content = $"Engine {{{{UpstreamTag}}}} and strategy {{{{{strategyTag}}}}}.";

        List<string> tags = [.. AgentTemplateTags.ExtractEngineTags(content)];

        Assert.Equal(["UpstreamTag"], tags);
    }

    [Theory]
    [InlineData("ValidatorOutput")]
    [InlineData("Verifiers.first")]
    public void ExtractEngineTags_NamespaceExclusionMatchesWholeBaseSegmentOnly(string tag)
    {
        List<string> tags = [.. AgentTemplateTags.ExtractEngineTags($"{{{{{tag}}}}}")];

        Assert.Equal([tag], tags);
    }

    [Fact]
    public void ExtractEngineTags_NamespaceExclusionIsCaseSensitive()
    {
        List<string> tags = [.. AgentTemplateTags.ExtractEngineTags("{{validator.decision}}")];

        Assert.Equal(["validator.decision"], tags);
    }

    [Theory]
    [InlineData("${{ github.ref }}")]
    [InlineData("${{github.ref}}")]
    [InlineData("{{ spaced tag }}")]
    [InlineData("{{ .Values.replicas }}")]
    public void ExtractEngineTags_ForeignTemplateSyntax_StaysInert(string literal)
    {
        string content = $"CI example stays literal: {literal}";

        Assert.Empty(AgentTemplateTags.ExtractEngineTags(content));
    }

    [Fact]
    public void ExtractEngineTags_RepeatedTag_IsDeduplicated()
    {
        const string content = "{{UpstreamTag}} twice: {{UpstreamTag}}";

        List<string> tags = [.. AgentTemplateTags.ExtractEngineTags(content)];

        Assert.Equal(["UpstreamTag"], tags);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no tags at all")]
    public void ExtractEngineTags_EmptyOrTagFreeContent_YieldsNothing(string? content)
    {
        Assert.Empty(AgentTemplateTags.ExtractEngineTags(content));
    }
}
