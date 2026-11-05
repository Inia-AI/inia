using IniaAi.FileFormats.Excel;
using IniaAi.FileFormats.Unlock;
using System.Globalization;
using static IniaAi.FileFormats.ContractTests.ContractFixture;

namespace IniaAi.FileFormats.ContractTests.Excel;

/// <summary>
/// What the document pipeline relies on from every <see cref="IWorkbookFormat"/> for .xlsx and .xlsm
/// files: the doc comments of <see cref="IWorkbookFormat"/>, <see cref="IWorkbookDocument"/> and
/// <see cref="IWorkbookSheet"/>, and how ExcelSheetAnalysisService reads a workbook's sheets and its
/// analysis config. The legacy binary .xls format is outside the contract: the open build does
/// not support it.
/// </summary>
public abstract class WorkbookFormatContract
{
    // Excel shows a number in the reader's locale; the expected texts are those of US English.
    private const string CELL_TEXT_CULTURE = "en-US";

    private const string SPACE = " ";
    private const string TWO_SPACES = "  ";

    private const int VALUES_SHEET = 0;
    private const int USED_RANGE_SHEET = 1;
    private const int EMPTY_SHEET = 3;
    private const int VALUE_COLUMN = 1;
    private const int ROW_WITH_NO_VALUE = 22;

    protected abstract IWorkbookFormat Format { get; }

    [Fact]
    public void Open_ListsEverySheetInWorkbookOrder_HiddenAndVeryHiddenOnesIncluded()
    {
        using IWorkbookDocument workbook = Open(CELL_TEXTS_WORKBOOK);

        Assert.Equal(["Values", "Used range", "Hidden", "Empty", "Very hidden"], workbook.Sheets.Select(sheet => sheet.Name));
        Assert.Equal([0, 1, 2, 3, 4], workbook.Sheets.Select(sheet => sheet.Index));
        Assert.Equal([true, true, false, true, false], workbook.Sheets.Select(sheet => sheet.IsVisible));
    }

    [Fact]
    public void Open_AMacroEnabledWorkbook_ListsEverySheetInWorkbookOrder_HiddenAndVeryHiddenOnesIncluded()
    {
        using IWorkbookDocument workbook = Open(MACRO_WORKBOOK);

        Assert.Equal(
            ["WordPage_1", "WordPage_2", "WordPage_3", "WordPage_4", "EBITDA Reconciliation", "Logs", "Inputs", ExcelAnalysisConfigSheet.SHEET_NAME],
            workbook.Sheets.Select(sheet => sheet.Name));
        Assert.Equal(Enumerable.Range(0, 8), workbook.Sheets.Select(sheet => sheet.Index));
        Assert.Equal([true, true, true, true, true, true, false, false], workbook.Sheets.Select(sheet => sheet.IsVisible));
    }

    [Fact]
    public void CellText_OfAVeryHiddenSheet_ReadsBackTheAnalysisConfigItHolds()
    {
        using IWorkbookDocument workbook = Open(MACRO_WORKBOOK);

        ExcelAnalysisConfig? config = ExcelAnalysisConfigSheet.TryRead(workbook);

        Assert.NotNull(config);
        Assert.Equal(ExcelAnalysisConfigSheet.CURRENT_SCHEMA_VERSION, config.SchemaVersion);
        Assert.Equal(ExcelAnalysisSource.BlockGenerated, config.Source);
    }

    [Theory]
    [InlineData(0, "  Net  asset value, as typed")]
    [InlineData(1, "42.5")]
    [InlineData(2, "1,234,567.89")]
    [InlineData(3, "1500")]
    [InlineData(4, "2024-12-31")]
    [InlineData(5, "31 Dec 2024")]
    [InlineData(6, "1.25%")]
    [InlineData(7, "50%")]
    [InlineData(8, "$1,234.50")]
    [InlineData(9, "($1,234.50)")]
    [InlineData(10, "-3.25")]
    [InlineData(11, "(1,500)")]
    [InlineData(12, "-2,500.50")]
    [InlineData(15, "#DIV/0!")]
    [InlineData(16, "#N/A")]
    [InlineData(17, "TRUE")]
    [InlineData(18, "FALSE")]
    [InlineData(19, "85")]
    [InlineData(20, "2025-01-01")]
    [InlineData(21, "TRUE")]
    [InlineData(ROW_WITH_NO_VALUE, "")]
    public void CellText_IsTheCellAsExcelShowsIt(int row, string expected)
    {
        Assert.Equal(expected, ValueText(row));
    }

