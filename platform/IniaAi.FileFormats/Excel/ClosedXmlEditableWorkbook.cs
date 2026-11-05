using ClosedXML.Excel;
using System.Diagnostics;

namespace IniaAi.FileFormats.Excel;

internal sealed class ClosedXmlEditableWorkbook(XLWorkbook workbook, Stream source) : IEditableWorkbook
{
    private const string BROKEN_REFERENCE = "#REF!";
    private const string TIME_LIMIT_FAILURE = "The formulas took longer than the time limit to calculate.";

    private readonly Dictionary<IXLWorksheet, ClosedXmlSheet> _sheets = [];

    public IReadOnlyList<IEditableSheet> Sheets => [.. workbook.Worksheets.OrderBy(sheet => sheet.Position).Select(SheetFor)];

    public IEditableSheet? FindSheet(string name)
    {
        IXLWorksheet? sheet = workbook.Worksheets.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return sheet is null ? null : SheetFor(sheet);
    }

    /// <summary>
    /// ClosedXML cannot stop a calculation part way, and its first read of a formula's value evaluates every formula
    /// the call invalidated, so the time limit is checked only before and after that read: a slow workbook runs to the
    /// end and then reports the time-limit failure. Only the Financial vertical, a paid module that reads with Aspose,
    /// calculates formulas today. A formula ClosedXML fails to evaluate keeps its stored value and is listed; one with
    /// an unknown function gets #NAME?, as in Excel.
    /// </summary>
    public FormulaCalculation CalculateFormulas(TimeSpan timeLimit)
    {
        var clock = Stopwatch.StartNew();
        var uncalculated = new List<SheetCell>();
        List<(IXLWorksheet Sheet, IXLCell Cell)> formulas = [.. workbook.Worksheets
            .SelectMany(sheet => sheet.CellsUsed(XLCellsUsedOptions.Contents).Where(cell => cell.HasFormula).Select(cell => (sheet, cell)))];
        foreach ((_, IXLCell cell) in formulas)
        {
            cell.InvalidateFormula();
        }

        foreach ((IXLWorksheet sheet, IXLCell cell) in formulas)
        {
            if (clock.Elapsed > timeLimit)
            {
                return new FormulaCalculation(TIME_LIMIT_FAILURE, uncalculated);
            }

            try
            {
                _ = cell.Value;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                uncalculated.Add(new SheetCell(sheet.Name, cell.Address.RowNumber - 1, cell.Address.ColumnNumber - 1));
            }
        }

        return new FormulaCalculation(null, uncalculated);
    }

    public IWrappedTextMeasurement MeasureWrappedText()
    {
        return new ClosedXmlWrappedTextMeasurement(workbook);
    }

    public void RemoveBrokenNames()
    {
        foreach (IXLDefinedName name in workbook.DefinedNames.ToList())
        {
            string refersTo = name.RefersTo ?? string.Empty;
            if (string.IsNullOrEmpty(refersTo) || refersTo.Contains(BROKEN_REFERENCE, StringComparison.Ordinal))
            {
                name.Delete();
            }
        }
    }

    public IEditableSheet ReplaceSheet(string name, SheetVisibility visibility)
    {
        if (FindSheet(name) is ClosedXmlSheet existing)
        {
            _ = _sheets.Remove(existing.Worksheet);
            existing.Worksheet.Delete();
        }

        IXLWorksheet sheet = workbook.Worksheets.Add(name);
        sheet.Visibility = visibility switch
        {
            SheetVisibility.Hidden => XLWorksheetVisibility.Hidden,
            SheetVisibility.VeryHidden => XLWorksheetVisibility.VeryHidden,
            _ => XLWorksheetVisibility.Visible,
        };
        return SheetFor(sheet);
    }

    /// <summary>
    /// ClosedXML writes a formula's result only when told to evaluate before saving. It then writes the result it holds:
    /// the calculated one after <see cref="CalculateFormulas"/>, or the one the file held.
    /// </summary>
    public byte[] Save()
    {
        using var output = new MemoryStream();
        workbook.SaveAs(output, new SaveOptions { EvaluateFormulasBeforeSaving = true });
        return output.ToArray();
    }

    public void Dispose()
    {
        workbook.Dispose();
        source.Dispose();
    }

    private ClosedXmlSheet SheetFor(IXLWorksheet sheet)
    {
        if (!_sheets.TryGetValue(sheet, out ClosedXmlSheet? wrapped))
        {
            wrapped = new ClosedXmlSheet(sheet);
            _sheets[sheet] = wrapped;
        }

        return wrapped;
    }
}
