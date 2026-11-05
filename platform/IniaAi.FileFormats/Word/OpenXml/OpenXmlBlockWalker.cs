using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;
using MathText = DocumentFormat.OpenXml.Math.Text;
using Vml = DocumentFormat.OpenXml.Vml;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>
/// Reads the main document of a package in reading order with every tracked change accepted. A field shows its
/// result, a text box follows its paragraph line by line, and a section's header and footer lines are kept apart,
/// without field text, keyed by the section's first block.
/// </summary>
internal sealed class OpenXmlBlockWalker
{
    private readonly MainDocumentPart _main;
    private readonly OpenXmlListNumbering _lists;
    private readonly List<TextBlock> _blocks = [];
    private readonly Dictionary<int, IReadOnlyList<string>> _headerFooterLines = [];

    public OpenXmlBlockWalker(MainDocumentPart main)
    {
        _main = main;
        _lists = new OpenXmlListNumbering(main.NumberingDefinitionsPart?.Numbering);
    }

    public (List<TextBlock> Blocks, Dictionary<int, IReadOnlyList<string>> HeaderFooterLines) Walk()
    {
        Body? body = _main.Document?.Body;
        if (body is null)
        {
            return (_blocks, _headerFooterLines);
        }

        int sectionStart = 0;
        foreach (OpenXmlElement block in BlockElements(body))
        {
            switch (block)
            {
                case Paragraph paragraph:
                    AddParagraph(paragraph);
                    if (paragraph.ParagraphProperties?.SectionProperties is SectionProperties sectionEnd)
                    {
                        AddSectionLines(sectionStart, sectionEnd);
                        sectionStart = _blocks.Count;
                    }

                    break;
                case Table table:
                    AddTable(table);
                    break;
            }
        }

        if (body.GetFirstChild<SectionProperties>() is SectionProperties last)
        {
            AddSectionLines(sectionStart, last);
        }

        return (_blocks, _headerFooterLines);
    }

    public static string ParagraphText(Paragraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (StyledText piece in Pieces(paragraph, literalOnly: false))
        {
            _ = builder.Append(piece.Text);
        }

        return builder.ToString();
    }

