using System.Text.RegularExpressions;
using System.Xml.Linq;
using IniaAi.FileFormats.Word;
using IniaAi.Testing.Ooxml;
using static IniaAi.FileFormats.ContractTests.TemplateFixtures;

namespace IniaAi.FileFormats.ContractTests.Word;

/// <summary>
/// What a workflow relies on from every <see cref="IWordTemplateFormat"/>. An implementation's test class derives
/// from this one and names the format; the tests read only the interface and the saved file.
/// </summary>
public abstract partial class WordTemplateContract
{
    private const string REVENUE_PARAGRAPH = "Revenue was {{Revenue}} in FY24.";
    private const string COMMENTARY_PARAGRAPH = "{{Commentary}}";
    private const string LINES_PARAGRAPH = "{{Lines}}";
    private const string BRACES_PARAGRAPH = "Remove {{braces}} here";
    private const string CELL_PARAGRAPH = "{{InCell}}";
    private const string LAST_PARAGRAPH = "Last paragraph";
    private const string ROUND_BULLET = "\u2022";
    private const int BULLET_TEXT_POSITION_TWIPS = 288;

    protected abstract IWordTemplateFormat Format { get; }

    [Fact]
    public void Paragraphs_CoverTheBodyTablesTextBoxesHeadersAndFooters_InDocumentOrder()
    {
        using IWordTemplate template = Format.Open(FillableDocument);

        List<string> texts = [.. template.Paragraphs.Select(paragraph => paragraph.Text)];

        Assert.Contains("Box {{Company}}", texts);
        Assert.Contains("Header {{Company}}", texts);
        Assert.Contains("Footer {{Year}}", texts);
        string[] body = [REVENUE_PARAGRAPH, COMMENTARY_PARAGRAPH, LINES_PARAGRAPH, BRACES_PARAGRAPH, CELL_PARAGRAPH, LAST_PARAGRAPH];
        List<int> positions = [.. body.Select(text => texts.IndexOf(text))];
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public void ReplaceText_ReplacesATagSplitAcrossRuns_InTheFormattingOfTheTag()
    {
        using IWordTemplate template = Format.Open(FillableDocument);

        template.ReplaceText(TagPattern(), match => match.Groups[1].Value == "Revenue" ? "USD 5m" : match.Value);

        byte[] saved = template.Save();
        var file = new WordFile(saved);
        Assert.True(file.ShowsPhrase(BodyParagraph(saved, "Revenue was USD 5m in FY24."), "USD 5m", file.IsBold));
    }

    [Fact]
    public void ReplaceText_ReachesTablesTextBoxesHeadersAndFooters()
    {
        using IWordTemplate template = Format.Open(FillableDocument);

        template.ReplaceText(TagPattern(), _ => "X");

        string text = OoxmlPackage.AllText(template.Save());
        Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
        Assert.Contains("Box X", text, StringComparison.Ordinal);
        Assert.Contains("Header X", text, StringComparison.Ordinal);
        Assert.Contains("Footer X", text, StringComparison.Ordinal);
        Assert.Contains("Remove X here", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceText_TurnsANewLineIntoALineBreakInsideTheParagraph()
    {
        using IWordTemplate template = Format.Open(FillableDocument);

        template.ReplaceText(TagPattern(), match => match.Value == CELL_PARAGRAPH ? "one\ntwo" : match.Value);

        Assert.Contains(template.Paragraphs, paragraph => paragraph.Text == "one\ntwo");
        Assert.NotNull(BodyParagraph(template.Save(), "one\ntwo"));
    }

    [Fact]
    public void ReplaceText_WritesTheReplacementVerbatim()
    {
        using IWordTemplate template = Format.Open(FillableDocument);

        template.ReplaceText(TagPattern(), match => match.Value == CELL_PARAGRAPH ? "P&L, R&D &p $1 \\n" : match.Value);

        Assert.NotNull(BodyParagraph(template.Save(), "P&L, R&D &p $1 \\n"));
    }

    [Fact]
    public void ReplaceWithMarkdown_RendersHeadingsListsTablesAndBold_InTheParagraphsFont()
    {
        using IWordTemplate template = Format.Open(FillableDocument);
        IWordTemplateParagraph commentary = template.Paragraphs.Single(paragraph => paragraph.Text == COMMENTARY_PARAGRAPH);

        commentary.ReplaceWithMarkdown("## Highlights\n\n- **Revenue** grew\n- Margins held\n\n| Metric | FY24 |\n|---|---|\n| EBITDA | 1.1 |");

        byte[] saved = template.Save();
        List<XElement> paragraphs = [.. OoxmlPackage.WordParagraphs(saved)];
        Assert.DoesNotContain(paragraphs, paragraph => OoxmlPackage.ParagraphText(paragraph).Contains("**", StringComparison.Ordinal));
        Assert.DoesNotContain(paragraphs, paragraph => OoxmlPackage.ParagraphText(paragraph) == COMMENTARY_PARAGRAPH);
        XElement revenue = paragraphs.Single(paragraph => OoxmlPackage.ParagraphText(paragraph) == "Revenue grew");
        var file = new WordFile(saved);
        Assert.Equal((ROUND_BULLET, BULLET_TEXT_POSITION_TWIPS, 0), (file.ListMarker(revenue), file.ListTextPosition(revenue), file.MarkerPosition(revenue)));
        Assert.True(file.ShowsPhrase(revenue, "Revenue", run => file.IsBold(run) && file.FontName(run) == "Arial"));
        Assert.Equal(2, OoxmlPackage.Part(saved, OoxmlPackage.DOCUMENT_PART).Descendants(OoxmlPackage.WordprocessingMl + "tbl").Count());
        Assert.Contains(paragraphs, paragraph => OoxmlPackage.ParagraphText(paragraph) == "EBITDA");
    }

    [Fact]
    public void ReplaceWithLines_PutsOneParagraphPerLine_InTheParagraphsFormatting()
    {
        using IWordTemplate template = Format.Open(FillableDocument);
        IWordTemplateParagraph lines = template.Paragraphs.Single(paragraph => paragraph.Text == LINES_PARAGRAPH);

        lines.ReplaceWithLines(["First", "Second", "Third"]);

        byte[] saved = template.Save();
        List<XElement> paragraphs = [.. OoxmlPackage.WordParagraphs(saved)];
        int first = paragraphs.FindIndex(paragraph => OoxmlPackage.ParagraphText(paragraph) == "First");
        Assert.Equal(["First", "Second", "Third"], paragraphs.Skip(first).Take(3).Select(OoxmlPackage.ParagraphText));
        var file = new WordFile(saved);
        Assert.All(paragraphs.Skip(first).Take(3), paragraph => Assert.True(file.ShowsPhrase(paragraph, OoxmlPackage.ParagraphText(paragraph), file.IsItalic)));
        Assert.DoesNotContain(paragraphs, paragraph => OoxmlPackage.ParagraphText(paragraph) == LINES_PARAGRAPH);
    }

    [Fact]
    public void ContentPutInAsAValue_IsNeverTemplateText()
    {
        using IWordTemplate template = Format.Open(FillableDocument);
        template.Paragraphs.Single(paragraph => paragraph.Text == COMMENTARY_PARAGRAPH).ReplaceWithMarkdown("Quote of **{{Revenue}}**");
        template.Paragraphs.Single(paragraph => paragraph.Text == LINES_PARAGRAPH).ReplaceWithLines(["Keep {{Revenue}}", "as written"]);

        template.ReplaceText(TagPattern(), _ => "REPLACED");

        string text = OoxmlPackage.AllText(template.Save());
        Assert.Contains("Quote of {{Revenue}}", text, StringComparison.Ordinal);
        Assert.Contains("Keep {{Revenue}}", text, StringComparison.Ordinal);
        Assert.Contains("Revenue was REPLACED in FY24.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveText_RemovesEveryOccurrenceFromTheParagraph()
    {
        using IWordTemplate template = Format.Open(FillableDocument);
        IWordTemplateParagraph braces = template.Paragraphs.Single(paragraph => paragraph.Text == BRACES_PARAGRAPH);

        braces.RemoveText("{{");
        braces.RemoveText("}}");

        Assert.Equal("Remove braces here", braces.Text);
        Assert.NotNull(BodyParagraph(template.Save(), "Remove braces here"));
    }

    [Fact]
    public void ToMarkdown_IsNullOrHoldsTheDocumentsText()
    {
        using IWordTemplate template = Format.Open(FillableDocument);

        string? markdown = template.ToMarkdown();

        Assert.True(markdown is null || markdown.Contains(LAST_PARAGRAPH, StringComparison.Ordinal), markdown);
    }

    [Fact]
    public void Save_KeepsADocxADocx()
    {
        using IWordTemplate template = Format.Open(FillableDocument);

        byte[] saved = template.Save();

        Assert.Equal(OoxmlPackage.DOCUMENT_TYPE, OoxmlPackage.ContentType(saved, OoxmlPackage.DOCUMENT_PART));
        Assert.Equal(".docx", Format.SaveExtension(saved));
    }

    [Fact]
    public void Save_KeepsADocumentWithMacrosADocm_WithItsMacros()
    {
        using IWordTemplate template = Format.Open(FillableMacroDocument);

        byte[] saved = template.Save();

        Assert.Equal(OoxmlPackage.MACRO_DOCUMENT_TYPE, OoxmlPackage.ContentType(saved, OoxmlPackage.DOCUMENT_PART));
        Assert.True(new WordFile(saved).HasMacros);
        Assert.Equal(".docm", Format.SaveExtension(saved));
    }

    [Fact]
    public void SaveExtension_IsNullForBytesThatHoldNoWordDocument()
    {
        Assert.Equal(".docx", Format.SaveExtension(FillableDocument));
        Assert.Equal(".docm", Format.SaveExtension(FillableMacroDocument));
        Assert.Null(Format.SaveExtension(EditableWorkbook));
        Assert.Null(Format.SaveExtension(NotADocument));
    }

    private static XElement BodyParagraph(byte[] document, string text)
    {
        return OoxmlPackage.WordParagraphs(document).Single(paragraph => OoxmlPackage.ParagraphText(paragraph) == text);
    }

    [GeneratedRegex(@"\{\{([^{}\r\n]+)\}\}")]
    private static partial Regex TagPattern();
}
