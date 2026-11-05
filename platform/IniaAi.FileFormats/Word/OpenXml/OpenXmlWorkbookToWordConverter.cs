using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Markdown;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>
/// The workbook-to-Word copy on free libraries: ClosedXML reads the workbook and Open XML SDK writes the document,
/// following the contract of <see cref="IWorkbookToWordConverter"/>.
/// </summary>
public sealed partial class OpenXmlWorkbookToWordConverter : IWorkbookToWordConverter
{
    private const string WORD_PAGE_PREFIX = WordPageSheets.PREFIX;
    private const int PAGE_WIDTH_TWIPS = (int)OpenXmlParts.LETTER_SHORT_SIDE_TWIPS;
    private const int PAGE_HEIGHT_TWIPS = (int)OpenXmlParts.LETTER_LONG_SIDE_TWIPS;
    private const int SINGLE_LINE_TWIPS = 240;
    private const int MARGIN_TWIPS = 1440;
    private const int HEADER_FOOTER_TWIPS = 720;
    private const int INDENT_TWIPS_PER_LEVEL = 288;
    private const int CELL_PADDING_HORIZONTAL_TWIPS = 80;
    private const int CELL_PADDING_VERTICAL_TWIPS = 30;
    private const int TWIPS_PER_PIXEL = 15;
    private const int TWIPS_PER_POINT = OpenXmlParts.TWIPS_PER_POINT;
    private const string PRINT_AREA_NAME = "_xlnm.Print_Area";
    private const string NAME_ATTRIBUTE = "name";
    private const string LOCAL_SHEET_ID_ATTRIBUTE = "localSheetId";

    public byte[] ConvertToWord(byte[] workbookBytes, string? footerNotice = null)
    {
        using var input = new MemoryStream(workbookBytes, writable: false);
        using XLWorkbook workbook = ClosedXmlWorkbookFormat.LoadForReading(input);
        List<IXLWorksheet> sheets = SelectSheets(workbook);
        Dictionary<int, string> printAreas = PrintAreas(input);

        using var output = new MemoryStream();
        using (var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());
            Body body = main.Document.Body!;
            // The workbook's default font, with no space between paragraphs, so an empty cell and the footer look as in Excel.
            OpenXmlParts.AddDefaultStyle(main, workbook.Style.Font.FontName, (int)Math.Round(workbook.Style.Font.FontSize * 2), spaceAfterTwips: 0, SINGLE_LINE_TWIPS);
            string footerId = OpenXmlParts.AddPageOfPagesFooter(main, JustificationValues.Left, notice: footerNotice);
            var markdown = new OpenXmlMarkdown(main);

            for (int i = 0; i < sheets.Count; i++)
            {
                IXLWorksheet sheet = sheets[i];
                bool landscape = sheet.PageSetup.PageOrientation == XLPageOrientation.Landscape;
                int contentWidth = (landscape ? PAGE_HEIGHT_TWIPS : PAGE_WIDTH_TWIPS) - (2 * MARGIN_TWIPS);
                Table? table = SheetTable(workbook, sheet, printAreas, contentWidth, markdown);
                if (table is not null)
                {
                    body.AppendChild(table);
                }

                SectionProperties section = Section(landscape, footerId);
                if (i < sheets.Count - 1)
                {
                    body.AppendChild(new Paragraph(new ParagraphProperties(section)));
                }
                else
                {
                    body.AppendChild(section);
                }
            }

            main.Document.Save();
        }