    /// <summary>The paragraphs and tables of a container in order, inside content controls and custom XML.</summary>
    public static IEnumerable<OpenXmlElement> BlockElements(OpenXmlElement container)
    {
        foreach (OpenXmlElement child in container.ChildElements)
        {
            switch (child)
            {
                case Paragraph or Table:
                    yield return child;
                    break;
                case SdtBlock sdt when sdt.SdtContentBlock is not null:
                    foreach (OpenXmlElement inner in BlockElements(sdt.SdtContentBlock))
                    {
                        yield return inner;
                    }

                    break;
                case CustomXmlBlock or SdtContentBlock:
                    foreach (OpenXmlElement inner in BlockElements(child))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    private void AddParagraph(Paragraph paragraph)
    {
        string markdown = ParagraphMarkdown(paragraph);
        List<WordPicture> pictures = Pictures(paragraph).ToList();
        if (markdown.Length > 0 || pictures.Count > 0)
        {
            _blocks.Add(new TextBlock(markdown, pictures));
        }

        foreach (string line in TextBoxLines(paragraph))
        {
            _blocks.Add(TextBlock.Text(line));
        }
    }

    private string ParagraphMarkdown(Paragraph paragraph)
    {
        List<StyledText> pieces = Pieces(paragraph, literalOnly: false).ToList();
        (int heading, int numId, int level) = ParagraphKind(paragraph);
        (ListKind list, string? label) = numId == 0
            ? (ListKind.None, string.Empty)
            : _lists.Next(numId, level);
        return WordPageMarkdown.Paragraph(pieces, heading, list, level, label);
    }

    private void AddTable(Table table)
    {
        var rows = new List<IReadOnlyList<string>>();
        var pictures = new List<WordPicture>();
        foreach (TableRow? row in table.Descendants<TableRow>().Where(row => row.Ancestors<Table>().First() == table))
        {
            if (row.TableRowProperties?.GetFirstChild<Deleted>() is not null)
            {
                continue;
            }

            var cells = new List<string>();
            foreach (TableCell? cell in row.Descendants<TableCell>().Where(cell => cell.Ancestors<TableRow>().First() == row))
            {
                var lines = new List<string>();
                foreach (Paragraph? paragraph in cell.Descendants<Paragraph>().Where(IsCellText))
                {
                    string text = ParagraphMarkdown(paragraph).Trim();
                    if (text.Length > 0)
                    {
                        lines.Add(text);
                    }

                    pictures.AddRange(Pictures(paragraph));
                    lines.AddRange(TextBoxLines(paragraph));
                }

                cells.Add(string.Join("\n", lines));
            }

            rows.Add(cells);
        }

        string markdown = WordPageMarkdown.Table(rows);
        if (markdown.Length > 0 || pictures.Count > 0)
        {
            _blocks.Add(new TextBlock(markdown, pictures));
        }
    }

    private static bool IsCellText(Paragraph paragraph)
    {
        return !paragraph.Ancestors<TextBoxContent>().Any();
    }

    private void AddSectionLines(int firstBlock, SectionProperties section)
    {
        var seen = new HashSet<string>();
        var lines = new List<string>();
        IEnumerable<string?> ids = section.Elements<HeaderReference>().Select(reference => reference.Id?.Value)
            .Concat(section.Elements<FooterReference>().Select(reference => reference.Id?.Value));
        foreach (string id in ids.OfType<string>())
        {
            if (_main.GetPartById(id) is not OpenXmlPart part || part.RootElement is null)
            {
                continue;
            }

            foreach (Paragraph paragraph in part.RootElement.Descendants<Paragraph>())
            {
                string line = string.Concat(Pieces(paragraph, literalOnly: true).Select(piece => piece.Text)).Trim();
                if (line.Length > 0 && seen.Add(line))
                {
                    lines.Add(line);
                }
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        int key = Math.Min(firstBlock, Math.Max(_blocks.Count - 1, 0));
        _headerFooterLines[key] = _headerFooterLines.TryGetValue(key, out IReadOnlyList<string>? earlier) ? [.. earlier, .. lines] : lines;
    }

    /// <summary>
    /// The visible text of a paragraph with its emphasis, outside text boxes. With <paramref name="literalOnly"/>,
    /// text inside a field, its result included, is left out.
    /// </summary>
    public static IEnumerable<StyledText> Pieces(Paragraph paragraph, bool literalOnly)
    {
        int fieldDepth = 0;
        var inFieldCode = new Stack<bool>();
        foreach (OpenXmlElement element in Visible(paragraph))
        {
            switch (element)
            {
                case FieldChar fieldChar:
                    FieldCharValues? type = fieldChar.FieldCharType?.Value;
                    if (type == FieldCharValues.Begin)
                    {
                        fieldDepth++;
                        inFieldCode.Push(true);
                    }
                    else if (type == FieldCharValues.Separate && inFieldCode.Count > 0)
                    {
                        _ = inFieldCode.Pop();
                        inFieldCode.Push(false);
                    }
                    else if (type == FieldCharValues.End && inFieldCode.Count > 0)
                    {
                        _ = inFieldCode.Pop();
                        fieldDepth--;
                    }

                    break;
                case OpenXmlLeafTextElement leaf when leaf is Text or MathText:
                    if (inFieldCode.Contains(true) || (literalOnly && fieldDepth > 0))
                    {
                        break;
                    }

                    Run? run = leaf.Ancestors<Run>().FirstOrDefault();
                    yield return new StyledText(leaf.Text, IsOn(run?.RunProperties?.Bold), IsOn(run?.RunProperties?.Italic));
                    break;
                case TabChar or PositionalTab:
                    yield return new StyledText("\t", false, false);
                    break;
                case Break or CarriageReturn:
                    yield return new StyledText("\n", false, false);
                    break;
                case NoBreakHyphen:
                    yield return new StyledText("-", false, false);
                    break;
            }
        }
    }

    // Deleted and moved-away text is skipped, inserted text kept, and a drawing's content left to the text box and
    // picture readers. Of two alternative contents, the first choice counts.
    private static IEnumerable<OpenXmlElement> Visible(OpenXmlElement element)
    {
        foreach (OpenXmlElement child in element.ChildElements)
        {
            switch (child)
            {
                case DeletedRun or MoveFromRun or Deleted or ParagraphProperties or RunProperties
                    or Drawing or Picture or EmbeddedObject or FieldCode or DeletedText:
                    continue;
                case AlternateContent alternate:
                    OpenXmlElement? choice = alternate.ChildElements.FirstOrDefault();
                    if (choice is not null)
                    {
                        foreach (OpenXmlElement inner in Visible(choice))
                        {
                            yield return inner;
                        }
                    }

                    continue;
            }

            if (child is OpenXmlLeafElement or OpenXmlLeafTextElement)
            {
                yield return child;
                continue;
            }

            foreach (OpenXmlElement inner in Visible(child))
            {
                yield return inner;
            }
        }
    }

    private static bool IsOn(OnOffType? value)
    {
        return value is not null && (value.Val is null || value.Val.Value);
    }

    private (int Heading, int NumId, int Level) ParagraphKind(Paragraph paragraph)
    {
        ParagraphProperties? properties = paragraph.ParagraphProperties;
        string? styleId = properties?.ParagraphStyleId?.Val?.Value;
        Styles? styles = _main.StyleDefinitionsPart?.Styles;
        List<Style> chain = StyleChain(styles, styleId).ToList();

        int heading = 0;
        string? name = chain.FirstOrDefault()?.StyleName?.Val?.Value?.ToLowerInvariant();
        if (name is not null && name.StartsWith("heading ", StringComparison.Ordinal)
            && int.TryParse(name["heading ".Length..], out int level))
        {
            heading = level;
        }
        else if (name == "title")
        {
            heading = 1;
        }
        else
        {
            int? outline = properties?.OutlineLevel?.Val?.Value
                ?? chain.Select(style => style.StyleParagraphProperties?.OutlineLevel?.Val?.Value).FirstOrDefault(value => value is not null);
            if (outline is int outlineLevel and < 9)
            {
                heading = outlineLevel + 1;
            }
        }

        NumberingProperties? numbering = properties?.NumberingProperties
            ?? chain.Select(style => style.StyleParagraphProperties?.NumberingProperties).FirstOrDefault(value => value is not null);
        int numId = numbering?.NumberingId?.Val?.Value ?? 0;
        return (heading, numId, numbering?.NumberingLevelReference?.Val?.Value ?? 0);
    }

    internal static IEnumerable<Style> StyleChain(Styles? styles, string? styleId)
    {
        var seen = new HashSet<string>();
        while (styles is not null && styleId is not null && seen.Add(styleId))
        {
            Style? style = styles.Elements<Style>().FirstOrDefault(candidate => candidate.StyleId?.Value == styleId);
            if (style is null)
            {
                yield break;
            }

            yield return style;
            styleId = style.BasedOn?.Val?.Value;
        }
    }


    private static IEnumerable<string> TextBoxLines(Paragraph paragraph)
    {
        foreach (TextBoxContent box in OwnTextBoxes(paragraph))
        {
            foreach (Paragraph? inner in box.Descendants<Paragraph>().Where(inner => inner.Ancestors<TextBoxContent>().First() == box))
            {
                string text = string.Concat(Pieces(inner, literalOnly: false).Select(piece => piece.Text));
                foreach (string line in text.Split('\n', '\r', '\t', '\v', '\f'))
                {
                    if (line.Trim().Length > 0)
                    {
                        yield return line.Trim();
                    }
                }
            }
        }
    }

    // The text boxes anchored in this paragraph, taking the first choice of an alternative content.
    private static IEnumerable<TextBoxContent> OwnTextBoxes(Paragraph paragraph)
    {
        return paragraph.Descendants<TextBoxContent>().Where(box =>
            box.Ancestors<Paragraph>().First() == paragraph && !InFallback(box));
    }

    private static bool InFallback(OpenXmlElement element)
    {
        return element.Ancestors<AlternateContentFallback>().Any();
    }

    private IEnumerable<WordPicture> Pictures(Paragraph paragraph)
    {
        IEnumerable<string?> ids = paragraph.Descendants<A.Blip>()
            .Where(blip => blip.Ancestors<Paragraph>().First() == paragraph && !InFallback(blip))
            .Select(blip => blip.Embed?.Value)
            .Concat(paragraph.Descendants<Vml.ImageData>()
                .Where(data => data.Ancestors<Paragraph>().First() == paragraph && !InFallback(data))
                .Select(data => data.RelationshipId?.Value));
        foreach (string id in ids.OfType<string>())
        {
            if (_main.GetPartById(id) is ImagePart image)
            {
                yield return new WordPicture(image);
            }
        }
    }
}
