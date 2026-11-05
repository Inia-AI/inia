using IniaAi.Core.Workflows.Agents;

namespace IniaAi.Core.Tests.Workflows.Agents;

public class AgentTemplateRendererTests
{
    private const string NO_DATA = "No data available";

    [Fact]
    public void Render_ResolvesDoubleBraceWhileSingleBraceStaysLiteral()
    {
        Dictionary<string, string?> inputs = new() { ["Counterparty.Name"] = "Acme" };

        AgentTemplateRenderer.AgentRenderResult result = AgentTemplateRenderer.Render(
            "single {Counterparty.Name} double {{Counterparty.Name}} json {\"a\": 1}",
            inputs);

        Assert.Equal("single {Counterparty.Name} double Acme json {\"a\": 1}", result.Content);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Render_SplicesSubstitutedValuesLiterallyWithoutNestedResolution()
    {
        Dictionary<string, string?> inputs = new()
        {
            ["Outer"] = "carries {{Inner}} and {Inner}",
            ["Inner"] = "MUST-NOT-APPEAR",
        };

        AgentTemplateRenderer.AgentRenderResult result = AgentTemplateRenderer.Render("{{Outer}}", inputs);

        Assert.Equal("carries {{Inner}} and {Inner}", result.Content);
        Assert.DoesNotContain("MUST-NOT-APPEAR", result.Content);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(NO_DATA)]
    public void Render_UnusableValuesLeaveTheTagVerbatimWithAWarning(string? value)
    {
        Dictionary<string, string?> inputs = new() { ["Tag"] = value };

        AgentTemplateRenderer.AgentRenderResult result = AgentTemplateRenderer.Render("before {{Tag}} after", inputs);

        Assert.Equal("before {{Tag}} after", result.Content);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("{{Tag}}", warning);
        Assert.DoesNotContain(NO_DATA, result.Content);
    }

    [Fact]
    public void Render_UnknownTagStaysVerbatimWithAWarning()
    {
        AgentTemplateRenderer.AgentRenderResult result = AgentTemplateRenderer.Render(
            "{{Nobody.Knows.Me}}", new Dictionary<string, string?>());

        Assert.Equal("{{Nobody.Knows.Me}}", result.Content);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("Unknown tag", warning);
    }

    [Fact]
    public void Render_UnbalancedAndBareBracesPassThrough()
    {
        Dictionary<string, string?> inputs = new() { ["Tag"] = "X" };

        AgentTemplateRenderer.AgentRenderResult result = AgentTemplateRenderer.Render(
            "open {{Tag but never closed, closer } alone, {{Tag}}} trailing", inputs);

        Assert.Equal("open {{Tag but never closed, closer } alone, X} trailing", result.Content);
    }

    [Fact]
    public void Render_MatchesTagsCaseSensitively()
    {
        Dictionary<string, string?> inputs = new() { ["tag"] = "lower" };

        AgentTemplateRenderer.AgentRenderResult result = AgentTemplateRenderer.Render("{{Tag}}", inputs);

        Assert.Equal("{{Tag}}", result.Content);
        _ = Assert.Single(result.Warnings);
    }
}
