using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

public class EditorAgentMarkdownTests
{
    private const string LIVE_ELEMENTS = "img, script, iframe, object, embed, details, svg, video, audio, source, link, style, form, input";

    [Fact]
    public void ABlockLink_BecomesAnInPageLinkCarryingTheBlockId()
    {
        IDocument html = Render("Read [Revenue](block:stable-1) first.");

        IElement link = Assert.Single(html.QuerySelectorAll("a"));
        Assert.Equal("Revenue", link.TextContent);
        Assert.Equal("#", link.GetAttribute("href"));
        Assert.Equal("stable-1", link.GetAttribute(EditorAgentMarkdown.BLOCK_ID_ATTRIBUTE));
    }

    [Theory]
    [InlineData("Use the ` key. <img src=x onerror=alert(1)>")]
    [InlineData("\\`x` <img src=x onerror=alert(2)>")]
    [InlineData("<details open ontoggle=alert(3)\n>x</details>")]
    [InlineData("<script>alert(4)</script>")]
    [InlineData("A <b onmouseover=alert(5)>tag</b> inline")]
    [InlineData("<iframe src=https://evil.example></iframe>")]
    [InlineData("![a\nb](https://evil.example/x.png)")]
    [InlineData("![x [y] z](https://evil.example/x.png)")]
    [InlineData("\\`![a](https://evil.example/x.png)`")]
    [InlineData("text ~~~ more\n![a](https://evil.example/x.png)")]
    [InlineData("```\nunclosed fence\n\n![a](https://evil.example/x.png) <img src=x onerror=alert(6)>")]
    [InlineData("~~~\n<img src=x onerror=alert(7)>")]
    [InlineData("[ref][1]\n\n[1]: https://evil.example")]
    [InlineData("![ref][1]\n\n[1]: https://evil.example/x.png")]
    [InlineData("<https://evil.example> and https://evil.example/bare")]
    [InlineData("[x](javascript:alert(8)) [y](block:a\"onclick=alert(9))")]
    public void NothingInAnAnswer_CanRunOrReachOut(string answer)
    {
        IDocument html = Render(answer);

        Assert.Empty(html.QuerySelectorAll(LIVE_ELEMENTS));
        Assert.All(html.QuerySelectorAll("*"), element =>
        {
            Assert.DoesNotContain(element.Attributes, attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
            Assert.Null(element.GetAttribute("src"));
            string? href = element.GetAttribute("href");
            Assert.True(href is null or "#", href);
        });
    }

    [Fact]
    public void OutsideLinksAndImages_KeepTheirText()
    {
        IDocument html = Render("See [the site](https://example.com/x) and ![the chart](https://example.com/c.png).");

        Assert.Equal("See the site and the chart.", html.Body!.TextContent.Trim());
    }

    [Fact]
    public void RawHtml_IsShownAsText()
    {
        IDocument html = Render("A <img src=x onerror=alert(1)> tag");

        Assert.Contains("<img src=x onerror=alert(1)>", html.Body!.TextContent);
    }

    [Fact]
    public void Markdown_StillFormats()
    {
        IDocument html = Render("## Heading\n\n- **bold** item\n\n`code`");

        Assert.NotNull(html.QuerySelector("h2"));
        Assert.NotNull(html.QuerySelector("li strong"));
        Assert.NotNull(html.QuerySelector("code"));
    }

    [Fact]
    public void ALinkToABlockTheWorkflowDoesNotHave_ShowsAsPlainText()
    {
        var workflow = new Workflow("Report");
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "a") { Name = "Fact tracking test", StableBlockId = "stable-a" };
        HashSet<string> ids = EditorAgentBlockLinks.Ids(workflow);

        IDocument html = new HtmlParser().ParseDocument(
            $"<html><body>{EditorAgentMarkdown.ToHtml("See [Fact tracking test](block:stable-a), [Summary](block:placeholder) and [Totals](block:3f2b9c1e-0000-4000-8000-000000000000).", ids.Contains)}</body></html>");

        IElement link = Assert.Single(html.QuerySelectorAll("a"));
        Assert.Equal("stable-a", link.GetAttribute(EditorAgentMarkdown.BLOCK_ID_ATTRIBUTE));
        Assert.Contains("Summary and Totals", html.Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockLink_ResolvesByStableIdThenEditorId_AndAMissingOneToNothing()
    {
        var workflow = new Workflow("Report");
        var section = new Section("Body", workflow);
        var stable = new Block(BlockTypes.AiQuery, section, "a") { Name = "A", StableBlockId = "stable-a" };
        var unstamped = new Block(BlockTypes.AiQuery, section, "b") { Name = "B" };

        Assert.Same(stable, EditorAgentBlockLinks.Resolve(workflow, "stable-a"));
        Assert.Same(unstamped, EditorAgentBlockLinks.Resolve(workflow, unstamped.Id));
        Assert.Null(EditorAgentBlockLinks.Resolve(workflow, "gone"));
    }

    [Fact]
    public void ADuplicatedStableId_FallsBackToTheEditorId()
    {
        var workflow = new Workflow("Report");
        var section = new Section("Body", workflow);
        var original = new Block(BlockTypes.AiQuery, section, "a") { Name = "A", StableBlockId = "shared" };
        var twin = new Block(BlockTypes.AiQuery, section, "c") { Name = "C", StableBlockId = "shared" };

        Assert.Same(twin, EditorAgentBlockLinks.Resolve(workflow, twin.Id));
        Assert.Same(original, EditorAgentBlockLinks.Resolve(workflow, "shared"));
    }

    [Theory]
    [InlineData(0.8, 3, "$0.80 reading, $3.00 writing, per million tokens")]
    [InlineData(1.25, 15, "$1.25 reading, $15.00 writing, per million tokens")]
    public void APrice_ShowsCents(double input, double output, string expected)
    {
        var model = new EditorAgentModelOption(
            "m", "M", "c", (decimal)input, (decimal)output, IsDefault: false);

        Assert.Equal(expected, EditorAgentText.Price(model));
    }

    [Fact]
    public void ADefaultModel_IsNamedByItsTier_AndAnyOtherModelByItsName()
    {
        var advanced = new EditorAgentModelOption(
            "m", "Claude Sonnet 5.5 (AWS Bedrock)", "c", null, null, IsDefault: true) { Tier = EditorAgentModelCatalog.ADVANCED_TIER };
        var expert = new EditorAgentModelOption(
            "d", "Claude Opus 5.5 (AWS Bedrock)", "c", null, null, IsDefault: false) { Tier = EditorAgentModelCatalog.EXPERT_TIER };
        var older = new EditorAgentModelOption(
            "o", "Claude Sonnet 5 (AWS Bedrock)", "c", null, null, IsDefault: false);

        Assert.Equal("Advanced · Claude Sonnet 5.5", EditorAgentText.ModelMenuName(advanced));
        Assert.Equal("Expert · Claude Opus 5.5", EditorAgentText.ModelMenuName(expert));
        Assert.Equal("Claude Sonnet 5", EditorAgentText.ModelMenuName(older));
    }

    [Fact]
    public void AFormFeedInACodeFenceInfoString_AddsNoClasses()
    {
        IDocument html = Render("```x\fmud-overlay\fmud-overlay-dark\fz-100\nSession expired.\n```");

        Assert.All(html.QuerySelectorAll("*"), element => Assert.Null(element.GetAttribute("class")));
        Assert.Contains("Session expired.", html.Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AVeryWideTableLine_RendersWithoutTablesAndQuickly()
    {
        string answer = "|a|\n|-|\n" + new string('|', 8000) + "\n" + string.Concat(Enumerable.Repeat("|x|\n", 2000));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        string html = EditorAgentMarkdown.ToHtml(answer);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        Assert.DoesNotContain("<table", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(128, '>')]
    [InlineData(200, '[')]
    public void DeeplyNestedInput_FallsBackToPlainText(int depth, char marker)
    {
        string answer = new string(marker, depth) + " x";

        string html = EditorAgentMarkdown.ToHtml(answer);

        Assert.StartsWith("<pre>", html, StringComparison.Ordinal);
    }

    private static IDocument Render(string markdown)
    {
        return new HtmlParser().ParseDocument($"<html><body>{EditorAgentMarkdown.ToHtml(markdown)}</body></html>");
    }
}
