using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace IniaAi.FileFormats.Word.OpenXml;

internal sealed class OpenXmlWordTemplateParagraph : IWordTemplateParagraph
{
    private const string AUTOMATIC_COLOUR = "auto";

    private readonly Paragraph _paragraph;
    private readonly MainDocumentPart _main;
    private readonly HashSet<Paragraph> _valueParagraphs;

    public OpenXmlWordTemplateParagraph(Paragraph paragraph, MainDocumentPart main, HashSet<Paragraph> valueParagraphs)
    {
        _paragraph = paragraph;
        _main = main;
        _valueParagraphs = valueParagraphs;
    }

    public string Text => ParagraphRuns.Text(_paragraph).Trim();

    public void ReplaceWithMarkdown(string markdown)
    {
        List<OpenXmlElement> blocks = new OpenXmlMarkdown(_main).Blocks(markdown, BaseFormat());
        foreach (OpenXmlElement block in blocks)
        {
            _ = _paragraph.InsertBeforeSelf(block);
            _valueParagraphs.UnionWith(block is Paragraph paragraph ? [paragraph] : block.Descendants<Paragraph>());
        }

        // A table cell, a header and a footer end with a paragraph, and a section break lives in the paragraph that
        // ends the section, so in those places this paragraph stays, empty.
        bool endsWithTable = blocks.Count > 0 && blocks[^1] is Table && _paragraph.NextSibling() is null;
        EmptyOrRemove(keep: endsWithTable || EndsASection());
    }

    public void ReplaceWithLines(IReadOnlyList<string> lines)
    {
        RunProperties? runProperties = _paragraph.Elements<Run>().FirstOrDefault()?.RunProperties;
        foreach (string line in lines)
        {
            var paragraph = new Paragraph();
            if (_paragraph.ParagraphProperties?.CloneNode(true) is ParagraphProperties properties)
            {
                properties.SectionProperties = null;
                paragraph.ParagraphProperties = properties;
            }

            var run = new Run();
            if (runProperties?.CloneNode(true) is RunProperties copied)
            {
                run.RunProperties = copied;
            }

            _ = run.AppendChild(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
            _ = paragraph.AppendChild(run);
            _ = _paragraph.InsertBeforeSelf(paragraph);
            _ = _valueParagraphs.Add(paragraph);
        }

        EmptyOrRemove(keep: EndsASection());
    }

    public void RemoveText(string text)
    {
        ParagraphRuns.Replace(_paragraph, new Regex(Regex.Escape(text)), _ => string.Empty);
    }

    private void EmptyOrRemove(bool keep)
    {
        if (keep)
        {
            _paragraph.RemoveAllChildren<Run>();
            _ = _valueParagraphs.Add(_paragraph);
        }
        else
        {
            _paragraph.Remove();
        }
    }

    private bool EndsASection()
    {
        return _paragraph.ParagraphProperties?.SectionProperties is not null;
    }

    /// <summary>
    /// The font, size and colour of the paragraph's first run and the paragraph's alignment, each from the run, its
    /// character style, the paragraph's style or the document defaults, whichever sets it first.
    /// </summary>
    private BaseFormat BaseFormat()
    {
        Styles? styles = _main.StyleDefinitionsPart?.Styles;
        RunProperties? run = _paragraph.Elements<Run>().FirstOrDefault()?.RunProperties;
        string? paragraphStyle = _paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value
            ?? styles?.Elements<Style>().FirstOrDefault(style => style.Type?.Value == StyleValues.Paragraph && style.Default?.Value == true)?.StyleId?.Value;
        List<Style> paragraphStyles = [.. OpenXmlBlockWalker.StyleChain(styles, paragraphStyle)];
        OpenXmlElement?[] runSources =
        [
            run,
            .. OpenXmlBlockWalker.StyleChain(styles, run?.RunStyle?.Val?.Value).Select(style => style.StyleRunProperties),
            .. paragraphStyles.Select(style => style.StyleRunProperties),
            styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle,
        ];

        string? font = runSources.Select(source => source?.GetFirstChild<RunFonts>()?.Ascii?.Value).FirstOrDefault(value => value is not null);
        string? colour = runSources.Select(source => source?.GetFirstChild<Color>()?.Val?.Value)
            .FirstOrDefault(value => value is not null && value != AUTOMATIC_COLOUR);
        int? halfPoints = runSources.Select(source => int.TryParse(source?.GetFirstChild<FontSize>()?.Val?.Value, out int size) ? size : (int?)null)
            .FirstOrDefault(value => value is not null);
        JustificationValues? alignment = (_paragraph.ParagraphProperties?.Justification
            ?? paragraphStyles.Select(style => style.StyleParagraphProperties?.Justification).FirstOrDefault(value => value is not null))?.Val?.Value;
        return new BaseFormat(font, halfPoints, colour, alignment);
    }
}
