using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdownTableRow = Markdig.Extensions.Tables.TableRow;
using WordTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>The font, size, colour and alignment that Markdown content takes from the place it goes into.</summary>
internal sealed record BaseFormat(string? FontName, int? HalfPoints, string? ColorHex, JustificationValues? Alignment);

/// <summary>
/// Turns Markdown into Word paragraphs, lists and tables in the base format. Headings keep their own size. A bullet is
/// a round bullet (U+2022) flush with the left edge with its text at 14.4 pt; a numbered list keeps its numbers.
/// </summary>
internal sealed class OpenXmlMarkdown
{
    public const string DOT_BULLET = "•";
    public const int BULLET_TEXT_INDENT_TWIPS = 288;

    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
    private static readonly int[] _headingHalfPoints = [32, 26, 24, 22, 22, 22];

    private readonly MainDocumentPart _main;
    private int? _bulletNumId;

    public OpenXmlMarkdown(MainDocumentPart main)
    {
        _main = main;
    }

    public List<OpenXmlElement> Blocks(string markdown, BaseFormat baseFormat)
    {
        var blocks = new List<OpenXmlElement>();
        foreach (Block block in Markdig.Markdown.Parse(markdown.Replace("\r\n", "\n"), _pipeline))
        {
            AddBlock(block, baseFormat, blocks, listLevel: 0);
        }

        return blocks;
    }

    private void AddBlock(Block block, BaseFormat baseFormat, List<OpenXmlElement> blocks, int listLevel)
    {
        switch (block)
        {
            case HeadingBlock heading:
                int size = _headingHalfPoints[Math.Clamp(heading.Level, 1, 6) - 1];
                blocks.Add(Paragraph(heading.Inline, baseFormat with { HalfPoints = size }, bold: true, alignment: null));
                break;
            case ParagraphBlock paragraph:
                blocks.Add(Paragraph(paragraph.Inline, baseFormat, bold: false, alignment: baseFormat.Alignment));
                break;
            case ListBlock list:
                int numId = list.IsOrdered
                    ? NewNumberedList(int.TryParse(list.OrderedStart, out int start) ? start : 1)
                    : BulletList();
                foreach (ListItemBlock item in list.OfType<ListItemBlock>())
                {
                    bool first = true;
                    foreach (Block child in item)
                    {
                        if (child is ListBlock nested)
                        {
                            AddBlock(nested, baseFormat, blocks, listLevel + 1);
                            continue;
                        }

                        ContainerInline? inline = (child as LeafBlock)?.Inline;
                        Paragraph paragraph = Paragraph(inline, baseFormat, bold: false, alignment: null);
                        if (first)
                        {
                            paragraph.ParagraphProperties ??= new ParagraphProperties();
                            paragraph.ParagraphProperties.NumberingProperties = new NumberingProperties(
                                new NumberingLevelReference { Val = Math.Min(listLevel, 8) },
                                new NumberingId { Val = numId });
                            first = false;
                        }

                        blocks.Add(paragraph);
                    }
                }

                break;
            case MarkdownTable table:
                blocks.Add(Table(table, baseFormat));
                break;
            case ThematicBreakBlock:
                break;
            case LeafBlock leaf:
                string text = string.Join("\n", leaf.Lines.Lines.Take(leaf.Lines.Count).Select(line => line.ToString()));
                blocks.Add(PlainParagraph(text, baseFormat));
                break;
            case ContainerBlock container:
                foreach (Block child in container)
                {
                    AddBlock(child, baseFormat, blocks, listLevel);
                }

                break;
        }
    }

    public static Paragraph PlainParagraph(string text, BaseFormat baseFormat)
    {
        var paragraph = new Paragraph();
        if (baseFormat.Alignment is JustificationValues alignment)
        {
            paragraph.ParagraphProperties = new ParagraphProperties(new Justification { Val = alignment });
        }

        AppendText(paragraph, text, RunProperties(baseFormat, bold: false, italic: false));
        return paragraph;
    }

    private static Paragraph Paragraph(ContainerInline? inline, BaseFormat baseFormat, bool bold, JustificationValues? alignment)
    {
        var paragraph = new Paragraph();
        if (alignment is JustificationValues value)
        {
            paragraph.ParagraphProperties = new ParagraphProperties(new Justification { Val = value });
        }

        if (inline is not null)
        {
            AppendInlines(paragraph, inline, baseFormat, bold, italic: false);
        }

        return paragraph;
    }

