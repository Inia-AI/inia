using IniaAi.Common.Text;

namespace IniaAi.Common.Tests.Text;

public class MarkdownRendererTests
{
    [Fact]
    public void ToHtml_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MarkdownRenderer.ToHtml(null));
    }

    [Fact]
    public void ToHtml_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MarkdownRenderer.ToHtml(string.Empty));
    }

    [Fact]
    public void ToHtml_PlainText_WrapsInParagraph()
    {
        string html = MarkdownRenderer.ToHtml("Hello world");
        Assert.Contains("<p>Hello world</p>", html);
    }

    [Fact]
    public void ToHtml_MarkdownBold_RendersStrong()
    {
        string html = MarkdownRenderer.ToHtml("**bold**");
        Assert.Contains("<strong>bold</strong>", html);
    }

    [Fact]
    public void ToHtml_MarkdownItalic_RendersEm()
    {
        string html = MarkdownRenderer.ToHtml("*italic*");
        Assert.Contains("<em>italic</em>", html);
    }

    [Fact]
    public void ToHtml_MarkdownHeading_RendersH1()
    {
        string html = MarkdownRenderer.ToHtml("# Heading");
        Assert.Contains("<h1", html);
        Assert.Contains("Heading", html);
        Assert.Contains("</h1>", html);
    }

    [Fact]
    public void ToHtml_MarkdownUnorderedList_RendersUl()
    {
        string html = MarkdownRenderer.ToHtml("- a\n- b\n- c");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>a</li>", html);
        Assert.Contains("<li>b</li>", html);
        Assert.Contains("<li>c</li>", html);
    }

    [Fact]
    public void ToHtml_MarkdownTable_RendersHtmlTable()
    {
        string md = "| Version | Release |\n|---|---|\n| 3.9 | October 2020 |\n| 3.10 | October 2021 |";
        string html = MarkdownRenderer.ToHtml(md);

        Assert.Contains("<table>", html);
        Assert.Contains("<thead>", html);
        Assert.Contains("<th>Version</th>", html);
        Assert.Contains("<th>Release</th>", html);
        Assert.Contains("<td>3.9</td>", html);
        Assert.Contains("<td>October 2020</td>", html);
    }

    [Fact]
    public void ToHtml_CodeFence_RendersPreCode()
    {
        string html = MarkdownRenderer.ToHtml("```\nconsole.log(\"hi\")\n```");
        Assert.Contains("<pre>", html);
        Assert.Contains("<code", html);
        Assert.Contains("console.log", html);
    }

    [Fact]
    public void ToHtml_SingleNewline_BecomesLineBreak()
    {
        string html = MarkdownRenderer.ToHtml("line1\nline2");
        Assert.Contains("line1", html);
        Assert.Contains("<br", html);
        Assert.Contains("line2", html);
    }

    [Fact]
    public void ToHtml_LegacyPWrappedContent_PreservesIt()
    {
        string html = MarkdownRenderer.ToHtml("<p>Hello world</p>");
        Assert.Contains("<p>Hello world</p>", html);
    }

    [Fact]
    public void ToHtml_FactTags_PassedThrough()
    {
        string html = MarkdownRenderer.ToHtml("The revenue was <fact id=\"c0\">$5M</fact>.");
        Assert.Contains("<fact id=\"c0\">$5M</fact>", html);
    }

    [Fact]
    public void ToHtml_GfmStrikethrough_RendersDel()
    {
        string html = MarkdownRenderer.ToHtml("~~struck~~");
        Assert.Contains("<del>struck</del>", html);
    }

    [Fact]
    public void ToHtml_GfmTaskList_RendersCheckboxInputs()
    {
        string html = MarkdownRenderer.ToHtml("- [x] done\n- [ ] todo");
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("checked", html);
    }

    [Fact]
    public void ToHtml_GfmAutoLink_RendersAnchor()
    {
        string html = MarkdownRenderer.ToHtml("see https://example.com for more");
        Assert.Contains("<a href=\"https://example.com\"", html);
    }

    [Fact]
    public void ToHtml_FootnoteSyntax_NotInterpretedAsAdvancedExtension()
    {
        string html = MarkdownRenderer.ToHtml("text with [^1] footnote ref\n\n[^1]: definition");
        Assert.DoesNotContain("<a href=\"#fn", html);
        Assert.DoesNotContain("class=\"footnote", html);
    }

    [Fact]
    public void ToHtmlForWordExport_MarkdownImage_ReplacedWithAltText()
    {
        string html = MarkdownRenderer.ToHtmlForWordExport("See ![Company Logo](https://example.com/logo.png) here.");
        Assert.DoesNotContain("<img", html);
        Assert.Contains("Company Logo", html);
    }

    [Fact]
    public void ToHtmlForWordExport_DataUrlImage_Preserved()
    {
        const string dataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        string html = MarkdownRenderer.ToHtmlForWordExport($"<img alt=\"pixel\" src=\"{dataUrl}\" />");
        Assert.Contains("<img", html);
        Assert.Contains("data:image/png;base64,", html);
    }

    [Fact]
    public void ToHtmlForWordExport_HtmlImageWithUrl_StrippedToAltText()
    {
        string html = MarkdownRenderer.ToHtmlForWordExport("<img src=\"https://example.com/x.png\" alt=\"x label\" />");
        Assert.DoesNotContain("<img", html);
        Assert.Contains("x label", html);
    }

    [Fact]
    public void ToHtmlForWordExport_HtmlImageWithoutAlt_StrippedEntirely()
    {
        string html = MarkdownRenderer.ToHtmlForWordExport("before <img src=\"https://example.com/x.png\" /> after");
        Assert.DoesNotContain("<img", html);
        Assert.Contains("before", html);
        Assert.Contains("after", html);
    }

    [Theory]
    [InlineData("[Click here](javascript:alert(1))")]
    [InlineData("[Click here](JaVaScRiPt:alert(1))")]
    [InlineData("[Click here](data:text/html;base64,PGI+eDwvYj4=)")]
    [InlineData("<a href=\"javascript:alert(1)\">Click here</a>")]
    [InlineData("<a href='&#106;avascript:alert(1)'>Click here</a>")]
    [InlineData("<a href=\"java\tscript:alert(1)\">Click here</a>")]
    public void ToHtmlForWordExport_UnsafeLink_KeepsItsTextAndDropsTheLink(string source)
    {
        string html = MarkdownRenderer.ToHtmlForWordExport(source);

        Assert.Contains("Click here", html);
        Assert.DoesNotContain("href", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[Site](https://example.com/a)", "https://example.com/a")]
    [InlineData("[Plain](http://example.com)", "http://example.com")]
    [InlineData("[Mail](mailto:credit@example.com)", "mailto:credit@example.com")]
    [InlineData("[Section](/reports/42#summary)", "/reports/42#summary")]
    public void ToHtmlForWordExport_SafeLink_KeepsTheLink(string source, string href)
    {
        Assert.Contains($"href=\"{href}\"", MarkdownRenderer.ToHtmlForWordExport(source));
    }

    [Theory]
    [InlineData("/reports/42")]
    [InlineData("reports/42")]
    [InlineData("#summary")]
    [InlineData("?tab=risks")]
    [InlineData("")]
    public void WithoutRelativeLinks_LinkWithNoAddressOfItsOwn_KeepsItsTextAndDropsTheLink(string href)
    {
        string html = MarkdownRenderer.WithoutRelativeLinks($"<p>See <a href=\"{href}\">the summary</a>.</p>");

        Assert.Contains("the summary", html);
        Assert.DoesNotContain("href", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("mailto:credit@example.com")]
    public void WithoutRelativeLinks_LinkWithItsOwnAddress_KeepsTheLink(string href)
    {
        Assert.Contains($"href=\"{href}\"", MarkdownRenderer.WithoutRelativeLinks($"<p><a href=\"{href}\">Site</a></p>"));
    }

    [Fact]
    public void ToPlainText_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MarkdownRenderer.ToPlainText(null));
    }

    [Fact]
    public void ToPlainText_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MarkdownRenderer.ToPlainText(string.Empty));
    }

    [Fact]
    public void ToPlainText_Bold_StripsMarkers()
    {
        Assert.Equal("bold", MarkdownRenderer.ToPlainText("**bold**"));
    }

    [Fact]
    public void ToPlainText_Italic_StripsMarkers()
    {
        Assert.Equal("italic", MarkdownRenderer.ToPlainText("*italic*"));
    }

    [Fact]
    public void ToPlainText_Heading_StripsHashes()
    {
        string text = MarkdownRenderer.ToPlainText("# Heading");
        Assert.Contains("Heading", text);
        Assert.DoesNotContain("#", text);
    }

    [Fact]
    public void ToPlainText_UnorderedList_StripsBulletMarkers()
    {
        string text = MarkdownRenderer.ToPlainText("- a\n- b\n- c");
        Assert.Contains("a", text);
        Assert.Contains("b", text);
        Assert.Contains("c", text);
        Assert.DoesNotContain("- ", text);
    }

    [Fact]
    public void ToPlainText_TrimsSurroundingWhitespace()
    {
        Assert.Equal("hello", MarkdownRenderer.ToPlainText("\n\n**hello**\n\n"));
    }
}
