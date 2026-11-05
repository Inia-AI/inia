using ClosedXML.Excel;
using ExcelNumberFormat;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Excel;

/// <summary>One ClosedXML sheet, for the document pipeline's reading and a workflow's writing alike.</summary>
internal sealed partial class ClosedXmlSheet : IEditableSheet
{
    private const string TRUE_TEXT = "TRUE";
    private const string FALSE_TEXT = "FALSE";
    private const double FIRST_OLE_DATE = -657435;

    private readonly IXLWorksheet _sheet;
    private (int Row, int Column)? _usedRange;

    public ClosedXmlSheet(IXLWorksheet sheet)
    {
        _sheet = sheet;
    }

    public string Name => _sheet.Name;

    public int Index => _sheet.Position - 1;

    public bool IsVisible => _sheet.Visibility == XLWorksheetVisibility.Visible;

    public int MaxDataRow => UsedRange.Row;

    public int MaxDataColumn => UsedRange.Column;

    internal IXLWorksheet Worksheet => _sheet;

    private bool IsDate1904 => _sheet.Workbook.Use1904DateSystem;

    /// <summary>Found once and again after a write, since a caller asks for it on every row it reads.</summary>
    private (int Row, int Column) UsedRange => _usedRange ??= (
        (_sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0) - 1,
        (_sheet.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0) - 1);

    public string CellText(int row, int column)
    {
        if (row > MaxDataRow || column > MaxDataColumn)
        {
            return string.Empty;
        }

        IXLCell cell = Cell(row, column);
        XLCellValue value = ValueOf(cell);
        if (value.IsBlank)
        {
            return string.Empty;
        }

        if (value.IsBoolean)
        {
            return value.GetBoolean() ? TRUE_TEXT : FALSE_TEXT;
        }

        if (value.IsText)
        {
            return value.GetText();
        }

        if (value.IsError)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return CollapseSpaces(NumberText(value.GetUnifiedNumber(), FormatCode(cell)));
    }

    public CellValue ReadValue(int row, int column)
    {
        if (row > MaxDataRow || column > MaxDataColumn)
        {
            return CellValue.Empty;
        }

        IXLCell cell = Cell(row, column);
        XLCellValue value = ValueOf(cell);
        if (value.IsBlank)
        {
            return CellValue.Empty;
        }

        if (value.IsBoolean)
        {
            return new CellValue.Boolean(value.GetBoolean());
        }

        if (value.IsText)
        {
            return new CellValue.Text(value.GetText());
        }

        if (value.IsError)
        {
            return new CellValue.Error(value.ToString(CultureInfo.InvariantCulture));
        }

        double number = value.GetUnifiedNumber();
        return ExcelNumberText.IsDateFormat(FormatCode(cell)) && TryDate(number, out DateTime date)
            ? new CellValue.Date(date)
            : new CellValue.Number(number);
    }

    public IEnumerable<TextCell> TextCells()
    {
        foreach (IXLCell cell in _sheet.CellsUsed(XLCellsUsedOptions.Contents).OrderBy(cell => cell.Address.RowNumber).ThenBy(cell => cell.Address.ColumnNumber))
        {
            XLCellValue value = ValueOf(cell);
            if (value.IsText)
            {
                yield return new TextCell(cell.Address.RowNumber - 1, cell.Address.ColumnNumber - 1, value.GetText(), cell.HasFormula);
            }
        }
    }

    public void WriteText(int row, int column, string text, IReadOnlyList<TextRun> boldRuns)
    {
        IXLCell cell = Cell(row, column);
        cell.Value = text;
        if (boldRuns.Count > 0)
        {
            IXLRichText richText = cell.GetRichText();
            richText.ClearText();
            int position = 0;
            foreach (TextRun run in boldRuns.OrderBy(run => run.Start))
            {
                AddRun(richText, text, position, run.Start - position, bold: false);
                AddRun(richText, text, run.Start, run.Length, bold: true);
                position = run.Start + run.Length;
            }

            AddRun(richText, text, position, text.Length - position, bold: false);
        }

        Written();
    }