    private static void AppendInlines(Paragraph paragraph, ContainerInline container, BaseFormat baseFormat, bool bold, bool italic)
    {
        foreach (Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendText(paragraph, literal.Content.ToString(), RunProperties(baseFormat, bold, italic));
                    break;
                case EmphasisInline emphasis:
                    bool strong = emphasis.DelimiterCount >= 2;
                    AppendInlines(paragraph, emphasis, baseFormat, bold || strong, italic || !strong || emphasis.DelimiterCount >= 3);
                    break;
                case CodeInline code:
                    AppendText(paragraph, code.Content, RunProperties(baseFormat with { FontName = "Courier New" }, bold, italic));
                    break;
                case LineBreakInline lineBreak:
                    paragraph.AppendChild(new Run(lineBreak.IsHard ? new Break() : new Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
                    break;
                case LinkInline link:
                    AppendInlines(paragraph, link, baseFormat, bold, italic);
                    break;
                case HtmlInline or HtmlEntityInline:
                    string raw = inline is HtmlEntityInline entity ? entity.Transcoded.ToString() : string.Empty;
                    if (raw.Length > 0)
                    {
                        AppendText(paragraph, raw, RunProperties(baseFormat, bold, italic));
                    }

                    break;
                case ContainerInline nested:
                    AppendInlines(paragraph, nested, baseFormat, bold, italic);
                    break;
            }
        }
    }

    public static void AppendText(Paragraph paragraph, string text, RunProperties? properties)
    {
        var run = new Run();
        if (properties is not null)
        {
            run.RunProperties = (RunProperties)properties.CloneNode(true);
        }

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.AppendChild(new Break());
            }

            run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }

        paragraph.AppendChild(run);
    }

    public static RunProperties RunProperties(BaseFormat baseFormat, bool bold, bool italic)
    {
        var properties = new RunProperties();
        if (baseFormat.FontName is string font)
        {
            properties.AppendChild(new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font, EastAsia = font });
        }

        if (bold)
        {
            properties.AppendChild(new Bold());
        }

        if (italic)
        {
            properties.AppendChild(new Italic());
        }

        if (baseFormat.ColorHex is string color)
        {
            properties.AppendChild(new Color { Val = color });
        }

        if (baseFormat.HalfPoints is int size)
        {
            properties.AppendChild(new FontSize { Val = size.ToString() });
        }

        return properties;
    }

    private WordTable Table(MarkdownTable table, BaseFormat baseFormat)
    {
        var word = new WordTable(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = OpenXmlParts.FULL_WIDTH_PCT },
            OpenXmlParts.SingleBorders()));
        int columns = table.OfType<MarkdownTableRow>().Select(row => row.Count).DefaultIfEmpty(1).Max();
        var grid = new TableGrid();
        for (int c = 0; c < columns; c++)
        {
            grid.AppendChild(new GridColumn());
        }

        word.AppendChild(grid);
        foreach (MarkdownTableRow row in table.OfType<MarkdownTableRow>())
        {
            var wordRow = new DocumentFormat.OpenXml.Wordprocessing.TableRow();
            foreach (MarkdownTableCell cell in row.OfType<MarkdownTableCell>())
            {
                var wordCell = new DocumentFormat.OpenXml.Wordprocessing.TableCell();
                bool added = false;
                foreach (ParagraphBlock block in cell.OfType<ParagraphBlock>())
                {
                    wordCell.AppendChild(Paragraph(block.Inline, baseFormat, bold: row.IsHeader, alignment: null));
                    added = true;
                }

                if (!added)
                {
                    wordCell.AppendChild(new Paragraph());
                }

                wordRow.AppendChild(wordCell);
            }

            for (int missing = row.Count; missing < columns; missing++)
            {
                wordRow.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.TableCell(new Paragraph()));
            }

            word.AppendChild(wordRow);
        }

        return word;
    }

    private int BulletList()
    {
        return _bulletNumId ??= AddList(NumberFormatValues.Bullet, DOT_BULLET);
    }

    private int NewNumberedList(int start)
    {
        return AddList(NumberFormatValues.Decimal, "%1.", start);
    }

    private int AddList(NumberFormatValues format, string levelText, int start = 1)
    {
        NumberingDefinitionsPart part = _main.NumberingDefinitionsPart ?? _main.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering ??= new Numbering();
        Numbering numbering = part.Numbering;
        int abstractId = numbering.Elements<AbstractNum>().Select(item => item.AbstractNumberId?.Value ?? 0).DefaultIfEmpty(-1).Max() + 1;
        int numId = numbering.Elements<NumberingInstance>().Select(item => item.NumberID?.Value ?? 0).DefaultIfEmpty(0).Max() + 1;

        var definition = new AbstractNum { AbstractNumberId = abstractId };
        for (int level = 0; level < 9; level++)
        {
            string text = format == NumberFormatValues.Bullet ? levelText : $"%{level + 1}.";
            Indentation indent = format == NumberFormatValues.Bullet
                ? new Indentation { Left = (BULLET_TEXT_INDENT_TWIPS * (level + 1)).ToString(), Hanging = BULLET_TEXT_INDENT_TWIPS.ToString() }
                : new Indentation { Left = (360 * (level + 1)).ToString(), Hanging = "360" };
            definition.AppendChild(new Level(
                new StartNumberingValue { Val = level == 0 ? start : 1 },
                new NumberingFormat { Val = format },
                new LevelSuffix { Val = LevelSuffixValues.Space },
                new LevelText { Val = text },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(indent))
            { LevelIndex = level });
        }

        // Every abstract definition comes before the first instance, as the schema requires.
        NumberingInstance? firstInstance = numbering.Elements<NumberingInstance>().FirstOrDefault();
        if (firstInstance is null)
        {
            numbering.AppendChild(definition);
        }
        else
        {
            numbering.InsertBefore(definition, firstInstance);
        }

        numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = abstractId }) { NumberID = numId });
        return numId;
    }
}