        return output.ToArray();
    }

    private static List<IXLWorksheet> SelectSheets(XLWorkbook workbook)
    {
        List<IXLWorksheet> wordPages = workbook.Worksheets
            .Where(sheet => sheet.Name.StartsWith(WORD_PAGE_PREFIX, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (wordPages.Count > 0)
        {
            return wordPages;
        }

        IXLWorksheet first = workbook.Worksheets.FirstOrDefault(sheet => sheet.Visibility == XLWorksheetVisibility.Visible)
            ?? throw new InvalidOperationException("The workbook has no visible sheet to convert to Word.");
        return [first];
    }

    // A sheet set up for print keeps its own scale and row heights; any other sheet fits the margins and lets its
    // rows grow to show all their text.
    private static Table? SheetTable(XLWorkbook workbook, IXLWorksheet sheet, IReadOnlyDictionary<int, string> printAreas, int contentWidth, OpenXmlMarkdown markdown)
    {
        IXLRange? range = (printAreas.TryGetValue(sheet.Position - 1, out string? address) ? sheet.Range(address) : null)
            ?? sheet.PageSetup.PrintAreas.FirstOrDefault()
            ?? sheet.RangeUsed(XLCellsUsedOptions.AllContents | XLCellsUsedOptions.MergedRanges);
        if (range is null)
        {
            return null;
        }

        int firstRow = range.RangeAddress.FirstAddress.RowNumber;
        int lastRow = range.RangeAddress.LastAddress.RowNumber;
        int firstColumn = range.RangeAddress.FirstAddress.ColumnNumber;
        int lastColumn = range.RangeAddress.LastAddress.ColumnNumber;
        List<int> rows = Enumerable.Range(firstRow, lastRow - firstRow + 1).Where(row => !sheet.Row(row).IsHidden).ToList();
        List<int> columns = Enumerable.Range(firstColumn, lastColumn - firstColumn + 1).Where(column => !sheet.Column(column).IsHidden).ToList();
        if (rows.Count == 0 || columns.Count == 0)
        {
            return null;
        }

        bool configured = sheet.PageSetup.PagesWide > 0 || sheet.PageSetup.PagesTall > 0 || (sheet.PageSetup.Scale is > 0 and not 100);
        List<int> widths = columns.Select(column => ((int)Math.Round(sheet.Column(column).Width * 7) + 5) * TWIPS_PER_PIXEL).ToList();
        int total = widths.Sum();
        double scale = 1d;
        if (configured && sheet.PageSetup.PagesWide <= 0 && sheet.PageSetup.PagesTall <= 0)
        {
            scale = sheet.PageSetup.Scale / 100d;
        }

        if (total * scale > contentWidth)
        {
            scale = contentWidth / (double)total;
        }

        widths = [.. widths.Select(width => (int)Math.Floor(width * scale))];

        Dictionary<(int Row, int Column), Merge> merges = Merges(sheet);
        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Dxa, Width = widths.Sum().ToString(CultureInfo.InvariantCulture) },
            new TableLayout { Type = TableLayoutValues.Fixed },
            configured
                ? new TableCellMarginDefault(new TopMargin { Width = "0", Type = TableWidthUnitValues.Dxa })
                : new TableCellMarginDefault(
                    new TopMargin { Width = $"{CELL_PADDING_VERTICAL_TWIPS}", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = CELL_PADDING_HORIZONTAL_TWIPS, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = $"{CELL_PADDING_VERTICAL_TWIPS}", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = CELL_PADDING_HORIZONTAL_TWIPS, Type = TableWidthValues.Dxa })));
        table.AppendChild(new TableGrid(widths.Select(width => new GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) })));

        foreach (int rowNumber in rows)
        {
            var row = new TableRow();
            if (configured)
            {
                int height = (int)Math.Round(sheet.Row(rowNumber).Height * scale * TWIPS_PER_POINT);
                row.AppendChild(new TableRowProperties(new TableRowHeight { Val = (uint)Math.Max(height, 1), HeightType = HeightRuleValues.Exact }));
            }

            for (int c = 0; c < columns.Count; c++)
            {
                int columnNumber = columns[c];
                Merge? merge = merges.GetValueOrDefault((rowNumber, columnNumber));
                if (merge is not null && merge.FirstRow == rowNumber && merge.FirstColumn != columnNumber)
                {
                    continue;
                }

                if (merge is not null && merge.FirstRow != rowNumber && merge.FirstColumn != columnNumber)
                {
                    continue;
                }

                int span = merge is null ? 1 : columns.Count(column => column >= merge.FirstColumn && column <= merge.LastColumn);
                int cellWidth = merge is null ? widths[c] : columns.Select((column, index) => (column, index))
                    .Where(item => item.column >= merge.FirstColumn && item.column <= merge.LastColumn)
                    .Sum(item => widths[item.index]);
                IXLCell anchor = merge is null ? sheet.Cell(rowNumber, columnNumber) : sheet.Cell(merge.FirstRow, merge.FirstColumn);
                var properties = new TableCellProperties(new TableCellWidth { Width = cellWidth.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa });
                if (span > 1)
                {
                    properties.AppendChild(new GridSpan { Val = span });
                }

                if (merge is not null && rows.Count(row => row >= merge.FirstRow && row <= merge.LastRow) > 1)
                {
                    properties.AppendChild(new VerticalMerge { Val = merge.FirstRow == rowNumber ? MergedCellValues.Restart : MergedCellValues.Continue });
                }

                IXLStyle style = anchor.Style;
                if (Borders(workbook, style) is TableCellBorders borders)
                {
                    properties.AppendChild(borders);
                }

                if (style.Fill.PatternType != XLFillPatternValues.None && Hex(workbook, style.Fill.BackgroundColor) is string fill)
                {
                    properties.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill });
                }

                properties.AppendChild(new TableCellVerticalAlignment
                {
                    Val = style.Alignment.Vertical switch
                    {
                        XLAlignmentVerticalValues.Top => TableVerticalAlignmentValues.Top,
                        XLAlignmentVerticalValues.Center => TableVerticalAlignmentValues.Center,
                        _ => TableVerticalAlignmentValues.Bottom,
                    },
                });

                var cell = new TableCell(properties);
                bool isContinuation = merge is not null && merge.FirstRow != rowNumber;
                foreach (OpenXmlElement block in isContinuation ? [new Paragraph()] : CellContent(workbook, anchor, markdown))
                {
                    cell.AppendChild(block);
                }

                row.AppendChild(cell);
            }

            table.AppendChild(row);
        }

        return table;
    }

    // When a workbook defines two print areas for a sheet, one of them a formula, ClosedXML keeps the formula one. The
    // plain range is the one Excel and the paid export print, so it is read from the defined names directly.
    private static Dictionary<int, string> PrintAreas(Stream workbook)
    {
        var areas = new Dictionary<int, string>();
        foreach (XElement name in WorkbookPackage.DefinedNames(workbook).Where(name => (string?)name.Attribute(NAME_ATTRIBUTE) == PRINT_AREA_NAME))
        {
            int? sheet = (int?)name.Attribute(LOCAL_SHEET_ID_ATTRIBUTE);
            Match match = PlainRange().Match(name.Value);
            if (sheet is not null && match.Success)
            {
                areas.TryAdd(sheet.Value, match.Groups[1].Value.Replace("$", string.Empty));
            }
        }

        return areas;
    }

    [GeneratedRegex(@"^(?:'[^']*'|[^!,]+)!(\$?[A-Z]{1,3}\$?\d+:\$?[A-Z]{1,3}\$?\d+)$")]
    private static partial Regex PlainRange();

    private static IEnumerable<OpenXmlElement> CellContent(XLWorkbook workbook, IXLCell cell, OpenXmlMarkdown markdown)
    {
        string text;
        try
        {
            text = cell.GetFormattedString();
        }
        catch (Exception)
        {
            text = cell.Value.ToString();
        }

        IXLStyle style = cell.Style;
        var baseFormat = new BaseFormat(
            style.Font.FontName,
            (int)Math.Round(style.Font.FontSize * 2),
            IsAutomatic(style.Font.FontColor) ? null : Hex(workbook, style.Font.FontColor),
            Alignment(cell));
        int indent = style.Alignment.Indent;

        List<OpenXmlElement> blocks;
        if (text.Length > 0 && MarkdownSyntax.IsLikelyMarkdown(text))
        {
            blocks = markdown.Blocks(text, baseFormat);
        }
        else
        {
            var paragraph = new Paragraph(new ParagraphProperties());
            if (baseFormat.Alignment is JustificationValues alignment)
            {
                paragraph.ParagraphProperties!.AppendChild(new Justification { Val = alignment });
            }

            RunProperties properties = OpenXmlMarkdown.RunProperties(baseFormat, style.Font.Bold, style.Font.Italic);
            if (style.Font.Underline != XLFontUnderlineValues.None)
            {
                properties.AppendChild(new Underline { Val = UnderlineValues.Single });
            }

            OpenXmlMarkdown.AppendText(paragraph, text, properties);
            paragraph.ParagraphProperties!.ParagraphMarkRunProperties = new ParagraphMarkRunProperties(
                properties.Elements<RunFonts>().Concat<OpenXmlElement>(properties.Elements<FontSize>()).Select(element => element.CloneNode(true)));
            blocks = [paragraph];
        }

        if (indent > 0)
        {
            foreach (Paragraph? paragraph in blocks.OfType<Paragraph>().Where(paragraph => paragraph.ParagraphProperties?.NumberingProperties is null))
            {
                paragraph.ParagraphProperties ??= new ParagraphProperties();
                paragraph.ParagraphProperties.Indentation = new Indentation { Left = (indent * INDENT_TWIPS_PER_LEVEL).ToString(CultureInfo.InvariantCulture) };
            }
        }

        if (blocks.Count == 0 || blocks[^1] is Table)
        {
            blocks.Add(new Paragraph());
        }

        return blocks;
    }

    private static JustificationValues? Alignment(IXLCell cell)
    {
        return cell.Style.Alignment.Horizontal switch
        {
            XLAlignmentHorizontalValues.Center or XLAlignmentHorizontalValues.CenterContinuous => JustificationValues.Center,
            XLAlignmentHorizontalValues.Right => JustificationValues.Right,
            XLAlignmentHorizontalValues.Justify => JustificationValues.Both,
            XLAlignmentHorizontalValues.Distributed => JustificationValues.Distribute,
            XLAlignmentHorizontalValues.Left or XLAlignmentHorizontalValues.Fill => JustificationValues.Left,
            _ => cell.DataType is XLDataType.Number or XLDataType.DateTime or XLDataType.TimeSpan
                ? JustificationValues.Right
                : null,
        };
    }

    private sealed record Merge(int FirstRow, int LastRow, int FirstColumn, int LastColumn);

    private static Dictionary<(int Row, int Column), Merge> Merges(IXLWorksheet sheet)
    {
        var map = new Dictionary<(int Row, int Column), Merge>();
        foreach (IXLRange range in sheet.MergedRanges)
        {
            var merge = new Merge(
                range.RangeAddress.FirstAddress.RowNumber,
                range.RangeAddress.LastAddress.RowNumber,
                range.RangeAddress.FirstAddress.ColumnNumber,
                range.RangeAddress.LastAddress.ColumnNumber);
            for (int row = merge.FirstRow; row <= merge.LastRow; row++)
            {
                for (int column = merge.FirstColumn; column <= merge.LastColumn; column++)
                {
                    map[(row, column)] = merge;
                }
            }
        }

        return map;
    }

    private static TableCellBorders? Borders(XLWorkbook workbook, IXLStyle style)
    {
        IXLBorder border = style.Border;
        TopBorder? top = Border<TopBorder>(workbook, border.TopBorder, border.TopBorderColor);
        LeftBorder? left = Border<LeftBorder>(workbook, border.LeftBorder, border.LeftBorderColor);
        BottomBorder? bottom = Border<BottomBorder>(workbook, border.BottomBorder, border.BottomBorderColor);
        RightBorder? right = Border<RightBorder>(workbook, border.RightBorder, border.RightBorderColor);
        if (top is null && left is null && bottom is null && right is null)
        {
            return null;
        }

        var borders = new TableCellBorders();
        foreach (BorderType edge in new BorderType?[] { top, left, bottom, right }.OfType<BorderType>())
        {
            borders.AppendChild(edge);
        }

        return borders;
    }

    private static T? Border<T>(XLWorkbook workbook, XLBorderStyleValues style, XLColor color)
        where T : BorderType, new()
    {
        if (style == XLBorderStyleValues.None)
        {
            return null;
        }

        (BorderValues value, uint size) = style switch
        {
            XLBorderStyleValues.Double => (BorderValues.Double, 4u),
            XLBorderStyleValues.Dashed or XLBorderStyleValues.MediumDashed => (BorderValues.Dashed, 4u),
            XLBorderStyleValues.Dotted => (BorderValues.Dotted, 4u),
            XLBorderStyleValues.Hair => (BorderValues.Single, 2u),
            XLBorderStyleValues.Medium => (BorderValues.Single, 12u),
            XLBorderStyleValues.Thick => (BorderValues.Single, 18u),
            _ => (BorderValues.Single, 4u),
        };
        return new T { Val = value, Size = size, Color = Hex(workbook, color) ?? "auto" };
    }

    // Excel's default font colour is the theme's first text colour or the system foreground: Word's automatic colour.
    private static bool IsAutomatic(XLColor color)
    {
        return (color.ColorType == XLColorType.Theme && color.ThemeColor == XLThemeColor.Text1 && color.ThemeTint == 0)
            || (color.ColorType == XLColorType.Indexed && color.Indexed == 64);
    }

    // A theme colour resolves through the workbook's theme; its tint moves it toward white or black.
    private static string? Hex(XLWorkbook workbook, XLColor? color)
    {
        if (color is null)
        {
            return null;
        }

        try
        {
            XLColor resolved = color.ColorType == XLColorType.Theme ? workbook.Theme.ResolveThemeColor(color.ThemeColor) : color;
            System.Drawing.Color rgb = resolved.Color;
            if (rgb.IsEmpty || (rgb.A == 0 && color.ColorType != XLColorType.Theme))
            {
                return null;
            }

            double tint = color.ColorType == XLColorType.Theme ? color.ThemeTint : 0d;
            int Tinted(int channel) => (int)Math.Round(tint >= 0 ? channel + ((255 - channel) * tint) : channel * (1 + tint));
            return $"{Tinted(rgb.R):X2}{Tinted(rgb.G):X2}{Tinted(rgb.B):X2}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SectionProperties Section(bool landscape, string footerId)
    {
        return new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = footerId },
            new PageSize
            {
                Width = (uint)(landscape ? PAGE_HEIGHT_TWIPS : PAGE_WIDTH_TWIPS),
                Height = (uint)(landscape ? PAGE_WIDTH_TWIPS : PAGE_HEIGHT_TWIPS),
                Orient = landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait,
            },
            new PageMargin
            {
                Top = MARGIN_TWIPS,
                Bottom = MARGIN_TWIPS,
                Left = MARGIN_TWIPS,
                Right = MARGIN_TWIPS,
                Header = HEADER_FOOTER_TWIPS,
                Footer = HEADER_FOOTER_TWIPS,
                Gutter = 0,
            });
    }
}