    /// <summary>
    /// An accounting format pads the currency sign and fills the cell's width with spaces, so how
    /// many spaces Excel shows depends on the column; the contract fixes only that they collapse.
    /// </summary>
    [Theory]
    [InlineData(13, "$288.30")]
    [InlineData(14, "$-")]
    public void CellText_OfAnAccountingFormat_CollapsesItsPaddingAndFill(int row, string expectedWithoutSpaces)
    {
        string text = ValueText(row);

        Assert.Equal(text.Trim(), text);
        Assert.DoesNotContain(TWO_SPACES, text);
        Assert.Equal(expectedWithoutSpaces, text.Replace(SPACE, string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void CellText_PastTheLastValue_IsEmpty()
    {
        using IWorkbookDocument workbook = Open(CELL_TEXTS_WORKBOOK);

        Assert.Equal(string.Empty, workbook.Sheets[VALUES_SHEET].CellText(ROW_WITH_NO_VALUE + 100, VALUE_COLUMN + 50));
    }

    [Fact]
    public void MaxDataRowAndColumn_AreTheLastOnesThatHoldAValue_AFormulaIncluded_AFormattedEmptyCellNot()
    {
        using IWorkbookDocument workbook = Open(CELL_TEXTS_WORKBOOK);

        IWorkbookSheet sheet = workbook.Sheets[USED_RANGE_SHEET];

        Assert.Equal(6, sheet.MaxDataRow);
        Assert.Equal(4, sheet.MaxDataColumn);
        Assert.Equal("14", sheet.CellText(6, 4));
    }

    [Fact]
    public void MaxDataRowAndColumn_OfAnEmptySheet_AreMinusOne()
    {
        using IWorkbookDocument workbook = Open(CELL_TEXTS_WORKBOOK);

        IWorkbookSheet sheet = workbook.Sheets[EMPTY_SHEET];

        Assert.Equal(-1, sheet.MaxDataRow);
        Assert.Equal(-1, sheet.MaxDataColumn);
        Assert.Equal(string.Empty, sheet.CellText(0, 0));
    }

    [Theory]
    [InlineData(PROTECTED_WORKBOOK)]
    [InlineData(PROTECTED_WORKBOOK_AGILE)]
    public void Open_AWorkbookThatStillNeedsItsPassword_FailsNamingTheDocument(string fixture)
    {
        IWorkbookFormat format = Format;
        string name = ContractFixture.NameOf(fixture);

        DocumentPasswordProtectedException failure = Assert.Throws<DocumentPasswordProtectedException>(
            () => format.Open(ContractFixture.Stream(fixture), name));

        Assert.Equal(name, failure.DocumentName);
    }

    [Fact]
    public void Dispose_DisposesTheStreamThatOpenTookOver()
    {
        MemoryStream stream = ContractFixture.Stream(CELL_TEXTS_WORKBOOK);

        Format.Open(stream, ContractFixture.NameOf(CELL_TEXTS_WORKBOOK)).Dispose();

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void Open_ThatFails_DisposesTheStream()
    {
        MemoryStream stream = ContractFixture.Stream(PROTECTED_WORKBOOK);

        _ = Assert.Throws<DocumentPasswordProtectedException>(() => Format.Open(stream, ContractFixture.NameOf(PROTECTED_WORKBOOK)));

        Assert.False(stream.CanRead);
    }

    private IWorkbookDocument Open(string fixture)
    {
        return Format.Open(ContractFixture.Stream(fixture), ContractFixture.NameOf(fixture));
    }

    private string ValueText(int row)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(CELL_TEXT_CULTURE);

        try
        {
            using IWorkbookDocument workbook = Open(CELL_TEXTS_WORKBOOK);

            return workbook.Sheets[VALUES_SHEET].CellText(row, VALUE_COLUMN);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