    public void WriteNumber(int row, int column, double value)
    {
        Cell(row, column).Value = value;
        Written();
    }

    public void WriteBoolean(int row, int column, bool value)
    {
        Cell(row, column).Value = value;
        Written();
    }

    /// <summary>Written as its serial number, so the cell keeps its number format: ClosedXML gives a General cell a date format.</summary>
    public void WriteDate(int row, int column, DateTime value)
    {
        Cell(row, column).Value = value.ToOADate() - ExcelNumberText.DateOffset(IsDate1904);
        Written();
    }

    public void Clear(int row, int column)
    {
        _ = Cell(row, column).Clear(XLClearOptions.Contents);
        Written();
    }

    public bool HasDateFormat(int row, int column)
    {
        return ExcelNumberText.IsDateFormat(FormatCode(Cell(row, column)));
    }

    public void SetNumberFormat(int row, int column, string format)
    {
        _ = Cell(row, column).Style.NumberFormat.SetFormat(format);
    }

    public CellLayout Layout(int row, int column)
    {
        IXLCell cell = Cell(row, column);
        return new CellLayout(DisplayArea(cell), cell.Style.Alignment.WrapText, cell.Style.Font.FontSize);
    }

    public bool IsRowHidden(int row)
    {
        return _sheet.Row(row + 1).IsHidden;
    }

    public bool IsColumnHidden(int column)
    {
        return _sheet.Column(column + 1).IsHidden;
    }

    public double RowHeight(int row)
    {
        return _sheet.Row(row + 1).Height;
    }

    public void SetRowHeight(int row, double points)
    {
        _sheet.Row(row + 1).Height = points;
    }

    internal IXLCell Cell(int row, int column)
    {
        return _sheet.Cell(row + 1, column + 1);
    }

    internal static CellRectangle DisplayArea(IXLCell cell)
    {
        IXLRange? merged = cell.MergedRange();
        int row = cell.Address.RowNumber - 1;
        int column = cell.Address.ColumnNumber - 1;
        return merged is null
            ? CellRectangle.Single(row, column)
            : new CellRectangle(
                merged.RangeAddress.FirstAddress.RowNumber - 1,
                merged.RangeAddress.FirstAddress.ColumnNumber - 1,
                merged.RangeAddress.LastAddress.RowNumber - 1,
                merged.RangeAddress.LastAddress.ColumnNumber - 1);
    }

    private static XLCellValue ValueOf(IXLCell cell)
    {
        return cell.HasFormula ? cell.CachedValue : cell.Value;
    }

    private void Written()
    {
        _usedRange = null;
    }

    private static void AddRun(IXLRichText richText, string text, int start, int length, bool bold)
    {
        if (length > 0)
        {
            _ = richText.AddText(text.Substring(start, length)).SetBold(bold);
        }
    }

    private bool TryDate(double serial, out DateTime date)
    {
        double oleDate = serial + ExcelNumberText.DateOffset(IsDate1904);
        bool valid = oleDate is > FIRST_OLE_DATE and < ExcelNumberText.FIRST_SERIAL_PAST_YEAR_9999;
        date = valid ? DateTime.FromOADate(oleDate) : default;
        return valid;
    }

    /// <summary>A number in a format that fails to format shows as the number itself, so one cell cannot stop the workbook.</summary>
    private string NumberText(double number, string format)
    {
        try
        {
            return ExcelNumberText.Format(number, format, IsDate1904, CultureInfo.CurrentCulture);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or IndexOutOfRangeException or OverflowException)
        {
            return number.ToString(CultureInfo.CurrentCulture);
        }
    }

    private static string FormatCode(IXLCell cell)
    {
        IXLNumberFormat format = cell.Style.NumberFormat;
        return string.IsNullOrEmpty(format.Format) ? BuiltInNumberFormats.Code(format.NumberFormatId, CultureInfo.CurrentCulture) : format.Format;
    }

    private static string CollapseSpaces(string value)
    {
        return MultiSpaceRegex().Replace(value, " ").Trim();
    }

    [GeneratedRegex("  +")]
    private static partial Regex MultiSpaceRegex();
}
