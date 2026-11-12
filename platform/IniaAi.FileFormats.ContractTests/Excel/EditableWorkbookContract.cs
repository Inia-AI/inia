using IniaAi.FileFormats.Excel;
using IniaAi.Testing.Ooxml;
using static IniaAi.FileFormats.ContractTests.TemplateFixtures;

namespace IniaAi.FileFormats.ContractTests.Excel;

/// <summary>
/// What a workflow relies on from every <see cref="IEditableWorkbookFormat"/>. An implementation's test class
/// derives from this one and names the format; the tests read only the interface and the saved file.
/// </summary>
public abstract class EditableWorkbookContract
{
    private static readonly TimeSpan _calculationLimit = TimeSpan.FromSeconds(30);

    protected abstract IEditableWorkbookFormat Format { get; }

    [Fact]
    public void Open_ListsEverySheetInWorkbookOrder_WithItsVisibility()
    {
        using IEditableWorkbook workbook = OpenFixture();

        Assert.Equal([INPUTS_SHEET, MODEL_SHEET, HIDDEN_SHEET, VERY_HIDDEN_SHEET], workbook.Sheets.Select(sheet => sheet.Name));
        Assert.Equal([0, 1, 2, 3], workbook.Sheets.Select(sheet => sheet.Index));
        Assert.Equal([true, true, false, false], workbook.Sheets.Select(sheet => sheet.IsVisible));
    }

    [Fact]
    public void FindSheet_IgnoresCase_AndIsNullForAMissingSheet()
    {
        using IEditableWorkbook workbook = OpenFixture();

        Assert.Equal(MODEL_SHEET, workbook.FindSheet("model")?.Name);
        Assert.Null(workbook.FindSheet("No such sheet"));
    }

