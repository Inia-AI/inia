using ClosedXML.Excel;
using ClosedXML.Graphics;

namespace IniaAi.FileFormats.Excel;

/// <summary>
/// Counts the lines a cell's text wraps to, as Excel lays it out: word by word across the visible columns of the cell's
/// display area, each run measured in its own font by ClosedXML's graphic engine. A font the server lacks is measured
/// in ClosedXML's fallback font, so a line can break a word earlier or later than Excel breaks it.
/// </summary>
internal sealed class ClosedXmlWrappedTextMeasurement(XLWorkbook workbook) : IWrappedTextMeasurement
{
    private const double DPI = 96;
    private const char SPACE = ' ';
    private const char NEW_LINE = '\n';

    // Excel keeps a margin inside the cell on each side of wrapped text.
    private const double CELL_MARGIN_PIXELS = 3;

    private readonly IXLGraphicEngine _engine = DefaultGraphicEngine.Instance.Value;

    public int? CountLines(IEditableSheet sheet, int row, int column)
    {
        if (sheet is not ClosedXmlSheet closedXmlSheet)
        {
            throw new ArgumentException("The sheet does not belong to a ClosedXML workbook.", nameof(sheet));
        }

        IXLCell cell = closedXmlSheet.Cell(row, column);
        CellRectangle area = ClosedXmlSheet.DisplayArea(cell);
        double width = Enumerable.Range(area.FirstColumn, area.LastColumn - area.FirstColumn + 1)
            .Select(index => closedXmlSheet.Worksheet.Column(index + 1))
            .Where(sheetColumn => !sheetColumn.IsHidden)
            .Sum(sheetColumn => ColumnPixels(sheetColumn.Width)) - (2 * CELL_MARGIN_PIXELS);
        return width <= 0 ? null : CountLines(Runs(cell), width);
    }

    public void Dispose()
    {
    }

    private int CountLines(List<(string Text, IXLFontBase Font)> runs, double width)
    {
        int lines = 1;
        double lineWidth = 0;
        foreach ((string text, IXLFontBase font) in runs)
        {
            string[] paragraphs = text.Split(NEW_LINE);
            for (int paragraph = 0; paragraph < paragraphs.Length; paragraph++)
            {
                if (paragraph > 0)
                {
                    lines++;
                    lineWidth = 0;
                }

                foreach (string word in paragraphs[paragraph].Split(SPACE))
                {
                    double space = lineWidth > 0 ? TextWidth(SPACE.ToString(), font) : 0;
                    double wordWidth = TextWidth(word, font);
                    if (lineWidth > 0 && lineWidth + space + wordWidth > width)
                    {
                        lines++;
                        lineWidth = 0;
                        space = 0;
                    }

                    lineWidth += space + wordWidth;
                    while (lineWidth > width)
                    {
                        lines++;
                        lineWidth -= width;
                    }
                }
            }
        }

        return lines;
    }

    private static List<(string Text, IXLFontBase Font)> Runs(IXLCell cell)
    {
        if (cell.HasRichText)
        {
            return [.. cell.GetRichText().Select(run => (run.Text, (IXLFontBase)run))];
        }

        string text = cell.HasFormula ? cell.CachedValue.ToString(System.Globalization.CultureInfo.CurrentCulture) : cell.GetString();
        return [(text, cell.Style.Font)];
    }

    private double TextWidth(string text, IXLFontBase font)
    {
        return text.Length == 0 ? 0 : _engine.GetTextWidth(text, font, DPI);
    }

    /// <summary>A column width in characters as Excel draws it in pixels: whole widths of the default font's widest digit.</summary>
    private double ColumnPixels(double characters)
    {
        double digit = _engine.GetMaxDigitWidth(workbook.Style.Font, DPI);
        return Math.Truncate(((256 * characters) + Math.Truncate(128 / digit)) / 256 * digit);
    }
}