    [Fact]
    public void ReadValue_GivesEachKindOfValue()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];

        Assert.Equal(new CellValue.Number(100), inputs.ReadValue(0, 1));
        Assert.Equal(new CellValue.Number(0.25), inputs.ReadValue(1, 1));
        Assert.Equal(new CellValue.Date(new DateTime(2025, 9, 30)), inputs.ReadValue(2, 1));
        Assert.Equal(new CellValue.Boolean(true), inputs.ReadValue(3, 1));
        Assert.Equal(new CellValue.Text("{{Company}}"), inputs.ReadValue(4, 1));
        Assert.Equal(new CellValue.Error("#DIV/0!"), inputs.ReadValue(5, 1));
        Assert.Equal(CellValue.Empty, inputs.ReadValue(0, 4));
        Assert.Equal(CellValue.Empty, inputs.ReadValue(500, 20));
    }

    [Fact]
    public void CellText_ShowsTheValueInItsNumberFormat()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];

        Assert.Equal("25%", inputs.CellText(1, 1));
        Assert.Equal("2025-09-30", inputs.CellText(2, 1));
        Assert.Equal(string.Empty, inputs.CellText(0, 4));
    }

    [Fact]
    public void TextCells_ListsTheTextRowByRow_AndMarksTextThatAFormulaProduced()
    {
        using IEditableWorkbook workbook = OpenFixture();

        List<TextCell> cells = [.. workbook.Sheets[0].TextCells()];

        Assert.Equal(cells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column), cells);
        Assert.Contains(new TextCell(4, 1, "{{Company}}", IsFormula: false), cells);
        Assert.Contains(new TextCell(2, 2, "{{x}}", IsFormula: true), cells);
        Assert.Contains(new TextCell(0, 5, "hidden column text", IsFormula: false), cells);
        Assert.DoesNotContain(cells, cell => cell is { Row: 0, Column: 1 } or { Row: 5, Column: 1 });
    }

    [Fact]
    public void WriteText_MakesOnlyTheCharactersOfTheBoldRunsBold()
    {
        using IEditableWorkbook workbook = OpenFixture();
        workbook.Sheets[0].WriteText(0, 6, "plain bold plain", [new TextRun(6, 4)]);

        byte[] saved = workbook.Save();

        Assert.Equal(new (string, bool)[] { ("plain ", false), ("bold", true), (" plain", false) }, OoxmlPackage.TextRuns(saved, INPUTS_SHEET, "G1"));
        using IEditableWorkbook reopened = Format.Open(saved);
        Assert.Equal(new CellValue.Text("plain bold plain"), reopened.Sheets[0].ReadValue(0, 6));
    }

    [Fact]
    public void WriteText_WithoutBoldRuns_WritesPlainText()
    {
        using IEditableWorkbook workbook = OpenFixture();
        workbook.Sheets[0].WriteText(0, 6, "just text", []);

        byte[] saved = workbook.Save();

        Assert.Equal(new (string, bool)[] { ("just text", false) }, OoxmlPackage.TextRuns(saved, INPUTS_SHEET, "G1"));
    }

    // A spreadsheet program reads a cell that starts with one of these as a formula. Model text is written
    // as text, so a document cannot make the customer's workbook compute or link.
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(1)")]
    [InlineData("=HYPERLINK(\"https://example.invalid\",\"x\")")]
    public void WriteText_StoresTextThatLooksLikeAFormula_AsText(string text)
    {
        using IEditableWorkbook workbook = OpenFixture();
        workbook.Sheets[0].WriteText(20, 6, text, []);

        using IEditableWorkbook reopened = Format.Open(workbook.Save());

        Assert.Equal(new TextCell(20, 6, text, IsFormula: false), Assert.Single(reopened.Sheets[0].TextCells(), cell => cell is { Row: 20, Column: 6 }));
    }

    [Fact]
    public void WrittenValues_ReadBackBeforeAndAfterSave()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];
        inputs.WriteNumber(20, 0, 1234.5);
        inputs.WriteBoolean(20, 1, false);
        inputs.WriteText(20, 2, "text", []);

        using IEditableWorkbook reopened = Format.Open(workbook.Save());

        foreach (IEditableSheet sheet in new[] { inputs, reopened.Sheets[0] })
        {
            Assert.Equal(new CellValue.Number(1234.5), sheet.ReadValue(20, 0));
            Assert.Equal(new CellValue.Boolean(false), sheet.ReadValue(20, 1));
            Assert.Equal(new CellValue.Text("text"), sheet.ReadValue(20, 2));
        }
    }

    [Fact]
    public void WriteDate_KeepsTheCellsNumberFormat()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];
        var date = new DateTime(2024, 2, 29);
        inputs.WriteDate(2, 1, date);
        inputs.WriteDate(20, 0, date);

        using IEditableWorkbook reopened = Format.Open(workbook.Save());

        Assert.Equal(new CellValue.Date(date), reopened.Sheets[0].ReadValue(2, 1));
        Assert.Equal("2024-02-29", reopened.Sheets[0].CellText(2, 1));
        Assert.Equal(new CellValue.Number(45351), reopened.Sheets[0].ReadValue(20, 0));
        Assert.False(reopened.Sheets[0].HasDateFormat(20, 0));
    }

    [Fact]
    public void Clear_RemovesTheValue_AndKeepsTheStyle()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];

        inputs.Clear(2, 1);
        inputs.Clear(4, 1);

        Assert.Equal(CellValue.Empty, inputs.ReadValue(2, 1));
        Assert.True(inputs.HasDateFormat(2, 1));
        Assert.Equal(CellValue.Empty, inputs.ReadValue(4, 1));
        Assert.DoesNotContain(inputs.TextCells(), cell => cell is { Row: 4, Column: 1 });
    }

    [Fact]
    public void HasDateFormat_IsTrueOnlyForADateFormat()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];

        Assert.True(inputs.HasDateFormat(2, 1));
        Assert.False(inputs.HasDateFormat(1, 1));
        Assert.False(inputs.HasDateFormat(0, 1));
    }

    [Fact]
    public void SetNumberFormat_KeepsTheRestOfTheStyle()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];

        inputs.SetNumberFormat(6, 1, "yyyy-mm-dd");

        Assert.True(inputs.HasDateFormat(6, 1));
        Assert.True(inputs.Layout(6, 1).WrapsText);
        Assert.Equal(10, inputs.Layout(6, 1).FontSize);
    }

    [Fact]
    public void CalculateFormulas_StoresTheResultsOfFormulasWhoseInputsChanged()
    {
        using IEditableWorkbook workbook = OpenFixture();
        workbook.Sheets[0].WriteNumber(0, 1, 7);

        FormulaCalculation calculation = workbook.CalculateFormulas(_calculationLimit);

        Assert.Null(calculation.Failure);
        Assert.Equal(new CellValue.Number(14), workbook.Sheets[0].ReadValue(0, 2));
        Assert.Equal(new CellValue.Number(8), workbook.Sheets[1].ReadValue(0, 0));
        Assert.Equal(new CellValue.Text("{{x}}"), workbook.Sheets[0].ReadValue(2, 2));
        using IEditableWorkbook reopened = Format.Open(workbook.Save());
        Assert.Equal(new CellValue.Number(14), reopened.Sheets[0].ReadValue(0, 2));
    }

    [Fact]
    public void CalculateFormulas_NeverKeepsTheStoredValueOfAnUnknownFunctionWithoutAWord()
    {
        using IEditableWorkbook workbook = OpenFixture();

        FormulaCalculation calculation = workbook.CalculateFormulas(_calculationLimit);

        bool listed = calculation.Uncalculated.Contains(new SheetCell(INPUTS_SHEET, 1, 2));
        Assert.True(listed || workbook.Sheets[0].ReadValue(1, 2) is CellValue.Error);
    }

    [Fact]
    public void RemoveBrokenNames_DropsWorkbookNamesThatPointNowhere_AndKeepsTheRest()
    {
        using IEditableWorkbook workbook = OpenFixture();

        workbook.RemoveBrokenNames();

        IReadOnlyList<string> names = OoxmlPackage.DefinedNames(workbook.Save());
        Assert.Contains("GoodName", names);
        Assert.Contains("SheetBroken", names);
        Assert.DoesNotContain("BrokenRef", names);
    }

    [Fact]
    public void ReplaceSheet_AddsAnEmptySheetLast_ReplacingOneOfTheSameName()
    {
        using IEditableWorkbook workbook = OpenFixture();
        workbook.ReplaceSheet("Stamp", SheetVisibility.VeryHidden).WriteText(0, 0, "first", []);

        IEditableSheet replaced = workbook.ReplaceSheet("stamp", SheetVisibility.VeryHidden);

        Assert.Equal(5, workbook.Sheets.Count);
        Assert.Equal("stamp", replaced.Name);
        Assert.Equal("stamp", workbook.Sheets[^1].Name);
        Assert.Equal(CellValue.Empty, workbook.Sheets[^1].ReadValue(0, 0));
        Assert.False(workbook.Sheets[^1].IsVisible);
        Assert.Equal("veryHidden", OoxmlPackage.SheetState(workbook.Save(), "stamp"));
    }

    [Fact]
    public void Save_KeepsAWorkbookAnXlsx()
    {
        using IEditableWorkbook workbook = OpenFixture();

        byte[] saved = workbook.Save();

        Assert.Equal(OoxmlPackage.WORKBOOK_TYPE, OoxmlPackage.ContentType(saved, OoxmlPackage.WORKBOOK_PART));
        Assert.Equal(".xlsx", Format.SaveExtension(saved));
    }

    [Fact]
    public void Save_KeepsAMacroWorkbookMacroEnabled_WithItsMacros()
    {
        using IEditableWorkbook workbook = Format.Open(CreditReviewTemplate);

        byte[] saved = workbook.Save();

        Assert.Equal(OoxmlPackage.MACRO_WORKBOOK_TYPE, OoxmlPackage.ContentType(saved, OoxmlPackage.WORKBOOK_PART));
        Assert.True(OoxmlPackage.HasPart(saved, "/xl/vbaProject.bin"));
        Assert.Equal(".xlsm", Format.SaveExtension(saved));
    }

    [Fact]
    public void SaveExtension_IsNullForBytesThatHoldNoWorkbook()
    {
        Assert.Equal(".xlsx", Format.SaveExtension(EditableWorkbook));
        Assert.Null(Format.SaveExtension(FillableDocument));
        Assert.Null(Format.SaveExtension(NotADocument));
    }

    [Fact]
    public void Layout_GivesTheMergedArea_TheWrapAndTheFontSize()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];

        Assert.Equal(new CellLayout(new CellRectangle(6, 1, 6, 3), WrapsText: true, FontSize: 10), inputs.Layout(6, 1));
        Assert.Equal(new CellRectangle(6, 1, 6, 3), inputs.Layout(6, 2).Area);
        Assert.Equal(new CellLayout(CellRectangle.Single(10, 1), WrapsText: false, FontSize: 14), inputs.Layout(10, 1));
    }

    [Fact]
    public void HiddenRowsAndColumns_AreReported()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];

        Assert.True(inputs.IsRowHidden(8));
        Assert.False(inputs.IsRowHidden(7));
        Assert.True(inputs.IsColumnHidden(5));
        Assert.False(inputs.IsColumnHidden(1));
    }

    [Fact]
    public void RowHeight_IsReadAndSetInPoints()
    {
        using IEditableWorkbook workbook = OpenFixture();
        Assert.Equal(30, workbook.Sheets[0].RowHeight(9));

        workbook.Sheets[0].SetRowHeight(0, 42.5);

        using IEditableWorkbook reopened = Format.Open(workbook.Save());
        Assert.Equal(42.5, reopened.Sheets[0].RowHeight(0));
    }

    [Fact]
    public void MeasureWrappedText_CountsLinesThatGrowWithTheText_OrNullWhenTextCannotBeLaidOut()
    {
        using IEditableWorkbook workbook = OpenFixture();
        IEditableSheet inputs = workbook.Sheets[0];
        int? shortText;
        int? threeLines;
        int? longText;

        using (IWrappedTextMeasurement measurement = workbook.MeasureWrappedText())
        {
            inputs.WriteText(6, 1, "Short", []);
            shortText = measurement.CountLines(inputs, 6, 1);
            inputs.WriteText(6, 1, "First\nSecond\nThird", []);
            threeLines = measurement.CountLines(inputs, 6, 1);
            inputs.WriteText(6, 1, string.Join(' ', Enumerable.Repeat("covenant headroom", 40)), []);
            longText = measurement.CountLines(inputs, 6, 1);
        }

        Assert.Equal([INPUTS_SHEET, MODEL_SHEET, HIDDEN_SHEET, VERY_HIDDEN_SHEET], workbook.Sheets.Select(sheet => sheet.Name));
        if (shortText is null)
        {
            Assert.Null(threeLines);
            Assert.Null(longText);
            return;
        }

        Assert.Equal(1, shortText);
        Assert.Equal(3, threeLines);
        Assert.True(longText > 3, $"{longText} lines");
    }

    private IEditableWorkbook OpenFixture()
    {
        return Format.Open(EditableWorkbook);
    }
}
